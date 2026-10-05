using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Domain.Ops
{
    /// <summary>One of the faction's own operations (own faction only: an enemy never receives a row, only <see cref="OpsPingRow"/>).</summary>
    internal struct OpsRow
    {
        public OpDomain Domain;
        public OpKind Kind;
        public OpState State;
        public bool Paused, HasTarget;
        public byte Percent, WorkPercent;
        public int Goal, TargetId, MyCr;
        public float X, Z;
        /// <summary>Host mission second: the countdown end while EXECUTE, the effect end while DONE (SAM net back, ASAT strike, FOB expiry), 0 otherwise.</summary>
        public float EndsAt;
    }

    /// <summary>What an enemy faction is allowed to know. The attacker's name rides along (<see cref="OpsRules"/> never sends a bar or a contributor).</summary>
    internal struct OpsPingRow
    {
        public OpKind Kind;
        public OpPingPhase Phase;
        public int Seq;
        /// <summary>A Loss ping names the satellite that died (0 OPTICAL, 1 RADAR, 2 KINETIC).</summary>
        public byte Detail;
        public float Until;
        public string Name;
    }

    internal struct OpsEventRow
    {
        public int Seq;
        public OpEventKind Kind;
        public OpDomain Domain;
        public OpKind Op;
    }

    /// <summary>An ASAT in its scripted ascent, visible to every faction (a smoke column and a rising light): the launch point and the seconds it has left.</summary>
    internal struct OpsFlightRow
    {
        public int Id;
        public float X, Z, EndsAt;
        public byte Seconds;
    }

    /// <summary>
    /// One faction's OPERATIONS state, always a full snapshot. Own rows and own satellite state go to the owning faction only; pings are the enemies' operations
    /// as the viewer may hear them; flights are everyone's. Times are host mission seconds; on the wire an expiry is the quantized seconds left from <see cref="Now"/>.
    /// </summary>
    internal sealed class OpsStateData
    {
        public byte Protocol;
        public bool Active, CyberOps, SofOps;
        public int Seq;
        public float Now;
        /// <summary>Bit i set while the faction's satellite i (OPTICAL, RADAR, KINETIC) is dead; <see cref="BirdPercent"/> is its rebuild progress.</summary>
        public byte BirdsDown;
        public readonly byte[] BirdPercent = new byte[SpaceRules.BirdCount];
        public readonly List<OpsRow> Rows = new List<OpsRow>();
        public readonly List<OpsPingRow> Pings = new List<OpsPingRow>();
        public readonly List<OpsEventRow> Events = new List<OpsEventRow>();
        public readonly List<OpsFlightRow> Flights = new List<OpsFlightRow>();

        public OpsStateData Clone()
        {
            var c = new OpsStateData { Protocol = Protocol, Active = Active, CyberOps = CyberOps, SofOps = SofOps, Seq = Seq, Now = Now, BirdsDown = BirdsDown };
            Array.Copy(BirdPercent, c.BirdPercent, BirdPercent.Length);
            c.Rows.AddRange(Rows); c.Pings.AddRange(Pings); c.Events.AddRange(Events); c.Flights.AddRange(Flights);
            return c;
        }

        public bool TryRow(OpDomain domain, out OpsRow row)
        {
            for (int i = 0; i < Rows.Count; i++) if (Rows[i].Domain == domain) { row = Rows[i]; return true; }
            row = default; return false;
        }

        /// <summary>The fields that decide whether anything changed since the last send (everything but Seq and Now).</summary>
        public bool SameAs(OpsStateData o)
        {
            if (o == null || Active != o.Active || CyberOps != o.CyberOps || SofOps != o.SofOps || BirdsDown != o.BirdsDown ||
                Rows.Count != o.Rows.Count || Pings.Count != o.Pings.Count || Events.Count != o.Events.Count || Flights.Count != o.Flights.Count) return false;
            for (int i = 0; i < BirdPercent.Length; i++) if (BirdPercent[i] != o.BirdPercent[i]) return false;
            for (int i = 0; i < Rows.Count; i++)
            {
                OpsRow a = Rows[i], b = o.Rows[i];
                if (a.Domain != b.Domain || a.Kind != b.Kind || a.State != b.State || a.Paused != b.Paused || a.HasTarget != b.HasTarget || a.Percent != b.Percent ||
                    a.WorkPercent != b.WorkPercent || a.Goal != b.Goal || a.TargetId != b.TargetId || a.MyCr != b.MyCr || !SpaceMirror.SamePoint(a.X, b.X) ||
                    !SpaceMirror.SamePoint(a.Z, b.Z) || !SpaceMirror.SameExpiry(a.EndsAt, b.EndsAt)) return false;
            }
            for (int i = 0; i < Pings.Count; i++)
                if (Pings[i].Seq != o.Pings[i].Seq || Pings[i].Detail != o.Pings[i].Detail || Pings[i].Phase != o.Pings[i].Phase || Pings[i].Kind != o.Pings[i].Kind || (Pings[i].Name ?? "") != (o.Pings[i].Name ?? "")) return false;
            for (int i = 0; i < Events.Count; i++) if (Events[i].Seq != o.Events[i].Seq) return false;
            for (int i = 0; i < Flights.Count; i++)
                if (Flights[i].Id != o.Flights[i].Id || !SpaceMirror.SamePoint(Flights[i].X, o.Flights[i].X) || !SpaceMirror.SamePoint(Flights[i].Z, o.Flights[i].Z) ||
                    !SpaceMirror.SameExpiry(Flights[i].EndsAt, o.Flights[i].EndsAt)) return false;
            return true;
        }
    }

    /// <summary>Engine-free OPERATIONS state codec (protocol 35). Every reader returns an inert value (Protocol 0 or the foreign byte alone) instead of throwing.</summary>
    internal static class OpsWire
    {
        public const int MaxRows = OpsDesk.Slots, MaxPings = 4, MaxEvents = 8, MaxFlights = 1, MaxName = 12;
        private const int MinRowBytes = 10, MinPingBytes = 5, MinEventBytes = 2, MinFlightBytes = 8;
        private const byte FlagActive = 1, FlagCyber = 2, FlagSof = 4;

        public static void WriteState(ISpaceWriter w, OpsStateData s)
        {
            w.WriteByte(s.Protocol);
            w.WriteByte((byte)((s.Active ? FlagActive : 0) | (s.CyberOps ? FlagCyber : 0) | (s.SofOps ? FlagSof : 0)));
            SpaceWire.WriteVar(w, (uint)Math.Max(0, s.Seq));
            SpaceWire.WriteFloat(w, s.Now);
            if (!s.Active) return;
            w.WriteByte(s.BirdsDown);
            for (int i = 0; i < s.BirdPercent.Length; i++) w.WriteByte(s.BirdPercent[i]);
            int n = Math.Min(s.Rows.Count, MaxRows);
            w.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                OpsRow r = s.Rows[i];
                w.WriteByte((byte)((int)r.Domain | ((int)r.Kind << 1) | ((int)r.State << 3) | (r.Paused ? 64 : 0) | (r.HasTarget ? 128 : 0)));
                w.WriteByte(r.Percent); w.WriteByte(r.WorkPercent);
                SpaceWire.WriteVar(w, (uint)Math.Max(0, r.Goal));
                SpaceWire.WriteVar(w, (uint)Math.Max(0, r.TargetId));
                SpaceWire.WriteVar(w, (uint)Math.Max(0, r.MyCr));
                SpaceWire.WriteCoordinate(w, r.X); SpaceWire.WriteCoordinate(w, r.Z);
                SpaceWire.WriteExpiry(w, r.EndsAt, s.Now);
            }
            n = Math.Min(s.Pings.Count, MaxPings);
            w.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                OpsPingRow p = s.Pings[i];
                w.WriteByte((byte)((int)p.Kind | ((int)p.Phase << 2) | (Math.Min((int)p.Detail, 3) << 4)));
                SpaceWire.WriteVar(w, (uint)Math.Max(0, p.Seq));
                SpaceWire.WriteExpiry(w, p.Until, s.Now);
                SpaceWire.WriteText(w, p.Name, MaxName);
            }
            n = Math.Min(s.Events.Count, MaxEvents);
            w.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                OpsEventRow e = s.Events[i];
                SpaceWire.WriteVar(w, (uint)Math.Max(0, e.Seq));
                w.WriteByte((byte)((int)e.Kind | ((int)e.Domain << 4) | ((int)e.Op << 5)));
            }
            n = Math.Min(s.Flights.Count, MaxFlights);
            w.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                OpsFlightRow f = s.Flights[i];
                SpaceWire.WriteVar(w, (uint)Math.Max(0, f.Id));
                SpaceWire.WriteCoordinate(w, f.X); SpaceWire.WriteCoordinate(w, f.Z);
                SpaceWire.WriteExpiry(w, f.EndsAt, s.Now);
                w.WriteByte(f.Seconds);
            }
        }

        public static OpsStateData ReadState(ISpaceReader r, byte protocol)
        {
            if (!r.TryReadByte(out byte version)) return Bad();
            if (version != protocol) return new OpsStateData { Protocol = version };
            var s = new OpsStateData { Protocol = version };
            if (!r.TryReadByte(out byte flags) || (flags & ~(FlagActive | FlagCyber | FlagSof)) != 0 || !SpaceWire.ReadInt(r, out int seq) ||
                !SpaceWire.ReadFloat(r, out float now) || !SpaceRules.MissionTime(now)) return Bad();
            s.Active = (flags & FlagActive) != 0; s.CyberOps = (flags & FlagCyber) != 0; s.SofOps = (flags & FlagSof) != 0; s.Seq = seq; s.Now = now;
            if (!s.Active) return s;
            if (!r.TryReadByte(out s.BirdsDown) || (s.BirdsDown & ~7) != 0) return Bad();
            for (int i = 0; i < s.BirdPercent.Length; i++) if (!r.TryReadByte(out s.BirdPercent[i]) || s.BirdPercent[i] > 100) return Bad();
            if (!r.TryReadByte(out byte n) || n > MaxRows || r.Remaining < n * MinRowBytes) return Bad();
            for (int i = 0; i < n; i++)
            {
                if (!r.TryReadByte(out byte bits) || ((bits >> 1) & 3) > (int)OpKind.Fob || ((bits >> 3) & 7) > (int)OpState.Broken ||
                    !r.TryReadByte(out byte pct) || pct > 100 || !r.TryReadByte(out byte work) || work > 100 || !SpaceWire.ReadInt(r, out int goal) ||
                    !SpaceWire.ReadInt(r, out int target) || !SpaceWire.ReadInt(r, out int mine) || !SpaceWire.ReadCoordinate(r, out float x) ||
                    !SpaceWire.ReadCoordinate(r, out float z) || !SpaceWire.ReadExpiry(r, now, out float ends)) return Bad();
                s.Rows.Add(new OpsRow
                {
                    Domain = (OpDomain)(bits & 1), Kind = (OpKind)((bits >> 1) & 3), State = (OpState)((bits >> 3) & 7), Paused = (bits & 64) != 0, HasTarget = (bits & 128) != 0,
                    Percent = pct, WorkPercent = work, Goal = goal, TargetId = target, MyCr = mine, X = x, Z = z, EndsAt = ends <= now ? 0f : ends
                });
            }
            if (!r.TryReadByte(out n) || n > MaxPings || r.Remaining < n * MinPingBytes) return Bad();
            for (int i = 0; i < n; i++)
            {
                if (!r.TryReadByte(out byte bits) || (bits & 3) < (int)OpKind.Asat || (bits & 3) > (int)OpKind.Fob || ((bits >> 2) & 3) > (int)OpPingPhase.Loss || (bits >> 4) > 3 ||
                    !SpaceWire.ReadInt(r, out int pseq) || !SpaceWire.ReadExpiry(r, now, out float until) || !SpaceWire.ReadText(r, MaxName, out string name)) return Bad();
                s.Pings.Add(new OpsPingRow { Kind = (OpKind)(bits & 3), Phase = (OpPingPhase)((bits >> 2) & 3), Detail = (byte)(bits >> 4), Seq = pseq, Until = until <= now ? 0f : until, Name = name });
            }
            if (!r.TryReadByte(out n) || n > MaxEvents || r.Remaining < n * MinEventBytes) return Bad();
            for (int i = 0; i < n; i++)
            {
                if (!SpaceWire.ReadInt(r, out int eseq) || !r.TryReadByte(out byte bits) || (bits & 15) < (int)OpEventKind.Started || (bits & 15) > (int)OpEventKind.Done ||
                    ((bits >> 5) & 3) > (int)OpKind.Fob || (bits >> 7) != 0) return Bad();
                s.Events.Add(new OpsEventRow { Seq = eseq, Kind = (OpEventKind)(bits & 15), Domain = (OpDomain)((bits >> 4) & 1), Op = (OpKind)((bits >> 5) & 3) });
            }
            if (!r.TryReadByte(out n) || n > MaxFlights || r.Remaining < n * MinFlightBytes) return Bad();
            for (int i = 0; i < n; i++)
            {
                if (!SpaceWire.ReadInt(r, out int id) || id <= 0 || !SpaceWire.ReadCoordinate(r, out float x) || !SpaceWire.ReadCoordinate(r, out float z) ||
                    !SpaceWire.ReadExpiry(r, now, out float ends) || !r.TryReadByte(out byte seconds)) return Bad();
                s.Flights.Add(new OpsFlightRow { Id = id, X = x, Z = z, EndsAt = ends <= now ? 0f : ends, Seconds = seconds });
            }
            return s;
        }

        private static OpsStateData Bad() => new OpsStateData();

        private sealed class Counter : ISpaceWriter
        {
            public int Bytes;
            public void WriteByte(byte value) => Bytes++;
        }

        public static int StateSize(OpsStateData s)
        {
            var c = new Counter();
            WriteState(c, s);
            return c.Bytes;
        }
    }

    /// <summary>The client's copy of its own faction's OPERATIONS state. Full snapshots only: anything not newer than the last one on this link is dropped.</summary>
    internal sealed class OpsMirror
    {
        public OpsStateData State { get; private set; } = new OpsStateData();
        public bool Known { get; private set; }
        public int Seq { get; private set; }
        public int Floor { get; private set; }

        public void Reset() { State = new OpsStateData(); Known = false; Seq = 0; }

        public void ResetLink() { Reset(); Floor = 0; }

        public bool Apply(OpsStateData d, byte protocol, float clientNow)
        {
            if (d == null || d.Protocol != protocol || d.Seq <= 0 || d.Seq <= Floor || !SpaceRules.MissionTime(clientNow)) return false;
            if (d.Rows.Count > OpsWire.MaxRows || d.Pings.Count > OpsWire.MaxPings || d.Events.Count > OpsWire.MaxEvents || d.Flights.Count > OpsWire.MaxFlights) return false;
            float offset = clientNow - d.Now;
            OpsStateData copy = d.Clone();
            copy.Now = clientNow;
            for (int i = 0; i < copy.Rows.Count; i++) { OpsRow r = copy.Rows[i]; if (r.EndsAt > 0f) r.EndsAt += offset; copy.Rows[i] = r; }
            for (int i = 0; i < copy.Pings.Count; i++) { OpsPingRow p = copy.Pings[i]; if (p.Until > 0f) p.Until += offset; copy.Pings[i] = p; }
            for (int i = 0; i < copy.Flights.Count; i++) { OpsFlightRow f = copy.Flights[i]; if (f.EndsAt > 0f) f.EndsAt += offset; copy.Flights[i] = f; }
            State = copy; Known = true; Seq = d.Seq; Floor = d.Seq;
            return true;
        }

        /// <summary>The client's own view of a satellite: false while an ASAT strike has it dead. Unknown mirror = alive.</summary>
        public bool BirdUp(BirdKind bird) => !Known || !State.Active || (byte)bird >= SpaceRules.BirdCount || (State.BirdsDown & (1 << (byte)bird)) == 0;
    }

    /// <summary>Host side: per-member send gating (change-only, at most one message per 2 s, a fresh full on a new member or an explicit sync).</summary>
    internal sealed class OpsSubscriptions
    {
        public const int MaxSubscribers = SpaceContacts.MaxPlayers;
        public const float MinGapSeconds = 2f;

        private sealed class Sub { public int Faction = int.MinValue; public float NextAt; public OpsStateData Last; public bool Force = true; }
        private readonly Dictionary<ulong, Sub> subs = new Dictionary<ulong, Sub>();
        private readonly List<ulong> removed = new List<ulong>();
        private int seq;

        public int Count => subs.Count;

        public void Clear() { subs.Clear(); removed.Clear(); }

        public void Resync(ulong player) { if (subs.TryGetValue(player, out Sub s)) { s.Force = true; s.NextAt = 0f; } }

        public void Prune(HashSet<ulong> keep)
        {
            removed.Clear();
            foreach (var pair in subs) if (keep == null || !keep.Contains(pair.Key)) removed.Add(pair.Key);
            for (int i = 0; i < removed.Count; i++) subs.Remove(removed[i]);
            removed.Clear();
        }

        public OpsStateData Next(ulong player, int faction, OpsStateData current, float now, float wall)
        {
            if (player == 0 || current == null || !SpaceRules.MissionTime(now) || !SpaceRules.MissionTime(wall) || seq == int.MaxValue) return null;
            if (!subs.TryGetValue(player, out Sub s))
            {
                if (subs.Count >= MaxSubscribers) return null;
                subs[player] = s = new Sub();
            }
            if (s.Faction != faction) { s.Faction = faction; s.Force = true; s.Last = null; }
            if (!s.Force && wall < s.NextAt) return null;
            if (!s.Force && s.Last != null && s.Last.SameAs(current)) return null;
            current.Seq = ++seq;
            current.Now = now;
            s.Last = current.Clone();
            s.Force = false;
            s.NextAt = wall + MinGapSeconds;
            return current;
        }
    }

    internal enum OpsNoticeKind : byte { None, Execute, Broken, Done, Ping }

    internal readonly struct OpsNotice
    {
        public readonly OpsNoticeKind Kind;
        public readonly string Text;
        public OpsNotice(OpsNoticeKind kind, string text) { Kind = kind; Text = text ?? ""; }
        public static readonly OpsNotice None = new OpsNotice(OpsNoticeKind.None, "");
    }

    /// <summary>
    /// The pilot's OPERATIONS HUD notice, derived only from the mirror: an own EXECUTE (T-60), BROKEN or executed event, or an enemy ping (what the enemy is doing).
    /// One every 3 s, silent on first sight of a mirror, and QUIET drops them.
    /// </summary>
    internal sealed class OpsNoticeTracker
    {
        public const float GapSeconds = 3f;
        private int lastEvent = -1, lastPing = -1;
        private float nextAt;

        public void Reset() { lastEvent = lastPing = -1; nextAt = 0f; }

        public OpsNotice Observe(bool known, OpsStateData state, float now, bool quiet)
        {
            if (!known || state == null || !state.Active) { Reset(); return OpsNotice.None; }
            int newestEvent = 0, newestPing = 0;
            foreach (OpsEventRow e in state.Events) newestEvent = Math.Max(newestEvent, e.Seq);
            foreach (OpsPingRow p in state.Pings) newestPing = Math.Max(newestPing, p.Seq);
            if (lastEvent < 0) { lastEvent = newestEvent; lastPing = newestPing; return OpsNotice.None; }
            OpsNotice found = OpsNotice.None;
            foreach (OpsEventRow e in state.Events)
            {
                if (e.Seq <= lastEvent) continue;
                if (e.Kind == OpEventKind.Broken) found = new OpsNotice(OpsNoticeKind.Broken, OpsWords.Event(e.Kind, e.Op));
                else if (e.Kind == OpEventKind.Execute && found.Kind != OpsNoticeKind.Broken) found = new OpsNotice(OpsNoticeKind.Execute, OpsWords.Event(e.Kind, e.Op));
                else if (e.Kind == OpEventKind.Fired && found.Kind == OpsNoticeKind.None) found = new OpsNotice(OpsNoticeKind.Done, OpsWords.Event(e.Kind, e.Op));
            }
            if (found.Kind == OpsNoticeKind.None)
                foreach (OpsPingRow p in state.Pings)
                    if (p.Seq > lastPing) found = new OpsNotice(OpsNoticeKind.Ping, OpsWords.Ping(p.Kind, p.Phase, p.Name, p.Detail));
            lastEvent = Math.Max(lastEvent, newestEvent);
            lastPing = Math.Max(lastPing, newestPing);
            if (quiet || found.Kind == OpsNoticeKind.None || now < nextAt) return OpsNotice.None;
            nextAt = now + GapSeconds;
            return found;
        }
    }

    /// <summary>The words of the OPERATION box that need no engine.</summary>
    internal static class OpsPageWords
    {
        /// <summary>The line under the header: what the bar is doing in one phrase.</summary>
        public static string Line(in OpsRow r, float now)
        {
            if (r.Kind == OpKind.None || r.State == OpState.Idle) return "NO OPERATION RUNNING";
            string head = OpsWords.Short(r.Kind) + " · " + OpsWords.State(r.State, r.EndsAt - now);
            switch (r.State)
            {
                case OpState.Execute: return head + " · PROTECT THE " + OpsWords.Anchor(r.Kind);
                case OpState.Done:
                    return head + (r.EndsAt > now ? " · " + (r.Kind == OpKind.Asat ? "IN FLIGHT " : r.Kind == OpKind.Fob ? "FOB UP " : "SAMS DOWN ") + OpsWords.Clock(r.EndsAt - now) : "");
                case OpState.Broken: return head + " · BAR 50 % · FUND TO RESUME";
                default: return head + " · " + r.Percent + " % OF " + r.Goal + " CR" + (r.Paused ? " · PAUSED: " + OpsWords.Anchor(r.Kind) + " DOWN" : "");
            }
        }

        /// <summary>The box's detail line: the bar, the viewer's own share and the work share while funding; what to protect in the countdown; what is running after it.</summary>
        public static string Detail(in OpsRow r, float now)
        {
            if (r.Kind == OpKind.None || r.State == OpState.Idle) return "";
            switch (r.State)
            {
                case OpState.Execute:
                    return "PROTECT THE " + OpsWords.Anchor(r.Kind) + (r.Kind == OpKind.Asat ? " AND THE LAUNCHER" : "");
                case OpState.Done:
                    return r.EndsAt > now ? (r.Kind == OpKind.Asat ? "ASCENT " : r.Kind == OpKind.Fob ? "FOB UP " : "SAM NET DOWN ") + OpsWords.Clock(r.EndsAt - now) : "EXECUTED";
                default:
                    return r.Percent + " % OF " + r.Goal + " CR · YOURS " + r.MyCr + " CR" + (r.WorkPercent > 0 ? " · WORK " + r.WorkPercent + " %" : "") +
                        (r.State == OpState.Broken ? " · BROKEN, FUND TO RESUME" : r.Paused ? " · PAUSED: " + OpsWords.Anchor(r.Kind) + " DOWN" : "");
            }
        }

        /// <summary>The satellite state line (NET page): which birds are dead and how far their rebuild has come; empty when all are up.</summary>
        public static string Birds(OpsStateData s)
        {
            if (s == null || !s.Active || s.BirdsDown == 0) return "";
            string text = "";
            for (int i = 0; i < SpaceRules.BirdCount; i++)
                if ((s.BirdsDown & (1 << i)) != 0) text += (text.Length > 0 ? " · " : "") + OpsWords.Bird(i) + " LOST " + s.BirdPercent[i] + " %";
            return "SAT " + text;
        }
    }
}
