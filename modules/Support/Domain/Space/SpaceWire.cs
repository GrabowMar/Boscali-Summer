using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    // Engine-free SPACE wire payloads and codec. SupportNet wraps these in Mirage messages and adapts NetworkWriter/Reader to
    // the two byte interfaces; nothing here ever allocates from a claimed count, and every reader returns an inert value
    // (Protocol 0, or the foreign Protocol byte alone) instead of throwing.

    internal enum SpaceCommandKind : byte
    {
        None = 0, OpenFeed = 1, Mark = 2, SendTasked = 3, ClaimTasked = 4, FeedActivity = 5, CloseFeed = 6,
        /// <summary>CYBER verbs (protocol 33). Target = an opaque node id (0 = the whole intrusion for a drop). CyberSync asks for a fresh CYBER state.</summary>
        CyberHop = 7, CyberBurn = 8, CyberDrop = 9, CyberSync = 10,
        /// <summary>SOF verbs (protocol 34). SofRaise carries nothing; SofOrder Target = team slot | verb << 2; SofMission Ids = { slot | kind << 2, target id or packed point }; SofDivert Ids = { slot, packed point }. SofSync asks for a fresh SOF state.</summary>
        SofRaise = 11, SofOrder = 12, SofMission = 13, SofDivert = 14, SofSync = 15,
        /// <summary>OPERATIONS verbs (protocol 35). OpFund Target = domain | tier &lt;&lt; 1 (tier 0 = 25 CR, 1 = 50 CR); OpPlan Ids = { kind, target } (a bird 0..2, a SAM C2 node id or a held building id); OpCancel Target = domain. OpSync asks for a fresh OPERATIONS state.</summary>
        OpFund = 16, OpPlan = 17, OpCancel = 18, OpSync = 19
    }

    /// <summary>What the feed may say about a contact before its MARK verdict. Never the truth: see <see cref="SpaceProbable"/>.</summary>
    internal enum ProbableClass : byte { Unknown = 0, Hostile = 1, Neutral = 2, Friendly = 3 }

    internal interface ISpaceWriter { void WriteByte(byte value); }

    /// <summary>A writer that only counts: the exact encoded size of a message, for the one-buffer budget.</summary>
    internal sealed class ByteCounter : ISpaceWriter
    {
        public int Bytes;
        public void WriteByte(byte value) => Bytes++;
    }

    internal interface ISpaceReader
    {
        /// <summary>Bytes left in the message. Readers bound every array by this before allocating.</summary>
        int Remaining { get; }
        bool TryReadByte(out byte value);
    }

    /// <summary>Client request. No faction, classification, price, effort or coordinate field exists: the host derives them all.</summary>
    internal readonly struct SpaceCommand
    {
        public const int MaxIds = TaskedBoard.MaxMarks;
        public readonly byte Protocol;
        public readonly SpaceCommandKind Kind;
        public readonly int RequestId;
        /// <summary>Opaque faction contact id (Mark) or TASKED post id (ClaimTasked).</summary>
        public readonly int Target;
        /// <summary>Opaque faction contact ids to post (SendTasked only; at most <see cref="MaxIds"/>).</summary>
        public readonly int[] Ids;

        public SpaceCommand(byte protocol, SpaceCommandKind kind, int requestId, int target = 0, int[] ids = null)
        {
            Protocol = protocol; Kind = kind; RequestId = requestId; Target = target; Ids = ids;
        }

        public bool Mutating => Kind == SpaceCommandKind.Mark || Kind == SpaceCommandKind.SendTasked || Kind == SpaceCommandKind.ClaimTasked || IsCyberVerb || IsSofVerb || IsOpsVerb;

        public bool IsOpsVerb => Kind >= SpaceCommandKind.OpFund && Kind <= SpaceCommandKind.OpCancel;

        public bool IsSofVerb => Kind >= SpaceCommandKind.SofRaise && Kind <= SpaceCommandKind.SofDivert;

        public bool IsCyberVerb => Kind == SpaceCommandKind.CyberHop || Kind == SpaceCommandKind.CyberBurn || Kind == SpaceCommandKind.CyberDrop;

        /// <summary>Stable digest of what the request asks for. A replayed request id with another payload is refused.</summary>
        public int Fingerprint()
        {
            unchecked
            {
                int h = (int)2166136261u;
                h = (h ^ (int)Kind) * 16777619;
                h = (h ^ Target) * 16777619;
                if (Ids != null)
                {
                    h = (h ^ Ids.Length) * 16777619;
                    for (int i = 0; i < Ids.Length; i++) h = (h ^ Ids[i]) * 16777619;
                }
                return h;
            }
        }
    }

    /// <summary>Host verdict for one mutating request. A MARK carries a <see cref="MarkVerdict"/> byte, SEND/CLAIM a <see cref="TaskedOutcome"/>.</summary>
    internal readonly struct SpaceReply
    {
        public const int MaxClaimant = 20;
        public readonly byte Protocol;
        public readonly SpaceCommandKind Kind;
        public readonly int RequestId, CallId, Charged, Detail;
        public readonly byte Outcome;
        public readonly bool Replayed;
        /// <summary>Callsign of the pilot who holds the call (ClaimedByOther only; printable ASCII, empty when unknown).</summary>
        public readonly string Claimant;

        public SpaceReply(byte protocol, SpaceCommandKind kind, int requestId, byte outcome, int callId = 0,
            int charged = 0, int detail = 0, bool replayed = false, string claimant = null)
        {
            Protocol = protocol; Kind = kind; RequestId = requestId; Outcome = outcome; CallId = callId;
            Charged = charged; Detail = detail; Replayed = replayed; Claimant = claimant ?? "";
        }

        public SpaceReply AsReplay() => new SpaceReply(Protocol, Kind, RequestId, Outcome, CallId, Charged, Detail, true, Claimant);
        public SpaceReply With(string claimant) => new SpaceReply(Protocol, Kind, RequestId, Outcome, CallId, Charged, Detail, Replayed, claimant);
        public MarkVerdict Verdict => (MarkVerdict)Outcome;
        public TaskedOutcome Tasked => (TaskedOutcome)Outcome;
        /// <summary>CYBER verbs carry a <see cref="CyberOutcome"/> byte; <see cref="CallId"/> is the node id.</summary>
        public CyberOutcome CyberVerdict => (CyberOutcome)Outcome;
        /// <summary>SOF verbs carry a <see cref="SofOutcome"/> byte; <see cref="CallId"/> is the team slot.</summary>
        public SofOutcome SofVerdict => (SofOutcome)Outcome;
        /// <summary>The queued claim has not resolved yet: a later push carries the final verdict.</summary>
        public bool Pending => Kind == SpaceCommandKind.ClaimTasked && Outcome == (byte)TaskedOutcome.Queued;
    }

    internal struct FeedContact
    {
        public int Id;
        /// <summary>Native ground PersistentID.Id (uint) for renderer resolution. Never an aircraft; 0 when the contact has none.</summary>
        public uint UnitId;
        public float X, Z, Expires;
        public ProbableClass Class;
        public byte Percent;
        public bool Moving;
        public BirdKind Source;

        public bool SameAs(in FeedContact o) =>
            Id == o.Id && UnitId == o.UnitId && SpaceMirror.SamePoint(X, o.X) && SpaceMirror.SamePoint(Z, o.Z) && Class == o.Class && Percent == o.Percent &&
            Moving == o.Moving && Source == o.Source && SpaceMirror.SameExpiry(Expires, o.Expires);
    }

    internal struct FeedMark
    {
        public int Id;
        public float X, Z, Expires;
        public bool Moving;
        public BirdKind Source;

        public bool SameAs(in FeedMark o) =>
            Id == o.Id && SpaceMirror.SamePoint(X, o.X) && SpaceMirror.SamePoint(Z, o.Z) && Moving == o.Moving && Source == o.Source && SpaceMirror.SameExpiry(Expires, o.Expires);
    }

    /// <summary>One of a post's fixed ground points: the host snapshot taken at SEND, valid for the post's whole 600 s.</summary>
    internal struct FeedPoint
    {
        public float X, Z;
        public BirdKind Source;
        public bool SameAs(in FeedPoint o) => SpaceMirror.SamePoint(X, o.X) && SpaceMirror.SamePoint(Z, o.Z) && Source == o.Source;
    }

    internal struct FeedPost
    {
        public int CallId;
        public SupportActionId Action;
        /// <summary>The post's OPS domain, derived from its action (also carried on the wire and checked there).</summary>
        public TaskedDomain Domain => TaskedKinds.DomainOf(Action);
        /// <summary>Launching is a held, physically launching call only; the 2 s reservation window is not shown as launching.</summary>
        public bool WatchOfficer, Own, Launching;
        /// <summary>The post's snapshot ground points (1..6, fixed at SEND). The first is where the rod flies.</summary>
        public FeedPoint[] Points;
        /// <summary>CR this viewer pays to claim it now (host quote, 0 when free) and the share that goes to contributors.</summary>
        public int Price, Payoff;
        public float Expires;
        /// <summary>Callsign holding the call while it launches (printable ASCII; empty otherwise).</summary>
        public string Claimant;
        /// <summary>Callsign of the human who posted it, for another viewer's OPERATOR card (empty for OVERLORD and for the viewer's own).</summary>
        public string Maker;

        public bool SameAs(in FeedPost o)
        {
            if (CallId != o.CallId || Action != o.Action || WatchOfficer != o.WatchOfficer || Own != o.Own ||
                Launching != o.Launching || Price != o.Price || Payoff != o.Payoff ||
                !SpaceMirror.SameExpiry(Expires, o.Expires) || (Claimant ?? "") != (o.Claimant ?? "") || (Maker ?? "") != (o.Maker ?? "")) return false;
            int a = Points?.Length ?? 0, b = o.Points?.Length ?? 0;
            if (a != b) return false;
            for (int i = 0; i < a; i++) if (!Points[i].SameAs(o.Points[i])) return false;
            return true;
        }
    }

    /// <summary>
    /// One SPACE state message. <see cref="Full"/> replaces the client mirror; otherwise the rows are upserts and the removed
    /// lists name ids to drop. <see cref="Generation"/> is host-wide monotonic and bumps on every full, faction change and
    /// scene reset, so a delayed packet from an earlier subscription can never restore stale or old-faction state.
    /// Times are host mission seconds; on the wire a row's expiry is the quantized seconds left from <see cref="Now"/>.
    /// </summary>
    internal sealed class SpaceStateData
    {
        public byte Protocol;
        public bool Full, Active, Feed;
        public int Generation;
        public float Now;
        public SpaceFamilyState Family;
        public byte UplinksLive, UplinksTotal, LiveMarks;
        /// <summary>Why this viewer cannot claim a TASKED call right now (None when they can); <see cref="GateDetail"/> is its number.</summary>
        public TaskedOutcome Gate;
        /// <summary>
        /// A quantity for the gate word (a price short, say). For COOLDOWN and FROZEN it is a host mission-time deadline in
        /// seconds, so the client counts down locally and a cooling member does not get a packet every second.
        /// </summary>
        public int GateDetail;
        /// <summary>
        /// The newest live TASKED post of the faction, always sent (a pilot with the feed closed still hears about it): its id (0 none),
        /// action, target count, who made it (WATCH OFFICER or a human, with that human's callsign unless it is the viewer's own).
        /// </summary>
        public int NewestPost;
        public SupportActionId NewestAction;
        public byte NewestTargets;
        public bool NewestWatchOfficer, NewestOwn;
        public string NewestMaker = "";
        /// <summary>Host mission-time second the RADAR bird can take another scan (0 = ready now); the client counts down locally.</summary>
        public int RadarReadyAt;
        /// <summary>The faction's ENEMY INTENT line, host-derived words only (empty when unknown).</summary>
        public string Intent = "";
        /// <summary>Not serialized: a message that did not fit one buffer is sent in two; the sender sends this one right after.</summary>
        public SpaceStateData Follow;
        public readonly List<FeedContact> Contacts = new List<FeedContact>();
        public readonly List<int> RemovedContacts = new List<int>();
        public readonly List<FeedMark> Marks = new List<FeedMark>();
        public readonly List<int> RemovedMarks = new List<int>();
        public readonly List<FeedPost> Posts = new List<FeedPost>();
        public readonly List<int> RemovedPosts = new List<int>();

        public bool HasRows => Contacts.Count + RemovedContacts.Count + Marks.Count + RemovedMarks.Count + Posts.Count + RemovedPosts.Count > 0;
    }

    internal static class SpaceWire
    {
        public const int MaxContacts = 48, MaxMarks = 12, MaxPosts = 12;
        /// <summary>Wire coordinates are signed 24-bit decimetres: +-838 km at 0.1 m, three bytes each. -8388608 is the invalid marker.</summary>
        public const float CoordinateLimit = 838860f;
        private const int InvalidCoordinate = -8388608;
        /// <summary>Largest state message the sender emits in one piece (Mirage pools 1300 B writer buffers).</summary>
        public const int StateBudget = 1200;
        public const int MaxMaker = 16, MaxIntent = 56;
        /// <summary><see cref="SpaceStateData.RadarReadyAt"/> value for "the RADAR scan cannot be taken": disabled by the host or no live uplink.</summary>
        public const int RadarUnavailable = int.MaxValue;
        /// <summary>Largest real deadline a reader accepts (mission seconds); anything above it, short of the marker, is clamped.</summary>
        public const int MaxDeadline = 100000000;
        private const int MinContactBytes = 11, MinMarkBytes = 10, MinPostBytes = 17, MaxExpiryDeciseconds = 65535, MaxContactDeciseconds = 255;
        private const byte FlagFull = 1, FlagActive = 2, FlagFeed = 4;

        // ---- Command ---------------------------------------------------------------------------

        public static void WriteCommand(ISpaceWriter w, in SpaceCommand c)
        {
            w.WriteByte(c.Protocol);
            w.WriteByte((byte)c.Kind);
            WriteVar(w, (uint)Math.Max(0, c.RequestId));
            switch (c.Kind)
            {
                case SpaceCommandKind.Mark:
                case SpaceCommandKind.ClaimTasked:
                case SpaceCommandKind.CyberHop:
                case SpaceCommandKind.CyberBurn:
                case SpaceCommandKind.CyberDrop:
                case SpaceCommandKind.SofOrder:
                case SpaceCommandKind.OpFund:
                case SpaceCommandKind.OpCancel:
                    WriteVar(w, (uint)Math.Max(0, c.Target));
                    break;
                case SpaceCommandKind.SendTasked:
                case SpaceCommandKind.SofMission:
                case SpaceCommandKind.SofDivert:
                case SpaceCommandKind.OpPlan:
                    int count = Math.Min(c.Ids?.Length ?? 0, SpaceCommand.MaxIds);
                    w.WriteByte((byte)count);
                    for (int i = 0; i < count; i++) WriteVar(w, (uint)Math.Max(0, c.Ids[i]));
                    break;
            }
        }

        /// <summary>A foreign protocol returns the Protocol byte alone; any malformed payload returns Protocol 0.</summary>
        public static SpaceCommand ReadCommand(ISpaceReader r, byte protocol)
        {
            if (!r.TryReadByte(out byte version)) return default;
            if (version != protocol) return new SpaceCommand(version, SpaceCommandKind.None, 0);
            if (!r.TryReadByte(out byte kindByte) || kindByte < (byte)SpaceCommandKind.OpenFeed || kindByte > (byte)SpaceCommandKind.OpSync ||
                !ReadInt(r, out int request)) return default;
            var kind = (SpaceCommandKind)kindByte;
            switch (kind)
            {
                case SpaceCommandKind.Mark:
                case SpaceCommandKind.ClaimTasked:
                case SpaceCommandKind.CyberHop:
                case SpaceCommandKind.CyberBurn:
                case SpaceCommandKind.CyberDrop:
                case SpaceCommandKind.SofOrder:
                case SpaceCommandKind.OpFund:
                case SpaceCommandKind.OpCancel:
                    return ReadInt(r, out int target) ? new SpaceCommand(version, kind, request, target) : default;
                case SpaceCommandKind.SendTasked:
                case SpaceCommandKind.SofMission:
                case SpaceCommandKind.SofDivert:
                case SpaceCommandKind.OpPlan:
                    if (!r.TryReadByte(out byte count) || count == 0 || count > SpaceCommand.MaxIds || r.Remaining < count) return default;
                    if (kind != SpaceCommandKind.SendTasked && count != 2) return default; // a SOF or OPERATIONS command carries exactly two ints
                    var ids = new int[count];
                    for (int i = 0; i < count; i++) if (!ReadInt(r, out ids[i])) return default;
                    return new SpaceCommand(version, kind, request, 0, ids);
                default:
                    return new SpaceCommand(version, kind, request);
            }
        }

        // ---- Reply -----------------------------------------------------------------------------

        public static void WriteReply(ISpaceWriter w, in SpaceReply v)
        {
            w.WriteByte(v.Protocol);
            w.WriteByte((byte)v.Kind);
            WriteVar(w, (uint)Math.Max(0, v.RequestId));
            w.WriteByte(v.Outcome);
            w.WriteByte(v.Replayed ? (byte)1 : (byte)0);
            WriteVar(w, (uint)Math.Max(0, v.CallId));
            WriteVar(w, (uint)Math.Max(0, v.Charged));
            WriteVar(w, (uint)Math.Max(0, v.Detail));
            WriteText(w, v.Claimant, SpaceReply.MaxClaimant);
        }

        public static SpaceReply ReadReply(ISpaceReader r, byte protocol)
        {
            if (!r.TryReadByte(out byte version)) return default;
            if (version != protocol) return new SpaceReply(version, SpaceCommandKind.None, 0, 0);
            if (!r.TryReadByte(out byte kindByte) || (kindByte != (byte)SpaceCommandKind.Mark && kindByte != (byte)SpaceCommandKind.SendTasked &&
                kindByte != (byte)SpaceCommandKind.ClaimTasked && (kindByte < (byte)SpaceCommandKind.CyberHop || kindByte > (byte)SpaceCommandKind.CyberDrop) &&
                (kindByte < (byte)SpaceCommandKind.SofRaise || kindByte > (byte)SpaceCommandKind.SofDivert) &&
                (kindByte < (byte)SpaceCommandKind.OpFund || kindByte > (byte)SpaceCommandKind.OpCancel)) || !ReadInt(r, out int request) ||
                !r.TryReadByte(out byte outcome) || !r.TryReadByte(out byte replay) || replay > 1 ||
                !ReadInt(r, out int call) || !ReadInt(r, out int charged) || !ReadInt(r, out int detail) ||
                !ReadText(r, SpaceReply.MaxClaimant, out string claimant)) return default;
            int max = kindByte == (byte)SpaceCommandKind.Mark ? (int)MarkVerdict.Capacity :
                kindByte >= (byte)SpaceCommandKind.OpFund ? (int)OpsWords.MaxOutcome :
                kindByte >= (byte)SpaceCommandKind.SofRaise ? (int)SofOutcomeWords.MaxOutcome :
                kindByte >= (byte)SpaceCommandKind.CyberHop ? (int)CyberWords.MaxOutcome : (int)TaskedOutcome.MarkExpired;
            if (outcome > max) return default; // an unknown verdict is never guessed at
            return new SpaceReply(version, (SpaceCommandKind)kindByte, request, outcome, call, charged, detail, replay == 1, claimant);
        }

        // ---- State -----------------------------------------------------------------------------

        public static void WriteState(ISpaceWriter w, SpaceStateData s)
        {
            w.WriteByte(s.Protocol);
            w.WriteByte((byte)((s.Full ? FlagFull : 0) | (s.Active ? FlagActive : 0) | (s.Feed ? FlagFeed : 0)));
            WriteVar(w, (uint)Math.Max(0, s.Generation));
            WriteFloat(w, s.Now);
            w.WriteByte((byte)s.Family);
            w.WriteByte(s.UplinksLive);
            w.WriteByte(s.UplinksTotal);
            w.WriteByte(s.LiveMarks);
            w.WriteByte((byte)s.Gate);
            WriteVar(w, (uint)Math.Max(0, s.GateDetail));
            WriteVar(w, (uint)Math.Max(0, s.NewestPost));
            if (s.NewestPost > 0)
            {
                w.WriteByte((byte)s.NewestAction);
                w.WriteByte((byte)((Math.Max(1, Math.Min(TaskedBoard.MaxMarks, (int)s.NewestTargets))) | (s.NewestWatchOfficer ? 8 : 0) | (s.NewestOwn ? 16 : 0)));
                WriteText(w, s.NewestMaker, MaxMaker);
            }
            WriteVar(w, (uint)Math.Max(0, s.RadarReadyAt));
            WriteText(w, s.Intent, MaxIntent);
            if (!s.Active || !s.Feed) return; // headline only: no rows are ever sent to a passive faction member
            int n = Math.Min(s.Contacts.Count, MaxContacts);
            w.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                FeedContact c = s.Contacts[i];
                WriteVar(w, (uint)Math.Max(0, c.Id));
                WriteVar(w, c.UnitId);
                WriteCoordinate(w, c.X); WriteCoordinate(w, c.Z);
                w.WriteByte((byte)((byte)c.Class | (c.Moving ? 4 : 0) | ((byte)c.Source == (byte)BirdKind.Radar ? 8 : 0)));
                w.WriteByte(c.Percent);
                WriteExpiry1(w, c.Expires, s.Now); // a reveal lives twenty seconds: one byte of deciseconds is enough
            }
            WriteIds(w, s.RemovedContacts, MaxContacts);
            n = Math.Min(s.Marks.Count, MaxMarks);
            w.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                FeedMark m = s.Marks[i];
                WriteVar(w, (uint)Math.Max(0, m.Id));
                WriteCoordinate(w, m.X); WriteCoordinate(w, m.Z);
                w.WriteByte((byte)((m.Moving ? 1 : 0) | (m.Source == BirdKind.Radar ? 2 : 0)));
                WriteExpiry(w, m.Expires, s.Now);
            }
            WriteIds(w, s.RemovedMarks, MaxMarks);
            n = Math.Min(s.Posts.Count, MaxPosts);
            w.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                FeedPost p = s.Posts[i];
                WriteVar(w, (uint)Math.Max(0, p.CallId));
                w.WriteByte((byte)p.Action);
                w.WriteByte((byte)((p.WatchOfficer ? 1 : 0) | (p.Own ? 2 : 0) | (p.Launching ? 4 : 0) | ((int)p.Domain << 3)));
                int points = Math.Min(p.Points?.Length ?? 0, TaskedBoard.MaxMarks);
                byte radar = 0;
                for (int j = 0; j < points; j++) if (p.Points[j].Source == BirdKind.Radar) radar |= (byte)(1 << j);
                w.WriteByte(radar);
                w.WriteByte((byte)points);
                for (int j = 0; j < points; j++) { WriteCoordinate(w, p.Points[j].X); WriteCoordinate(w, p.Points[j].Z); }
                WriteVar(w, (uint)Math.Max(0, p.Price));
                WriteVar(w, (uint)Math.Max(0, p.Payoff));
                WriteExpiry(w, p.Expires, s.Now);
                WriteText(w, p.Claimant, SpaceReply.MaxClaimant);
                WriteText(w, p.Maker, MaxMaker);
            }
            WriteIds(w, s.RemovedPosts, MaxPosts);
        }

        public static SpaceStateData ReadState(ISpaceReader r, byte protocol)
        {
            if (!r.TryReadByte(out byte version)) return Bad();
            if (version != protocol) return new SpaceStateData { Protocol = version };
            var s = new SpaceStateData { Protocol = version };
            if (!r.TryReadByte(out byte flags) || (flags & ~(FlagFull | FlagActive | FlagFeed)) != 0 ||
                !ReadInt(r, out int generation) || !ReadFloat(r, out float now) || !SpaceRules.MissionTime(now) ||
                !r.TryReadByte(out byte family) || family > (byte)SpaceFamilyState.Dark ||
                !r.TryReadByte(out s.UplinksLive) || !r.TryReadByte(out s.UplinksTotal) || s.UplinksTotal > 2 || s.UplinksLive > s.UplinksTotal ||
                !r.TryReadByte(out s.LiveMarks) || s.LiveMarks > MaxMarks ||
                !r.TryReadByte(out byte gate) || gate > (byte)TaskedOutcome.MarkExpired ||
                !ReadInt(r, out int gateDetail) || !ReadInt(r, out int newest)) return Bad();
            if (newest > 0)
            {
                if (!r.TryReadByte(out byte newestAction) || !TaskedKinds.TryGet((SupportActionId)newestAction, out _) ||
                    !r.TryReadByte(out byte newestBits) || (newestBits & ~31) != 0 || (newestBits & 7) < 1 || (newestBits & 7) > TaskedBoard.MaxMarks ||
                    !ReadText(r, MaxMaker, out string newestMaker)) return Bad();
                s.NewestPost = newest; s.NewestAction = (SupportActionId)newestAction; s.NewestTargets = (byte)(newestBits & 7);
                s.NewestWatchOfficer = (newestBits & 8) != 0; s.NewestOwn = (newestBits & 16) != 0; s.NewestMaker = newestMaker;
            }
            if (!ReadInt(r, out int radarReady) || !ReadText(r, MaxIntent, out string intent)) return Bad();
            s.RadarReadyAt = radarReady == RadarUnavailable ? RadarUnavailable : Math.Min(radarReady, MaxDeadline); s.Intent = intent;
            s.Full = (flags & FlagFull) != 0; s.Active = (flags & FlagActive) != 0; s.Feed = (flags & FlagFeed) != 0;
            s.Generation = generation; s.Now = now; s.Family = (SpaceFamilyState)family; s.Gate = (TaskedOutcome)gate; s.GateDetail = gateDetail;
            if (!s.Active || !s.Feed) return s;

            if (!r.TryReadByte(out byte n) || n > MaxContacts || r.Remaining < n * MinContactBytes) return Bad();
            for (int i = 0; i < n; i++)
            {
                if (!ReadInt(r, out int id) || !ReadVar(r, out uint unit) || !ReadCoordinate(r, out float x) || !ReadCoordinate(r, out float z) ||
                    !r.TryReadByte(out byte bits) || (bits & ~15) != 0 || (bits & 3) > (byte)ProbableClass.Friendly ||
                    !r.TryReadByte(out byte percent) || percent > 100 || !ReadExpiry1(r, now, out float expires)) return Bad();
                s.Contacts.Add(new FeedContact
                {
                    Id = id, UnitId = unit, X = x, Z = z, Class = (ProbableClass)(bits & 3), Moving = (bits & 4) != 0,
                    Source = (bits & 8) != 0 ? BirdKind.Radar : BirdKind.Optical, Percent = percent, Expires = expires
                });
            }
            if (!ReadIds(r, s.RemovedContacts, MaxContacts)) return Bad();

            if (!r.TryReadByte(out n) || n > MaxMarks || r.Remaining < n * MinMarkBytes) return Bad();
            for (int i = 0; i < n; i++)
            {
                if (!ReadInt(r, out int id) || !ReadCoordinate(r, out float x) || !ReadCoordinate(r, out float z) ||
                    !r.TryReadByte(out byte bits) || (bits & ~3) != 0 || !ReadExpiry(r, now, out float expires)) return Bad();
                s.Marks.Add(new FeedMark { Id = id, X = x, Z = z, Moving = (bits & 1) != 0, Source = (bits & 2) != 0 ? BirdKind.Radar : BirdKind.Optical, Expires = expires });
            }
            if (!ReadIds(r, s.RemovedMarks, MaxMarks)) return Bad();

            if (!r.TryReadByte(out n) || n > MaxPosts || r.Remaining < n * MinPostBytes) return Bad();
            for (int i = 0; i < n; i++)
            {
                if (!ReadInt(r, out int call) || !r.TryReadByte(out byte action) || !TaskedKinds.TryGet((SupportActionId)action, out _) ||
                    !r.TryReadByte(out byte bits) || (bits & ~31) != 0 || ((bits >> 3) & 3) != (int)TaskedKinds.DomainOf((SupportActionId)action) || !r.TryReadByte(out byte radar) || (radar & ~63) != 0 ||
                    !r.TryReadByte(out byte pointCount) || pointCount == 0 || pointCount > TaskedBoard.MaxMarks || (radar >> pointCount) != 0 ||
                    r.Remaining < pointCount * 6) return Bad();
                var points = new FeedPoint[pointCount];
                for (int j = 0; j < pointCount; j++)
                {
                    if (!ReadCoordinate(r, out float px) || !ReadCoordinate(r, out float pz)) return Bad();
                    points[j] = new FeedPoint { X = px, Z = pz, Source = (radar & (1 << j)) != 0 ? BirdKind.Radar : BirdKind.Optical };
                }
                if (!ReadInt(r, out int price) || !ReadInt(r, out int payoff) || !ReadExpiry(r, now, out float expires) ||
                    !ReadText(r, SpaceReply.MaxClaimant, out string claimant) || !ReadText(r, MaxMaker, out string maker)) return Bad();
                s.Posts.Add(new FeedPost
                {
                    CallId = call, Action = (SupportActionId)action, WatchOfficer = (bits & 1) != 0, Own = (bits & 2) != 0, Launching = (bits & 4) != 0,
                    Points = points, Price = price, Payoff = payoff, Expires = expires, Claimant = claimant, Maker = maker
                });
            }
            return ReadIds(r, s.RemovedPosts, MaxPosts) ? s : Bad();
        }

        private static SpaceStateData Bad() => new SpaceStateData();

        /// <summary>True when the wire can carry this coordinate (finite, inside +-838 km). Hosts filter rows with this.</summary>
        public static bool Codable(float value) => SpaceRules.Finite(value) && Math.Abs(value) <= CoordinateLimit;

        /// <summary>Exact encoded size of a state message, for the one-buffer budget.</summary>
        public static int StateSize(SpaceStateData s)
        {
            var counter = new ByteCounter();
            WriteState(counter, s);
            return counter.Bytes;
        }

        // ---- Primitives ------------------------------------------------------------------------

        internal static void WriteVar(ISpaceWriter w, uint value)
        {
            while (value >= 0x80u) { w.WriteByte((byte)(value | 0x80u)); value >>= 7; }
            w.WriteByte((byte)value);
        }

        internal static bool ReadVar(ISpaceReader r, out uint value)
        {
            value = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                if (!r.TryReadByte(out byte b)) return false;
                if (shift == 28 && (b & 0xF0) != 0) return false; // would overflow 32 bits
                value |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return true;
            }
            return false;
        }

        internal static bool ReadInt(ISpaceReader r, out int value)
        {
            value = 0;
            if (!ReadVar(r, out uint raw) || raw > int.MaxValue) return false;
            value = (int)raw;
            return true;
        }

        internal static void WriteFloat(ISpaceWriter w, float value)
        {
            int bits = BitConverter.SingleToInt32Bits(value);
            w.WriteByte((byte)bits); w.WriteByte((byte)(bits >> 8)); w.WriteByte((byte)(bits >> 16)); w.WriteByte((byte)(bits >> 24));
        }

        internal static bool ReadFloat(ISpaceReader r, out float value)
        {
            value = 0;
            if (r.Remaining < 4 || !r.TryReadByte(out byte a) || !r.TryReadByte(out byte b) || !r.TryReadByte(out byte c) || !r.TryReadByte(out byte d)) return false;
            value = BitConverter.Int32BitsToSingle(a | (b << 8) | (c << 16) | (d << 24));
            return true;
        }

        internal static void WriteCoordinate(ISpaceWriter w, float value)
        {
            // A value the wire cannot carry is written as the invalid marker so the reader refuses the whole message.
            int v = SpaceRules.Finite(value) && Math.Abs(value) <= CoordinateLimit ? (int)Math.Round(value * 10d) : InvalidCoordinate;
            w.WriteByte((byte)v); w.WriteByte((byte)(v >> 8)); w.WriteByte((byte)(v >> 16));
        }

        internal static bool ReadCoordinate(ISpaceReader r, out float value)
        {
            value = 0;
            if (!r.TryReadByte(out byte a) || !r.TryReadByte(out byte b) || !r.TryReadByte(out byte c)) return false;
            int v = a | (b << 8) | (c << 16);
            if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000); // sign-extend the 24-bit value
            if (v == InvalidCoordinate) return false;
            value = v / 10f;
            return true;
        }

        private static void WriteExpiry1(ISpaceWriter w, float expires, float now)
        {
            double left = SpaceRules.Finite(expires) && SpaceRules.Finite(now) ? Math.Round((expires - now) * 10d) : 0d;
            w.WriteByte((byte)Math.Max(0d, Math.Min(MaxContactDeciseconds, left)));
        }

        private static bool ReadExpiry1(ISpaceReader r, float now, out float expires)
        {
            expires = 0;
            if (!r.TryReadByte(out byte ds)) return false;
            expires = now + ds / 10f;
            return true;
        }

        internal static void WriteExpiry(ISpaceWriter w, float expires, float now)
        {
            double left = SpaceRules.Finite(expires) && SpaceRules.Finite(now) ? Math.Round((expires - now) * 10d) : 0d;
            int ds = (int)Math.Max(0d, Math.Min(MaxExpiryDeciseconds, left));
            w.WriteByte((byte)ds); w.WriteByte((byte)(ds >> 8));
        }

        internal static bool ReadExpiry(ISpaceReader r, float now, out float expires)
        {
            expires = 0;
            if (!r.TryReadByte(out byte lo) || !r.TryReadByte(out byte hi)) return false;
            expires = now + (lo | (hi << 8)) / 10f;
            return true;
        }

        private static void WriteIds(ISpaceWriter w, List<int> ids, int capacity)
        {
            int n = Math.Min(ids.Count, capacity);
            w.WriteByte((byte)n);
            for (int i = 0; i < n; i++) WriteVar(w, (uint)Math.Max(0, ids[i]));
        }

        private static bool ReadIds(ISpaceReader r, List<int> into, int capacity)
        {
            if (!r.TryReadByte(out byte n) || n > capacity || r.Remaining < n) return false;
            for (int i = 0; i < n; i++)
            {
                if (!ReadInt(r, out int id)) return false;
                into.Add(id);
            }
            return true;
        }

        internal static void WriteText(ISpaceWriter w, string text, int max)
        {
            text = Clean(text, max);
            w.WriteByte((byte)text.Length);
            for (int i = 0; i < text.Length; i++) w.WriteByte((byte)text[i]);
        }

        internal static bool ReadText(ISpaceReader r, int max, out string text)
        {
            text = "";
            if (!r.TryReadByte(out byte n) || n > max || r.Remaining < n) return false;
            var chars = new char[n];
            for (int i = 0; i < n; i++)
            {
                if (!r.TryReadByte(out byte b) || b < 0x20 || b > 0x7E) return false;
                chars[i] = (char)b;
            }
            text = new string(chars);
            return true;
        }

        /// <summary>Printable ASCII only, bounded: a callsign can carry nothing the reader would refuse.</summary>
        public static string Clean(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int n = Math.Min(text.Length, max);
            var chars = new char[n];
            for (int i = 0; i < n; i++) chars[i] = text[i] >= 0x20 && text[i] <= 0x7E ? text[i] : '?';
            return new string(chars);
        }
    }
}
