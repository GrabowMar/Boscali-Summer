using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Domain.Cyber
{
    /// <summary>What OVERLORD needs from the live game that the CYBER desk does not know. The runtime implements it over the real players and board; the offline sims over scripted ones.</summary>
    internal interface ICyberWatchWorld
    {
        /// <summary>Connected humans of the faction.</summary>
        int Humans { get; }

        /// <summary>Metres from the point to the nearest airborne human pilot of the faction; +infinity when there is none.</summary>
        float NearestPilotMeters(float x, float z);

        /// <summary>A BURN package can ride the TASKED board right now: someone can fly it (a human is in the faction), the SPACE desk is up, the board has room above its reserve and no OVERLORD CYBER post is live.</summary>
        bool PackageRoom { get; }
    }

    internal enum CyberWatchAction : byte { None = 0, Hop = 1, Burn = 2, Drop = 3 }

    /// <summary>Why OVERLORD did nothing on a think (the transcript and the log name it). None when it acted.</summary>
    internal enum CyberWatchWhy : byte { None, NotDue, Paced, Suspended, NoAnchors, NoSlot, NoNode, Holding, Resting, Failed }

    internal readonly struct CyberWatchPlan
    {
        public readonly CyberWatchAction Action;
        public readonly CyberWatchWhy Why;
        public readonly int NodeId;
        public readonly NodeKind Kind;
        public readonly WatchCode Code;
        public readonly int A, B;
        public readonly CyberOutcome Outcome;

        public CyberWatchPlan(CyberWatchAction action, CyberWatchWhy why, int nodeId = 0, NodeKind kind = NodeKind.Radar, WatchCode code = WatchCode.None, int a = 0, int b = 0,
            CyberOutcome outcome = CyberOutcome.None)
        { Action = action; Why = why; NodeId = nodeId; Kind = kind; Code = code; A = a; B = b; Outcome = outcome; }

        /// <summary>The reason string every action carries.</summary>
        public string Reason => WatchWords.Reason(Code, A, B);

        internal static CyberWatchPlan Idle(CyberWatchWhy why) => new CyberWatchPlan(CyberWatchAction.None, why);
    }

    /// <summary>
    /// WATCH OFFICER OVERLORD for CYBER (spec section 4), one faction. It works the same <see cref="CyberDesk"/> a human operator does, as the reserved identity: it hops toward the most valuable
    /// reachable revealed node (SAM C2, RADAR, UPLINK, RELAY, DATA CENTER), goes deeper when a more valuable node is in chain reach, BURNs a node into a package when the trace passes 70 % or a
    /// pilot is near the front, and DROPs before the trace can fill. It sees only <see cref="CyberDesk.Visible"/> (the nodes the faction has really sighted), so it can never name a node the faction
    /// has not revealed. It has no wallet: its starts and upkeep are free, its packages go to the board labelled WATCH OFFICER and their fee goes to HQ FUND. Bounded by the pacer (one action every 10 s
    /// in this domain, every 30 s for a faction with no humans).
    /// </summary>
    internal sealed class CyberWatchBrain
    {
        public const float ThinkSeconds = 2f, BurnTrace = 70f, SafeTrace = 96f, HopBudgetTrace = 92f, PilotBurnMeters = 40000f, PilotBurnMinHoldSeconds = 45f,
            RestSeconds = 45f, AiRestSeconds = 150f, BurnSpacingSeconds = 480f, NodeBackoffSeconds = 60f, BurnBackoffSeconds = 30f;
        public const int MaxHeldByOverlord = 2, MaxBackoffs = 32;
        private const ulong Me = SpaceContacts.WatchOfficerId;

        private readonly WatchIdle idle = new WatchIdle();
        private readonly Dictionary<int, float> backoff = new Dictionary<int, float>(MaxBackoffs);
        private readonly List<EwSource> trucks = new List<EwSource>(AnchorRules.MaxTrucks);
        private readonly List<CyberNode> held = new List<CyberNode>(CyberRules.MaxHeldPerFaction);
        private readonly List<int> scratch = new List<int>(MaxBackoffs);
        private float nextThinkAt, restUntil, burnBackoffUntil, clock, lastBurnAt = float.NegativeInfinity;

        public int Hops, Deepers, Burns, Drops, Failures, Thinks;
        public CyberWatchPlan Last { get; private set; }

        /// <summary>A human of this faction did a CYBER verb (HOP, BURN or DROP): OVERLORD yields (60 s alone, 300 s with company).</summary>
        public void RecordHuman(float now) => idle.RecordHuman(now);

        public bool Idle(int humans, float now) => idle.Idle(humans, now);

        public bool Due(float now) => SpaceRules.MissionTime(now) && now >= nextThinkAt;

        /// <summary>Do not think again for <paramref name="seconds"/> (the adapter's cheap poll while nothing can happen).</summary>
        public void Defer(float now, float seconds)
        {
            if (SpaceRules.MissionTime(now) && SpaceRules.Finite(seconds) && seconds > 0f) nextThinkAt = Math.Max(nextThinkAt, now + seconds);
        }

        public void Reset()
        {
            idle.Reset(); backoff.Clear(); trucks.Clear(); held.Clear();
            nextThinkAt = restUntil = burnBackoffUntil = clock = 0f; lastBurnAt = float.NegativeInfinity;
            Hops = Deepers = Burns = Drops = Failures = Thinks = 0;
            Last = default;
        }

        // ---- Trace arithmetic ------------------------------------------------------------------

        /// <summary>The trace the intrusion will have after <paramref name="window"/> seconds if nothing is done: the running hop adds 1.5 %/s, every held node 0.5 %/s, all scaled by <paramref name="factor"/>.</summary>
        public static float Projected(CyberIntrusion x, float factor, float now, float window)
        {
            float remain = x.HopTarget != 0 ? Math.Max(0f, x.HopEndsAt - now) : 0f;
            float t1 = Math.Min(window, remain), t2 = Math.Max(0f, window - t1);
            int heldNow = x.HeldCount;
            float trace = x.Trace + (CyberRules.HopTraceRate(x.HopKind) * (x.HopTarget != 0 ? 1f : 0f) + CyberRules.HeldTraceRate * heldNow) * factor * t1;
            trace += CyberRules.HeldTraceRate * (heldNow + (x.HopTarget != 0 ? 1 : 0)) * factor * t2;
            return trace;
        }

        /// <summary>The trace a new hop to <paramref name="kind"/> would leave on arrival plus one pacing window of holding it.</summary>
        public static float AfterHop(float trace, int heldNow, NodeKind kind, bool exploit, float factor, float window) =>
            trace + (CyberRules.HopTraceRate(kind) + CyberRules.HeldTraceRate * heldNow) * factor * CyberRules.HopSeconds(kind, exploit) +
            CyberRules.HeldTraceRate * (heldNow + 1) * factor * window;

        /// <summary>SAM C2 over RADAR over UPLINK over RELAY over DATA CENTER (spec section 4).</summary>
        public static int Priority(NodeKind kind)
        {
            switch (kind)
            {
                case NodeKind.SamC2: return 5;
                case NodeKind.Radar: return 4;
                case NodeKind.Uplink: return 3;
                case NodeKind.Relay: return 2;
                default: return 1;
            }
        }

        // ---- The think -------------------------------------------------------------------------

        /// <summary>One step: decide, and when the pacer allows, act through the desk. Returns what happened (or why nothing did).</summary>
        public CyberWatchPlan Step(CyberDesk desk, ICyberWatchWorld world, WatchPacer pacer, bool aiFaction, float now)
        {
            if (!SpaceRules.MissionTime(now) || now < nextThinkAt) return CyberWatchPlan.Idle(CyberWatchWhy.NotDue);
            nextThinkAt = now + ThinkSeconds;
            clock = now;
            Thinks++;
            CyberWatchPlan plan = Decide(desk, world, pacer, aiFaction, now);
            if (plan.Action == CyberWatchAction.None) { Last = plan; return plan; }
            if (!pacer.CanAct(WatchDomain.Cyber, aiFaction, now)) { Last = CyberWatchPlan.Idle(CyberWatchWhy.Paced); return Last; }
            Last = Execute(desk, plan, pacer, aiFaction, now);
            return Last;
        }

        private CyberWatchPlan Decide(CyberDesk desk, ICyberWatchWorld world, WatchPacer pacer, bool aiFaction, float now)
        {
            pacer.SetUrgent(WatchDomain.Cyber, false); // refreshed below only while an intrusion could be traced inside one AI gap
            if (desk.Anchors.Count(AnchorKind.EwTruck) == 0) return CyberWatchPlan.Idle(CyberWatchWhy.NoAnchors);
            Prune(now);
            int humans = world.Humans;
            bool quiet = idle.Idle(humans, now);
            float factor = desk.TraceFactor(now);
            // How long a trace can run before OVERLORD may act again: the think cadence, plus the pacer's wait now; a hop planned now arrives later, so it assumes the worst wait the pacer can impose (the shared 30 s of an AI faction).
            float margin = ThinkSeconds + 1f;
            float window = margin + pacer.WaitSeconds(WatchDomain.Cyber, aiFaction, now);
            float planned = margin + (aiFaction ? WatchPacer.AiGap(0) : 0f);
            CyberIntrusion mine = desk.Network.Of(Me);
            // An AI faction shares one limiter between both domains: while this intrusion could be traced inside one full AI gap, SOF may not take the limiter (and a SOF team in danger gets it first).
            pacer.SetUrgent(WatchDomain.Cyber, aiFaction && mine != null && Projected(mine, factor, now, margin + WatchPacer.AiGap(0)) >= SafeTrace);
            if (mine != null)
            {
                CollectHeld(desk, mine);
                // Safety first, whatever the idle rule says: leave before the trace can fill in the time it takes OVERLORD to act again.
                float projected = Projected(mine, factor, now, window);
                if (projected >= SafeTrace) return new CyberWatchPlan(CyberWatchAction.Drop, CyberWatchWhy.None, 0, NodeKind.Radar, WatchCode.CyberDropTrace, 0, Pct(mine.Trace));
                if (!quiet) return Yield(desk, world, mine, now);
                if (mine.HopTarget != 0) return CyberWatchPlan.Idle(CyberWatchWhy.Holding);
                CyberWatchPlan burn = BurnPlan(desk, world, mine, now);
                if (burn.Action != CyberWatchAction.None) return burn;
                // A faction with no humans has nobody to fly a package: it lets the node go at the same 70 % a BURN would, so its holds are short bursts rather than a standing blackout.
                if (aiFaction && mine.HopTarget == 0 && mine.HeldCount > 0 && mine.Trace >= BurnTrace)
                    return new CyberWatchPlan(CyberWatchAction.Drop, CyberWatchWhy.None, 0, NodeKind.Radar, WatchCode.CyberDropTrace, 0, Pct(mine.Trace));
                return DeeperPlan(desk, mine, factor, planned, now);
            }
            if (!quiet) return CyberWatchPlan.Idle(CyberWatchWhy.Suspended);
            if (now < restUntil) return CyberWatchPlan.Idle(CyberWatchWhy.Resting);
            return StartPlan(desk, world, humans, factor, planned, now);
        }

        private CyberWatchPlan Yield(CyberDesk desk, ICyberWatchWorld world, CyberIntrusion mine, float now)
        {
            if (mine.HopTarget == 0 && world.PackageRoom && now >= burnBackoffUntil && now - lastBurnAt >= BurnSpacingSeconds && BestHeld(desk, mine, out CyberNode node))
                return new CyberWatchPlan(CyberWatchAction.Burn, CyberWatchWhy.None, node.Id, node.Kind, WatchCode.CyberBurnYield, (int)node.Kind, 0);
            return new CyberWatchPlan(CyberWatchAction.Drop, CyberWatchWhy.None, 0, NodeKind.Radar, WatchCode.CyberDropYield, 0, Pct(mine.Trace));
        }

        private CyberWatchPlan BurnPlan(CyberDesk desk, ICyberWatchWorld world, CyberIntrusion mine, float now)
        {
            // About one package per 8 minutes (core 8.2: AI staff posts roughly that often), and only with room on the board.
            if (mine.HopTarget != 0 || mine.HeldCount == 0 || !world.PackageRoom || now < burnBackoffUntil || now - lastBurnAt < BurnSpacingSeconds) return CyberWatchPlan.Idle(CyberWatchWhy.Holding);
            if (mine.Trace >= BurnTrace && BestHeld(desk, mine, out CyberNode top))
                return new CyberWatchPlan(CyberWatchAction.Burn, CyberWatchWhy.None, top.Id, top.Kind, WatchCode.CyberBurnTrace, (int)top.Kind, Pct(mine.Trace));
            // A package helps the pilots when one is within reach of the node (a bird jam helps nobody who flies).
            CyberNode best = default; float bestMeters = float.PositiveInfinity; bool found = false;
            foreach (HeldNode h in mine.Held)
            {
                if (h.Kind == NodeKind.Uplink || now - h.Since < PilotBurnMinHoldSeconds || !TryNode(desk, h.NodeId, out CyberNode n)) continue;
                float meters = world.NearestPilotMeters(n.X, n.Z);
                if (meters <= PilotBurnMeters && (!found || meters < bestMeters)) { best = n; bestMeters = meters; found = true; }
            }
            if (found) return new CyberWatchPlan(CyberWatchAction.Burn, CyberWatchWhy.None, best.Id, best.Kind, WatchCode.CyberBurnPilots, (int)best.Kind, (int)Math.Min(255f, Math.Round(bestMeters / 1000f)));
            return CyberWatchPlan.Idle(CyberWatchWhy.Holding);
        }

        private CyberWatchPlan DeeperPlan(CyberDesk desk, CyberIntrusion mine, float factor, float window, float now)
        {
            if (mine.HeldCount >= MaxHeldByOverlord || mine.HeldCount == 0) return CyberWatchPlan.Idle(CyberWatchWhy.Holding);
            int bar = 0;
            foreach (HeldNode h in mine.Held) bar = Math.Max(bar, Priority(h.Kind));
            CyberNode pick = default; bool found = false;
            foreach (CyberNode n in desk.Visible)
            {
                if (desk.Network.IsEngaged(n.Id) || Backed(n.Id) || Priority(n.Kind) <= bar) continue;
                if (!CyberGraph.Reachable(n, null, held, out _, out int via) || via == 0) continue;
                if (AfterHop(mine.Trace, mine.HeldCount, n.Kind, CyberRules.Exploit(n.Kind), factor, window) > HopBudgetTrace) continue;
                if (!found || Better(n, pick)) { pick = n; found = true; }
            }
            if (!found) return CyberWatchPlan.Idle(CyberWatchWhy.Holding);
            return new CyberWatchPlan(CyberWatchAction.Hop, CyberWatchWhy.None, pick.Id, pick.Kind, WatchCode.CyberDeeper, (int)pick.Kind, Pct(mine.Trace));
        }

        private CyberWatchPlan StartPlan(CyberDesk desk, ICyberWatchWorld world, int humans, float factor, float window, float now)
        {
            if (humans > 0 && desk.Network.Active.Count >= CyberRules.IntrusionCap(humans) - 1) return CyberWatchPlan.Idle(CyberWatchWhy.NoSlot); // one intrusion always stays free for a human
            desk.Anchors.CopyTrucks(trucks);
            if (trucks.Count == 0) return CyberWatchPlan.Idle(CyberWatchWhy.NoAnchors);
            CyberNode pick = default; bool found = false;
            foreach (CyberNode n in desk.Visible)
            {
                if (desk.Network.IsEngaged(n.Id) || Backed(n.Id)) continue;
                if (!CyberGraph.Reachable(n, trucks, null, out int truck, out _) || truck < 0) continue;
                if (desk.Network.CanStart(Me, truck, humans, now, out _) != CyberOutcome.None) continue;
                if (AfterHop(0f, 0, n.Kind, CyberRules.Exploit(n.Kind), factor, window) > HopBudgetTrace) continue;
                if (!found || Better(n, pick)) { pick = n; found = true; }
            }
            if (!found) return CyberWatchPlan.Idle(CyberWatchWhy.NoNode);
            return new CyberWatchPlan(CyberWatchAction.Hop, CyberWatchWhy.None, pick.Id, pick.Kind, WatchCode.CyberHop, (int)pick.Kind, 0);
        }

        private CyberWatchPlan Execute(CyberDesk desk, in CyberWatchPlan plan, WatchPacer pacer, bool aiFaction, float now)
        {
            CyberResult r;
            switch (plan.Action)
            {
                case CyberWatchAction.Hop: r = desk.Hop(Me, plan.NodeId); break;
                case CyberWatchAction.Burn: r = desk.Burn(Me, plan.NodeId); break;
                default: r = desk.Drop(Me, plan.NodeId); break;
            }
            if (!r.Ok)
            {
                Failures++;
                if (plan.Action == CyberWatchAction.Burn) burnBackoffUntil = now + BurnBackoffSeconds;
                else if (plan.Action == CyberWatchAction.Hop) Back(plan.NodeId, now + NodeBackoffSeconds);
                Defer(now, 5f);
                return new CyberWatchPlan(CyberWatchAction.None, CyberWatchWhy.Failed, plan.NodeId, plan.Kind, plan.Code, plan.A, plan.B, r.Outcome);
            }
            pacer.NoteAct(WatchDomain.Cyber, aiFaction, now);
            switch (plan.Action)
            {
                case CyberWatchAction.Hop: if (plan.Code == WatchCode.CyberDeeper) Deepers++; else Hops++; break;
                case CyberWatchAction.Burn: Burns++; lastBurnAt = now; break;
                default: Drops++; break;
            }
            // A burned or dropped intrusion rests before the next one: effects come in bursts, not as a permanent state.
            if (plan.Action != CyberWatchAction.Hop && desk.Network.Of(Me) == null) restUntil = now + (aiFaction ? AiRestSeconds : RestSeconds);
            return new CyberWatchPlan(plan.Action, CyberWatchWhy.None, plan.NodeId, plan.Kind, plan.Code, plan.A, plan.B, r.Outcome);
        }

        // ---- Helpers ---------------------------------------------------------------------------

        private static int Pct(float trace) => (int)Math.Max(0, Math.Min(100, Math.Round(trace)));

        /// <summary>Higher priority first; closer to the front next; the lower id last (a stable choice).</summary>
        private static bool Better(in CyberNode a, in CyberNode b)
        {
            int pa = Priority(a.Kind), pb = Priority(b.Kind);
            if (pa != pb) return pa > pb;
            if (a.FrontDistance != b.FrontDistance) return a.FrontDistance < b.FrontDistance;
            return a.Id < b.Id;
        }

        private static bool TryNode(CyberDesk desk, int id, out CyberNode node)
        {
            IReadOnlyList<CyberNode> v = desk.Visible;
            for (int i = 0; i < v.Count; i++) if (v[i].Id == id) { node = v[i]; return true; }
            node = default; return false;
        }

        private void CollectHeld(CyberDesk desk, CyberIntrusion mine)
        {
            held.Clear();
            foreach (HeldNode h in mine.Held) if (TryNode(desk, h.NodeId, out CyberNode n)) held.Add(n);
        }

        private bool BestHeld(CyberDesk desk, CyberIntrusion mine, out CyberNode best)
        {
            best = default; bool found = false;
            foreach (HeldNode h in mine.Held)
                if (TryNode(desk, h.NodeId, out CyberNode n) && (!found || Better(n, best))) { best = n; found = true; }
            return found;
        }

        private bool Backed(int id) => backoff.TryGetValue(id, out float until) && until > clock;

        private void Back(int id, float until)
        {
            if (!backoff.ContainsKey(id) && backoff.Count >= MaxBackoffs) return;
            backoff[id] = until;
        }

        private void Prune(float now)
        {
            if (backoff.Count == 0) return;
            scratch.Clear();
            foreach (var pair in backoff) if (now >= pair.Value) scratch.Add(pair.Key);
            for (int i = 0; i < scratch.Count; i++) backoff.Remove(scratch[i]);
        }
    }
}
