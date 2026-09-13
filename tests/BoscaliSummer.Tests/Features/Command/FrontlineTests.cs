using System;
using BoscaliSummer.Features.Command.Runtime;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class FrontlineTests
    {
        public static void Run()
        {
            TacticalSectorGrid fast = MakeFront(), slow = MakeFront();
            var sites = new FrontlineSite[256];
            int siteCount = fast.CopyFrontlineSites(sites);
            TestAssert.That(siteCount > 0, "Opposing territory supplies frontline trench sites");
            for (int i = 0; i < siteCount; i++)
            {
                var site = sites[i];
                TestAssert.That(fast.WorldToCell(site.X, site.Z, out int c, out int r) &&
                    fast.GetSectorControl(c, r) == SectorControl.Friendly, "Trench sites remain on their owner's side");
                fast.WorldToCell(site.X + site.ThreatX * 200f, site.Z + site.ThreatZ * 200f, out c, out r);
                TestAssert.That(fast.GetSectorControl(c, r) != SectorControl.Friendly,
                    "Trench threat faces the same boundary drawn on the map");
            }
            TestAssert.That(fast.CopyFrontlineSites(new FrontlineSite[1]) == 1, "Frontline copies respect caller capacity");
            fast.WorldToCell(5000f, 0f, out int col, out int row);
            float initial = fast.GetSectorHoldStrength(col, row);
            int originalTerritory = fast.FriendlySectorCount;
            TestAssert.That(initial < 0f, "The advance begins in hostile territory");
            Advance(fast, 0.5f, 80);
            Advance(slow, 2f, 20);
            float captured = fast.GetSectorHoldStrength(col, row);
            TestAssert.That(captured > 0.1f && fast.GetSectorControl(col, row) == SectorControl.Friendly,
                "Sustained pressure must push the frontline across its original strategic boundary");
            TestAssert.That(fast.FriendlySectorCount > originalTerritory,
                "The advance must affect an area, not only the vehicle's cell");
            for (int r = 0; r < fast.ResolutionY; r++)
                for (int c = 0; c < fast.ResolutionX; c++)
                    TestAssert.That(Math.Abs(fast.GetSectorHoldStrength(c, r) - slow.GetSectorHoldStrength(c, r)) < 0.0001f,
                        "Equal elapsed time must produce equal control at different refresh rates");

            Snapshot(fast);
            fast.EvaluateSectors(0f);
            TestAssert.That(fast.GetSectorHoldStrength(col, row) == captured,
                "A new snapshot or paused mission must preserve control history");
            fast.EvaluateSectors(30f);
            TestAssert.That(fast.GetSectorHoldStrength(col, row) < captured,
                "Departed contacts cannot leave accumulating pressure behind");
            fast.EvaluateSectors(270f);
            TestAssert.That(fast.GetSectorControl(col, row) == SectorControl.Hostile,
                "Unsupported advances gradually recover toward strategic ownership");

            Snapshot(fast);
            fast.RegisterNode(2, "Captured base", 20000f, 0f, SectorControl.Friendly, 25000f, true);
            TestAssert.That(fast.TotalNodesCount == 2, "Ownership updates must replace the same node, not duplicate it");
            fast.EvaluateSectors(300f);
            TestAssert.That(fast.HostileSectorCount == 0, "Actual base capture must move strategic influence");
            TestAssert.That(fast.CopyFrontlineSites(sites) == 0, "No trenches without an opposing frontline");
            fast.ResetAll();
            TestAssert.That(fast.FriendlySectorCount == 0 && fast.NeutralSectorCount == fast.TotalSectors &&
                fast.TotalNodesCount == 0 && fast.GetSectorHoldStrength(col, row) == 0f,
                "A new scene or faction clears nodes, history and telemetry");

            TestBoundsAndObservations();
        }

        private static TacticalSectorGrid MakeFront()
        {
            var grid = new TacticalSectorGrid(32, 100000f);
            Snapshot(grid);
            grid.EvaluateSectors(0f);
            return grid;
        }

        private static void Snapshot(TacticalSectorGrid grid)
        {
            grid.Clear();
            grid.RegisterNode(1, "Friendly base", -20000f, 0f, SectorControl.Friendly, 25000f, true);
            grid.RegisterNode(2, "Hostile base", 20000f, 0f, SectorControl.Hostile, 25000f, true);
        }

        private static void Advance(TacticalSectorGrid grid, float step, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Snapshot(grid);
                grid.AddTroopPresence(5000f, 0f, 10f, false);
                grid.EvaluateSectors(step);
            }
        }

        private static void TestBoundsAndObservations()
        {
            var portrait = new TacticalSectorGrid(64, 80000f, 160000f);
            TestAssert.That(portrait.ResolutionX == 32 && portrait.ResolutionY == 64 && portrait.TotalSectors <= 4096,
                "Portrait theaters preserve square cells without exceeding grid capacity");
            var extreme = new TacticalSectorGrid(64, 1001f, 10000000f);
            TestAssert.That(extreme.ResolutionX == 1 && extreme.ResolutionY == 64,
                "Extreme aspect ratios remain within the fixed buffer");
            extreme.EvaluateSectors();
            TestAssert.That(!portrait.WorldToCell(float.NaN, 0f, out _, out _) &&
                !portrait.WorldToCell(float.PositiveInfinity, 0f, out _, out _) &&
                !portrait.WorldToCell(float.MaxValue, 0f, out _, out _) &&
                !portrait.WorldToCell(40000f, 0f, out _, out _),
                "Nonfinite and out-of-map coordinates never enter the grid");
            portrait.SetWorldSize(float.PositiveInfinity, float.NaN);
            TestAssert.That(portrait.WorldSizeX == 80000f && portrait.WorldSizeY == 160000f,
                "Invalid theater dimensions leave the current map intact");
            portrait.AddTroopPresence(0f, 0f, float.NaN, false);
            portrait.AddTroopPresence(0f, 0f, -10f, true);
            portrait.AddTroopPresence(0f, 0f, 10f, true, float.PositiveInfinity);
            portrait.RegisterNode(1, "Invalid", float.NaN, 0f, SectorControl.Friendly, 1f, true);
            portrait.EvaluateSectors(float.NaN);
            TestAssert.That(portrait.NeutralSectorCount == portrait.TotalSectors && portrait.TotalNodesCount == 0,
                "Invalid observations cannot create phantom territory");
            TestAssert.That(TacticalSectorGrid.ObservationConfidence(0f) == 1f &&
                TacticalSectorGrid.ObservationConfidence(15f) == 0.5f &&
                TacticalSectorGrid.ObservationConfidence(30f) == 0f &&
                TacticalSectorGrid.ObservationConfidence(-1f) == 0f &&
                TacticalSectorGrid.ObservationConfidence(float.NaN) == 0f,
                "Known contacts lose all pressure by 30 seconds and invalid timestamps are rejected");
            for (int i = 0; i < 1000; i++) portrait.RegisterNode(i, "Node", 0f, 0f, SectorControl.Friendly, 0f, false);
            TestAssert.That(portrait.TotalNodesCount == TacticalSectorGrid.MaximumNodes, "Node growth has a hard ceiling");
            portrait.EvaluateSectors();
            portrait.SetWorldSize(100000f);
            TestAssert.That(portrait.TotalNodesCount == 0 && portrait.FriendlySectorCount == 0,
                "Dimension changes invalidate the old coordinate-space history");
            foreach (Color32 pixel in portrait.BakeTexture(128, 128, false, false, 1f))
                TestAssert.That(pixel.a == 0, "Disabling both overlays must leave a transparent texture");
        }
    }
}
