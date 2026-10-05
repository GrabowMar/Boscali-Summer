using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>M3a CYBER NET page words, the map projection and the HUD notice tracker.</summary>
    internal static class CyberNetWordsTests
    {
        public static void Run()
        {
            CheckNetWords();
        }

        private static void Eq<T>(T actual, T expected, string message) =>
            TestAssert.That(Equals(actual, expected), message + " (got " + actual + ", want " + expected + ")");

        private static void Near(float actual, float expected, string message, float tolerance = 0.02f) =>
            TestAssert.That(Math.Abs(actual - expected) <= tolerance, message + " (got " + actual + ", want " + expected + ")");

        private static void CheckNetWords()
        {
            Eq(CyberNetWords.Code(NodeKind.SamC2), "SAM", "code");
            Eq(CyberNetWords.Name(NodeKind.DataCenter), "DATA CENTER", "name");
            Eq(CyberNetWords.Node(NodeKind.Radar, 12), "RADAR #12", "node label");
            Eq(CyberNetWords.Clock(61f), "1:01", "clock");
            Eq(CyberNetWords.Clock(-5f), "0:00", "negative clock");
            Eq(CyberNetWords.TraceTone(49), 0, "trace tone green");
            Eq(CyberNetWords.TraceTone(50), 1, "trace tone amber");
            Eq(CyberNetWords.TraceTone(80), 2, "trace tone red");

            var truck = new CyberAnchorRow { Kind = AnchorKind.EwTruck, Health = AnchorHealth.Live };
            Eq(CyberNetWords.AnchorLine(truck, 0, 100f), "EW TRUCK 1 · LIVE · REACH 18 KM", "live truck");
            truck.Health = AnchorHealth.Damaged; truck.LockUntil = 160f;
            Eq(CyberNetWords.AnchorLine(truck, 1, 100f), "EW TRUCK 2 · DAMAGED · REACH 11 KM · TRACED 1:00", "damaged and traced truck (10.8 km rounds to 11)");
            truck.Health = AnchorHealth.Down; truck.Rebuild = 45;
            Eq(CyberNetWords.AnchorLine(truck, 0, 0f), "EW TRUCK 1 · DOWN · RESTORE 45 %", "down truck");
            var dc = new CyberAnchorRow { Kind = AnchorKind.DataCenter, Health = AnchorHealth.Live };
            Eq(CyberNetWords.AnchorLine(dc, 0, 0f), "DATA CENTER 1 · LIVE · TRACE -20 %, UPKEEP -25 %", "live data center");
            dc.Health = AnchorHealth.Damaged;
            Eq(CyberNetWords.AnchorLine(dc, 0, 0f), "DATA CENTER 1 · DAMAGED", "damaged data center");

            var st = new CyberStateData { Active = true };
            st.Anchors.Add(new CyberAnchorRow { Kind = AnchorKind.EwTruck, Health = AnchorHealth.Live });
            st.Anchors.Add(new CyberAnchorRow { Kind = AnchorKind.EwTruck, Health = AnchorHealth.Down });
            st.Anchors.Add(new CyberAnchorRow { Kind = AnchorKind.DataCenter, Health = AnchorHealth.Damaged });
            st.Nodes.Add(new CyberNodeRow { Id = 1, Kind = NodeKind.Radar, Held = true });
            st.Nodes.Add(new CyberNodeRow { Id = 2, Kind = NodeKind.SamC2 });
            Eq(CyberNetWords.Sub(st), "1 TRUCK · 1 DATA CENTER · 2 NODES · 1 HELD", "header sub-line");
            Eq(CyberNetWords.Sub(new CyberStateData()), "NO EW ASSETS ONLINE", "inactive sub-line");
            Eq(CyberNetWords.Sub(null), "NO EW ASSETS ONLINE", "null sub-line");

            Eq(CyberNetWords.EventLine(new CyberEventRow { Kind = CyberEventKind.NodeHeld, Node = NodeKind.SamC2, NodeId = 7 }, 0f), "NODE HELD · SAM C2 #7", "held line");
            Eq(CyberNetWords.EventLine(new CyberEventRow { Kind = CyberEventKind.Released, Node = NodeKind.Relay, NodeId = 3, Reason = IntrusionEnd.Burned }, 0f), "NODE RELEASED · RELAY #3 · BURNED", "burned line");
            Eq(CyberNetWords.EventLine(new CyberEventRow { Kind = CyberEventKind.HopStarted, Node = NodeKind.Uplink, NodeId = 9 }, 0f), "HOP STARTED · UPLINK #9", "hop line");
            TestAssert.That(CyberNetWords.EventLine(new CyberEventRow { Kind = CyberEventKind.Traced }, 0f).StartsWith("INTRUSION TRACED", StringComparison.Ordinal), "traced line");
            for (byte b = 0; b <= (byte)IntrusionEnd.Scene; b++) TestAssert.That(CyberNetWords.ReasonWord((IntrusionEnd)b).Length > 0, "reason " + b);

            var hop = new CyberIntrusionRow { Id = 1, Phase = IntrusionPhase.Hopping, HopTarget = 2, HopEndsAt = 130f, HopSeconds = 30, Held = new int[0] };
            Eq(CyberNetWords.IntrusionLine(hop, st, 114.5f), "HOPPING · SAM C2 #2 · 16 S", "hop line words");
            Near(CyberNetWords.HopProgress(hop, 115f), 0.5f, "hop progress half");
            Near(CyberNetWords.HopProgress(hop, 200f), 1f, "hop progress clamps");
            var hold = new CyberIntrusionRow { Id = 1, Phase = IntrusionPhase.Holding, Held = new[] { 1, 2 } };
            Eq(CyberNetWords.IntrusionLine(hold, st, 0f), "HOLDING 2 NODES", "holding words");
            Near(CyberNetWords.HopProgress(hold, 5f), 0f, "no hop, no progress");

            // projection: north up, a minimum 20 km window, reach circles in pixels
            var proj = new CyberMapProjection();
            proj.Fit(new List<MapPoint> { new MapPoint(0f, 0f), new MapPoint(10000f, 0f) }, 478f, 230f);
            MapPoint mid = proj.ToScreen(5000f, 0f);
            Near(mid.X, 239f, "the points' centre is the map centre", 0.5f);
            Near(mid.Y, 115f, "the points' centre is the map centre (y)", 0.5f);
            TestAssert.That(proj.ToScreen(5000f, 1000f).Y < mid.Y, "north is up");
            TestAssert.That(proj.ToScreen(0f, 0f).X < mid.X && proj.ToScreen(10000f, 0f).X > mid.X, "east is right");
            Near(proj.Pixels(20000f) / 230f, 1f, "a 20 km window fills the shorter side", 0.01f);
            var wide = new CyberMapProjection();
            wide.Fit(new List<MapPoint> { new MapPoint(-50000f, 0f), new MapPoint(50000f, 0f) }, 478f, 230f);
            TestAssert.That(wide.ToScreen(-50000f, 0f).X > 0f && wide.ToScreen(50000f, 0f).X < 478f, "every point stays inside the box");
            var empty = new CyberMapProjection();
            empty.Fit(null, 478f, 230f);
            Near(empty.ToScreen(0f, 0f).X, 239f, "an empty map is centred", 0.5f);
            empty.Fit(new List<MapPoint> { new MapPoint(float.NaN, 0f) }, 478f, 230f);
            TestAssert.That(!float.IsNaN(empty.ToScreen(0f, 0f).X), "NaN points are ignored");

            // HUD notices: only the operator's own NODE HELD / TRACED, silent on first sight, 3 s apart, QUIET drops them
            var tracker = new CyberNoticeTracker();
            var s0 = new CyberStateData { Active = true };
            s0.Events.Add(new CyberEventRow { Seq = 5, Kind = CyberEventKind.NodeHeld, Own = true });
            Eq(tracker.Observe(true, s0, 10f, false), CyberNoticeKind.None, "first sight never replays history");
            var s1 = new CyberStateData { Active = true };
            s1.Events.Add(new CyberEventRow { Seq = 5, Kind = CyberEventKind.NodeHeld, Own = true });
            s1.Events.Add(new CyberEventRow { Seq = 6, Kind = CyberEventKind.NodeHeld, Own = false });
            Eq(tracker.Observe(true, s1, 11f, false), CyberNoticeKind.None, "a teammate's node is not my notice");
            s1.Events.Add(new CyberEventRow { Seq = 7, Kind = CyberEventKind.NodeHeld, Own = true });
            Eq(tracker.Observe(true, s1, 12f, false), CyberNoticeKind.Held, "my node held");
            s1.Events.Add(new CyberEventRow { Seq = 8, Kind = CyberEventKind.Traced, Own = true });
            Eq(tracker.Observe(true, s1, 13f, false), CyberNoticeKind.None, "inside the 3 s gap");
            s1.Events.Add(new CyberEventRow { Seq = 9, Kind = CyberEventKind.NodeHeld, Own = true });
            s1.Events.Add(new CyberEventRow { Seq = 10, Kind = CyberEventKind.Traced, Own = true });
            Eq(tracker.Observe(true, s1, 16f, false), CyberNoticeKind.Traced, "traced outranks held in one poll");
            s1.Events.Add(new CyberEventRow { Seq = 11, Kind = CyberEventKind.NodeHeld, Own = true });
            Eq(tracker.Observe(true, s1, 30f, true), CyberNoticeKind.None, "QUIET silences it");
            Eq(tracker.Observe(false, s1, 31f, false), CyberNoticeKind.None, "an unknown mirror resets the tracker");
            Eq(tracker.Observe(true, s1, 32f, false), CyberNoticeKind.None, "and the next sight is silent again");
        }
    }
}
