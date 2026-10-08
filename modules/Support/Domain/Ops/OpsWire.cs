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
    internal sealed class OpsStateData : FactionStateData<OpsStateData>
    {
        public bool Active, CyberOps, SofOps;
        /// <summary>Bit i set while the faction's satellite i (OPTICAL, RADAR, KINETIC) is dead; <see cref="BirdPercent"/> is its rebuild progress.</summary>
        public byte BirdsDown;
        /// <summary>Same bits for the strongest enemy's constellation (alive or dead is public: its launches are visible); rides in the BirdsDown byte's bits 3..5.</summary>
        public byte EnemyBirdsDown;
        public readonly byte[] BirdPercent = new byte[SpaceRules.BirdCount];
        /// <summary>Each satellite's geostationary state: indices 0..2 the viewer's own birds, 3..5 the strongest enemy's (public: a geostationary bird is visible to all).</summary>
        public readonly GeoBird[] Geo = new GeoBird[OpsWire.GeoCount];
        public readonly List<OpsRow> Rows = new List<OpsRow>();
        public readonly List<OpsPingRow> Pings = new List<OpsPingRow>();
        public readonly List<OpsEventRow> Events = new List<OpsEventRow>();
        public readonly List<OpsFlightRow> Flights = new List<OpsFlightRow>();
        /// <summary>The last OVERLORD actions of the viewer's own faction (a reason code and two arguments each): the console reads them. Never another faction's.</summary>
        public readonly List<WatchLogRow> Log = new List<WatchLogRow>();

        public override OpsStateData Clone()
        {
            var c = new OpsStateData { Protocol = Protocol, Active = Active, CyberOps = CyberOps, SofOps = SofOps, Seq = Seq, Now = Now, BirdsDown = BirdsDown, EnemyBirdsDown = EnemyBirdsDown };
            Array.Copy(BirdPercent, c.BirdPercent, BirdPercent.Length);
            Array.Copy(Geo, c.Geo, Geo.Length);
            c.Rows.AddRange(Rows); c.Pings.AddRange(Pings); c.Events.AddRange(Events); c.Flights.AddRange(Flights); c.Log.AddRange(Log);
            return c;
        }

        public bool TryRow(OpDomain domain, out OpsRow row)
        {
            for (int i = 0; i < Rows.Count; i++) if (Rows[i].Domain == domain) { row = Rows[i]; return true; }
            row = default; return false;
        }

        /// <summary>The fields that decide whether anything changed since the last send (everything but Seq and Now).</summary>
        public override bool SameAs(OpsStateData o)
        {
            if (o == null || Active != o.Active || CyberOps != o.CyberOps || SofOps != o.SofOps || BirdsDown != o.BirdsDown || EnemyBirdsDown != o.EnemyBirdsDown ||
                Rows.Count != o.Rows.Count || Pings.Count != o.Pings.Count || Events.Count != o.Events.Count || Flights.Count != o.Flights.Count || Log.Count != o.Log.Count) return false;
            for (int i = 0; i < BirdPercent.Length; i++) if (BirdPercent[i] != o.BirdPercent[i]) return false;
            for (int i = 0; i < Geo.Length; i++) if (!OpsWire.SameGeo(Geo[i], o.Geo[i])) return false;
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
            for (int i = 0; i < Log.Count; i++) if (Log[i].Seq != o.Log[i].Seq) return false;
            for (int i = 0; i < Flights.Count; i++)
                if (Flights[i].Id != o.Flights[i].Id || !SpaceMirror.SamePoint(Flights[i].X, o.Flights[i].X) || !SpaceMirror.SamePoint(Flights[i].Z, o.Flights[i].Z) ||
                    !SpaceMirror.SameExpiry(Flights[i].EndsAt, o.Flights[i].EndsAt)) return false;
            return true;
        }
    }

    /// <summary>Engine-free OPERATIONS state codec (protocol 36). Every reader returns an inert value (Protocol 0 or the foreign byte alone) instead of throwing.</summary>
    internal static class OpsWire
    {
        public const int MaxRows = OpsDesk.Slots, MaxPings = 4, MaxEvents = 8, MaxFlights = 1, MaxName = 12, MaxLog = WatchLogRing.Capacity;
        private const int MinRowBytes = 10, MinPingBytes = 5, MinEventBytes = 2, MinFlightBytes = 8, MinLogBytes = 4;
        private const byte FlagActive = 1, FlagCyber = 2, FlagSof = 4;
        public const int GeoCount = SpaceRules.BirdCount * 2;
        private const int GeoBytes = 11;

        private static int Cell(float x) => (int)Math.Round(Math.Max(0f, Math.Min(1f, x)) * 65535f);
        private static void Put16(ISpaceWriter w, int v) { w.WriteByte((byte)v); w.WriteByte((byte)(v >> 8)); }

        /// <summary>The same wire image: positions to 1/65535, the burn start to the second, the fuel to a percent.</summary>
        public static bool SameGeo(in GeoBird a, in GeoBird b) =>
            Cell(a.FromU) == Cell(b.FromU) && Cell(a.FromV) == Cell(b.FromV) && Cell(a.ToU) == Cell(b.ToU) && Cell(a.ToV) == Cell(b.ToV) &&
            Math.Round(a.Fuel) == Math.Round(b.Fuel) && (a.Length <= 0f || SpaceMirror.SameExpiry(a.DepartAt, b.DepartAt));

        private static void WriteGeo(ISpaceWriter w, in GeoBird g, float now)
        {
            Put16(w, Cell(g.FromU)); Put16(w, Cell(g.FromV)); Put16(w, Cell(g.ToU)); Put16(w, Cell(g.ToV));
            Put16(w, g.Length > 0f ? (int)Math.Max(0d, Math.Min(ushort.MaxValue, Math.Round(now - g.DepartAt))) : 0); // seconds since the burn began
            w.WriteByte((byte)Math.Max(0, Math.Min(100, (int)Math.Round(g.Fuel))));
        }

        private static bool ReadGeo(ISpaceReader r, float now, out GeoBird g)
        {
            g = default;
            var v = new int[5];
            for (int i = 0; i < 5; i++) { if (!r.TryReadByte(out byte lo) || !r.TryReadByte(out byte hi)) return false; v[i] = lo | (hi << 8); }
            if (!r.TryReadByte(out byte fuel) || fuel > 100) return false;
            g = new GeoBird(v[0] / 65535f, v[1] / 65535f, v[2] / 65535f, v[3] / 65535f, now - v[4], fuel);
            return true;
        }

        public static void WriteState(ISpaceWriter w, OpsStateData s)
        {
            w.WriteByte(s.Protocol);
            w.WriteByte((byte)((s.Active ? FlagActive : 0) | (s.CyberOps ? FlagCyber : 0) | (s.SofOps ? FlagSof : 0)));
            SpaceWire.WriteVar(w, (uint)Math.Max(0, s.Seq));
            SpaceWire.WriteFloat(w, s.Now);
            if (!s.Active) { WriteLog(w, s); return; } // OVERLORD's log still reaches the faction console when OPERATIONS is off
            w.WriteByte((byte)((s.BirdsDown & 7) | ((s.EnemyBirdsDown & 7) << 3)));
            for (int i = 0; i < s.BirdPercent.Length; i++) w.WriteByte(s.BirdPercent[i]);
            for (int i = 0; i < GeoCount; i++) WriteGeo(w, s.Geo[i], s.Now);
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
            WriteLog(w, s);
        }

        private static void WriteLog(ISpaceWriter w, OpsStateData s)
        {
            int n = Math.Min(s.Log.Count, MaxLog);
            w.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                WatchLogRow l = s.Log[i];
                SpaceWire.WriteVar(w, (uint)Math.Max(0, l.Seq));
                w.WriteByte((byte)((int)l.Domain | ((int)l.Code << 2)));
                w.WriteByte(l.A); w.WriteByte(l.B);
            }
        }

        private static bool ReadLog(ISpaceReader r, OpsStateData s)
        {
            if (!r.TryReadByte(out byte n) || n > MaxLog || r.Remaining < n * MinLogBytes) return false;
            for (int i = 0; i < n; i++)
            {
                if (!SpaceWire.ReadInt(r, out int lseq) || lseq <= 0 || !r.TryReadByte(out byte bits) || (bits & 3) > (int)WatchDomain.Ops ||
                    (bits >> 2) < (int)WatchCode.CyberHop || (bits >> 2) > (int)WatchCode.OpFund || !r.TryReadByte(out byte a) || !r.TryReadByte(out byte b)) return false;
                s.Log.Add(new WatchLogRow { Seq = lseq, Domain = (WatchDomain)(bits & 3), Code = (WatchCode)(bits >> 2), A = a, B = b });
            }
            return true;
        }

        public static OpsStateData ReadState(ISpaceReader r, byte protocol)
        {
            if (!r.TryReadByte(out byte version)) return Bad();
            if (version != protocol) return new OpsStateData { Protocol = version };
            var s = new OpsStateData { Protocol = version };
            if (!r.TryReadByte(out byte flags) || (flags & ~(FlagActive | FlagCyber | FlagSof)) != 0 || !SpaceWire.ReadInt(r, out int seq) ||
                !SpaceWire.ReadFloat(r, out float now) || !SpaceRules.MissionTime(now)) return Bad();
            s.Active = (flags & FlagActive) != 0; s.CyberOps = (flags & FlagCyber) != 0; s.SofOps = (flags & FlagSof) != 0; s.Seq = seq; s.Now = now;
            if (!s.Active) return ReadLog(r, s) ? s : Bad();
            if (!r.TryReadByte(out byte birds) || (birds & ~63) != 0) return Bad();
            s.BirdsDown = (byte)(birds & 7); s.EnemyBirdsDown = (byte)(birds >> 3);
            for (int i = 0; i < s.BirdPercent.Length; i++) if (!r.TryReadByte(out s.BirdPercent[i]) || s.BirdPercent[i] > 100) return Bad();
            if (r.Remaining < GeoCount * GeoBytes) return Bad();
            for (int i = 0; i < GeoCount; i++) if (!ReadGeo(r, now, out s.Geo[i])) return Bad();
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
            return ReadLog(r, s) ? s : Bad();
        }

        private static OpsStateData Bad() => new OpsStateData();

        public static int StateSize(OpsStateData s)
        {
            var c = new ByteCounter();
            WriteState(c, s);
            return c.Bytes;
        }
    }

    /// <summary>The client's copy of its own faction's OPERATIONS state (see <see cref="FactionMirror{T}"/>).</summary>
    internal sealed class OpsMirror : FactionMirror<OpsStateData>
    {
        protected override bool Bounds(OpsStateData d) =>
            d.Rows.Count <= OpsWire.MaxRows && d.Pings.Count <= OpsWire.MaxPings && d.Events.Count <= OpsWire.MaxEvents && d.Flights.Count <= OpsWire.MaxFlights && d.Log.Count <= OpsWire.MaxLog;

        protected override OpsStateData Shift(OpsStateData d, float offset, float clientNow)
        {
            OpsStateData copy = d.Clone();
            copy.Now = clientNow;
            for (int i = 0; i < copy.Rows.Count; i++) { OpsRow r = copy.Rows[i]; if (r.EndsAt > 0f) r.EndsAt += offset; copy.Rows[i] = r; }
            for (int i = 0; i < copy.Pings.Count; i++) { OpsPingRow p = copy.Pings[i]; if (p.Until > 0f) p.Until += offset; copy.Pings[i] = p; }
            for (int i = 0; i < copy.Flights.Count; i++) { OpsFlightRow f = copy.Flights[i]; if (f.EndsAt > 0f) f.EndsAt += offset; copy.Flights[i] = f; }
            for (int i = 0; i < copy.Geo.Length; i++) { GeoBird g = copy.Geo[i]; copy.Geo[i] = new GeoBird(g.FromU, g.FromV, g.ToU, g.ToV, g.DepartAt + offset, g.Fuel); }
            return copy;
        }

        /// <summary>The client's own view of a satellite: false while an ASAT strike has it dead. Unknown mirror = alive.</summary>
        public bool BirdUp(BirdKind bird) => !Known || !State.Active || (byte)bird >= SpaceRules.BirdCount || (State.BirdsDown & (1 << (byte)bird)) == 0;
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
    internal sealed class OpsNoticeTracker : NoticeTrackerBase
    {
        private int lastEvent = -1, lastPing = -1;

        public void Reset() { lastEvent = lastPing = -1; ResetGate(); }

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
            return Admit(found.Kind != OpsNoticeKind.None, now, quiet) ? found : OpsNotice.None;
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
                    return head + (r.EndsAt > now ? " · " + (r.Kind == OpKind.Asat ? "IN FLIGHT " : r.Kind == OpKind.Fob ? "FOB UP " : "SAMS DOWN ") + SpaceRules.Clock(r.EndsAt - now) : "");
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
                    return r.EndsAt > now ? (r.Kind == OpKind.Asat ? "ASCENT " : r.Kind == OpKind.Fob ? "FOB UP " : "SAM NET DOWN ") + SpaceRules.Clock(r.EndsAt - now) : "EXECUTED";
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
