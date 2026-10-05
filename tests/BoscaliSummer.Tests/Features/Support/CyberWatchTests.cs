using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>M6b WATCH OFFICER OVERLORD for CYBER: target priority, fog, reach, trace safety, BURN rules, the idle rule, the pacing bound and the free, reserved identity (spec section 4).</summary>
    internal static class CyberWatchTests
    {
        private const int Us = 7, Them = 9;
        private const ulong Human = 111UL, Overlord = SpaceContacts.WatchOfficerId;

        public static void Run()
        {
            CheckPriorityAndFog();
            CheckReach();
            CheckBurnAtSeventy();
            CheckDropBeforeTraced();
            CheckPilotsAndUplink();
            CheckDeeper();
            CheckYield();
            CheckSlotsAndFree();
            CheckPacing();
        }

        private static void Eq<T>(T actual, T expected, string message) =>
            TestAssert.That(Equals(actual, expected), message + " (got " + actual + ", want " + expected + ")");

        private sealed class Ports : ICyberPorts
        {
            public float Clock; public int HumanCount = 1, Balance = 1000, Charged;
            public bool BoardOpen = true;
            public readonly List<string> Posted = new List<string>();
            public float Now => Clock;
            public int Humans => HumanCount;
            public int Owner => Us;
            public CyberOutcome TrySpend(ulong op, int cr, out int detail)
            {
                detail = 0;
                if (op == Overlord) return CyberOutcome.None; // the reserved identity has no wallet: free, as the host seam does
                if (Balance < cr) { detail = cr; return CyberOutcome.LowCredit; }
                Balance -= cr; Charged += cr;
                return CyberOutcome.None;
            }
            public void Refund(ulong op, int cr) { if (op != Overlord) Balance += cr; }
            public CyberOutcome PostPackage(ulong op, in CyberNode node, in PackageDef def, bool exploit, float effort)
            {
                if (!BoardOpen) return CyberOutcome.BoardFull;
                Posted.Add(def.Label + "@" + node.Id + (op == Overlord ? " by OVERLORD" : ""));
                return CyberOutcome.None;
            }
        }

        private sealed class World : ICyberWatchWorld
        {
            public int HumanCount = 1;
            public bool Room = true;
            public float PilotMeters = float.PositiveInfinity;
            public int Humans => HumanCount;
            public bool PackageRoom => Room;
            public float NearestPilotMeters(float x, float z) => PilotMeters;
        }

        private sealed class Rig
        {
            public readonly Ports Ports = new Ports();
            public readonly World World = new World();
            public readonly CyberAnchorSet Anchors;
            public readonly CyberDesk Desk;
            public readonly CyberWatchBrain Brain = new CyberWatchBrain();
            public readonly WatchPacer Pacer = new WatchPacer();
            public readonly List<CyberEvent> Events = new List<CyberEvent>();
            public readonly List<float> ActionTimes = new List<float>();
            public readonly List<CyberWatchPlan> Acts = new List<CyberWatchPlan>();
            public bool Ai;
            public int NoTarget;

            public Rig(int trucks = 1, int dataCenters = 1)
            {
                Anchors = new CyberAnchorSet(trucks, dataCenters);
                for (int i = 0; i < trucks; i++) Anchors.SetPosition(AnchorKind.EwTruck, i, 0f, 0f);
                Desk = new CyberDesk(Ports, Anchors);
                Desk.Happened += Events.Add;
            }

            public void Observe(params NodeObservation[] o) => Desk.Refresh(o);

            /// <summary>One mission second: the clock, the desk, then OVERLORD (humans of the world decide the pacing mode).</summary>
            public void Tick(float seconds = 1f)
            {
                for (float t = 0; t < seconds; t += 1f)
                {
                    Ports.Clock += 1f;
                    Ports.HumanCount = World.HumanCount;
                    Desk.Tick();
                    CyberWatchPlan p = Brain.Step(Desk, World, Pacer, Ai, Ports.Clock);
                    if (p.Action != CyberWatchAction.None) { ActionTimes.Add(Ports.Clock); Acts.Add(p); }
                    if (p.Outcome == CyberOutcome.NoTarget) NoTarget++;
                }
            }

            public int IdOf(NodeKind kind, uint key) { Desk.Ids.TryGet(kind, key, out int id); return id; }
            public bool Traced() => Events.Exists(e => e.Kind == CyberEventKind.Traced);
        }

        private static NodeObservation Obs(NodeKind kind, uint key, float x, float z, bool sighted = true, float front = 0f) =>
            new NodeObservation(new NodeSeed(kind, key, x, z, front), Them, sighted, false);

        private static void CheckPriorityAndFog()
        {
            var rig = new Rig();
            rig.World.HumanCount = 0; rig.Ai = true;
            // The unrevealed SAM C2 is the juiciest node on the map and must never be touched: nothing about it is visible to the faction.
            rig.Observe(Obs(NodeKind.SamC2, 40, 3000f, 0f, sighted: false), Obs(NodeKind.Relay, 41, 2000f, 0f), Obs(NodeKind.Radar, 42, 4000f, 0f),
                Obs(NodeKind.DataCenter, 43, 5000f, 0f), Obs(NodeKind.Uplink, 44, 6000f, 0f));
            rig.Tick(4f);
            CyberWatchPlan first = rig.Acts.Count > 0 ? rig.Acts[0] : default;
            Eq(first.Action, CyberWatchAction.Hop, "OVERLORD opens an intrusion");
            Eq(first.Kind, NodeKind.Radar, "the most valuable REVEALED node (the SAM C2 is not revealed): RADAR over UPLINK, RELAY and DATA CENTER");
            Eq(first.Code, WatchCode.CyberHop, "the reason code");
            TestAssert.That(first.Reason.StartsWith("HOP RADAR", StringComparison.Ordinal), "the reason string says what it did: " + first.Reason);
            Eq(rig.IdOf(NodeKind.SamC2, 40), 0, "the unrevealed node was never given an id");
            rig.Observe(Obs(NodeKind.SamC2, 40, 3000f, 0f), Obs(NodeKind.Relay, 41, 2000f, 0f), Obs(NodeKind.Radar, 42, 4000f, 0f));
            Eq(rig.NoTarget, 0, "no hop was ever answered NO SUCH NODE: OVERLORD names only what the desk shows");

            var order = new Rig();
            order.World.HumanCount = 0; order.Ai = true;
            order.Observe(Obs(NodeKind.Relay, 41, 2000f, 0f), Obs(NodeKind.SamC2, 40, 3000f, 0f), Obs(NodeKind.Radar, 42, 4000f, 0f), Obs(NodeKind.DataCenter, 43, 5000f, 0f));
            order.Tick(4f);
            Eq(order.Acts[0].Kind, NodeKind.SamC2, "SAM C2 beats RADAR, UPLINK, RELAY and DATA CENTER");

            var onlyDc = new Rig();
            onlyDc.World.HumanCount = 0; onlyDc.Ai = true;
            onlyDc.Observe(Obs(NodeKind.DataCenter, 43, 5000f, 0f), Obs(NodeKind.Relay, 41, 2000f, 0f));
            onlyDc.Tick(4f);
            Eq(onlyDc.Acts[0].Kind, NodeKind.Relay, "RELAY before DATA CENTER (and a data center hop that would leave the trace too high is not started)");
        }

        private static void CheckReach()
        {
            var rig = new Rig();
            rig.World.HumanCount = 0; rig.Ai = true;
            rig.Observe(Obs(NodeKind.SamC2, 40, 30000f, 0f)); // beyond the 18 km truck reach
            rig.Tick(10f);
            Eq(rig.Acts.Count, 0, "a revealed node out of reach is not attempted");
            Eq(rig.Brain.Last.Why, CyberWatchWhy.NoNode, "and the reason is named");

            var down = new Rig();
            down.World.HumanCount = 0; down.Ai = true;
            down.Anchors.Set(AnchorKind.EwTruck, 0, 0f, true, 0f);
            down.Observe(Obs(NodeKind.Radar, 70, 1000f, 0f));
            down.Tick(10f);
            Eq(down.Acts.Count, 0, "a down EW truck starts nothing");
        }

        private static void CheckBurnAtSeventy()
        {
            var rig = new Rig();
            rig.World.HumanCount = 1;
            rig.Observe(Obs(NodeKind.SamC2, 40, 3000f, 0f));
            rig.Tick(130f);
            Eq(rig.Acts[0].Action, CyberWatchAction.Hop, "a hop first");
            CyberWatchPlan burn = rig.Acts.Find(p => p.Action == CyberWatchAction.Burn);
            Eq(burn.Action, CyberWatchAction.Burn, "OVERLORD burns the node when the trace passes 70 %");
            Eq(burn.Code, WatchCode.CyberBurnTrace, "burn reason");
            TestAssert.That(burn.B >= 70, "the reason carries the trace (" + burn.B + " %)");
            TestAssert.That(burn.Reason.Contains("TRACE"), "the reason string names the trace: " + burn.Reason);
            Eq(rig.Ports.Posted.Count, 1, "one package on the board");
            TestAssert.That(rig.Ports.Posted[0].EndsWith("by OVERLORD", StringComparison.Ordinal), "posted by the reserved identity");
            Eq(rig.Ports.Charged, 0, "OVERLORD pays nothing: no wallet");
            Eq(rig.Ports.Balance, 1000, "no CR moved");
            Eq(rig.Desk.Network.Of(Overlord) == null, true, "the burn ended its intrusion");
            Eq(rig.Traced(), false, "never traced");

            var full = new Rig();
            full.World.HumanCount = 1; full.World.Room = false;
            full.Observe(Obs(NodeKind.SamC2, 40, 3000f, 0f));
            full.Tick(200f);
            Eq(full.Acts.Exists(p => p.Action == CyberWatchAction.Burn), false, "no package room: no burn");
            Eq(full.Acts.Exists(p => p.Action == CyberWatchAction.Drop), true, "it leaves with a DROP instead");
            Eq(full.Traced(), false, "and is never traced");
        }

        private static void CheckDropBeforeTraced()
        {
            // A faction with no humans has no pilots to fly a package: it holds, then drops at the last safe moment.
            var rig = new Rig();
            rig.World.HumanCount = 0; rig.World.Room = false; rig.Ai = true;
            rig.Observe(Obs(NodeKind.SamC2, 40, 3000f, 0f), Obs(NodeKind.Radar, 41, 6000f, 0f));
            rig.Tick(900f);
            Eq(rig.Traced(), false, "an AI faction is never TRACED in 15 minutes");
            TestAssert.That(rig.Brain.Drops >= 3, "it dropped repeatedly (" + rig.Brain.Drops + ")");
            Eq(rig.Brain.Burns, 0, "an AI faction never burns: nothing on the board is for it");
            Eq(rig.Acts.Exists(p => p.Action == CyberWatchAction.Drop && p.Code == WatchCode.CyberDropTrace), true, "the drop says why");
            CyberWatchPlan drop = rig.Acts.Find(p => p.Action == CyberWatchAction.Drop);
            TestAssert.That(drop.B >= 60 && drop.B <= 99, "dropped with the trace high but not full (" + drop.B + " %)");
            // Gaps: AI factions run one action every 30 s at most.
            for (int i = 1; i < rig.ActionTimes.Count; i++) TestAssert.That(rig.ActionTimes[i] - rig.ActionTimes[i - 1] >= 30f, "AI gap " + (rig.ActionTimes[i] - rig.ActionTimes[i - 1]));

            // An enemy holding our data center speeds the trace x1.3; the safety margin scales with it.
            var fast = new Rig();
            fast.World.HumanCount = 0; fast.World.Room = false; fast.Ai = true;
            fast.Desk.EnemyTraceFactor = _ => 1.3f;
            fast.Observe(Obs(NodeKind.SamC2, 40, 3000f, 0f), Obs(NodeKind.Radar, 41, 6000f, 0f));
            fast.Tick(900f);
            Eq(fast.Traced(), false, "never traced even with the enemy's trace boost");
        }

        private static void CheckPilotsAndUplink()
        {
            var rig = new Rig();
            rig.World.HumanCount = 1; rig.World.PilotMeters = 25000f;
            rig.Observe(Obs(NodeKind.Radar, 41, 3000f, 0f));
            rig.Tick(100f);
            CyberWatchPlan burn = rig.Acts.Find(p => p.Action == CyberWatchAction.Burn);
            Eq(burn.Code, WatchCode.CyberBurnPilots, "a pilot 25 km from a held radar: burn it into a JAM RADAR package");
            TestAssert.That(burn.B == 25 && burn.Reason.Contains("25 KM"), "the reason names the distance: " + burn.Reason);
            TestAssert.That(rig.ActionTimes[rig.ActionTimes.Count - 1] - rig.ActionTimes[0] >= 45f, "only after the node was held for 45 s");

            var uplink = new Rig();
            uplink.World.HumanCount = 1; uplink.World.PilotMeters = 5000f;
            uplink.Observe(Obs(NodeKind.Uplink, 44, 3000f, 0f));
            uplink.Tick(80f);
            Eq(uplink.Acts.Exists(p => p.Action == CyberWatchAction.Burn), false, "a bird jam helps no pilot: an uplink is burned only by the trace rule");
            uplink.Tick(120f);
            Eq(uplink.Acts.Exists(p => p.Action == CyberWatchAction.Burn && p.Code == WatchCode.CyberBurnTrace), true, "the trace rule still applies to it");

            var far = new Rig();
            far.World.HumanCount = 1; far.World.PilotMeters = 90000f;
            far.Observe(Obs(NodeKind.Radar, 41, 3000f, 0f));
            far.Tick(100f);
            Eq(far.Acts.Exists(p => p.Action == CyberWatchAction.Burn), false, "no pilot near the node: nothing to burn for");
        }

        private static void CheckDeeper()
        {
            var rig = new Rig();
            rig.World.HumanCount = 1;
            // The relay is in truck reach; the SAM C2 is 12 km beyond the relay and out of truck reach.
            rig.Observe(Obs(NodeKind.Relay, 41, 15000f, 0f), Obs(NodeKind.SamC2, 40, 26000f, 0f));
            rig.Tick(120f);
            CyberWatchPlan deeper = rig.Acts.Find(p => p.Code == WatchCode.CyberDeeper);
            Eq(deeper.Action, CyberWatchAction.Hop, "OVERLORD chains from the held relay to the SAM C2 in its reach");
            Eq(deeper.Kind, NodeKind.SamC2, "the deeper target");
            TestAssert.That(deeper.Reason.StartsWith("HOP DEEPER TO SAM C2", StringComparison.Ordinal), deeper.Reason);
            Eq(rig.Traced(), false, "and still never traced");
        }

        private static void CheckYield()
        {
            var rig = new Rig();
            rig.World.HumanCount = 1;
            rig.Observe(Obs(NodeKind.Radar, 41, 3000f, 0f));
            rig.Tick(30f);
            TestAssert.That(rig.Desk.Network.Of(Overlord) != null, "OVERLORD holds an intrusion");
            rig.Brain.RecordHuman(rig.Ports.Clock);
            rig.Tick(4f);
            Eq(rig.Desk.Network.Of(Overlord) == null, true, "a human verb makes OVERLORD release its intrusion");
            Eq(rig.Acts[rig.Acts.Count - 1].Action == CyberWatchAction.Burn || rig.Acts[rig.Acts.Count - 1].Action == CyberWatchAction.Drop, true, "by burn or drop");
            int before = rig.Acts.Count;
            rig.Tick(55f);
            Eq(rig.Acts.Count, before, "solo: nothing for 60 s after the human's verb");
            rig.Tick(30f);
            TestAssert.That(rig.Acts.Count > before, "then OVERLORD works again");

            var group = new Rig();
            group.World.HumanCount = 3;
            group.Observe(Obs(NodeKind.Radar, 41, 3000f, 0f));
            group.Brain.RecordHuman(1f);
            group.Tick(290f);
            Eq(group.Acts.Count, 0, "2 or more humans: out for 300 s");
            group.Tick(30f);
            TestAssert.That(group.Acts.Count > 0, "and back afterwards");
        }

        private static void CheckSlotsAndFree()
        {
            // One intrusion stays free for a human: with two humans (cap 2) and a human intrusion running, OVERLORD does not start.
            var rig = new Rig();
            rig.World.HumanCount = 2;
            rig.Observe(Obs(NodeKind.Radar, 41, 3000f, 0f), Obs(NodeKind.Radar, 42, 4000f, 0f));
            rig.Ports.HumanCount = 2;
            Eq(rig.Desk.Hop(Human, rig.IdOf(NodeKind.Radar, 41)).Outcome, CyberOutcome.Started, "a human starts an intrusion");
            rig.World.HumanCount = 2;
            rig.Tick(100f);
            Eq(rig.Acts.Exists(p => p.Action == CyberWatchAction.Hop), false, "no slot for OVERLORD while a human holds one of two");
            Eq(rig.Desk.Network.Of(Overlord) == null, true, "none started");
            Eq(rig.Brain.Last.Why, CyberWatchWhy.NoSlot, "and it says so");
        }

        private static void CheckPacing()
        {
            var rig = new Rig();
            rig.World.HumanCount = 1;
            rig.Observe(Obs(NodeKind.SamC2, 40, 3000f, 0f), Obs(NodeKind.Radar, 41, 4000f, 0f), Obs(NodeKind.Relay, 42, 2000f, 0f));
            rig.Tick(900f);
            TestAssert.That(rig.ActionTimes.Count >= 4, "OVERLORD kept working (" + rig.ActionTimes.Count + " actions)");
            for (int i = 1; i < rig.ActionTimes.Count; i++) TestAssert.That(rig.ActionTimes[i] - rig.ActionTimes[i - 1] >= 10f, "at most one action per 10 s: gap " + (rig.ActionTimes[i] - rig.ActionTimes[i - 1]));
            Eq(rig.Traced(), false, "never traced in 15 minutes");
            TestAssert.That(WatchPacer.AiGap() == 30f, "the AI gap is 30 s");
        }
    }
}
