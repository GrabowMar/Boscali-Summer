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
            int traceCount = CopyTraces(fast, out FrontlineTracePoint[] points, out int[] lengths, out _);            TestAssert.That(traceCount > 0, "Opposing territory supplies ordered front traces");
            int totalPoints = 0;
            float cellSize = fast.CellSize;
            for (int t = 0; t < traceCount; t++)
            {
                TestAssert.That(lengths[t] >= 2, "Every front trace carries at least two stations");
                int start = totalPoints;
                totalPoints += lengths[t];
                for (int i = start + 1; i < start + lengths[t]; i++)
                {
                    float dx = points[i].X - points[i - 1].X, dz = points[i].Z - points[i - 1].Z;
                    float step = (float)Math.Sqrt(dx * dx + dz * dz);
                    TestAssert.That(step <= cellSize * 1.6f, "Trace stations are chained cell to cell");
                    TestAssert.That(fast.WorldToCell(points[i].X, points[i].Z, out int c, out int r) &&
                        fast.GetSectorControl(c, r) != SectorControl.Neutral,
                        "The trace runs on the boundary, not through the rear");
                    TestAssert.That(fast.WorldToCell(points[i].X - cellSize, points[i].Z, out c, out r) &&
                        fast.GetSectorControl(c, r) == SectorControl.Friendly,
                        "The owner holds the ground one cell behind the trace");
                }
            }
            TestAssert.That(fast.CopyFrontlineTraces(new FrontlineTracePoint[1], new int[0], new float[0]) == 0,
                "Trace copies respect caller capacity");
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
            TestAssert.That(CopyTraces(fast, out _, out _, out _) == 0, "No trenches without an opposing frontline");
            fast.ResetAll();
            TestAssert.That(fast.FriendlySectorCount == 0 && fast.NeutralSectorCount == fast.TotalSectors &&
                fast.TotalNodesCount == 0 && fast.GetSectorHoldStrength(col, row) == 0f,
                "A new scene or faction clears nodes, history and telemetry");

            TestBoundsAndObservations();
            TestContactPressure();
            TestContestedPressure();
            TestContestedTint();
            TestContourGeometry();
            TestClusterTree();
            TestEncirclement();
        }

        private static void TestContourGeometry()
        {
            // A straight north-south front between two bases: the ordered trace runs along
            // the boundary, station by station, and sits on the crossing itself.
            var straight = new TacticalSectorGrid(1000f, 100000f);
            Snapshot(straight);
            straight.EvaluateSectors(0f);
            int traceCount = CopyTraces(straight, out FrontlineTracePoint[] points, out int[] lengths, out _);
            TestAssert.That(traceCount == 1, "A clean straight front chains into one trace");
            TestAssert.That(straight.FrontlineLengthMetres > 10000f,
                "Front length is measured in metres, not cell edges");
            float minZ = float.MaxValue, maxZ = float.MinValue;
            int offset = 0;
            for (int t = 0; t < traceCount; t++)
            {
                for (int i = offset + 1; i < offset + lengths[t]; i++)
                {
                    float dx = points[i].X - points[i - 1].X, dz = points[i].Z - points[i - 1].Z;
                    TestAssert.That(Math.Abs(dz) > Math.Abs(dx), "A north-south front chains north to south");
                    TestAssert.That(Math.Abs(points[i].X) < 8000f,
                        "The trace sits on the actual boundary between the two bases");
                    minZ = Math.Min(minZ, points[i].Z);
                    maxZ = Math.Max(maxZ, points[i].Z);
                }
                offset += lengths[t];
            }
            TestAssert.That(maxZ - minZ > 10000f, "The front is traced along its length, not sampled at one point");

            TestDiagonalContour();
        }

        private static void TestDiagonalContour()
        {
            // A single hostile corner against three friendly cells: the contour crosses the
            // shared cell edges diagonally, so its threat must be diagonal too — the whole
            // point of interpolated stretches over a cell-edge bitmask.
            var hold = new[] { 1f, -1f, -1f, -1f };
            var friendly = new[] { 1f, 0f, 0f, 0f };
            var hostile = new[] { 0f, 1f, 1f, 1f };
            var segments = new SectorFrontSegment[16];
            int count = SectorContour.Extract(hold, friendly, hostile, 2, 2, 50000f, -50000f, -50000f, segments);
            TestAssert.That(count == 1, "A corner contest yields one front stretch");
            TestAssert.That(Math.Abs(segments[0].ThreatX) > 0.5f && Math.Abs(segments[0].ThreatX) < 0.95f &&
                Math.Abs(segments[0].ThreatZ) > 0.5f && Math.Abs(segments[0].ThreatZ) < 0.95f,
                "A diagonal front yields a diagonal threat normal");
            TestAssert.That(Math.Abs(segments[0].AX - segments[0].BX) > 1f &&
                Math.Abs(segments[0].AZ - segments[0].BZ) > 1f,
                "The contour endpoints cross two different cell edges");
            TestAssert.That(segments[0].Pressure > 0.9f,
                "Opposing ground forces on both cells are full contact pressure");
        }

        private static void TestContestedPressure()
        {
            var grid = new TacticalSectorGrid(1000f, 100000f);
            Snapshot(grid);
            grid.AddTroopPresence(2000f, 0f, 10f, false);
            grid.AddTroopPresence(-2000f, 0f, 10f, true);
            grid.EvaluateSectors(30f);
            int count = CopyTraces(grid, out _, out _, out float[] pressure);
            TestAssert.That(count > 0, "An engaged border still supplies a front trace");
            bool contested = false;
            for (int i = 0; i < count; i++)
                if (pressure[i] > 0.5f) contested = true;
            TestAssert.That(contested,
                "A border with both sides' ground forces present reports contested pressure");
        }

        private static void TestContactPressure()
        {
            var grid = new TacticalSectorGrid(1000f, 100000f);
            grid.WorldToCell(0f, 0f, out int col, out int row);

            grid.AddTroopPresence(0f, 0f, 4f, false, 0f);
            TestAssert.That(grid.GetSectorPressure(col, row) == 0f,
                "One side's presence alone is not contact pressure");
            grid.AddTroopPresence(0f, 0f, 1f, true, 0f);
            float oneSided = grid.GetSectorPressure(col, row);
            TestAssert.That(oneSided > 0.3f && oneSided < 0.5f,
                "Four-to-one odds read as one-sided contact, not a firefight");
            grid.AddTroopPresence(0f, 0f, 3f, true, 0f);
            TestAssert.That(grid.GetSectorPressure(col, row) > 0.95f,
                "Even forces on one cell read as full contact");
            TestAssert.That(grid.GetSectorPressure(-1, 0) == 0f && grid.GetSectorPressure(0, grid.ResolutionY) == 0f,
                "Off-grid cells report no pressure");
        }

        /// <summary>
        /// A contested square is hatched from the two faction colours only, with the stripe
        /// runs following the control percentage — never a third "contested" colour.
        /// </summary>
        private static void TestContestedTint()
        {
            // Coarse cells on purpose: the hatch must read at this texture size.
            var grid = new TacticalSectorGrid(10000f, 100000f);
            grid.AddTroopPresence(0f, 0f, 2f, false, 0f);
            grid.EvaluateSectors();
            grid.AddTroopPresence(0f, 0f, 8f, true, 0f);
            grid.EvaluateSectors();
            grid.WorldToCell(0f, 0f, out int col, out int row);
            TestAssert.That(grid.GetSectorControl(col, row) == SectorControl.Contested,
                "Ground held while hostile forces push on it is contested");

            Color32[] pixels = grid.BakeTexture(128, 128, true, 0.35f);
            int red = 0, blue = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                TestAssert.That(!(pixel.r > 150 && pixel.g > 130 && pixel.b < 110),
                    "Contested ground never bakes yellow");
                if (pixel.a == 0) continue;
                if (pixel.r > pixel.b) red++;
                else if (pixel.b > pixel.r) blue++;
            }
            TestAssert.That(red > 0 && blue > 0, "A contested square draws both factions' colours");
            TestAssert.That(blue > red * 2, "Stripe width follows the control percentage");
        }

        /// <summary>
        /// The bake clusters the field with a partition tree: a uniform board is one block,
        /// a divided one is a handful, and non-mergeable (contested) cells stay their own.
        /// </summary>
        private static void TestClusterTree()
        {
            var keys = new byte[16];
            for (int i = 0; i < keys.Length; i++) keys[i] = 1;
            var clusters = new SectorClusterTree.Cluster[16];
            int count = SectorClusterTree.Build(keys, 4, 4, clusters);
            TestAssert.That(count == 1 && clusters[0].Width == 4 && clusters[0].Height == 4,
                "A uniform field clusters into one block covering every cell");

            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    keys[r * 4 + c] = (byte)(c < 2 ? 1 : 2);
            count = SectorClusterTree.Build(keys, 4, 4, clusters);
            TestAssert.That(count == 2 && clusters[0].Width == 2 && clusters[0].Height == 4,
                "A theater split down the middle clusters into two blocks");

            keys[5] = (byte)(2 | SectorClusterTree.NonMergeable);
            count = SectorClusterTree.Build(keys, 4, 4, clusters);
            int covered = 0;
            bool isolated = false;
            for (int i = 0; i < count; i++)
            {
                covered += clusters[i].Width * clusters[i].Height;
                if (clusters[i].X == 1 && clusters[i].Y == 1)
                    isolated = clusters[i].Width == 1 && clusters[i].Height == 1;
            }
            TestAssert.That(isolated, "A non-mergeable cell stays its own cluster");
            TestAssert.That(covered == 16, "Clusters cover every cell exactly once");

            var field = new TacticalSectorGrid(1000f, 100000f);
            field.RegisterNode(1, "Only base", 0f, 0f, SectorControl.Friendly, 0f, true);
            field.EvaluateSectors();
            TestAssert.That(field.TotalSectors == 10000 && field.FriendlySectorCount == field.TotalSectors,
                "Base-grid cells tile a 100 km theater at one square per kilometre");
            TestAssert.That(field.ClusterCount == 1, "A single-colour field bakes as one cluster");

            Snapshot(field);
            field.EvaluateSectors(0f);
            TestAssert.That(field.ClusterCount < field.TotalSectors / 8,
                "A divided theater bakes as blocks, not one cluster per cell");
        }

        /// <summary>
        /// Enclosed ground with no opposing presence is claimed by the enclosing side; a
        /// pocket holding enemy troops, or an anchored airbase, is never auto-captured.
        /// </summary>
        private static void TestEncirclement()
        {
            var open = new TacticalSectorGrid(1000f, 9000f);
            EncircledPocket(open, hostileNode: true, anchoredBase: false, hostileTroops: false);
            open.EvaluateSectors(0f);
            open.WorldToCell(0f, 0f, out int col, out int row);
            TestAssert.That(open.GetSectorControl(col, row) == SectorControl.Hostile,
                "A cut-off hostile cell starts hostile");
            EncircledPocket(open, hostileNode: true, anchoredBase: false, hostileTroops: false);
            open.EvaluateSectors(300f);
            TestAssert.That(open.GetSectorControl(col, row) == SectorControl.Friendly,
                "An encircled pocket with no enemy troops is claimed by the enclosing side");

            var held = new TacticalSectorGrid(1000f, 9000f);
            EncircledPocket(held, hostileNode: true, anchoredBase: false, hostileTroops: true);
            held.EvaluateSectors(0f);
            EncircledPocket(held, hostileNode: true, anchoredBase: false, hostileTroops: true);
            held.EvaluateSectors(300f);
            held.WorldToCell(0f, 0f, out col, out row);
            TestAssert.That(held.GetSectorControl(col, row) != SectorControl.Friendly,
                "An encircled pocket holding enemy troops is not auto-captured");

            var anchored = new TacticalSectorGrid(1000f, 9000f);
            EncircledPocket(anchored, hostileNode: true, anchoredBase: true, hostileTroops: false);
            anchored.EvaluateSectors(0f);
            EncircledPocket(anchored, hostileNode: true, anchoredBase: true, hostileTroops: false);
            anchored.EvaluateSectors(300f);
            anchored.WorldToCell(0f, 0f, out col, out row);
            TestAssert.That(anchored.GetSectorControl(col, row) == SectorControl.Hostile,
                "An encircled airbase stays anchored to its real ownership");
        }

        /// <summary>
        /// One observation snapshot: friendly troops on the eight cells around (0,0), an
        /// optional hostile node or base in the middle and optional hostile troops with it.
        /// </summary>
        private static void EncircledPocket(TacticalSectorGrid grid, bool hostileNode, bool anchoredBase, bool hostileTroops)
        {
            grid.Clear();
            grid.WorldToCell(0f, 0f, out int col, out int row);
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    grid.CellToCenter(col + dx, row + dz, out float ringX, out float ringZ);
                    grid.AddTroopPresence(ringX, ringZ, 5f, false, 0f);
                }
            }
            if (hostileNode)
            {
                grid.CellToCenter(col, row, out float x, out float z);
                grid.RegisterNode(99, "Cut off", x, z, SectorControl.Hostile, 2000f, anchoredBase);
            }
            if (hostileTroops) grid.AddTroopPresence(0f, 0f, 5f, true, 0f);
        }

        private static int CopyTraces(TacticalSectorGrid grid, out FrontlineTracePoint[] points,
            out int[] lengths, out float[] pressure)        {
            points = new FrontlineTracePoint[FrontlineTraceLimits.MaximumPoints];
            lengths = new int[FrontlineTraceLimits.MaximumTraces];
            pressure = new float[FrontlineTraceLimits.MaximumTraces];
            return grid.CopyFrontlineTraces(points, lengths, pressure);
        }

        private static TacticalSectorGrid MakeFront()
        {
            var grid = new TacticalSectorGrid(1000f, 100000f);
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
            var portrait = new TacticalSectorGrid(1000f, 80000f, 160000f);
            TestAssert.That(portrait.CellSize == 1000f && portrait.ResolutionX == 80 && portrait.ResolutionY == 160 &&
                portrait.TotalSectors <= TacticalSectorGrid.MaximumCells,
                "Portrait theaters keep base-grid cells without exceeding the cell budget");
            var extreme = new TacticalSectorGrid(1000f, 1001f, 10000000f);
            TestAssert.That(extreme.CellSize > 1000f && extreme.TotalSectors <= TacticalSectorGrid.MaximumCells,
                "Giant theaters coarsen the cells in powers of two and stay within the fixed buffer");
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
            for (int i = 0; i < 1000; i++) portrait.RegisterNode(i, "Node", 0f, 0f, SectorControl.Friendly, 0f, false);
            TestAssert.That(portrait.TotalNodesCount == TacticalSectorGrid.MaximumNodes, "Node growth has a hard ceiling");
            portrait.EvaluateSectors();
            portrait.SetWorldSize(100000f);
            TestAssert.That(portrait.TotalNodesCount == 0 && portrait.FriendlySectorCount == 0,
                "Dimension changes invalidate the old coordinate-space history");
            foreach (Color32 pixel in portrait.BakeTexture(128, 128, false, 1f))
                TestAssert.That(pixel.a == 0, "Disabling the sector overlay must leave a transparent texture");
        }
    }
}
