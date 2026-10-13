using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class TerrainGhostTests
    {
        private const int Side = TerrainGhostGrid.Side;
        private const int Nodes = TerrainGhostGrid.Nodes;
        private const float Cell = TerrainGhostGrid.CellMetres;
        private const int Centre = TerrainGhostGrid.Cells / 2;
        private static readonly GhostTint Ink = new GhostTint(56, 222, 82);
        private static readonly GhostTint Caution = new GhostTint(243, 217, 74);

        public static void Run()
        {
            TestSampleAndLandMask();
            TestSizesAndIndices();
            TestSnapping();
            TestEdgeFade();
            TestNearFade();
            TestSeaThinning();
            TestCautionRule();
            TestHorizonFade();
            TestThinning();
            TestStrength();
            TestFillTime();
        }

        // ------------------------------------------------------------------ the sampler

        private static void TestSampleAndLandMask()
        {
            // A 3 x 3 sheet, 100 m apart, centred on the origin: x and z run -100..100.
            var heights = new float[] { 10, 20, 30, 40, 50, 60, 70, 80, 90 };
            var field = new TerrainHeightField(heights, AllTrue(9), 3, 100f, 0f);
            TestAssert.Near(field.Sample(-100f, -100f), 10f, "Height field: a corner sample reads its own height");
            TestAssert.Near(field.Sample(100f, -100f), 30f, "Height field: the x axis runs along the row");
            TestAssert.Near(field.Sample(-100f, 100f), 70f, "Height field: the z axis runs along the column");
            TestAssert.Near(field.Sample(0f, 0f), 50f, "Height field: the centre reads the middle sample");
            TestAssert.Near(field.Sample(-50f, -100f), 15f, "Height field: halfway along an edge averages its two ends");
            TestAssert.Near(field.Sample(50f, 50f), 70f, "Height field: a cell centre averages its four corners");
            TestAssert.Near(field.Sample(25f, 0f), 52.5f, "Height field: a quarter step is a quarter of the slope");
            TestAssert.Near(field.Sample(-150f, 0f), 0f, "Height field: off the sheet there is only sea");
            TestAssert.Near(field.Sample(float.NaN, 0f), 0f, "Height field: a position that is not a number reads as sea");
            TestAssert.That(field.IsLand(0f, 0f) && field.IsLand(-100f, 100f), "Height field: dry samples are land");
            TestAssert.That(!field.IsLand(-160f, 0f) && !field.IsLand(0f, 160f), "Height field: off the sheet is not land");

            // A shore: one sea sample (encoded at -200 m) beside dry ground. Water counts as the sea level, never as -200 m.
            var shore = new TerrainHeightField(new float[] { -200f, 100f, 100f, 100f },
                new[] { false, true, true, true }, 2, 100f, 0f);
            TestAssert.Near(shore.Sample(-50f, -50f), 0f, "Height field: a sea sample reads the sea level");
            TestAssert.Near(shore.Sample(0f, -50f), 50f, "Height field: a shore slopes from the sea level, not from the encoding floor");
            TestAssert.Near(shore.Sample(50f, -50f), 100f, "Height field: dry ground keeps its height");
            TestAssert.That(!shore.IsLand(-50f, -50f) && !shore.IsLand(-10f, -50f) && shore.IsLand(10f, -50f) && shore.IsLand(50f, 50f),
                "Height field: land is where the nearest sample is dry");
            var pit = new TerrainHeightField(new float[] { -30f, -30f, -30f, -30f }, AllTrue(4), 2, 100f, 0f);
            TestAssert.Near(pit.Sample(0f, 0f), 0f, "Height field: dry ground below the water level reads the water level");
            var raised = new TerrainHeightField(new float[] { -30f, -30f, -30f, -30f }, AllTrue(4), 2, 100f, 5f);
            TestAssert.Near(raised.Sample(0f, 0f), 5f, "Height field: the sea level is the field's own");
            TestAssert.Throws<ArgumentException>(() => new TerrainHeightField(new float[4], new bool[3], 2, 100f, 0f),
                "Height field: a mask of another size is refused");
        }

        // ------------------------------------------------------------------ the buffers

        private static void TestSizesAndIndices()
        {
            TestAssert.Eq(TerrainGhostGrid.VertexCount, 8450, "Terrain ghost: two strips of 65 x 65 nodes");
            TestAssert.Eq(TerrainGhostGrid.IndexCount, 16640, "Terrain ghost: 2 x 65 lines of 64 segments, two indices each");
            TestAssert.Near(TerrainGhostGrid.Cells * TerrainGhostGrid.CellMetres, 20480f, "Terrain ghost: about 10 km each way from the centre");
            TestAssert.Eq(Marshal.SizeOf<GhostVertex>(), 16, "Terrain ghost: a vertex is a position and a colour, 16 bytes");

            int[] indices = TerrainGhostGrid.BuildIndices();
            TestAssert.Eq(indices.Length, TerrainGhostGrid.IndexCount, "Terrain ghost: the index buffer is the size the mesh expects");
            var used = new bool[TerrainGhostGrid.VertexCount];
            for (int p = 0; p < indices.Length; p += 2)
            {
                int a = indices[p], b = indices[p + 1];
                TestAssert.That(a >= 0 && b < TerrainGhostGrid.VertexCount && b == a + 1,
                    "Terrain ghost: a segment joins a vertex to the next one along its line");
                TestAssert.That((a % Nodes) % Side < Side - 1, "Terrain ghost: no segment runs from the end of one line into the next");
                used[a] = used[b] = true;
            }
            TestAssert.That(Array.IndexOf(used, false) < 0, "Terrain ghost: every vertex belongs to a line");
        }

        // ------------------------------------------------------------------ snapping

        private static void TestSnapping()
        {
            TerrainGhostGrid.Snap(0f, 0f, out int x, out int z);
            TestAssert.That(x == 0 && z == 0, "Terrain ghost: the origin is the centre of cell 0");
            TerrainGhostGrid.Snap(159f, -159f, out x, out z);
            TestAssert.That(x == 0 && z == 0, "Terrain ghost: anywhere inside a cell keeps its centre");
            TerrainGhostGrid.Snap(161f, -161f, out x, out z);
            TestAssert.That(x == 1 && z == -1, "Terrain ghost: past half a cell the next cell takes over, either way");
            TerrainGhostGrid.Snap(-5000f, 12345f, out x, out z);
            TestAssert.That(x == -16 && z == 39, "Terrain ghost: cells count from the global origin (got " + x + ", " + z + ")");
            TestAssert.Near(TerrainGhostGrid.Anchor(3), 960f, "Terrain ghost: a cell's centre is a whole number of cells from the origin");

            // A ramp east: height is 50 m plus one per sample. The lines must stay on the same ground when the grid moves a cell.
            var field = Field(201, Cell, (ix, iz) => 50f + ix, null);
            var here = new GhostVertex[TerrainGhostGrid.VertexCount];
            var next = new GhostVertex[TerrainGhostGrid.VertexCount];
            TerrainGhostGrid.FillHeights(field, 5, -3, here);
            TerrainGhostGrid.FillHeights(field, 6, -3, next);
            for (int j = 0; j < Side; j++)
            for (int i = 0; i < Side; i++)
            {
                GhostVertex v = here[j * Side + i];
                float gx = TerrainGhostGrid.Anchor(5) + v.X, gz = TerrainGhostGrid.Anchor(-3) + v.Z;
                TestAssert.Near(gx % Cell, 0f, "Terrain ghost: every line lies on a multiple of the cell size");
                TestAssert.Near(gz % Cell, 0f, "Terrain ghost: ... in both directions");
                TestAssert.Near(v.Y, field.Sample(gx, gz), "Terrain ghost: a vertex reads the ground under it");
                if (i < Side - 1)
                    TestAssert.Near(next[j * Side + i].Y, here[j * Side + i + 1].Y,
                        "Terrain ghost: after the grid steps a cell a line keeps the height of the ground it was on");
            }
            for (int j = 0; j < Side; j++)
            for (int i = 0; i < Side; i++)
            {
                GhostVertex along = here[j * Side + i], across = here[Nodes + i * Side + j];
                TestAssert.That(along.X == across.X && along.Y == across.Y && along.Z == across.Z,
                    "Terrain ghost: the two lines through a node agree on where the node is");
            }
        }

        // ------------------------------------------------------------------ the alpha rules

        private static void TestEdgeFade()
        {
            // Flat dry ground 100 m up, the aircraft 2900 m above it at the centre: only the edge fade is in play.
            var field = Field(201, Cell, (ix, iz) => 100f, null);
            var v = Filled(field, 0, 0, 0f, 3000f, 0f, 1f);
            byte full = (byte)(TerrainGhostGrid.BaseAlpha * 255f + .5f);
            for (int j = 0; j < Side; j++)
            for (int i = 0; i < Side; i++)
            {
                GhostVertex a = v[j * Side + i];
                float d = (float)Math.Sqrt(a.X * a.X + a.Z * a.Z);
                // The far odd lines are thinned out (see TestThinning): the lines whose number is a multiple of four, and anything within 4 km, are the ones kept whole.
                if (d <= TerrainGhostGrid.FadeStart * TerrainGhostGrid.Radius && TerrainGhostGrid.LineThinning(j - Centre, d) >= 1f)
                    TestAssert.Eq(a.A, full, "Terrain ghost: lines are at full strength out to 60% of the radius");
                if (d >= TerrainGhostGrid.Radius) TestAssert.Eq(a.A, (byte)0, "Terrain ghost: nothing is drawn at the edge or beyond");
                TestAssert.That(a.A <= full, "Terrain ghost: the edge only takes strength away");
            }
            int row = Centre * Side;
            byte last = full;
            bool fell = false;
            for (int i = Centre; i < Side; i++)
            {
                byte alpha = v[row + i].A;
                TestAssert.That(alpha <= last, "Terrain ghost: strength never rises again going out from the aircraft");
                if (alpha < last) fell = true;
                last = alpha;
            }
            TestAssert.That(fell && last == 0, "Terrain ghost: strength falls smoothly to zero at the edge");
            byte mid = v[row + Centre + 25].A;
            TestAssert.That(mid > 0 && mid < full / 2 + 8 && mid > full / 2 - 8,
                "Terrain ghost: 78% of the way out it is about half strength (got " + mid + ")");
            TestAssert.That(v[row + Centre + 20].A < full && v[row + Centre + 20].A > mid, "Terrain ghost: the fall starts after 60% of the radius");
        }

        private static void TestNearFade()
        {
            // The aircraft hangs over the middle node, 150 m above the ground: that node is 150 m away, half way through the 300 m fade.
            var field = Field(201, Cell, (ix, iz) => 100f, null);
            var v = Filled(field, 0, 0, 0f, 250f, 0f, 1f);
            byte full = (byte)(TerrainGhostGrid.BaseAlpha * 255f + .5f);
            GhostVertex under = v[Centre * Side + Centre];
            TestAssert.That(Math.Abs(under.A - full / 2) <= 1, "Terrain ghost: a line 150 m from the aircraft is at half strength (got " + under.A + ")");
            TestAssert.Eq(v[Centre * Side + Centre + 1].A, full, "Terrain ghost: a line 350 m away is at full strength");
            TestAssert.That(!(v[Centre * Side + Centre + 1].R == Caution.R), "Terrain ghost: ground exactly 150 m below the aircraft is not yet caution");

            v = Filled(field, 0, 0, 0f, 100f, 0f, 1f);
            TestAssert.Eq(v[Centre * Side + Centre].A, (byte)0, "Terrain ghost: a line at the aircraft itself is gone");
            TestAssert.Eq(v[Centre * Side + Centre + 1].A, (byte)(full * 2), "Terrain ghost: ... while the ground 320 m off, level with it, glows at double strength, no horizon dimming");
        }

        private static void TestSeaThinning()
        {
            var sea = Field(201, Cell, (ix, iz) => -200f, (ix, iz) => false);
            byte thin = (byte)(TerrainGhostGrid.BaseAlpha * TerrainGhostGrid.SeaStrength * 255f + .5f);
            var v = Filled(sea, 0, 0, 0f, 3000f, 0f, 1f);
            for (int j = 0; j < Side; j++)
            for (int i = 0; i < Side; i++)
            {
                GhostVertex a = v[j * Side + i], b = v[Nodes + i * Side + j];
                float d = (float)Math.Sqrt(a.X * a.X + a.Z * a.Z);
                if (d > TerrainGhostGrid.FadeStart * TerrainGhostGrid.Radius) continue;
                TestAssert.Near(a.Y, 0f, "Terrain ghost: the sea is drawn at sea level");
                TestAssert.Eq(a.A, j % 4 == 0 ? thin : (byte)0, "Terrain ghost: over the sea every fourth line along X is kept, at 30% strength");
                TestAssert.Eq(b.A, i % 4 == 0 ? thin : (byte)0, "Terrain ghost: ... and every fourth line along Z");
            }
            // The kept lines are numbered on the ground, so they stay put when the grid snaps one cell east.
            v = Filled(sea, 1, 0, Cell, 3000f, 0f, 1f);
            for (int i = 0; i < Side; i++)
            {
                GhostVertex a = v[Nodes + i * Side + Centre];
                if (Math.Abs(a.X) > TerrainGhostGrid.FadeStart * TerrainGhostGrid.Radius) continue;
                TestAssert.Eq(a.A, (TerrainGhostGrid.Anchor(1) + a.X) % (4 * Cell) == 0f ? thin : (byte)0,
                    "Terrain ghost: the same ground lines are kept after the grid moves a cell");
            }
            // Land is not thinned.
            var land = Filled(Field(201, Cell, (ix, iz) => 100f, null), 0, 0, 0f, 3000f, 0f, 1f);
            TestAssert.Eq(land[Centre * Side + Centre + 1].A, (byte)(TerrainGhostGrid.BaseAlpha * 255f + .5f), "Terrain ghost: land lines are all kept");
        }

        private static void TestCautionRule()
        {
            // West of the origin the ground is 900 m, east of it 700 m (a sample every cell, so the nodes read them exactly).
            var field = Field(201, Cell, (ix, iz) => ix < 100 ? 900f : 700f, null);
            byte glow = (byte)(TerrainGhostGrid.BaseAlpha * TerrainGhostGrid.CautionBoost * 255f + .5f);
            var v = new GhostVertex[TerrainGhostGrid.VertexCount];
            TerrainGhostGrid.FillHeights(field, 0, 0, v);
            int counted = TerrainGhostGrid.FillColours(0, 0, 0f, 1000f, 0f, 1f, Ink, Caution, 0f, v);
            int seen = 0;
            for (int j = 0; j < Side; j++)
            for (int i = 0; i < Side; i++)
            {
                GhostVertex a = v[j * Side + i];
                float d = (float)Math.Sqrt(a.X * a.X + a.Z * a.Z);
                if (d > TerrainGhostGrid.FadeStart * TerrainGhostGrid.Radius || d < TerrainGhostGrid.NearMetres + 150f) continue;
                // Out here the ink carries the horizon factor for how far below the aircraft's horizontal the vertex lies, and the thinning of its line; caution ground carries neither.
                float factor = TerrainGhostGrid.HorizonFactor(1000f - a.Y, d) * TerrainGhostGrid.LineThinning(j - Centre, d);
                byte expectInk = (byte)(TerrainGhostGrid.BaseAlpha * factor * 255f + .5f);
                if (a.X < 0f)
                {
                    TestAssert.That(a.R == Caution.R && a.G == Caution.G && a.B == Caution.B && a.A == glow,
                        "Terrain ghost: ground higher than 150 m under the aircraft is caution at double strength, whatever its angle or distance");
                    seen++;
                }
                else
                    TestAssert.That(a.R == Ink.R && a.G == Ink.G && a.B == Ink.B && Math.Abs(a.A - expectInk) <= 1,
                        "Terrain ghost: lower ground is the HUD ink at normal strength");
            }
            TestAssert.That(seen > 100, "Terrain ghost: the caution ground is in the grid");
            int drawn = 0;
            for (int p = 0; p < v.Length; p++) if (v[p].R == Caution.R && v[p].A > 0) drawn++;
            TestAssert.Eq(counted, drawn, "Terrain ghost: the fill reports how many vertices glow");

            // The rule is strict: ground exactly 150 m under the aircraft is still ink, a metre higher is caution.
            TerrainGhostGrid.FillColours(0, 0, 0f, 1050f, 0f, 1f, Ink, Caution, 0f, v);
            TestAssert.That(v[Centre * Side + Centre - 5].R == Ink.R, "Terrain ghost: ground exactly 150 m under the aircraft is not caution");
            TerrainGhostGrid.FillColours(0, 0, 0f, 1049f, 0f, 1f, Ink, Caution, 0f, v);
            TestAssert.That(v[Centre * Side + Centre - 5].R == Caution.R, "Terrain ghost: ground 151 m under it is");
        }

        private static void TestHorizonFade()
        {
            const float Rad = (float)Math.PI / 180f;
            // The pure rule: straight below and from 8 degrees down is full, 1 degree and less (and ground above the aircraft) is the 25% floor, in between it is linear in the angle.
            TestAssert.Near(TerrainGhostGrid.HorizonFactor(1000f, 0f), 1f, "Terrain ghost: ground straight below is at full strength");
            TestAssert.Near(TerrainGhostGrid.HorizonFactor(1000f, 1000f / (float)Math.Tan(8f * Rad)), 1f, "Terrain ghost: ground 8 degrees below the horizontal is at full strength");
            TestAssert.Near(TerrainGhostGrid.HorizonFactor(1000f, 1000f / (float)Math.Tan(20f * Rad)), 1f, "Terrain ghost: ... and steeper stays full");
            TestAssert.Near(TerrainGhostGrid.HorizonFactor(1000f, 1000f / (float)Math.Tan(1f * Rad)), .25f, "Terrain ghost: ground 1 degree below the horizontal is at the 25% floor");
            TestAssert.Near(TerrainGhostGrid.HorizonFactor(0f, 5000f), .25f, "Terrain ghost: ground level with the aircraft is at the floor");
            TestAssert.Near(TerrainGhostGrid.HorizonFactor(-500f, 3000f), .25f, "Terrain ghost: ground above the aircraft is at the floor");
            TestAssert.Near(TerrainGhostGrid.HorizonFactor(-500f, 0f), .25f, "Terrain ghost: ... even straight above");
            float flat = 4000f;
            TestAssert.Near(TerrainGhostGrid.HorizonFactor(flat * (float)Math.Tan(4.5f * Rad), flat), .625f, "Terrain ghost: half way down the ramp (4.5 degrees) it is 62.5%");
            float last = 0f;
            for (float degrees = 0f; degrees <= 12f; degrees += .25f)
            {
                float factor = TerrainGhostGrid.HorizonFactor(flat * (float)Math.Tan(degrees * Rad), flat);
                TestAssert.That(factor >= last - 1e-6f, "Terrain ghost: the fade never falls as the ground gets steeper");
                last = factor;
            }

            // Through the grid: the aircraft is 3000 m up over a plain 100 m high. The ground at 3200 m east is raised to 8 degrees below, 4160 m east to 4.5 degrees below, 1600 m west above the aircraft.
            float eight = 3000f - 3200f * (float)Math.Tan(8f * Rad), mid = 3000f - 4160f * (float)Math.Tan(4.5f * Rad);
            var field = Field(201, Cell, (ix, iz) => iz == 100 && ix == 110 ? eight : iz == 100 && ix == 113 ? mid : iz == 100 && ix == 95 ? 3200f : 100f, null);
            var v = Filled(field, 0, 0, 0f, 3000f, 0f, 1f);
            byte full = (byte)(TerrainGhostGrid.BaseAlpha * 255f + .5f);
            GhostVertex at8 = v[Centre * Side + Centre + 10], at45 = v[Centre * Side + Centre + 13], above = v[Centre * Side + Centre - 5];
            TestAssert.That(at8.R == Ink.R && Math.Abs(at8.A - full) <= 1, "Terrain ghost: the vertex 8 degrees below is HUD ink at full strength (got " + at8.A + ")");
            TestAssert.That(at45.R == Ink.R && Math.Abs(at45.A - (byte)(TerrainGhostGrid.BaseAlpha * .625f * 255f + .5f)) <= 1, "Terrain ghost: the vertex 4.5 degrees below is at 62.5% (got " + at45.A + ")");
            TestAssert.That(above.R == Caution.R && Math.Abs(above.A - (byte)(TerrainGhostGrid.BaseAlpha * TerrainGhostGrid.CautionBoost * 255f + .5f)) <= 1,
                "Terrain ghost: ground above the aircraft keeps the caution colour and its double, with no horizon dimming (got " + above.A + ")");
        }

        private static float S(float t)
        {
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return t * t * (3f - 2f * t);
        }

        private static void TestThinning()
        {
            // The pure rule: the line numbered n, m metres from the aircraft.
            const float M = 1000f;
            TestAssert.Near(TerrainGhostGrid.LineThinning(1, 3f * M), 1f, "Terrain ghost: an odd line at 3 km is kept");
            TestAssert.Near(TerrainGhostGrid.LineThinning(1, 4f * M), 1f, "Terrain ghost: ... and at 4 km");
            TestAssert.Near(TerrainGhostGrid.LineThinning(1, 4.5f * M), .5f, "Terrain ghost: an odd line at 4.5 km is at half");
            TestAssert.Near(TerrainGhostGrid.LineThinning(-3, 4.5f * M), .5f, "Terrain ghost: ... odd negative numbers too");
            TestAssert.Near(TerrainGhostGrid.LineThinning(1, 5f * M), 0f, "Terrain ghost: an odd line at 5 km is gone");
            TestAssert.Near(TerrainGhostGrid.LineThinning(1, 6f * M), 0f, "Terrain ghost: ... and at 6 km");
            TestAssert.Near(TerrainGhostGrid.LineThinning(2, 6f * M), 1f, "Terrain ghost: a line numbered 2 mod 4 at 6 km is kept");
            TestAssert.Near(TerrainGhostGrid.LineThinning(-2, 6f * M), 1f, "Terrain ghost: ... as is -2 (2 mod 4)");
            TestAssert.Near(TerrainGhostGrid.LineThinning(2, 7.5f * M), .5f, "Terrain ghost: ... at half at 7.5 km");
            TestAssert.Near(TerrainGhostGrid.LineThinning(2, 9f * M), 0f, "Terrain ghost: ... and gone at 9 km");
            TestAssert.Near(TerrainGhostGrid.LineThinning(4, 9f * M), 1f, "Terrain ghost: a line numbered a multiple of four at 9 km is kept");
            TestAssert.Near(TerrainGhostGrid.LineThinning(0, 20f * M), 1f, "Terrain ghost: ... and line 0 at any distance");
            float last = 1f;
            for (float m = 0f; m <= 10f * M; m += 100f)
            {
                float t = TerrainGhostGrid.LineThinning(1, m);
                TestAssert.That(t <= last + 1e-6f, "Terrain ghost: an odd line only fades with distance");
                last = t;
            }

            // Through the grid: the aircraft hangs 3000 m over a plain 100 m high at the cell centre, so a vertex's distance is its offset.
            var field = Field(201, Cell, (ix, iz) => 100f, null);
            var v = Filled(field, 0, 0, 0f, 3000f, 0f, 1f);
            byte full = (byte)(TerrainGhostGrid.BaseAlpha * 255f + .5f);
            // Row 1 (an odd line, 320 m north): its nodes at 2880 m east (3 km) stay whole, at 4480 m east (4.5 km) are part way, at 6080 m (6 km) are gone.
            GhostVertex near = v[(Centre + 1) * Side + Centre + 9], mid = v[(Centre + 1) * Side + Centre + 14], far = v[(Centre + 1) * Side + Centre + 19];
            TestAssert.Eq(near.A, full, "Terrain ghost: a node of an odd line 3 km out is at full strength");
            float midDistance = (float)Math.Sqrt(14f * Cell * 14f * Cell + Cell * Cell);
            byte midExpected = (byte)(TerrainGhostGrid.BaseAlpha * TerrainGhostGrid.LineThinning(1, midDistance) * 255f + .5f);
            TestAssert.That(Math.Abs(mid.A - midExpected) <= 1 && mid.A > 0 && mid.A < full, "Terrain ghost: ... 4.5 km out it is part way (got " + mid.A + ", wanted " + midExpected + ")");
            TestAssert.Eq(far.A, (byte)0, "Terrain ghost: ... and 6 km out it is gone");
            // Row 2 (a line numbered 2 mod 4): kept at 6 km, gone at 9 km; row 4 (a multiple of four): kept at 9 km (the edge fade only).
            GhostVertex two6 = v[(Centre + 2) * Side + Centre + 19], two9 = v[(Centre + 2) * Side + Centre + 28], four9 = v[(Centre + 4) * Side + Centre + 28];
            TestAssert.Eq(two6.A, full, "Terrain ghost: a node of a 2 mod 4 line 6 km out is kept");
            TestAssert.Eq(two9.A, (byte)0, "Terrain ghost: ... 9 km out it is gone");
            float d9 = (float)Math.Sqrt(28f * Cell * 28f * Cell + 4f * Cell * 4f * Cell);
            byte fourExpected = (byte)(TerrainGhostGrid.BaseAlpha * (1f - S((d9 / TerrainGhostGrid.Radius - TerrainGhostGrid.FadeStart) / (1f - TerrainGhostGrid.FadeStart))) * 255f + .5f);
            TestAssert.That(four9.A > 0 && Math.Abs(four9.A - fourExpected) <= 1, "Terrain ghost: a node of a multiple-of-four line 9 km out is kept (the edge fade only: got " + four9.A + ", wanted " + fourExpected + ")");
            // The same holds for the lines that run along Z.
            GhostVertex across = v[Nodes + (Centre + 19) * Side + Centre + 1];
            TestAssert.Eq(across.A, (byte)0, "Terrain ghost: an odd line along Z is thinned the same way (6 km out is gone)");

            // Caution ground is never thinned or dimmed: ground 80 m under the aircraft (caution), on an odd line, 5.1 km out (1 degree down) and 9 km out.
            var cautionField = Field(201, Cell, (ix, iz) => 2920f, null);
            var c = Filled(cautionField, 0, 0, 0f, 3000f, 0f, 1f);
            GhostVertex c5 = c[(Centre + 1) * Side + Centre + 16], c9 = c[(Centre + 1) * Side + Centre + 28];
            d9 = (float)Math.Sqrt(28f * Cell * 28f * Cell + Cell * Cell);
            TestAssert.That(c5.R == Caution.R && c5.A == (byte)(TerrainGhostGrid.BaseAlpha * TerrainGhostGrid.CautionBoost * 255f + .5f),
                "Terrain ghost: caution ground on an odd line 5.1 km out and 1 degree down is at full caution strength (got " + c5.A + ")");
            byte c9Expected = (byte)(TerrainGhostGrid.BaseAlpha * TerrainGhostGrid.CautionBoost * (1f - S((d9 / TerrainGhostGrid.Radius - TerrainGhostGrid.FadeStart) / (1f - TerrainGhostGrid.FadeStart))) * 255f + .5f);
            TestAssert.That(c9.R == Caution.R && c9.A > 0 && Math.Abs(c9.A - c9Expected) <= 1,
                "Terrain ghost: ... and 9 km out on an odd line it is the edge fade only (got " + c9.A + ", wanted " + c9Expected + ")");

            // The kept set does not move with the snapped origin: the same ground node has the same strength whichever cell the grid is centred on.
            var a = new GhostVertex[TerrainGhostGrid.VertexCount];
            var b = new GhostVertex[TerrainGhostGrid.VertexCount];
            TerrainGhostGrid.FillHeights(field, 0, 0, a);
            TerrainGhostGrid.FillColours(0, 0, 160f, 3000f, -80f, 1f, Ink, Caution, field.SeaLevel, a);
            TerrainGhostGrid.FillHeights(field, 1, 0, b);
            TerrainGhostGrid.FillColours(1, 0, 160f, 3000f, -80f, 1f, Ink, Caution, field.SeaLevel, b);
            int compared = 0;
            for (int j = 0; j < Side; j++)
            for (int i = 1; i < Side; i++)
            {
                // Node (i, j) of the grid at cell 0 is node (i - 1, j) of the grid at cell 1.
                TestAssert.Eq(a[j * Side + i].A, b[j * Side + i - 1].A, "Terrain ghost: a node along X keeps its strength when the grid moves a cell");
                TestAssert.Eq(a[Nodes + i * Side + j].A, b[Nodes + (i - 1) * Side + j].A, "Terrain ghost: ... and so does a node along Z");
                if (a[j * Side + i].A > 0) compared++;
            }
            TestAssert.That(compared > 1000, "Terrain ghost: the comparison covered the lit lines");
        }

        private static void TestStrength()
        {
            var field = Field(201, Cell, (ix, iz) => 100f, null);
            byte full = (byte)(TerrainGhostGrid.BaseAlpha * 255f + .5f);
            var v = Filled(field, 0, 0, 0f, 3000f, 0f, 0f);
            int lit = 0;
            foreach (GhostVertex a in v) lit += a.A;
            TestAssert.Eq(lit, 0, "Terrain ghost: strength 0 draws nothing");
            v = Filled(field, 0, 0, 0f, 3000f, 0f, .5f);
            TestAssert.That(Math.Abs(v[Centre * Side + Centre + 1].A - full / 2) <= 1, "Terrain ghost: half strength halves the lines");
            v = Filled(field, 0, 0, 0f, 3000f, 0f, 7f);
            TestAssert.Eq(v[Centre * Side + Centre + 1].A, full, "Terrain ghost: strength is clamped to 1");
        }

        // ------------------------------------------------------------------ cost

        private static void TestFillTime()
        {
            // The real sheet: 513 x 513 samples, 160 m apart, rolling ground with a coast.
            var field = Field(513, 160f,
                (ix, iz) => 600f + 500f * (float)(Math.Sin(ix * .05) * Math.Cos(iz * .04)) + 150f * (float)Math.Sin(ix * .31 + iz * .27),
                (ix, iz) => ix > 40);
            var v = new GhostVertex[TerrainGhostGrid.VertexCount];
            double heights = 0, colours = 0;
            const int warm = 30, runs = 400;
            for (int n = -warm; n < runs; n++)
            {
                int cx = 10 + n % 37, cz = -20 + n % 23;
                float px = TerrainGhostGrid.Anchor(cx) + 70f, pz = TerrainGhostGrid.Anchor(cz) - 40f;
                long a = Stopwatch.GetTimestamp();
                TerrainGhostGrid.FillHeights(field, cx, cz, v);
                long b = Stopwatch.GetTimestamp();
                TerrainGhostGrid.FillColours(cx, cz, px, 1500f, pz, .35f, Ink, Caution, field.SeaLevel, v);
                long c = Stopwatch.GetTimestamp();
                if (n < 0) continue;
                heights += (b - a) * 1000.0 / Stopwatch.Frequency;
                colours += (c - b) * 1000.0 / Stopwatch.Frequency;
            }
            heights /= runs;
            colours /= runs;
            Console.WriteLine("TerrainGhostGrid fill: heights {0:0.000} ms (cell change), colours {1:0.000} ms (twice a second), both {2:0.000} ms", heights, colours, heights + colours);
            TestAssert.That(heights + colours < 1.0, "Terrain ghost: a full fill must stay well under 1 ms (took " + (heights + colours) + " ms)");
        }

        // ------------------------------------------------------------------ fixtures

        private static bool[] AllTrue(int count)
        {
            var all = new bool[count];
            for (int i = 0; i < count; i++) all[i] = true;
            return all;
        }

        /// <summary>A square sheet centred on the origin; <paramref name="dry"/> defaults to dry everywhere.</summary>
        private static TerrainHeightField Field(int side, float metres, Func<int, int, float> height, Func<int, int, bool> dry)
        {
            var heights = new float[side * side];
            var mask = new bool[heights.Length];
            for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
            {
                heights[z * side + x] = height(x, z);
                mask[z * side + x] = dry == null || dry(x, z);
            }
            return new TerrainHeightField(heights, mask, side, metres, 0f);
        }

        private static GhostVertex[] Filled(TerrainHeightField field, int cellX, int cellZ, float planeX, float planeY, float planeZ, float strength)
        {
            var v = new GhostVertex[TerrainGhostGrid.VertexCount];
            TerrainGhostGrid.FillHeights(field, cellX, cellZ, v);
            TerrainGhostGrid.FillColours(cellX, cellZ, planeX, planeY, planeZ, strength, Ink, Caution, field.SeaLevel, v);
            return v;
        }
    }
}
