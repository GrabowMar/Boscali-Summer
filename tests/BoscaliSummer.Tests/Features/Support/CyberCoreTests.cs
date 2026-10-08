using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>M3a CYBER pure core, part 2: effect timers, packages, rules, the intrusion state machine and the desk (spec §1.2, §1.3).</summary>
    internal static class CyberCoreTests
    {
        private const int Us = 7, Them = 9;
        private const ulong Op = 111UL, Op2 = 222UL, Op3 = 333UL;

        public static void Run()
        {
            CheckEffects();
            CheckRules();
            CheckIntrusion();
            CheckDeskStartAndFog();
            CheckDeskHoldBurnDrop();
            CheckDeskTraceAndUpkeep();
            CheckDeskCaps();
            CheckSpaceJam();
        }

        private static void Near(float actual, float expected, string message, float tolerance = 0.02f) =>
            TestAssert.That(Math.Abs(actual - expected) <= tolerance, message + " (got " + actual + ", want " + expected + ")");

        private static CyberNode Node(int id, NodeKind kind, float x, float z, float front = 0f, uint unit = 0u) =>
            new CyberNode(id, kind, x, z, unit, front, Them);

        // ---- Effects ---------------------------------------------------------------------------

        private static void CheckEffects()
        {
            var book = new CyberEffectBook();
            var radar = Node(5, NodeKind.Radar, 1000f, 1000f, unit: 77u);
            TestAssert.Eq(book.RadarRangeFactor(Them, 77u, 1000f, 1000f, 0f), 1f, "no effect, no jam");
            TestAssert.That(book.Add(CyberPackages.Hold(radar, Us)), "hold added");
            Near(book.RadarRangeFactor(Them, 77u, 0f, 0f, 5f), 0.4f, "the held radar is at 40 % range");
            Near(book.RadarRangeFactor(Them, 78u, 1000f, 1000f, 5f), 1f, "another radar is untouched by a unit hold");
            Near(book.RadarRangeFactor(Us, 77u, 0f, 0f, 5f), 1f, "the owner's own radar is untouched");
            book.ReleaseHold(5, 6f);
            Near(book.RadarRangeFactor(Them, 77u, 0f, 0f, 7f), 1f, "release restores the radar at once");

            var sam = Node(6, NodeKind.SamC2, 0f, 0f);
            book.Add(CyberPackages.Hold(sam, Us));
            TestAssert.That(book.AnyLaunchBlock(10f), "sam hold blocks launches");
            TestAssert.That(book.LaunchBlocked(Them, true, 2999f, 0f, 10f), "a SAM inside 3 km is blocked");
            TestAssert.That(!book.LaunchBlocked(Them, true, 3001f, 0f, 10f), "a SAM outside 3 km is not");
            TestAssert.That(!book.LaunchBlocked(Them, false, 100f, 0f, 10f), "a SAM block does not stop an AAA gun");
            TestAssert.That(!book.LaunchBlocked(Us, true, 100f, 0f, 10f), "friendly SAMs keep firing");
            book.ReleaseHold(6, 20f);
            TestAssert.That(book.LaunchBlocked(Them, true, 100f, 0f, 29f), "SAMs stay blocked for 10 s after release");
            TestAssert.That(!book.LaunchBlocked(Them, true, 100f, 0f, 30f), "then they reacquire");
            book.Expire(30f);
            TestAssert.Eq(book.Count, 0, "expired effects leave the book");

            var relay = Node(7, NodeKind.Relay, 0f, 0f);
            book.Add(CyberPackages.Hold(relay, Us));
            TestAssert.That(book.ShareBlocked(Them, 7999f, 0f, 1f) && !book.ShareBlocked(Them, 8001f, 0f, 1f), "relay silences sharing inside 8 km");
            TestAssert.That(book.AnyShareBlock(1f), "share prefilter");

            var uplink = Node(8, NodeKind.Uplink, 0f, 0f);
            book.Add(CyberPackages.Hold(uplink, Us));
            Near(book.CooldownFactor(Them, 1f), 1.5f, "bird jam +50 % cooldown");
            Near(book.OpticalFactor(Them, 1f), 0.5f, "bird jam halves the optical footprint");
            Near(book.CooldownFactor(Us, 1f), 1f, "the jammer's own birds are fine");

            var dc = Node(9, NodeKind.DataCenter, 0f, 0f);
            book.Add(CyberPackages.Hold(dc, Us));
            Near(book.TraceFactor(Them, 1f), 1.3f, "holding their data center speeds their trace");
            Near(book.TraceFactor(Us, 1f), 1f, "not ours");

            // Packages: area effect, every other faction, timed, x1.5 on EXPLOIT.
            CyberPackages.TryOf(NodeKind.Radar, out PackageDef jam);
            var pk = CyberPackages.Package(jam, 41, 500f, 500f, Us, false, 100f);
            var fresh = new CyberEffectBook();
            fresh.Add(pk);
            Near(fresh.RadarRangeFactor(Them, 1u, 600f, 500f, 189f), 0.4f, "JAM RADAR hits every radar inside 1.5 km");
            Near(fresh.RadarRangeFactor(Them, 1u, 2100f, 500f, 189f), 1f, "outside 1.5 km");
            Near(fresh.RadarRangeFactor(Them, 1u, 600f, 500f, 190f), 1f, "JAM RADAR lasts 90 s");
            Near(fresh.RadarRangeFactor(Us, 1u, 600f, 500f, 150f), 1f, "not on the firing faction");
            CyberPackages.TryOf(NodeKind.Uplink, out PackageDef bird);
            var exploit = CyberPackages.Package(bird, 42, 0f, 0f, Us, true, 0f);
            Near(exploit.Until, 270f, "EXPLOIT makes BIRD JAM 3 min x1.5");
            TestAssert.Eq(CyberPackages.All.Count, 5, "five packages");
            TestAssert.That(CyberPackages.TryOfAction(SupportActionId.CyberBlackout, out PackageDef black) && black.Node == NodeKind.DataCenter && black.Radius == 12000f, "blackout is the 12 km data center package");
            TestAssert.That(!CyberPackages.TryOfAction(SupportActionId.Artillery, out _), "the rod is not a CYBER package");
            TestAssert.Eq(jam.Payoff(false), "RADAR RANGE -60 % WITHIN 1.5 KM · 1:30", "payoff words");

            var full = new CyberEffectBook();
            for (int i = 0; i < CyberEffectBook.Capacity; i++) TestAssert.That(full.Add(CyberPackages.Package(jam, 1000 + i, 0f, 0f, Us, false, 0f)), "fill " + i);
            TestAssert.That(!full.Add(CyberPackages.Package(jam, 5000, 0f, 0f, Us, false, 0f)), "a full book refuses");
            TestAssert.That(full.Add(CyberPackages.Package(jam, 1000, 9f, 9f, Us, false, 0f)), "the same source replaces itself");
        }

        // ---- Rules -----------------------------------------------------------------------------

        private static void CheckRules()
        {
            TestAssert.Eq(CyberRules.IntrusionCap(1), 2, "two intrusions");
            TestAssert.Eq(CyberRules.IntrusionCap(5), 3, "three at 5 humans");
            Near(CyberRules.HopSeconds(NodeKind.Radar, false), 20f, "radar hop");
            Near(CyberRules.HopSeconds(NodeKind.Relay, false), 20f, "relay hop");
            Near(CyberRules.HopSeconds(NodeKind.SamC2, false), 30f, "sam hop");
            Near(CyberRules.HopSeconds(NodeKind.Uplink, false), 30f, "uplink hop");
            Near(CyberRules.HopSeconds(NodeKind.DataCenter, false), 40f, "data center hop");
            Near(CyberRules.HopSeconds(NodeKind.Uplink, true), 22.5f, "EXPLOIT hop is -25 %");
            Near(CyberRules.HopTraceRate(NodeKind.Radar), 1.5f, "radar trace rate");
            Near(CyberRules.HopTraceRate(NodeKind.SamC2), 1.5f, "sam trace rate");
            Near(CyberRules.HopTraceRate(NodeKind.DataCenter), 1.5f, "data center trace rate");
            TestAssert.That(CyberRules.Exploit(NodeKind.Uplink) && !CyberRules.Exploit(NodeKind.Radar), "only an uplink is an EXPLOIT target");
            TestAssert.Eq(CyberRules.StartCostOf(false), 6, "a hop costs 6 allocation");
            TestAssert.Eq(CyberRules.StartCostOf(true), 5, "start cost on EXPLOIT");
            TestAssert.Eq(CyberRules.Upkeep(0, false), 0, "no nodes, no upkeep");
            TestAssert.Eq(CyberRules.Upkeep(1, false), 0, "held nodes cost nothing (the director keeps them)");
            TestAssert.Eq(CyberRules.Upkeep(4, false), 0, "four nodes");
            TestAssert.Eq(CyberRules.Upkeep(4, true), 0, "data center four nodes");
            Near(CyberRules.ClampFactor(float.NaN), 1f, "NaN factor");
            Near(CyberRules.ClampFactor(99f), 3f, "factor ceiling");
            Near(CyberRules.ClampFactor(0f), 0.25f, "factor floor");
            TestAssert.Eq(CyberWords.Of(CyberOutcome.LowCredit, 30), "NEGATIVE: LOW ALLOCATION — NEED 30", "low allocation words");
            TestAssert.Eq(CyberWords.Of(CyberOutcome.TruckLocked, 42), "NEGATIVE: EW TRUCK TRACED — LOCKED 42 S", "locked words");
            TestAssert.That(CyberWords.Of(CyberOutcome.NoTarget).StartsWith("NEGATIVE: ", StringComparison.Ordinal), "refusals use the NEGATIVE voice");
            for (byte b = 1; b <= CyberWords.MaxOutcome; b++) TestAssert.That(CyberWords.Of((CyberOutcome)b, 1).Length > 0, "outcome " + b + " has words");
        }

        // ---- Intrusion -------------------------------------------------------------------------

        private static void CheckIntrusion()
        {
            var x = new CyberIntrusion(1, Op, 0, 0f);
            TestAssert.Eq(x.Phase, IntrusionPhase.Idle, "a new intrusion is idle");
            TestAssert.That(x.TryBeginHop(5, NodeKind.Radar, 20f, 0f), "begin a hop");
            TestAssert.Eq(x.Phase, IntrusionPhase.Hopping, "hopping");
            TestAssert.That(!x.TryBeginHop(6, NodeKind.Radar, 20f, 0f), "one hop at a time");
            IntrusionStep s = x.Advance(10f, 1f);
            Near(x.Trace, 7.5f, "a 10 s gap counts only the last 5 s: 5 s x 1.5 %/s", 0.05f);
            x = new CyberIntrusion(1, Op, 0, 0f);
            x.TryBeginHop(5, NodeKind.Radar, 20f, 0f);
            for (int t = 1; t <= 19; t++) x.Advance(t, 1f);
            Near(x.Trace, 28.5f, "19 s of radar hop is 28.5 %");
            Near(x.HopProgress(19f), 0.95f, "hop progress");
            s = x.Advance(20f, 1f);
            TestAssert.Eq(s.Arrived, 5, "arrived at 20 s");
            Near(x.Trace, 30f, "30 % at arrival");
            TestAssert.Eq(x.Phase, IntrusionPhase.Holding, "holding");
            TestAssert.That(x.Holds(5) && x.HeldCount == 1 && x.Hops == 1, "node held, one hop counted");
            for (int t = 21; t <= 40; t++) s = x.Advance(t, 1f);
            Near(x.Trace, 40f, "a held node adds 0.5 %/s");

            // Upkeep ticks: one per 10 s held.
            x = new CyberIntrusion(2, Op, 0, 0f);
            x.TryBeginHop(5, NodeKind.Radar, 20f, 0f);
            int ticks = 0;
            for (int t = 1; t <= 50; t++) ticks += x.Advance(t, 0f).UpkeepTicks;
            TestAssert.Eq(ticks, 3, "arrival at 20 s, upkeep at 30, 40, 50 s");

            // Deeper: the hop trace rate stacks with the held-node rate.
            x = new CyberIntrusion(3, Op, 0, 0f);
            x.TryBeginHop(5, NodeKind.Radar, 20f, 0f);
            for (int t = 1; t <= 20; t++) x.Advance(t, 1f);
            Near(x.Trace, 30f, "first node");
            TestAssert.That(x.TryBeginHop(6, NodeKind.Relay, 20f, 20f), "go deeper");
            for (int t = 21; t <= 40; t++) x.Advance(t, 1f);
            Near(x.Trace, 30f + 20f * (1.5f + 0.5f), "deeper hop: 1.5 %/s plus 0.5 %/s for the held node", 0.05f);
            TestAssert.Eq(x.HeldCount, 2, "two held");

            // Traced: trace hits 100 before arrival (an 80 s hop at 1.5 %/s would reach 120 %).
            x = new CyberIntrusion(4, Op, 0, 0f);
            x.TryBeginHop(9, NodeKind.DataCenter, 80f, 0f);
            bool traced = false; int arrived = 0;
            for (int t = 1; t <= 80; t++) { s = x.Advance(t, 1f); traced |= s.Traced; arrived += s.Arrived; if (traced) break; }
            TestAssert.That(traced && arrived == 0, "traced before the node arrives");
            TestAssert.Eq(x.EndReason, IntrusionEnd.Traced, "end reason");
            TestAssert.Eq(x.HeldCount, 0, "nothing held after a trace");
            Near(x.Trace, 100f, "trace clamps at 100");
            TestAssert.Eq(x.Advance(60f, 1f).Arrived, 0, "an ended intrusion does nothing");
            TestAssert.That(!x.TryBeginHop(1, NodeKind.Radar, 5f, 60f), "an ended intrusion cannot hop");

            // Factor scales the gain; a hitch cannot trace a pilot out.
            x = new CyberIntrusion(5, Op, 0, 0f);
            x.TryBeginHop(5, NodeKind.Radar, 100f, 0f);
            x.Advance(10f, 0.5f);
            Near(x.Trace, 3.75f, "factor 0.5, only the last 5 s count: 5 s x 1.5 %/s x 0.5", 0.05f);

            // Burn / drop semantics.
            x = new CyberIntrusion(6, Op, 0, 0f);
            x.TryBeginHop(5, NodeKind.Radar, 1f, 0f);
            x.Advance(1f, 1f);
            x.TryBeginHop(6, NodeKind.Relay, 1f, 1f);
            x.Advance(2f, 1f);
            TestAssert.Eq(x.HeldCount, 2, "two held");
            TestAssert.That(x.Release(5, IntrusionEnd.Burned) && !x.Ended, "releasing one of two keeps the intrusion");
            TestAssert.That(!x.Release(5, IntrusionEnd.Burned), "a released node cannot be released twice");
            TestAssert.That(x.Release(6, IntrusionEnd.Dropped) && x.Ended && x.EndReason == IntrusionEnd.Dropped, "releasing the last ends it");
        }

        // ---- Desk ------------------------------------------------------------------------------

        private sealed class Ports : ICyberPorts
        {
            public float Clock; public int HumanCount = 1, Balance = 1000;
            public bool BoardOpen = true, Frozen;
            public readonly List<string> Posted = new List<string>();
            public float Now => Clock;
            public int Humans => HumanCount;
            public int OwnerKey = Us;
            public int Owner => OwnerKey;
            public CyberOutcome TrySpend(ulong op, int cr, out int detail)
            {
                detail = 0;
                if (Frozen) return CyberOutcome.Frozen;
                if (Balance < cr) { detail = cr; return CyberOutcome.LowCredit; }
                Balance -= cr;
                return CyberOutcome.None;
            }
            public void Refund(ulong op, int cr) { Balance += cr; }
            public CyberOutcome PostPackage(ulong op, in CyberNode node, in PackageDef def, bool exploit, float effort)
            {
                if (!BoardOpen) return CyberOutcome.BoardFull;
                Posted.Add(def.Label + "@" + node.Id + "x" + effort + (exploit ? "!" : ""));
                return CyberOutcome.None;
            }
        }

        private sealed class Rig
        {
            public readonly Ports Ports = new Ports();
            public readonly CyberAnchorSet Anchors;
            public readonly CyberDesk Desk;
            public readonly List<CyberEvent> Events = new List<CyberEvent>();
            public Rig(int trucks = 1, int dataCenters = 0, int owner = Us)
            {
                Ports.OwnerKey = owner;
                Anchors = new CyberAnchorSet(trucks, dataCenters);
                for (int i = 0; i < trucks; i++) Anchors.SetPosition(AnchorKind.EwTruck, i, 0f, 0f);
                Desk = new CyberDesk(Ports, Anchors);
                Desk.Happened += Events.Add;
            }
            public void Observe(params NodeObservation[] o) => Desk.Refresh(o);
            public void Run(float seconds)
            {
                for (float t = 0; t < seconds; t += 1f) { Ports.Clock += 1f; Desk.Tick(); }
            }
            public int IdOf(NodeKind kind, uint key) { Desk.Ids.TryGet(kind, key, out int id); return id; }
        }

        private static NodeObservation Obs(NodeKind kind, uint key, float x, float z, bool sighted = true, bool gone = false, float front = 0f) =>
            new NodeObservation(new NodeSeed(kind, key, x, z, front), Them, sighted, gone);

        private static void CheckDeskStartAndFog()
        {
            // Fog: no id until a node first becomes visible, and its position freezes at the last sighted refresh.
            var fz = new Rig();
            fz.Observe(Obs(NodeKind.Radar, 70, 10000f, 0f, sighted: false));
            TestAssert.That(!fz.Desk.Ids.TryGet(NodeKind.Radar, 70u, out _), "an unsighted node is given no id");
            fz.Observe(Obs(NodeKind.Radar, 70, 4000f, 0f));
            TestAssert.Eq(fz.Desk.Visible.Count, 1, "sighted: listed");
            fz.Observe(Obs(NodeKind.Radar, 70, 9000f, 500f, sighted: false));
            Near(fz.Desk.Visible[0].X, 4000f, "an unsighted refresh does not move the node (frozen at the last sighting)");
            Near(fz.Desk.Visible[0].Z, 0f, "z frozen too");

            var rig = new Rig();
            rig.Observe(Obs(NodeKind.Radar, 70, 10000f, 0f, sighted: false));
            TestAssert.Eq(rig.Desk.Visible.Count, 0, "an unsighted node is not listed (fog)");
            int hidden = rig.IdOf(NodeKind.Radar, 70);
            CyberResult miss = rig.Desk.Hop(Op, hidden), none = rig.Desk.Hop(Op, 424242);
            TestAssert.Eq(miss.Outcome, CyberOutcome.NoTarget, "a hidden node answers NO TARGET");
            TestAssert.Eq(none.Outcome, CyberOutcome.NoTarget, "an unknown id answers the same");
            TestAssert.Eq(miss.Words, none.Words, "identical words for hidden and unknown ids");
            TestAssert.Eq(rig.Ports.Balance, 1000, "a refusal charges nothing");

            rig.Observe(Obs(NodeKind.Radar, 70, 10000f, 0f), Obs(NodeKind.Radar, 71, 30000f, 0f), Obs(NodeKind.SamC2, 72, 5000f, 5000f, sighted: false));
            TestAssert.Eq(rig.Desk.Visible.Count, 2, "two sighted nodes listed, the unsighted one stays hidden");
            int near = rig.IdOf(NodeKind.Radar, 70), far = rig.IdOf(NodeKind.Radar, 71);
            TestAssert.Eq(rig.Desk.Hop(Op, far).Outcome, CyberOutcome.OutOfReach, "a visible node beyond the truck reach");
            TestAssert.Eq(rig.Ports.Balance, 1000, "out of reach charges nothing");

            CyberResult go = rig.Desk.Hop(Op, near);
            TestAssert.Eq(go.Outcome, CyberOutcome.Started, "start an intrusion");
            TestAssert.Eq(go.Charged, 6, "6 allocation");
            TestAssert.Eq(rig.Ports.Balance, 994, "wallet charged");
            TestAssert.Eq(rig.Desk.Network.Of(Op).Phase, IntrusionPhase.Hopping, "hopping");
            TestAssert.Eq(rig.Desk.Hop(Op, near).Outcome, CyberOutcome.StillHopping, "a second hop while hopping");
            rig.Run(21f);
            TestAssert.That(rig.Desk.Network.Holds(near), "node held after 20 s");
            TestAssert.Eq(rig.Events[0].Kind, CyberEventKind.HopStarted, "hop event");
            TestAssert.That(rig.Events.Exists(e => e.Kind == CyberEventKind.NodeHeld && e.NodeId == near), "held event");
            Near(rig.Desk.Effects.RadarRangeFactor(Them, 70u, 0f, 0f, rig.Ports.Clock), 0.4f, "the held radar is jammed", 0.001f);

            // A node that ages out of fog stays visible while held.
            rig.Run(100f);
            rig.Observe(Obs(NodeKind.Radar, 70, 10000f, 0f, sighted: false));
            TestAssert.Eq(rig.Desk.Visible.Count, 1, "held nodes never leave the list");

            // Cost and refusals: low credit, frozen.
            var poor = new Rig();
            poor.Ports.Balance = 5;
            poor.Observe(Obs(NodeKind.Radar, 70, 1000f, 0f));
            CyberResult low = poor.Desk.Hop(Op, poor.IdOf(NodeKind.Radar, 70));
            TestAssert.Eq(low.Outcome, CyberOutcome.LowCredit, "5 allocation cannot start");
            TestAssert.Eq(low.Detail, 6, "need 6");
            TestAssert.Eq(poor.Desk.Network.Active.Count, 0, "nothing started");
            poor.Ports.Balance = 100; poor.Ports.Frozen = true;
            TestAssert.Eq(poor.Desk.Hop(Op, poor.IdOf(NodeKind.Radar, 70)).Outcome, CyberOutcome.Frozen, "frozen wallet");

            // EXPLOIT: an uplink hop is 22.5 s and costs 5 allocation.
            var ex = new Rig();
            ex.Observe(Obs(NodeKind.Uplink, 80, 1000f, 0f));
            CyberResult up = ex.Desk.Hop(Op, ex.IdOf(NodeKind.Uplink, 80));
            TestAssert.Eq(up.Charged, 5, "EXPLOIT start cost");
            Near(ex.Desk.Network.Of(Op).HopEndsAt, 22.5f, "EXPLOIT hop time");

            // No truck standing: TRUCK DOWN.
            var dead = new Rig();
            dead.Anchors.Set(AnchorKind.EwTruck, 0, 0f, true, 0f);
            dead.Observe(Obs(NodeKind.Radar, 70, 1000f, 0f));
            TestAssert.Eq(dead.Desk.Hop(Op, dead.IdOf(NodeKind.Radar, 70)).Outcome, CyberOutcome.TruckDown, "a down truck cannot start");
            var none2 = new CyberDesk(new Ports(), new CyberAnchorSet(0, 0));
            none2.Refresh(new[] { Obs(NodeKind.Radar, 70, 1000f, 0f) });
            TestAssert.Eq(none2.Hop(Op, 1).Outcome, CyberOutcome.Unavailable, "a faction with no EW assets is unavailable");
        }

        private static void CheckDeskHoldBurnDrop()
        {
            // A SAM C2 hop straight from the truck (30 s at 1.5 %/s = 45 %): the block runs while held, BURN posts SAM NET DOWN.
            var a = new Rig();
            a.Observe(Obs(NodeKind.SamC2, 72, 12000f, 0f));
            int sam = a.IdOf(NodeKind.SamC2, 72);
            a.Desk.Hop(Op, sam);
            a.Run(31f);
            TestAssert.That(a.Desk.Network.Holds(sam), "SAM C2 held");
            TestAssert.That(a.Desk.Effects.LaunchBlocked(Them, true, 12000f, 100f, a.Ports.Clock), "SAMs in the cluster cannot launch");
            CyberResult burn = a.Desk.Burn(Op, sam);
            TestAssert.Eq(burn.Outcome, CyberOutcome.Burned, "burn");
            TestAssert.Eq(a.Ports.Posted.Count, 1, "one package posted");
            TestAssert.Eq(a.Ports.Posted[0], "SAM NET DOWN@" + sam + "x1", "package label, node and effort = hops");
            TestAssert.That(!a.Desk.Network.Holds(sam) && a.Desk.Network.Of(Op) == null, "the last node released ends the intrusion");
            TestAssert.That(a.Desk.Effects.LaunchBlocked(Them, true, 12000f, 100f, a.Ports.Clock + 9f), "the block lingers 10 s");
            TestAssert.That(!a.Desk.Effects.LaunchBlocked(Them, true, 12000f, 100f, a.Ports.Clock + 10f), "then SAMs reacquire");
            TestAssert.Eq(a.Desk.Burn(Op, sam).Outcome, CyberOutcome.NoTarget, "burning twice is NO TARGET");

            // Radar, then deeper to a relay (20 s at 2 %/s with the held node): burn one, keep the other, drop it.
            var rig = new Rig();
            rig.Observe(Obs(NodeKind.Radar, 70, 5000f, 0f), Obs(NodeKind.Relay, 72, 12000f, 0f), Obs(NodeKind.Radar, 73, 40000f, 0f));
            int radar = rig.IdOf(NodeKind.Radar, 70), relay = rig.IdOf(NodeKind.Relay, 72), far = rig.IdOf(NodeKind.Radar, 73);
            rig.Desk.Hop(Op, radar);
            rig.Run(21f);
            TestAssert.Eq(rig.Desk.Hop(Op, far).Outcome, CyberOutcome.OutOfReach, "deeper needs a held node within 12 km");
            TestAssert.Eq(rig.Desk.Hop(Op, relay).Outcome, CyberOutcome.HopStarted, "go deeper from the held radar (7 km)");
            TestAssert.Eq(rig.Ports.Balance, 994, "deeper is free");
            rig.Run(21f);
            TestAssert.That(rig.Desk.Network.Holds(relay) && rig.Desk.Effects.ShareBlocked(Them, 12000f, 100f, rig.Ports.Clock), "relay held, sharing silenced near it");
            TestAssert.Eq(rig.Desk.Burn(Op2, radar).Outcome, CyberOutcome.NoTarget, "burning another operator's node is NO TARGET");
            TestAssert.Eq(rig.Desk.Burn(Op, relay).Outcome, CyberOutcome.Burned, "burn the relay");
            TestAssert.Eq(rig.Ports.Posted[0], "SPOOF IFF@" + relay + "x2", "two hops of effort");
            TestAssert.That(!rig.Desk.Network.Holds(relay) && rig.Desk.Network.Holds(radar), "only the burned node is released");
            TestAssert.That(!rig.Desk.Effects.ShareBlocked(Them, 12000f, 100f, rig.Ports.Clock), "burn ends the hold at once");

            rig.Ports.BoardOpen = false;
            TestAssert.Eq(rig.Desk.Burn(Op, radar).Outcome, CyberOutcome.BoardFull, "board full");
            TestAssert.That(rig.Desk.Network.Holds(radar), "nothing released when the board is full");
            TestAssert.Eq(rig.Desk.Drop(Op, radar).Outcome, CyberOutcome.Dropped, "drop still works");
            TestAssert.Eq(rig.Desk.Network.Of(Op), null, "the last node released ends the intrusion");
            Near(rig.Desk.Effects.RadarRangeFactor(Them, 70u, 0f, 0f, rig.Ports.Clock), 1f, "drop restores the radar", 0.001f);
            TestAssert.Eq(rig.Desk.Drop(Op, 0).Outcome, CyberOutcome.NoTarget, "nothing to drop");

            // DROP 0 ends the whole intrusion; the node leaves the list once the sighting and the 30 s linger are over.
            var two = new Rig();
            two.Observe(Obs(NodeKind.Radar, 70, 5000f, 0f), Obs(NodeKind.Relay, 72, 12000f, 0f));
            two.Desk.Hop(Op, two.IdOf(NodeKind.Radar, 70));
            two.Run(21f);
            two.Desk.Hop(Op, two.IdOf(NodeKind.Relay, 72));
            two.Run(21f);
            TestAssert.Eq(two.Desk.Network.Of(Op).HeldCount, 2, "two held");
            TestAssert.Eq(two.Desk.Drop(Op, 0).Outcome, CyberOutcome.Dropped, "drop the whole intrusion");
            TestAssert.Eq(two.Desk.Network.Of(Op), null, "intrusion gone");
            TestAssert.Eq(two.Events.FindAll(e => e.Kind == CyberEventKind.Released).Count, 2, "both nodes released");
            two.Ports.Clock += 200f;
            two.Observe(Obs(NodeKind.Radar, 70, 5000f, 0f, sighted: false));
            TestAssert.Eq(two.Desk.Visible.Count, 0, "long after the sighting and the 30 s linger the node is fogged again");
        }

        private static void CheckDeskTraceAndUpkeep()
        {
            // Trace: a node held too long traces out (30 % at arrival, then 0.5 %/s: 100 % at about 160 s); the truck locks 90 s.
            var rig = new Rig();
            rig.Observe(Obs(NodeKind.Radar, 90, 4000f, 0f));
            int dc = rig.IdOf(NodeKind.Radar, 90);
            rig.Desk.Hop(Op, dc);
            rig.Run(150f);
            TestAssert.That(!rig.Events.Exists(e => e.Kind == CyberEventKind.Traced), "still holding at 150 s");
            rig.Run(20f);
            TestAssert.That(rig.Events.Exists(e => e.Kind == CyberEventKind.Traced), "traced at about 160 s");
            TestAssert.Eq(rig.Desk.Network.Of(Op), null, "intrusion gone");
            TestAssert.Eq(rig.Desk.Hop(Op, dc).Outcome, CyberOutcome.TruckLocked, "the traced truck is locked");
            TestAssert.Eq(rig.Desk.Hop(Op, dc).Detail, (int)Math.Ceiling(rig.Anchors.LockUntil(0) - rig.Ports.Clock), "locked seconds");
            TestAssert.That(rig.Anchors.RevealUntil(0) > rig.Ports.Clock, "the truck is revealed to the enemy");
            rig.Run(95f);
            TestAssert.Eq(rig.Desk.Hop(Op, dc).Outcome, CyberOutcome.Started, "the lock ends after 90 s");

            // Own data center: -20 % trace (data center hop 1.5 %/s x 0.8 = 1.2 %/s: 40 s = 48 %, no trace).
            var safe = new Rig(1, 1);
            safe.Observe(Obs(NodeKind.DataCenter, 90, 4000f, 0f));
            safe.Desk.Hop(Op, safe.IdOf(NodeKind.DataCenter, 90));
            safe.Run(40f);
            TestAssert.That(safe.Desk.Network.Holds(safe.IdOf(NodeKind.DataCenter, 90)), "with our data center standing the 40 s hop completes at 48 %");

            // An enemy holding our data center node makes our trace x1.3 (and with ours standing 1.04).
            // The hold lives in the HOLDER's desk (them); our desk reads it through the cross-faction port the runtime folds.
            var boosted = new Rig();
            var holder = new Rig(1, 0, Them);
            boosted.Desk.EnemyTraceFactor = n => holder.Desk.Effects.TraceFactor(Us, n);
            Near(boosted.Desk.TraceFactor(0f), 1f, "nobody holds our data center node yet");
            holder.Desk.Effects.Add(CyberPackages.Hold(new CyberNode(1, NodeKind.DataCenter, 0f, 0f, 0u, 0f, Us), Them));
            Near(boosted.Desk.TraceFactor(0f), 1.3f, "enemy holds our data center node");
            Near(holder.Desk.TraceFactor(0f), 1f, "holding their node does not speed the holder's own trace");

            // Two desks: faction Them holds Us's data center, so Us's intrusion traces 1.3x faster than an unheld twin.
            var calm = new Rig(); var hit = new Rig(); var jammer = new Rig(1, 0, Them);
            hit.Desk.EnemyTraceFactor = n => jammer.Desk.Effects.TraceFactor(Us, n);
            jammer.Desk.Effects.Add(CyberPackages.Hold(new CyberNode(2, NodeKind.DataCenter, 0f, 0f, 0u, 0f, Us), Them));
            foreach (Rig r in new[] { calm, hit })
            {
                r.Observe(Obs(NodeKind.Radar, 70, 5000f, 0f));
                r.Desk.Hop(Op, r.IdOf(NodeKind.Radar, 70));
                r.Run(10f);
            }
            float calmTrace = calm.Desk.Network.Of(Op).Trace, hitTrace = hit.Desk.Network.Of(Op).Trace;
            Near(hitTrace / calmTrace, 1.3f, "the held data center raises the enemy trace by 30 %", 0.01f);

            // Upkeep (OPS FRONTS S2): holding a node costs nothing; the director keeps held nodes, an empty wallet drops nothing.
            var up = new Rig();
            up.Observe(Obs(NodeKind.Radar, 70, 5000f, 0f));
            up.Desk.Hop(Op, up.IdOf(NodeKind.Radar, 70));
            up.Run(40f);
            TestAssert.Eq(up.Ports.Balance, 1000 - 6, "arrival at 20 s; the upkeep ticks at 30 and 40 s are free");
            up.Ports.Balance = 0;
            up.Run(30f);
            TestAssert.That(up.Desk.Network.Of(Op) != null, "an empty wallet drops nothing: upkeep is free");

            // Truck killed mid-intrusion.
            var kill = new Rig();
            kill.Observe(Obs(NodeKind.Radar, 70, 5000f, 0f));
            kill.Desk.Hop(Op, kill.IdOf(NodeKind.Radar, 70));
            kill.Run(25f);
            kill.Anchors.Set(AnchorKind.EwTruck, 0, 0f, true, kill.Ports.Clock);
            kill.Run(1f);
            TestAssert.Eq(kill.Desk.Network.Of(Op), null, "a dead truck ends the intrusion");
            TestAssert.That(kill.Events.Exists(e => e.Kind == CyberEventKind.Released && e.Reason == IntrusionEnd.SourceDown), "released as source down");

            // A held node that simply stops appearing among the real candidates (its airbase changed hands) is lost too.
            var vanish = new Rig();
            vanish.Observe(Obs(NodeKind.Relay, 70, 5000f, 0f));
            vanish.Desk.Hop(Op, vanish.IdOf(NodeKind.Relay, 70));
            vanish.Run(21f);
            TestAssert.That(vanish.Desk.Network.Of(Op) != null, "relay held");
            vanish.Observe();
            TestAssert.Eq(vanish.Desk.Network.Of(Op), null, "an engaged node absent from the observations is lost");

            // The node dies: held node lost.
            var lost = new Rig();
            lost.Observe(Obs(NodeKind.Radar, 70, 5000f, 0f));
            lost.Desk.Hop(Op, lost.IdOf(NodeKind.Radar, 70));
            lost.Run(21f);
            lost.Observe(Obs(NodeKind.Radar, 70, 5000f, 0f, gone: true));
            TestAssert.Eq(lost.Desk.Network.Of(Op), null, "a destroyed node ends the intrusion");
            TestAssert.That(lost.Events.Exists(e => e.Kind == CyberEventKind.Released && e.Reason == IntrusionEnd.NodeLost), "released as node lost");
        }

        private static void CheckDeskCaps()
        {
            var rig = new Rig(2);
            rig.Anchors.SetPosition(AnchorKind.EwTruck, 1, 0f, 0f);
            rig.Observe(Obs(NodeKind.Radar, 1, 1000f, 0f), Obs(NodeKind.Radar, 2, 2000f, 0f), Obs(NodeKind.Radar, 3, 3000f, 0f), Obs(NodeKind.Radar, 4, 4000f, 0f));
            int a = rig.IdOf(NodeKind.Radar, 1), b = rig.IdOf(NodeKind.Radar, 2), c = rig.IdOf(NodeKind.Radar, 3), d = rig.IdOf(NodeKind.Radar, 4);
            TestAssert.Eq(rig.Desk.Hop(Op, a).Outcome, CyberOutcome.Started, "first intrusion");
            TestAssert.Eq(rig.Desk.Hop(Op2, a).Outcome, CyberOutcome.NodeBusy, "the same node twice");
            TestAssert.Eq(rig.Desk.Hop(Op2, b).Outcome, CyberOutcome.Started, "second intrusion");
            TestAssert.Eq(rig.Desk.Hop(Op3, c).Outcome, CyberOutcome.IntrusionCap, "two concurrent intrusions below five humans");
            rig.Ports.HumanCount = 5;
            TestAssert.Eq(rig.Desk.Hop(Op3, c).Outcome, CyberOutcome.Started, "three at five humans");
            TestAssert.Eq(rig.Ports.Balance, 1000 - 18, "three starts paid");

            // Four nodes engaged across the faction at five humans: three operators hold one each, one goes deeper, a fifth is refused.
            var four = new Rig();
            four.Ports.HumanCount = 5;
            four.Observe(Obs(NodeKind.Radar, 1, 1000f, 0f), Obs(NodeKind.Radar, 2, 2000f, 0f), Obs(NodeKind.Radar, 3, 3000f, 0f),
                Obs(NodeKind.Radar, 4, 4000f, 0f), Obs(NodeKind.Radar, 5, 5000f, 0f));
            int[] ids = new int[5];
            for (int i = 0; i < 5; i++) ids[i] = four.IdOf(NodeKind.Radar, (uint)(i + 1));
            TestAssert.Eq(four.Desk.Hop(Op, ids[0]).Outcome, CyberOutcome.Started, "operator one");
            TestAssert.Eq(four.Desk.Hop(Op2, ids[1]).Outcome, CyberOutcome.Started, "operator two");
            TestAssert.Eq(four.Desk.Hop(Op3, ids[2]).Outcome, CyberOutcome.Started, "operator three");
            four.Run(21f);
            TestAssert.Eq(four.Desk.Hop(Op, ids[3]).Outcome, CyberOutcome.HopStarted, "operator one goes deeper");
            TestAssert.Eq(four.Desk.Network.Engaged, 4, "three held plus one hop in flight");
            TestAssert.Eq(four.Desk.Hop(Op2, ids[4]).Outcome, CyberOutcome.HeldCap, "a fifth node is refused");
            four.Run(21f);
            TestAssert.Eq(four.Desk.Network.Of(Op).HeldCount, 2, "operator one holds two");
            TestAssert.Eq(four.Desk.Network.Engaged, 4, "four held");
        }
        // ---- BIRD JAM on the SPACE state -----------------------------------------------------------------

        private static void CheckSpaceJam()
        {
            var state = new SpaceState(1);
            Near(state.CooldownFactor, 1f, "no jam, healthy uplink");
            state.JamFactor = 1.5f;
            Near(state.CooldownFactor, 1.5f, "BIRD JAM +50 %");
            TestAssert.That(state.TryReserve(BirdTask.Scan, 0f, out SpaceTaskReservation reservation) && state.Commit(reservation, 0f, 21f), "a scan starts");
            Near(state.CooldownRemaining(BirdTask.Scan, 0f), 135f, "the 90 s scan cooldown becomes 135 s");
            state.SetUplink(0, 0.4f, false, 10f);
            Near(state.CooldownFactor, 1.25f * 1.5f, "damaged and jammed stack");
            state.JamFactor = float.NaN;
            Near(state.CooldownFactor, 1.25f, "a NaN jam reads as none");
            state.JamFactor = 0.2f;
            Near(state.CooldownFactor, 1.25f, "a jam below 1 is ignored: nothing can speed a faction up");
            state.JamFactor = 99f;
            Near(state.CooldownFactor, 1.25f, "an absurd jam is ignored");
        }

    }
}
