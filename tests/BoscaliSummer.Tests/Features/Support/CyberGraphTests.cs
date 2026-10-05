using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>M3a CYBER pure core, part 1: node builder, ids, fog, reach graph, anchors, restore bar and placement (spec §1.1, §1.2).</summary>
    internal static class CyberGraphTests
    {
        private const int Them = 9;

        public static void Run()
        {
            CheckBuilder();
            CheckIds();
            CheckReveal();
            CheckGraph();
            CheckAnchors();
            CheckPlacement();
        }

        private static void Eq<T>(T actual, T expected, string message) =>
            TestAssert.That(Equals(actual, expected), message + " (got " + actual + ", want " + expected + ")");

        private static void Near(float actual, float expected, string message, float tolerance = 0.02f) =>
            TestAssert.That(Math.Abs(actual - expected) <= tolerance, message + " (got " + actual + ", want " + expected + ")");

        // ---- Builder ---------------------------------------------------------------------------

        private static void CheckBuilder()
        {
            var units = new List<SourceUnit>
            {
                new SourceUnit(50, SourceClass.SamRadar, 100f, 0f, 9000f),
                new SourceUnit(10, SourceClass.SamRadar, 0f, 0f, 9000f),
                new SourceUnit(11, SourceClass.SamLauncher, 1000f, 0f, 9000f),
                new SourceUnit(12, SourceClass.SamLauncher, 2000f, 0f, 9000f),
                new SourceUnit(13, SourceClass.SamLauncher, 2500f, 0f, 9000f),
                new SourceUnit(14, SourceClass.SamLauncher, 2999f, 0f, 9000f),
                new SourceUnit(20, SourceClass.SamLauncher, 9000f, 0f, 9000f),
                new SourceUnit(30, SourceClass.Radar, 20000f, 0f, 5000f),
                new SourceUnit(40, SourceClass.Other, 1f, 1f, 1f),
            };
            var points = new List<SourcePoint> { new SourcePoint(900, NodeKind.Relay, 30000f, 0f, 100f), new SourcePoint(901, NodeKind.DataCenter, 31000f, 0f, 120f) };
            var seeds = new List<NodeSeed>();
            CyberNodeBuilder.Build(units, points, seeds);
            Eq(seeds.Count, 5, "two SAM clusters, one radar, two points");
            Eq(seeds[0].Kind, NodeKind.SamC2, "first cluster kind");
            Eq(seeds[0].Key, 10u, "cluster seeded by the lowest SAM radar id");
            Eq(seeds[1].Key, 50u, "second SAM radar forms its own cluster from the leftovers");
            Eq(seeds[2].Kind, NodeKind.Radar, "radar after clusters");
            Eq(seeds[2].Key, 30u, "radar key");
            Eq(seeds[3].Kind, NodeKind.Relay, "point passes through");
            Eq(seeds[4].Kind, NodeKind.DataCenter, "point passes through");

            // 10 takes its three nearest launchers (11, 12, 13); 50 can then only take 14 (2899 m away). 20 is out of range of both.
            var reversed = new List<SourceUnit>(units);
            reversed.Reverse();
            var again = new List<NodeSeed>();
            CyberNodeBuilder.Build(reversed, points, again);
            Eq(again.Count, seeds.Count, "input order does not change the node count");
            for (int i = 0; i < seeds.Count; i++) Eq(again[i].Key, seeds[i].Key, "input order does not change node " + i);

            // A SAM launcher with no radar makes no node; NaN positions are dropped.
            var bare = new List<SourceUnit> { new SourceUnit(1, SourceClass.SamLauncher, 0f, 0f, 0f), new SourceUnit(2, SourceClass.Radar, float.NaN, 0f, 0f) };
            CyberNodeBuilder.Build(bare, null, seeds);
            Eq(seeds.Count, 0, "no node from a bare launcher or a NaN radar");
            CyberNodeBuilder.Build(null, null, seeds);
            Eq(seeds.Count, 0, "null inputs are empty");
        }

        private static void CheckIds()
        {
            var t = new NodeIdTable();
            int a = t.GetOrAdd(NodeKind.Radar, 5u), b = t.GetOrAdd(NodeKind.SamC2, 5u);
            TestAssert.That(a > 0 && b > 0 && a != b, "distinct (kind, key) pairs get distinct ids");
            Eq(t.GetOrAdd(NodeKind.Radar, 5u), a, "the same pair keeps its id");
            TestAssert.That(t.TryKey(a, out NodeKind kind, out uint key) && kind == NodeKind.Radar && key == 5u, "id maps back");
            TestAssert.That(!t.TryKey(9999, out _, out _), "unknown id has no key");
            for (uint i = 100; t.Count < NodeIdTable.Capacity; i++) t.GetOrAdd(NodeKind.Radar, i);
            Eq(t.GetOrAdd(NodeKind.Radar, 99999u), 0, "a full table hands out no id");
            Eq(t.GetOrAdd(NodeKind.Radar, 5u), a, "known pairs still resolve when full");
            t.Clear();
            Eq(t.Count, 0, "clear empties the table");
        }

        private static void CheckReveal()
        {
            var r = new NodeReveal();
            TestAssert.That(!r.Visible(1, 0f, false), "never seen is not visible");
            r.Note(1, 10f);
            TestAssert.That(r.Visible(1, 10f, false) && r.Visible(1, 99f, false), "visible for 90 s after a sighting");
            TestAssert.That(!r.Visible(1, 100f, false), "hidden again after 90 s");
            TestAssert.That(r.Visible(1, 500f, true), "held is always visible");
            r.Linger(1, 200f);
            TestAssert.That(r.Visible(1, 229f, false) && !r.Visible(1, 230f, false), "30 s of fog after a release");
            r.Note(0, 1f);
            r.Note(-3, 1f);
            TestAssert.That(!r.Visible(0, 1f, false), "id 0 is never stamped");
        }

        // ---- Graph -----------------------------------------------------------------------------

        private static CyberNode Node(int id, NodeKind kind, float x, float z, float front = 0f, uint unit = 0u) =>
            new CyberNode(id, kind, x, z, unit, front, Them);

        private static void CheckGraph()
        {
            var trucks = new List<EwSource> { new EwSource(0, 0f, 0f, 18000f), new EwSource(1, 50000f, 0f, 10800f) };
            var far = Node(1, NodeKind.Radar, 18001f, 0f);
            var edge = Node(2, NodeKind.Radar, 18000f, 0f);
            var second = Node(3, NodeKind.Radar, 45000f, 0f);
            TestAssert.That(!CyberGraph.Reachable(far, trucks, null, out _, out _), "just outside the truck reach");
            TestAssert.That(CyberGraph.Reachable(edge, trucks, null, out int via, out _) && via == 0, "exactly at reach counts");
            TestAssert.That(CyberGraph.Reachable(second, trucks, null, out via, out _) && via == 1, "the second truck reaches its own side");

            var held = new List<CyberNode> { edge };
            var deep = Node(4, NodeKind.SamC2, 29999f, 0f);
            TestAssert.That(CyberGraph.Reachable(deep, trucks, held, out via, out int viaNode) && via == -1 && viaNode == 2, "a held node chains 12 km further");
            TestAssert.That(!CyberGraph.Reachable(Node(5, NodeKind.SamC2, 30001f, 0f), trucks, held, out _, out _), "past the chain reach");
            TestAssert.That(!CyberGraph.Reachable(edge, null, new List<CyberNode> { edge }, out _, out _), "a node is not reachable from itself");
            TestAssert.That(!CyberGraph.InReach(0f, 0f, 1f, 0f, 0f) && !CyberGraph.InReach(0f, 0f, 1f, 0f, float.NaN), "zero or NaN reach reaches nothing");

            var many = new List<CyberNode>();
            for (int i = 1; i <= 20; i++) many.Add(Node(i, NodeKind.Radar, i * 1000f, 0f, front: 1000f * (20 - i)));
            var keep = new HashSet<int> { 1 };
            var picked = new List<CyberNode>();
            Eq(CyberGraph.Select(many, keep, CyberGraph.MaxNodes, picked), 16, "16 in all, the held node counted");
            Eq(picked[0].Id, 1, "held node first");
            TestAssert.That(picked.Exists(n => n.Id == 20) && picked.Exists(n => n.Id == 6) && !picked.Exists(n => n.Id == 5), "closest to the front first");
            Eq(CyberGraph.Select(many, null, 3, picked), 3, "cap respected without a keep set");
            Eq(picked[0].Id, 20, "nearest the front leads");
            Eq(CyberGraph.Select(null, null, 5, picked), 0, "null candidates select nothing");

            var edges = new List<CyberEdge>();
            var nodes = new List<CyberNode> { Node(10, NodeKind.Radar, 5000f, 0f), Node(11, NodeKind.Radar, 15000f, 0f), Node(12, NodeKind.Radar, 40000f, 0f) };
            var heldIds = new HashSet<int> { 11 };
            CyberGraph.Edges(trucks, nodes, heldIds, edges);
            TestAssert.That(edges.Contains(new CyberEdge(CyberEdge.Truck(0), 10)) && edges.Contains(new CyberEdge(CyberEdge.Truck(0), 11)), "truck edges");
            TestAssert.That(edges.Contains(new CyberEdge(11, 10)), "held node edge within 12 km");
            TestAssert.That(!edges.Exists(e => e.To == 12 && e.From == 11), "no edge past 12 km");
            Eq(CyberEdge.Truck(2), -3, "truck encoding");
        }

        // ---- Anchors ---------------------------------------------------------------------------

        private static void CheckAnchors()
        {
            Eq(AnchorRules.Of(1f, false), AnchorHealth.Live, "full health is live");
            Eq(AnchorRules.Of(0.5f, false), AnchorHealth.Damaged, "half health is damaged");
            Eq(AnchorRules.Of(0.9f, true), AnchorHealth.Down, "down wins");
            Eq(AnchorRules.Of(float.NaN, false), AnchorHealth.Down, "NaN health reads down");
            Near(AnchorRules.Reach(AnchorHealth.Live), 18000f, "live reach");
            Near(AnchorRules.Reach(AnchorHealth.Damaged), 10800f, "damaged reach is -40 %");
            Near(AnchorRules.Reach(AnchorHealth.Down), 0f, "down reach");
            Near(AnchorRules.RebuildGoal(AnchorKind.EwTruck), 100f, "truck rebuild");
            Near(AnchorRules.RebuildGoal(AnchorKind.DataCenter), 300f, "data center rebuild");

            var set = new CyberAnchorSet(2, 1);
            set.SetPosition(AnchorKind.EwTruck, 0, 100f, 200f);
            set.SetPosition(AnchorKind.EwTruck, 1, 300f, 400f);
            Eq(set.Count(AnchorKind.EwTruck), 2, "two trucks");
            Eq(set.LiveCount(AnchorKind.EwTruck), 2, "both live at the start");
            TestAssert.That(set.DataCenterUp, "a fresh data center stands");
            set.Set(AnchorKind.EwTruck, 0, 0.4f, false, 50f);
            Eq(set.Health(AnchorKind.EwTruck, 0), AnchorHealth.Damaged, "damaged");
            Near(set.TruckReach(0), 10800f, "damaged truck reach");
            set.Set(AnchorKind.EwTruck, 0, 0f, true, 60f);
            Eq(set.Health(AnchorKind.EwTruck, 0), AnchorHealth.Down, "down");
            Near(set.DownSince(AnchorKind.EwTruck, 0), 60f, "down since");
            set.Set(AnchorKind.EwTruck, 0, 0f, true, 70f);
            Near(set.DownSince(AnchorKind.EwTruck, 0), 60f, "down since keeps the first time");
            TestAssert.That(!set.PastGrace(AnchorKind.EwTruck, 0, 179f) && set.PastGrace(AnchorKind.EwTruck, 0, 180f), "120 s grace");
            var list = new List<EwSource>();
            Eq(set.CopyTrucks(list), 1, "a down truck draws no reach");
            Eq(list[0].Index, 1, "the standing truck is listed");
            set.Restore(AnchorKind.EwTruck, 0, 5f, 6f, 400f);
            Eq(set.Health(AnchorKind.EwTruck, 0), AnchorHealth.Live, "restored");
            TestAssert.That(float.IsPositiveInfinity(set.DownSince(AnchorKind.EwTruck, 0)), "restored clears the down time");
            TestAssert.That(set.TryPosition(AnchorKind.EwTruck, 0, out float x, out float z) && x == 5f && z == 6f, "restored position");

            set.Trace(1, 1000f);
            TestAssert.That(set.TruckLocked(1, 1089f) && !set.TruckLocked(1, 1090f), "traced truck locked for 90 s");
            Near(set.RevealUntil(1), 1120f, "traced truck revealed for 120 s");
            TestAssert.That(!set.TruckLocked(5, 0f), "unknown truck is never locked");
            Eq(set.Health(AnchorKind.EwTruck, 9), AnchorHealth.Down, "unknown anchor reads down");

            set.Set(AnchorKind.DataCenter, 0, 0f, true, 10f);
            TestAssert.That(!set.DataCenterUp, "a down data center gives no bonus");

            var bar = new RebuildBar(100f);
            Near(bar.Fund(30f), 30f, "chip-in");
            Near(bar.Fund(500f), 70f, "the bar takes only what it needs");
            TestAssert.That(bar.Complete && bar.Fraction >= 1f, "complete");
            bar.Reset();
            Near(bar.AutoFund(60f, 1000f, false), 40f / 1f, "auto-fund is 40 CR per minute");
            Near(bar.Value, 40f, "auto-fund value");
            Near(bar.AutoFund(60f, 10f, false), 10f, "the treasury caps the auto-fund");
            Near(bar.AutoFund(60f, 0f, true), 0f, "a flat seed takes nothing from the treasury");
            Near(bar.Value, 90f, "flat seed still fills the bar");
            Near(bar.AutoFund(-5f, 100f, false), 0f, "negative seconds fund nothing");

            var fund = new HqFundLedger();
            TestAssert.That(!fund.TrySpend(1, 10f), "an empty fund spends nothing");
            fund.Add(1, 50f);
            TestAssert.That(fund.TrySpend(1, 20f), "spend within balance");
            Near(fund.Balance(1), 30f, "balance after spend");
            TestAssert.That(!fund.TrySpend(1, 31f) && !fund.TrySpend(1, float.NaN), "no overdraft, no NaN");
        }

        private static void CheckPlacement()
        {
            // diagonal 100 km: at least 15 km from the front, at least 15 km apart
            var legal = new List<SiteCandidate>
            {
                new SiteCandidate(1, 0f, 0f, 100f, 20000f),
                new SiteCandidate(2, 5000f, 0f, 90f, 20000f),
                new SiteCandidate(3, 30000f, 0f, 80f, 20000f),
                new SiteCandidate(4, 60000f, 0f, 200f, 10000f),
                new SiteCandidate(5, 90000f, 0f, 70f, 40000f),
            };
            var chosen = new List<int>();
            Eq(CyberPlacement.Pick(legal, 100000f, 2, null, 0f, chosen), 2, "two picks");
            TestAssert.That(chosen[0] == 1 && chosen[1] == 3, "best rear first; the 5 km neighbour is too close; the 10 km-from-front site is filtered");
            Eq(CyberPlacement.Pick(legal, 100000f, 5, null, 0f, chosen), 3, "three sites satisfy front and separation");
            TestAssert.That(chosen.Contains(1) && chosen.Contains(3) && chosen.Contains(5) && !chosen.Contains(4), "ids 1, 3, 5");
            var avoid = new List<SiteCandidate> { new SiteCandidate(0, 0f, 0f, 0f, 0f) };
            CyberPlacement.Pick(legal, 100000f, 2, avoid, 600f, chosen);
            TestAssert.That(chosen[0] == 2 && chosen[1] == 3, "an avoided spot is skipped, the runner-up leads");
            Eq(CyberPlacement.Pick(legal, 0f, 2, null, 0f, chosen), 0, "no diagonal, no picks");
            Eq(CyberPlacement.Pick(null, 100000f, 2, null, 0f, chosen), 0, "null candidates pick nothing");
            Eq(CyberPlacement.Pick(legal, 100000f, 0, null, 0f, chosen), 0, "max 0 picks nothing");
            var tie = new List<SiteCandidate> { new SiteCandidate(9, 0f, 0f, 10f, 99999f), new SiteCandidate(3, 50000f, 0f, 10f, 99999f) };
            CyberPlacement.Pick(tie, 100000f, 1, null, 0f, chosen);
            Eq(chosen[0], 3, "equal rear scores break by the lower id");
            var junk = new List<SiteCandidate> { new SiteCandidate(1, float.NaN, 0f, 1f, 99999f), new SiteCandidate(2, 0f, 0f, float.NaN, 99999f) };
            Eq(CyberPlacement.Pick(junk, 100000f, 3, null, 0f, chosen), 0, "NaN candidates are dropped");
        }

    }
}
