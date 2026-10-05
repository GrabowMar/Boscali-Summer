using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Domain.Sof
{
    internal struct SofCampRow
    {
        public AnchorHealth Health;
        public float X, Z;
        /// <summary>Restore fund bar 0..100 (0 while the camp stands).</summary>
        public byte Rebuild;
    }

    internal struct SofTeamRow
    {
        public byte Slot;
        public TeamState State;
        public Insertion Insert;
        public bool Push, Hold, Wounded, LiftWaiting, Carried, Lasing, HasDest, Exploit;
        public float X, Z, DestX, DestZ, TargetX, TargetZ;
        public byte Exposure, Ammo, Odds;
        public MissionKind Mission;
        public int TargetId;
        /// <summary>Host mission second at which the current phase ends (deploy, on-site end, recovery, lost-timer); 0 when none.</summary>
        public float EndsAt;
    }

    internal struct SofTargetRow
    {
        public int Id;
        public TargetKind Kind;
        public AnchorSub Sub;
        public float X, Z;
        public bool Exploit, Resisted;
    }

    internal struct SofHeldRow
    {
        public int Id;
        public float X, Z, Until;
    }

    internal struct SofEnemyRow
    {
        public float X, Z;
    }

    internal struct SofEventRow
    {
        public int Seq;
        public SofEventKind Kind;
        public byte Slot;
        public MissionKind Mission;
    }

    /// <summary>
    /// One faction's SOF state, always a full snapshot (never a delta). Members of the owning faction only. Own teams, own camps, revealed targets, held
    /// buildings and revealed enemy teams (a position only). Times are host mission seconds; on the wire an expiry is the quantized seconds left from <see cref="Now"/>.
    /// </summary>
    internal sealed class SofStateData
    {
        public byte Protocol;
        public bool Active;
        public int Seq;
        public float Now;
        public byte TeamCap, TapIntrusions;
        public float TapUntil;
        public readonly List<SofCampRow> Camps = new List<SofCampRow>();
        public readonly List<SofTeamRow> Teams = new List<SofTeamRow>();
        public readonly List<SofTargetRow> Targets = new List<SofTargetRow>();
        public readonly List<SofHeldRow> Held = new List<SofHeldRow>();
        public readonly List<SofEnemyRow> Enemies = new List<SofEnemyRow>();
        public readonly List<SofEventRow> Events = new List<SofEventRow>();

        public SofStateData Clone()
        {
            var c = new SofStateData { Protocol = Protocol, Active = Active, Seq = Seq, Now = Now, TeamCap = TeamCap, TapIntrusions = TapIntrusions, TapUntil = TapUntil };
            c.Camps.AddRange(Camps); c.Teams.AddRange(Teams); c.Targets.AddRange(Targets); c.Held.AddRange(Held); c.Enemies.AddRange(Enemies); c.Events.AddRange(Events);
            return c;
        }

        /// <summary>The fields that decide whether anything changed since the last send (everything but Seq and Now).</summary>
        public bool SameAs(SofStateData o)
        {
            if (o == null || Active != o.Active || TeamCap != o.TeamCap || TapIntrusions != o.TapIntrusions || !SpaceMirror.SameExpiry(TapUntil, o.TapUntil) ||
                Camps.Count != o.Camps.Count || Teams.Count != o.Teams.Count || Targets.Count != o.Targets.Count || Held.Count != o.Held.Count ||
                Enemies.Count != o.Enemies.Count || Events.Count != o.Events.Count) return false;
            for (int i = 0; i < Camps.Count; i++)
            {
                SofCampRow a = Camps[i], b = o.Camps[i];
                if (a.Health != b.Health || a.Rebuild != b.Rebuild || !SpaceMirror.SamePoint(a.X, b.X) || !SpaceMirror.SamePoint(a.Z, b.Z)) return false;
            }
            for (int i = 0; i < Teams.Count; i++)
            {
                SofTeamRow a = Teams[i], b = o.Teams[i];
                if (a.Slot != b.Slot || a.State != b.State || a.Insert != b.Insert || a.Push != b.Push || a.Hold != b.Hold || a.Wounded != b.Wounded || a.LiftWaiting != b.LiftWaiting ||
                    a.Carried != b.Carried || a.Lasing != b.Lasing || a.HasDest != b.HasDest || a.Exploit != b.Exploit || a.Exposure != b.Exposure || a.Ammo != b.Ammo || a.Odds != b.Odds ||
                    a.Mission != b.Mission || a.TargetId != b.TargetId || !SpaceMirror.SamePoint(a.X, b.X) || !SpaceMirror.SamePoint(a.Z, b.Z) ||
                    !SpaceMirror.SamePoint(a.DestX, b.DestX) || !SpaceMirror.SamePoint(a.DestZ, b.DestZ) || !SpaceMirror.SamePoint(a.TargetX, b.TargetX) ||
                    !SpaceMirror.SamePoint(a.TargetZ, b.TargetZ) || !SpaceMirror.SameExpiry(a.EndsAt, b.EndsAt)) return false;
            }
            for (int i = 0; i < Targets.Count; i++)
            {
                SofTargetRow a = Targets[i], b = o.Targets[i];
                if (a.Id != b.Id || a.Kind != b.Kind || a.Sub != b.Sub || a.Exploit != b.Exploit || a.Resisted != b.Resisted || !SpaceMirror.SamePoint(a.X, b.X) || !SpaceMirror.SamePoint(a.Z, b.Z)) return false;
            }
            for (int i = 0; i < Held.Count; i++)
            {
                SofHeldRow a = Held[i], b = o.Held[i];
                if (a.Id != b.Id || !SpaceMirror.SamePoint(a.X, b.X) || !SpaceMirror.SamePoint(a.Z, b.Z) || !SpaceMirror.SameExpiry(a.Until, b.Until)) return false;
            }
            for (int i = 0; i < Enemies.Count; i++)
                if (!SpaceMirror.SamePoint(Enemies[i].X, o.Enemies[i].X) || !SpaceMirror.SamePoint(Enemies[i].Z, o.Enemies[i].Z)) return false;
            for (int i = 0; i < Events.Count; i++) if (Events[i].Seq != o.Events[i].Seq) return false;
            return true;
        }
    }

    /// <summary>Engine-free SOF state codec (protocol 34). Every reader returns an inert value (Protocol 0 or the foreign byte alone) instead of throwing.</summary>
    internal static class SofWire
    {
        public const int MaxCamps = CampRules.MaxCamps, MaxTeams = SofRules.MaxTeams, MaxTargets = SofDesk.MaxVisible, MaxHeld = SofRules.HeldCap, MaxEnemies = 4, MaxEvents = 3;
        private const int MinCampBytes = 8, MinTeamBytes = 27, MinTargetBytes = 8, MinHeldBytes = 9, MinEnemyBytes = 6, MinEventBytes = 3;
        private const byte FlagActive = 1;

        public static void WriteState(ISpaceWriter w, SofStateData s)
        {
            w.WriteByte(s.Protocol);
            w.WriteByte(s.Active ? FlagActive : (byte)0);
            SpaceWire.WriteVar(w, (uint)Math.Max(0, s.Seq));
            SpaceWire.WriteFloat(w, s.Now);
            if (!s.Active) return;
            w.WriteByte(s.TeamCap);
            w.WriteByte(s.TapIntrusions);
            SpaceWire.WriteExpiry(w, s.TapUntil, s.Now);
            int n = Math.Min(s.Camps.Count, MaxCamps);
            w.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                SofCampRow c = s.Camps[i];
                w.WriteByte((byte)c.Health);
                SpaceWire.WriteCoordinate(w, c.X); SpaceWire.WriteCoordinate(w, c.Z);
                w.WriteByte(c.Rebuild);
            }
            n = Math.Min(s.Teams.Count, MaxTeams);
            w.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                SofTeamRow t = s.Teams[i];
                w.WriteByte((byte)(t.Slot | ((int)t.State << 2)));
                w.WriteByte((byte)((int)t.Insert | (t.Push ? 2 : 0) | (t.Hold ? 4 : 0) | (t.Wounded ? 8 : 0) | (t.LiftWaiting ? 16 : 0) | (t.Carried ? 32 : 0) | (t.Lasing ? 64 : 0) | (t.HasDest ? 128 : 0)));
                w.WriteByte((byte)((int)t.Mission | (t.Exploit ? 8 : 0)));
                SpaceWire.WriteCoordinate(w, t.X); SpaceWire.WriteCoordinate(w, t.Z);
                SpaceWire.WriteCoordinate(w, t.DestX); SpaceWire.WriteCoordinate(w, t.DestZ);
                SpaceWire.WriteCoordinate(w, t.TargetX); SpaceWire.WriteCoordinate(w, t.TargetZ);
                w.WriteByte(t.Exposure); w.WriteByte(t.Ammo); w.WriteByte(t.Odds);
                SpaceWire.WriteVar(w, (uint)Math.Max(0, t.TargetId));
                SpaceWire.WriteExpiry(w, t.EndsAt, s.Now);
            }
            n = Math.Min(s.Targets.Count, MaxTargets);
            w.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                SofTargetRow r = s.Targets[i];
                SpaceWire.WriteVar(w, (uint)Math.Max(0, r.Id));
                w.WriteByte((byte)((int)r.Kind | ((int)r.Sub << 2) | (r.Exploit ? 16 : 0) | (r.Resisted ? 32 : 0)));
                SpaceWire.WriteCoordinate(w, r.X); SpaceWire.WriteCoordinate(w, r.Z);
            }
            n = Math.Min(s.Held.Count, MaxHeld);
            w.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                SofHeldRow h = s.Held[i];
                SpaceWire.WriteVar(w, (uint)Math.Max(0, h.Id));
                SpaceWire.WriteCoordinate(w, h.X); SpaceWire.WriteCoordinate(w, h.Z);
                SpaceWire.WriteExpiry(w, h.Until, s.Now);
            }
            n = Math.Min(s.Enemies.Count, MaxEnemies);
            w.WriteByte((byte)n);
            for (int i = 0; i < n; i++) { SpaceWire.WriteCoordinate(w, s.Enemies[i].X); SpaceWire.WriteCoordinate(w, s.Enemies[i].Z); }
            n = Math.Min(s.Events.Count, MaxEvents);
            w.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                SofEventRow e = s.Events[i];
                SpaceWire.WriteVar(w, (uint)Math.Max(0, e.Seq));
                w.WriteByte((byte)((int)e.Kind | ((int)e.Mission << 4)));
                w.WriteByte(e.Slot);
            }
        }

        public static SofStateData ReadState(ISpaceReader r, byte protocol)
        {
            if (!r.TryReadByte(out byte version)) return Bad();
            if (version != protocol) return new SofStateData { Protocol = version };
            var s = new SofStateData { Protocol = version };
            if (!r.TryReadByte(out byte flags) || (flags & ~FlagActive) != 0 || !SpaceWire.ReadInt(r, out int seq) || !SpaceWire.ReadFloat(r, out float now) || !SpaceRules.MissionTime(now)) return Bad();
            s.Active = (flags & FlagActive) != 0; s.Seq = seq; s.Now = now;
            if (!s.Active) return s;
            if (!r.TryReadByte(out s.TeamCap) || s.TeamCap > MaxTeams || !r.TryReadByte(out s.TapIntrusions) || !SpaceWire.ReadExpiry(r, now, out float tap) ||
                !r.TryReadByte(out byte n) || n > MaxCamps || r.Remaining < n * MinCampBytes) return Bad();
            s.TapUntil = tap <= now ? 0f : tap;
            for (int i = 0; i < n; i++)
            {
                if (!r.TryReadByte(out byte health) || health > (byte)AnchorHealth.Down || !SpaceWire.ReadCoordinate(r, out float x) || !SpaceWire.ReadCoordinate(r, out float z) ||
                    !r.TryReadByte(out byte rebuild) || rebuild > 100) return Bad();
                s.Camps.Add(new SofCampRow { Health = (AnchorHealth)health, X = x, Z = z, Rebuild = rebuild });
            }
            if (!r.TryReadByte(out n) || n > MaxTeams || r.Remaining < n * MinTeamBytes) return Bad();
            for (int i = 0; i < n; i++)
            {
                if (!r.TryReadByte(out byte a) || (a & 3) >= MaxTeams || (a >> 2) > (byte)TeamState.Lost || !r.TryReadByte(out byte b) || !r.TryReadByte(out byte c) || (c & 7) > (byte)MissionKind.Tap || (c & ~15) != 0 ||
                    !SpaceWire.ReadCoordinate(r, out float x) || !SpaceWire.ReadCoordinate(r, out float z) || !SpaceWire.ReadCoordinate(r, out float dx) || !SpaceWire.ReadCoordinate(r, out float dz) ||
                    !SpaceWire.ReadCoordinate(r, out float tx) || !SpaceWire.ReadCoordinate(r, out float tz) || !r.TryReadByte(out byte exposure) || exposure > 100 ||
                    !r.TryReadByte(out byte ammo) || ammo > 100 || !r.TryReadByte(out byte odds) || odds > 100 || !SpaceWire.ReadInt(r, out int target) || !SpaceWire.ReadExpiry(r, now, out float ends)) return Bad();
                s.Teams.Add(new SofTeamRow
                {
                    Slot = (byte)(a & 3), State = (TeamState)(a >> 2), Insert = (Insertion)(b & 1), Push = (b & 2) != 0, Hold = (b & 4) != 0, Wounded = (b & 8) != 0, LiftWaiting = (b & 16) != 0,
                    Carried = (b & 32) != 0, Lasing = (b & 64) != 0, HasDest = (b & 128) != 0, Mission = (MissionKind)(c & 7), Exploit = (c & 8) != 0, X = x, Z = z, DestX = dx, DestZ = dz,
                    TargetX = tx, TargetZ = tz, Exposure = exposure, Ammo = ammo, Odds = odds, TargetId = target, EndsAt = ends <= now ? 0f : ends
                });
            }
            if (!r.TryReadByte(out n) || n > MaxTargets || r.Remaining < n * MinTargetBytes) return Bad();
            for (int i = 0; i < n; i++)
            {
                if (!SpaceWire.ReadInt(r, out int id) || id <= 0 || !r.TryReadByte(out byte bits) || (bits & ~63) != 0 || (bits & 3) > (int)TargetKind.Relay || ((bits >> 2) & 3) > (int)AnchorSub.Camp ||
                    !SpaceWire.ReadCoordinate(r, out float x) || !SpaceWire.ReadCoordinate(r, out float z)) return Bad();
                s.Targets.Add(new SofTargetRow { Id = id, Kind = (TargetKind)(bits & 3), Sub = (AnchorSub)((bits >> 2) & 3), Exploit = (bits & 16) != 0, Resisted = (bits & 32) != 0, X = x, Z = z });
            }
            if (!r.TryReadByte(out n) || n > MaxHeld || r.Remaining < n * MinHeldBytes) return Bad();
            for (int i = 0; i < n; i++)
            {
                if (!SpaceWire.ReadInt(r, out int id) || id <= 0 || !SpaceWire.ReadCoordinate(r, out float x) || !SpaceWire.ReadCoordinate(r, out float z) || !SpaceWire.ReadExpiry(r, now, out float until)) return Bad();
                s.Held.Add(new SofHeldRow { Id = id, X = x, Z = z, Until = until <= now ? 0f : until });
            }
            if (!r.TryReadByte(out n) || n > MaxEnemies || r.Remaining < n * MinEnemyBytes) return Bad();
            for (int i = 0; i < n; i++)
            {
                if (!SpaceWire.ReadCoordinate(r, out float x) || !SpaceWire.ReadCoordinate(r, out float z)) return Bad();
                s.Enemies.Add(new SofEnemyRow { X = x, Z = z });
            }
            if (!r.TryReadByte(out n) || n > MaxEvents || r.Remaining < n * MinEventBytes) return Bad();
            for (int i = 0; i < n; i++)
            {
                if (!SpaceWire.ReadInt(r, out int seq2) || !r.TryReadByte(out byte bits) || (bits & 15) < (int)SofEventKind.Raised || (bits & 15) > (int)SofEventKind.Retaken || (bits >> 4) > (int)MissionKind.Tap ||
                    !r.TryReadByte(out byte slot) || slot >= MaxTeams) return Bad();
                s.Events.Add(new SofEventRow { Seq = seq2, Kind = (SofEventKind)(bits & 15), Mission = (MissionKind)(bits >> 4), Slot = slot });
            }
            return s;
        }

        private static SofStateData Bad() => new SofStateData();

        private sealed class Counter : ISpaceWriter
        {
            public int Bytes;
            public void WriteByte(byte value) => Bytes++;
        }

        public static int StateSize(SofStateData s)
        {
            var c = new Counter();
            WriteState(c, s);
            return c.Bytes;
        }
    }

    /// <summary>The client's copy of its own faction's SOF state. Full snapshots only: anything not newer than the last one on this link is dropped.</summary>
    internal sealed class SofMirror
    {
        public SofStateData State { get; private set; } = new SofStateData();
        public bool Known { get; private set; }
        public int Seq { get; private set; }
        public int Floor { get; private set; }

        public void Reset() { State = new SofStateData(); Known = false; Seq = 0; }

        public void ResetLink() { Reset(); Floor = 0; }

        public bool Apply(SofStateData d, byte protocol, float clientNow)
        {
            if (d == null || d.Protocol != protocol || d.Seq <= 0 || d.Seq <= Floor || !SpaceRules.MissionTime(clientNow)) return false;
            if (d.Camps.Count > SofWire.MaxCamps || d.Teams.Count > SofWire.MaxTeams || d.Targets.Count > SofWire.MaxTargets || d.Held.Count > SofWire.MaxHeld ||
                d.Enemies.Count > SofWire.MaxEnemies || d.Events.Count > SofWire.MaxEvents) return false;
            float offset = clientNow - d.Now;
            SofStateData copy = d.Clone();
            copy.Now = clientNow;
            if (copy.TapUntil > 0f) copy.TapUntil += offset;
            for (int i = 0; i < copy.Teams.Count; i++) { SofTeamRow t = copy.Teams[i]; if (t.EndsAt > 0f) t.EndsAt += offset; copy.Teams[i] = t; }
            for (int i = 0; i < copy.Held.Count; i++) { SofHeldRow h = copy.Held[i]; if (h.Until > 0f) h.Until += offset; copy.Held[i] = h; }
            State = copy; Known = true; Seq = d.Seq; Floor = d.Seq;
            return true;
        }

        /// <summary>The point a lasing team holds (the AIM: TEAM source for any CALL). False when no team lases.</summary>
        public bool TryLase(out string callsign, out float x, out float z)
        {
            callsign = ""; x = z = 0f;
            if (!Known || !State.Active) return false;
            for (int i = 0; i < State.Teams.Count; i++)
                if (State.Teams[i].Lasing && State.Teams[i].State == TeamState.OnSite) { callsign = SofRules.Callsign(State.Teams[i].Slot); x = State.Teams[i].TargetX; z = State.Teams[i].TargetZ; return true; }
            return false;
        }
    }

    /// <summary>Host side: per-member send gating (change-only, at most one message per 2 s, a fresh full on a new member or an explicit sync).</summary>
    internal sealed class SofSubscriptions
    {
        public const int MaxSubscribers = SpaceContacts.MaxPlayers;
        public const float MinGapSeconds = 2f;

        private sealed class Sub { public int Faction = int.MinValue; public float NextAt; public SofStateData Last; public bool Force = true; }
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

        public SofStateData Next(ulong player, int faction, SofStateData current, float now, float wall)
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

    internal enum SofNoticeKind : byte { None, Pinned, Lost, Success }

    /// <summary>The pilot's SOF HUD notice, derived only from the faction mirror: TEAM PINNED, TEAM LOST and mission success events, one every 3 s, silent on first sight of a mirror.</summary>
    internal sealed class SofNoticeTracker
    {
        public const float GapSeconds = 3f;
        private int lastSeq = -1;
        private float nextAt;

        public void Reset() { lastSeq = -1; nextAt = 0f; }

        public SofNoticeKind Observe(bool known, SofStateData state, float now, bool quiet)
        {
            if (!known || state == null) { Reset(); return SofNoticeKind.None; }
            int newest = 0;
            foreach (SofEventRow e in state.Events) newest = Math.Max(newest, e.Seq);
            if (lastSeq < 0) { lastSeq = newest; return SofNoticeKind.None; }
            SofNoticeKind found = SofNoticeKind.None;
            foreach (SofEventRow e in state.Events)
            {
                if (e.Seq <= lastSeq) continue;
                if (e.Kind == SofEventKind.Lost) found = SofNoticeKind.Lost;
                else if (e.Kind == SofEventKind.Pinned && found != SofNoticeKind.Lost) found = SofNoticeKind.Pinned;
                else if (e.Kind == SofEventKind.Success && found == SofNoticeKind.None) found = SofNoticeKind.Success;
            }
            lastSeq = Math.Max(lastSeq, newest);
            if (quiet || found == SofNoticeKind.None || now < nextAt) return SofNoticeKind.None;
            nextAt = now + GapSeconds;
            return found;
        }
    }

    /// <summary>The page words that need no engine.</summary>
    internal static class SofPageWords
    {
        public static string Sub(SofStateData s)
        {
            if (s == null || !s.Active) return "NO CAMP STANDING";
            int live = 0, teams = 0;
            foreach (SofCampRow c in s.Camps) if (c.Health != AnchorHealth.Down) live++;
            foreach (SofTeamRow t in s.Teams) if (t.State != TeamState.Lost) teams++;
            return live + " CAMP" + (live == 1 ? "" : "S") + " · " + teams + "/" + s.TeamCap + " TEAMS · " + s.Held.Count + " HELD";
        }

        public static string EventLine(in SofEventRow e)
        {
            string team = SofRules.Callsign(e.Slot);
            switch (e.Kind)
            {
                case SofEventKind.Raised: return team + " DEPLOYED AT THE CAMP";
                case SofEventKind.Departed: return team + " MOVING OUT";
                case SofEventKind.OnSite: return team + " ON SITE · " + SofWords.Kind(e.Mission);
                case SofEventKind.Pinned: return team + " PINNED · NEEDS COVER";
                case SofEventKind.Unpinned: return team + " BROKE CONTACT";
                case SofEventKind.Lost: return team + " LOST";
                case SofEventKind.Success: return team + " " + SofWords.Kind(e.Mission) + " COMPLETE";
                case SofEventKind.Failed: return team + " " + SofWords.Kind(e.Mission) + " FAILED · TEAM RETURNING";
                case SofEventKind.Home: return team + " BACK AT BASE";
                case SofEventKind.LiftUp: return team + " BOARDED A HELICOPTER";
                case SofEventKind.LiftDown: return team + " LANDED";
                case SofEventKind.Seized: return "BUILDING SEIZED · HELD 10:00";
                default: return "HELD BUILDING LOST";
            }
        }

        /// <summary>The 3-line teams box row: <c>A-1 · MOVING · ETA 2:14 · EXP 34 % · ODDS 62 %</c>.</summary>
        public static string TeamLine(in SofTeamRow t, float now)
        {
            string head = SofRules.Callsign(t.Slot) + " · " + SofWords.State(t.State);
            switch (t.State)
            {
                case TeamState.Raising: return head + " · " + SofWords.Clock(t.EndsAt - now);
                case TeamState.Recovering: return head + " · " + SofWords.Clock(t.EndsAt - now);
                case TeamState.Lost: return head;
                case TeamState.Pinned: return head + " · LOST IN " + SofWords.Clock(t.EndsAt - now) + " · EXP " + t.Exposure + " %";
                case TeamState.OnSite: return head + " · " + SofWords.Kind(t.Mission) + (t.Mission == MissionKind.Lase ? " · HOLDING" : " · " + SofWords.Clock(t.EndsAt - now)) + " · EXP " + t.Exposure + " %";
                case TeamState.Moving:
                case TeamState.Returning:
                    float d = SofRules.Distance(t.X, t.Z, t.DestX, t.DestZ);
                    float speed = SofRules.SpeedMetresPerSecond * (t.Push ? SofRules.PushSpeedFactor : 1f);
                    return head + (t.Hold ? " · HOLD" : t.Push ? " · PUSH" : "") + (t.Carried ? " · HELO" : " · ETA " + SofWords.Clock(d / speed)) + " · EXP " + t.Exposure + " %";
                default:
                    return head + (t.Carried ? " · IN HELO" : t.LiftWaiting ? " · LIFT REQUESTED" : "") + " · AMMO " + t.Ammo + " · EXP " + t.Exposure + " %";
            }
        }
    }
}
