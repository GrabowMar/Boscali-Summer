using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    /// <summary>The weight class of a revealed ground contact for ranking: the host maps a native definition onto one of these.</summary>
    internal enum WatchKind : byte { Other, AirDefence, Armour }

    internal enum WatchAction : byte { None, Scan, Post }

    /// <summary>Why OVERLORD did nothing on a think (for the transcript and for the pilot-facing words). None when it acted.</summary>
    internal enum WatchWhy : byte
    {
        None, NotDue, Suspended, NoUplink, Waiting, BoardFull, OverlordCap, MarkRoom, MarkRate, RodNotReady, ScanNotReady, NoSite
    }

    internal enum WatchScan : byte { Radar, Optical }

    /// <summary>One revealed enemy ground contact OVERLORD may MARK: exactly what the host admitted to the faction, nothing else.</summary>
    internal readonly struct WatchTarget
    {
        public readonly int Id;
        public readonly float X, Z, Value, ExpiresAt, FrontMeters;
        public readonly WatchKind Kind;
        public readonly bool Moving, Strategic;

        /// <param name="value">The vanilla unit definition's value (the same figure kill pay uses).</param>
        /// <param name="frontMeters">Distance to the nearest friendly-held objective; negative when unknown.</param>
        public WatchTarget(int id, float x, float z, float value, WatchKind kind, bool moving, bool strategic, float expiresAt, float frontMeters)
        {
            Id = id; X = x; Z = z; Value = value; Kind = kind; Moving = moving; Strategic = strategic; ExpiresAt = expiresAt; FrontMeters = frontMeters;
        }
    }

    /// <summary>A public map objective OVERLORD may point a scan at when it has no contact: never an enemy unit position.</summary>
    internal readonly struct WatchSite
    {
        public readonly int Key;
        public readonly float X, Z;
        /// <summary>The OPTICAL bird could image this very site right now: daylight and a sky clear enough (the host samples it per site).</summary>
        public readonly bool OpticalOk;
        public WatchSite(int key, float x, float z, bool opticalOk = false) { Key = key; X = x; Z = z; OpticalOk = opticalOk; }
    }

    /// <summary>The host facts one think needs. Times are mission seconds; a ready-in of zero or less is ready now.</summary>
    internal struct WatchInputs
    {
        public float Now;
        public int Humans;
        /// <summary>The faction has a live uplink (SPACE is not dark).</summary>
        public bool Linked;
        public int BoardCount, BoardCapacity, OverlordPosts, LiveMarks;
        public float RodReadyIn, RadarReadyIn, OpticalReadyIn;
    }

    /// <summary>One decision. A post carries up to three contact ids; a scan carries a site and the bird to use.</summary>
    internal readonly struct WatchPlan
    {
        public readonly WatchAction Action;
        public readonly WatchWhy Why;
        public readonly WatchScan Scan;
        public readonly int SiteKey, Count;
        public readonly float SiteX, SiteZ;
        private readonly int a, b, c;

        public WatchPlan(WatchAction action, WatchWhy why, WatchScan scan = WatchScan.Radar, int siteKey = 0, float siteX = 0f, float siteZ = 0f,
            int count = 0, int id0 = 0, int id1 = 0, int id2 = 0)
        {
            Action = action; Why = why; Scan = scan; SiteKey = siteKey; SiteX = siteX; SiteZ = siteZ; Count = count; a = id0; b = id1; c = id2;
        }

        public int Id(int index) => index == 0 ? a : index == 1 ? b : index == 2 ? c : 0;
        internal static WatchPlan Idle(WatchWhy why) => new WatchPlan(WatchAction.None, why);
    }

    /// <summary>
    /// The pure decisions of WATCH OFFICER OVERLORD for one faction. Nothing here reads the game: the host adapter gathers the
    /// facts (<see cref="WatchInputs"/>, the revealed contacts, the public scan sites) and carries out the plan through the same
    /// MARK and SEND paths a human uses. OVERLORD never invents a target: it only ever picks from the contacts the host has
    /// already admitted to the faction, and with none it can at most ask for a legal scan.
    /// </summary>
    internal sealed class WatchOfficerPolicy
    {
        public const float ThinkSeconds = 2f, SoloIdleSeconds = 60f, GroupIdleSeconds = 300f, NoContactSeconds = 90f,
            RodWindowSeconds = 90f, ClusterMeters = 1500f, MinRevealLeft = 4f, MarkWindowSeconds = 60f,
            PostedSeconds = TaskedBoard.CallSeconds, SkipSeconds = 60f, FailureBackoffSeconds = 10f, ScanFailureBackoffSeconds = 30f,
            NearnessMeters = 30000f;
        public const int MaxOverlordPosts = 2, ReserveBoardSlots = 2, ReserveHumanMarks = 4, MarksPerMinute = SpaceContacts.AttemptsPerMinute,
            MaxTargets = 3, MaxRemembered = 32, MaxSites = 16;

        private readonly float[] markTimes = new float[MarksPerMinute];
        private int markHead, markCount;
        private readonly Dictionary<int, float> remembered = new Dictionary<int, float>(MaxRemembered);
        private readonly Dictionary<int, float> scanned = new Dictionary<int, float>(MaxSites);
        private readonly List<int> scratch = new List<int>(MaxRemembered);
        private readonly WatchTarget[] picks = new WatchTarget[MaxTargets];
        private float nextThinkAt, lastHumanAt = float.NegativeInfinity, noTargetSince = float.NaN;

        // ---- Idle rule ---------------------------------------------------------------------------

        /// <summary>
        /// Which CALLS count as a human working SPACE: only the scan and the camera. Firing a rod, a TASKED claim or any other CALL is
        /// not SPACE work and never suspends OVERLORD (the other human verbs are the MARK and SEND commands).
        /// </summary>
        public static bool CountsAsHumanWork(Runtime.SupportActionId action) =>
            action == Runtime.SupportActionId.Recon || action == Runtime.SupportActionId.SatCamera;

        /// <summary>A human of this faction did a SPACE domain verb (MARK, SEND, CLAIM, a scan). Looking at the feed is not work.</summary>
        public void RecordHuman(float now)
        {
            if (SpaceRules.MissionTime(now) && now > lastHumanAt) lastHumanAt = now;
        }

        /// <summary>
        /// Solo (one human): OVERLORD works SPACE unless that human did a domain verb in the last 60 s. Two or more humans: it stays
        /// out for 300 s after any human verb. A faction with no humans always has OVERLORD (it feeds the AI pilots' board).
        /// </summary>
        public bool Idle(int humans, float now)
        {
            if (humans <= 0) return true;
            return now - lastHumanAt >= (humans == 1 ? SoloIdleSeconds : GroupIdleSeconds);
        }

        // ---- Scoring -----------------------------------------------------------------------------

        /// <summary>
        /// The weight class of a native ground unit: its short unit code first (SAM, AAA, radar trucks are air defence; tanks, IFVs
        /// and artillery are armour), then its role identity. Pure so the mapping is tested without the game.
        /// </summary>
        public static WatchKind KindOf(string code, float antiSurface, float antiAir)
        {
            switch (code)
            {
                case "SAM IR": case "SAM R": case "SPAAG": case "AA": case "AAA": case "MANPADS": case "LADS": case "RDR":
                    return WatchKind.AirDefence;
                case "MBT": case "IFV": case "ARTY": case "MLRS": case "TBM":
                    return WatchKind.Armour;
            }
            if (SpaceRules.Finite(antiAir) && antiAir >= 0.5f) return WatchKind.AirDefence;
            if (SpaceRules.Finite(antiSurface) && antiSurface >= 0.5f) return WatchKind.Armour;
            return WatchKind.Other;
        }

        /// <summary>
        /// Vanilla value x type weight (air defence 2, armour 1.5) x strategic 1.5 x nearness to the nearest friendly-held
        /// objective (up to 1.5), halved for a mover: a static MARK lasts 480 s against 180.
        /// </summary>
        public static float Score(in WatchTarget t)
        {
            float value = SpaceRules.Finite(t.Value) && t.Value > 0.1f ? t.Value : 0.1f;
            float weight = t.Kind == WatchKind.AirDefence ? 2f : t.Kind == WatchKind.Armour ? 1.5f : 1f;
            if (t.Strategic) weight *= 1.5f;
            float near = 1f;
            if (SpaceRules.Finite(t.FrontMeters) && t.FrontMeters >= 0f)
                near = 1f + 0.5f * Math.Max(0f, Math.Min(1f, 1f - t.FrontMeters / NearnessMeters));
            return value * weight * near * (t.Moving ? 0.5f : 1f);
        }

        // ---- The think ---------------------------------------------------------------------------

        /// <summary>True when a think would run at this clock (cheap; the adapter asks before it gathers anything).</summary>
        public bool Due(float now) => SpaceRules.MissionTime(now) && now >= nextThinkAt;

        public WatchPlan Think(in WatchInputs inputs, IReadOnlyList<WatchTarget> targets, IReadOnlyList<WatchSite> sites)
        {
            float now = inputs.Now;
            if (!SpaceRules.MissionTime(now) || now < nextThinkAt) return WatchPlan.Idle(WatchWhy.NotDue);
            if (!Idle(inputs.Humans, now)) { noTargetSince = float.NaN; return WatchPlan.Idle(WatchWhy.Suspended); }
            nextThinkAt = now + ThinkSeconds;
            if (!inputs.Linked) return WatchPlan.Idle(WatchWhy.NoUplink);
            Prune(now);

            WatchWhy gate = Gate(inputs, now);
            int best = -1;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; targets != null && i < targets.Count; i++)
            {
                WatchTarget t = targets[i];
                if (!Usable(t, now)) continue;
                float score = Score(t);
                if (score > bestScore || (score == bestScore && t.Id < targets[best].Id)) { best = i; bestScore = score; }
            }
            if (best < 0) return NoTarget(inputs, sites, now, gate);
            noTargetSince = float.NaN;
            return gate != WatchWhy.None ? WatchPlan.Idle(gate) : Strike(inputs, targets, best, now);
        }

        private bool Usable(in WatchTarget t, float now) =>
            // A point the wire cannot carry is never posted: the pilots could not see that card.
            t.Id > 0 && SpaceWire.Codable(t.X) && SpaceWire.Codable(t.Z) && SpaceRules.Finite(t.ExpiresAt) &&
            t.ExpiresAt - now >= MinRevealLeft && !remembered.ContainsKey(t.Id);

        /// <summary>
        /// Everything that stops a post, in the order a transcript should name it. A scan is pointless while any of these holds
        /// (a reveal lasts 20 s and nothing could be posted from it), so it gates the scan too.
        /// </summary>
        private WatchWhy Gate(in WatchInputs inputs, float now)
        {
            if (remembered.Count >= MaxRemembered) return WatchWhy.Waiting; // full memory refuses: never forget a live post
            if (inputs.OverlordPosts >= MaxOverlordPosts) return WatchWhy.OverlordCap;
            if (inputs.BoardCount + 1 > inputs.BoardCapacity - ReserveBoardSlots) return WatchWhy.BoardFull;
            if (!(inputs.RodReadyIn <= RodWindowSeconds)) return WatchWhy.RodNotReady;
            if (SpaceContacts.MaxMarks - ReserveHumanMarks - inputs.LiveMarks < 1) return WatchWhy.MarkRoom;
            if (MarksPerMinute - MarksInWindow(now) < 1) return WatchWhy.MarkRate;
            return WatchWhy.None;
        }

        private WatchPlan NoTarget(in WatchInputs inputs, IReadOnlyList<WatchSite> sites, float now, WatchWhy gate)
        {
            if (float.IsNaN(noTargetSince)) noTargetSince = now;
            if (now - noTargetSince < NoContactSeconds) return WatchPlan.Idle(WatchWhy.Waiting);
            if (gate != WatchWhy.None) return WatchPlan.Idle(gate);
            int site = -1;
            float oldest = float.PositiveInfinity;
            for (int i = 0; sites != null && i < sites.Count; i++)
            {
                if (!SpaceWire.Codable(sites[i].X) || !SpaceWire.Codable(sites[i].Z)) continue;
                float last = scanned.TryGetValue(sites[i].Key, out float at) ? at : float.NegativeInfinity;
                if (site < 0 || last < oldest) { site = i; oldest = last; }
            }
            if (site < 0) return WatchPlan.Idle(WatchWhy.NoSite);
            // The bird is chosen for the site actually picked: SAR works through cloud and at night, so it is the default; optical
            // only when this site's sky is clear by day and the radar bird is not available.
            WatchScan kind;
            if (inputs.RadarReadyIn <= 0f) kind = WatchScan.Radar;
            else if (sites[site].OpticalOk && inputs.OpticalReadyIn <= 0f) kind = WatchScan.Optical;
            else return WatchPlan.Idle(WatchWhy.ScanNotReady);
            return new WatchPlan(WatchAction.Scan, WatchWhy.None, kind, sites[site].Key, sites[site].X, sites[site].Z);
        }

        private WatchPlan Strike(in WatchInputs inputs, IReadOnlyList<WatchTarget> targets, int best, float now)
        {
            int room = SpaceContacts.MaxMarks - ReserveHumanMarks - inputs.LiveMarks;
            int rate = MarksPerMinute - MarksInWindow(now);
            int limit = Math.Min(MaxTargets, Math.Min(room, rate));

            WatchTarget lead = targets[best];
            int count = 0;
            picks[count++] = lead;
            // Neighbours of the lead ride along by score; the rod only flies at the first point, the rest is context for the pilot.
            while (count < limit)
            {
                int next = -1;
                float nextScore = float.NegativeInfinity;
                for (int i = 0; i < targets.Count; i++)
                {
                    WatchTarget t = targets[i];
                    if (!Usable(t, now) || Taken(t.Id, count)) continue;
                    float dx = t.X - lead.X, dz = t.Z - lead.Z;
                    if (dx * dx + dz * dz > ClusterMeters * ClusterMeters) continue;
                    float score = Score(t);
                    if (score > nextScore || (score == nextScore && t.Id < targets[next].Id)) { next = i; nextScore = score; }
                }
                if (next < 0) break;
                picks[count++] = targets[next];
            }
            return new WatchPlan(WatchAction.Post, WatchWhy.None, count: count, id0: picks[0].Id,
                id1: count > 1 ? picks[1].Id : 0, id2: count > 2 ? picks[2].Id : 0);
        }

        private bool Taken(int id, int count)
        {
            for (int i = 0; i < count; i++) if (picks[i].Id == id) return true;
            return false;
        }

        // ---- Bookkeeping the adapter reports back --------------------------------------------------

        /// <summary>Every MARK attempt spends the host's six-a-minute allowance, confirmed or not.</summary>
        public void NoteMarkAttempt(float now)
        {
            if (!SpaceRules.MissionTime(now)) return;
            markTimes[markHead] = now;
            markHead = (markHead + 1) % markTimes.Length;
            if (markCount < markTimes.Length) markCount++;
        }

        public int MarksInWindow(float now)
        {
            int n = 0;
            for (int i = 0; i < markCount; i++) if (now - markTimes[i] < MarkWindowSeconds) n++;
            return n;
        }

        /// <summary>One post per target for the post's whole 600 s life.</summary>
        public void NotePosted(int[] ids, int count, float now) => Remember(ids, count, now + PostedSeconds, 0f, now);

        /// <summary>A failed MARK or SEND: skip those contacts for a minute and back off ten seconds.</summary>
        public void NoteFailed(int[] ids, int count, float now) => Remember(ids, count, now + SkipSeconds, FailureBackoffSeconds, now);

        public void NoteScan(float now, int siteKey)
        {
            if (!SpaceRules.MissionTime(now)) return;
            StampSite(now, siteKey);
            noTargetSince = now; // the next scan is a full 90 s of silence away
        }

        private void StampSite(float now, int siteKey)
        {
            if (!scanned.ContainsKey(siteKey) && scanned.Count >= MaxSites)
            {
                int oldest = 0;
                float at = float.PositiveInfinity;
                foreach (var pair in scanned) if (pair.Value < at) { at = pair.Value; oldest = pair.Key; }
                scanned.Remove(oldest);
            }
            scanned[siteKey] = now;
        }

        // ---- MARKs OVERLORD confirmed whose SEND failed ----------------------------------------

        public const int MaxStrandedTries = 3;
        private readonly int[] stranded = new int[MaxTargets];
        private int strandedCount, strandedTries;

        /// <summary>OVERLORD's confirmed MARKs are live but unposted: they are tried again on a later think rather than stranding mark slots.</summary>
        public bool HasStranded => strandedCount > 0;

        public void NoteStranded(int[] ids, int count)
        {
            strandedCount = 0; strandedTries = 0;
            for (int i = 0; ids != null && i < count && i < ids.Length && strandedCount < stranded.Length; i++)
                if (ids[i] > 0) stranded[strandedCount++] = ids[i];
        }

        public int CopyStranded(int[] into)
        {
            int n = Math.Min(strandedCount, into.Length);
            Array.Copy(stranded, into, n);
            return n;
        }

        /// <summary>A retry's result. A post, or the third refusal, ends it; the marks then lapse on their own clock.</summary>
        public void NoteStrandedTry(bool posted)
        {
            if (posted || ++strandedTries >= MaxStrandedTries) strandedCount = 0;
        }

        /// <summary>Do not think again for <paramref name="seconds"/> (the adapter's cheap poll while a human works the domain).</summary>
        public void Defer(float now, float seconds)
        {
            if (SpaceRules.MissionTime(now) && SpaceRules.Finite(seconds) && seconds > 0f) nextThinkAt = Math.Max(nextThinkAt, now + seconds);
        }

        /// <summary>A refused scan backs off thirty seconds and stamps its site, so the next scan rotates to another one.</summary>
        public void NoteScanFailed(float now, int siteKey = int.MinValue)
        {
            if (!SpaceRules.MissionTime(now)) return;
            nextThinkAt = Math.Max(nextThinkAt, now + ScanFailureBackoffSeconds);
            if (siteKey != int.MinValue) StampSite(now, siteKey);
        }

        /// <summary>Scene change: forget every post, scan, MARK and human verb.</summary>
        public void Reset()
        {
            markHead = markCount = 0;
            remembered.Clear(); scanned.Clear(); strandedCount = strandedTries = 0;
            nextThinkAt = 0f; lastHumanAt = float.NegativeInfinity; noTargetSince = float.NaN;
        }

        private void Remember(int[] ids, int count, float until, float backoff, float now)
        {
            if (!SpaceRules.MissionTime(now) || ids == null) return;
            Prune(now);
            for (int i = 0; i < count && i < ids.Length; i++)
            {
                if (ids[i] <= 0) continue;
                if (remembered.Count >= MaxRemembered && !remembered.ContainsKey(ids[i])) continue; // bounded: a full table keeps its live entries
                remembered[ids[i]] = until;
            }
            if (backoff > 0f) nextThinkAt = Math.Max(nextThinkAt, now + backoff);
        }

        private void Prune(float now)
        {
            if (remembered.Count == 0) return;
            scratch.Clear();
            foreach (var pair in remembered) if (now >= pair.Value) scratch.Add(pair.Key);
            for (int i = 0; i < scratch.Count; i++) remembered.Remove(scratch[i]);
            scratch.Clear();
        }
    }
}
