using System;
using System.Diagnostics;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class HoloMapTests
    {
        private static readonly GhostTint Ink = new GhostTint(127, 223, 154);

        public static void Run()
        {
            TestTapAndHold();
            TestKeyWithMfdUp();
            TestDisabled();
            TestTaps();
            TestProgrammaticAndSim();
            TestCloses();
            TestSync();
            TestCellSizes();
            TestHeights();
            TestIndices();
            TestColours();
            TestWaterLines();
            TestEdgeFade();
            TestLineStrength();
            TestMatrices();
            TestDepthOrder();
            TestProjection();
            TestAircraftPlace();
            TestFit();
            TestExtent();
            TestEdgePoint();
            TestOffer();
            TestWords();
            TestScaleBar();
            TestPresentation();
            TestRanges();
            TestSizes();
            TestWedge();
            TestMiniMarks();
            TestFillTime();
        }

        // ------------------------------------------------------------------ the map key: tap for the MFD map, hold for the holo

        private const float Tap = HoloMapState.TapSeconds;

        /// <summary>The key goes down at <paramref name="at"/> with the corner map showing: the game's Maximize call, as the key path reaches it, and the first frame's tick.</summary>
        private static void Press(HoloMapState state, float at, bool available = true)
        {
            TestAssert.That(state.KeyDown(available, at) == available, "Holo state: the key's Maximize is swallowed exactly when the holo is available");
            TestAssert.Eq(state.Tick(at, true), HoloStep.None, "Holo state: nothing happens on the frame the key goes down");
        }

        private static void TestTapAndHold()
        {
            var state = new HoloMapState();
            TestAssert.Eq(state.Stage, HoloStage.Mini, "Holo state: starts at the corner map");
            TestAssert.That(!state.KeyDriven, "Holo state: ... with nothing waiting on the key");

            // A tap: nothing opens on key down, the MFD opens on release.
            Press(state, 10f);
            TestAssert.Eq(state.Stage, HoloStage.Pending, "Holo state: key down holds the Maximize back");
            TestAssert.That(!state.Holo && state.KeyDriven, "Holo state: ... without a holo, and the key decides what is next");
            TestAssert.Eq(state.Tick(10.1f, true), HoloStep.None, "Holo state: still down within the tap time: nothing yet");
            TestAssert.Eq(state.Tick(10.15f, false), HoloStep.OpenMfd, "Holo state: let go within 0.2 s is a tap: open the MFD map");
            TestAssert.Eq(state.Stage, HoloStage.Mini, "Holo state: ... which waits at the corner map for the Maximize it calls");
            state.Maximized();
            TestAssert.Eq(state.Stage, HoloStage.Mfd, "Holo state: the Maximize postfix agrees");
            TestAssert.Eq(state.Tick(10.2f, false), HoloStep.None, "Holo state: and the tap is spent");

            // A hold: the holo appears at 0.2 s, stays as long as the key does, and no MFD opens.
            state = new HoloMapState();
            Press(state, 20f);
            TestAssert.Eq(state.Tick(20f + Tap - .01f, true), HoloStep.None, "Holo state: just short of 0.2 s the holo is not up");
            TestAssert.Eq(state.Tick(20f + Tap, true), HoloStep.ShowHolo, "Holo state: held for 0.2 s the holo is shown");
            TestAssert.That(state.Holo && state.Stage == HoloStage.Holo && state.KeyDriven, "Holo state: ... and it is up");
            for (float at = 20.3f; at < 22f; at += .1f)
                TestAssert.Eq(state.Tick(at, true), HoloStep.None, "Holo state: the holo stays while the key is held");
            TestAssert.That(state.Holo, "Holo state: ... still up after two seconds");
            TestAssert.Eq(state.Tick(22.1f, false), HoloStep.HideHolo, "Holo state: letting go hides the holo");
            TestAssert.That(!state.Holo && state.Stage == HoloStage.Mini, "Holo state: ... back at the corner map");
            for (float at = 22.2f; at < 23f; at += .1f)
                TestAssert.Eq(state.Tick(at, false), HoloStep.None, "Holo state: ... and no MFD map opens after a hold");
            TestAssert.That(!state.KeyDriven, "Holo state: ... and nothing waits on the key");

            // A hold, then a tap: the hold left nothing behind.
            Press(state, 30f);
            TestAssert.Eq(state.Tick(30.05f, false), HoloStep.OpenMfd, "Holo state: hold, release, then a tap opens the MFD map");
        }

        private static void TestKeyWithMfdUp()
        {
            // With the maximised map up the game's key handler calls Minimize, which is never held back: the machine only hears the postfix.
            var state = new HoloMapState();
            state.Maximized();
            TestAssert.That(!state.KeyDown(true, 5f), "Holo state: a key Maximize while the MFD is up is not taken");
            TestAssert.Eq(state.Stage, HoloStage.Mfd, "Holo state: ... and starts nothing");
            state.Minimized();
            TestAssert.Eq(state.Stage, HoloStage.Mini, "Holo state: the key minimises the MFD map at once, to the corner map");
            for (float at = 5f; at < 7f; at += .1f)
                TestAssert.Eq(state.Tick(at, true), HoloStep.None, "Holo state: the key still held after Minimize starts no hold");
            TestAssert.That(!state.Holo && !state.KeyDriven, "Holo state: ... no holo, nothing waiting");
            TestAssert.Eq(state.Tick(7.1f, false), HoloStep.None, "Holo state: ... and its release opens nothing");
        }

        private static void TestDisabled()
        {
            // The holo off, or no material, no baked terrain, no aircraft, a map the game refuses: the original Maximize runs on key down, no waiting.
            var state = new HoloMapState();
            TestAssert.That(!state.KeyDown(false, 1f), "Holo state: with the holo unavailable the key is not taken");
            TestAssert.Eq(state.Stage, HoloStage.Mini, "Holo state: ... the stage waits for the Maximize postfix");
            TestAssert.Eq(state.Tick(1f, true), HoloStep.None, "Holo state: ... and no hold starts");
            for (float at = 1.1f; at < 2f; at += .1f)
                TestAssert.Eq(state.Tick(at, true), HoloStep.None, "Holo state: ... however long the key is held");
            state.Maximized();
            TestAssert.Eq(state.Stage, HoloStage.Mfd, "Holo state: the MFD map opens at once, as without the holo");
            TestAssert.Eq(state.Tick(2f, false), HoloStep.None, "Holo state: ... and its release opens nothing more");
        }

        private static void TestTaps()
        {
            // Two quick taps: the first opens the MFD map, the second is the key's Minimize.
            var state = new HoloMapState();
            Press(state, 0f);
            TestAssert.Eq(state.Tick(.08f, false), HoloStep.OpenMfd, "Holo state: the first tap opens the MFD map");
            state.Maximized();
            state.Minimized();
            Press(state, .25f);
            TestAssert.Eq(state.Tick(.33f, false), HoloStep.OpenMfd, "Holo state: after the second press minimised it, a third is a tap again");

            // A Maximize the game refuses (an aircraft is being chosen): the tap is spent, the next one works.
            state = new HoloMapState();
            Press(state, 0f);
            TestAssert.Eq(state.Tick(.05f, false), HoloStep.OpenMfd, "Holo state: a tap asks for the MFD map");
            state.Sync(false);
            TestAssert.Eq(state.Stage, HoloStage.Mini, "Holo state: ... a refused Maximize leaves the corner map");
            Press(state, .2f);
            TestAssert.Eq(state.Tick(.3f, false), HoloStep.OpenMfd, "Holo state: ... and the next tap asks again");

            // A frame that took longer than the tap time while the key was let go: the hold was never seen and opens nothing.
            state = new HoloMapState();
            Press(state, 0f);
            TestAssert.Eq(state.Tick(.5f, false), HoloStep.None, "Holo state: a release seen after the tap time is not a tap");
            TestAssert.Eq(state.Stage, HoloStage.Mini, "Holo state: ... and nothing is left waiting");
            // ... while a hold seen late still shows the holo.
            Press(state, 1f);
            TestAssert.Eq(state.Tick(1.5f, true), HoloStep.ShowHolo, "Holo state: a hold first seen after a long frame shows the holo");
        }

        private static void TestProgrammaticAndSim()
        {
            // Another caller's Maximize (a menu, a panel, automation) never goes through KeyDown: it opens the MFD at once.
            var state = new HoloMapState();
            state.Maximized();
            TestAssert.Eq(state.Stage, HoloStage.Mfd, "Holo state: a programmatic Maximize from MINI opens the maximised map, not the holo");

            state = new HoloMapState();
            Press(state, 0f);
            state.Maximized();
            TestAssert.Eq(state.Stage, HoloStage.Mfd, "Holo state: a programmatic Maximize with a tap waiting takes the MFD map");
            TestAssert.Eq(state.Tick(.05f, false), HoloStep.None, "Holo state: ... and the tap is dropped, not opened twice");

            state = new HoloMapState();
            Press(state, 0f);
            state.Tick(Tap, true);
            state.Maximized();
            TestAssert.That(!state.Holo, "Holo state: a programmatic Maximize with the holo up closes it for the maximised map");
            TestAssert.Eq(state.Tick(1f, true), HoloStep.None, "Holo state: ... and the key still held shows nothing");

            // The sim hook opens the holo without the key and it stays until it is closed.
            state = new HoloMapState();
            TestAssert.That(state.Open(true) && state.Holo && !state.KeyDriven, "Holo state: the sim hook opens the holo from MINI, and the key does not own it");
            TestAssert.Eq(state.Tick(5f, false), HoloStep.None, "Holo state: ... no key held does not close it");
            TestAssert.That(state.Open(true) && state.Holo, "Holo state: ... opening again keeps it up");
            state.Close();
            TestAssert.That(!state.Holo, "Holo state: closing it by hand closes it");
            state.Open(true);
            TestAssert.That(!state.KeyDown(true, 6f) && state.Stage == HoloStage.Mfd, "Holo state: the key with the sim hook's holo up is the way on to the MFD map");
            TestAssert.That(!state.Open(true), "Holo state: the sim hook does not open the holo over the maximised map");
            state.Minimized();
            TestAssert.That(!state.Open(false), "Holo state: ... or when it is unavailable");
            Press(state, 7f);
            TestAssert.That(!state.Open(true), "Holo state: ... or while a tap waits");
        }

        private static void TestCloses()
        {
            // Esc, a pause, death, eject, a lost aircraft, the setting off: Close. A waiting tap is dropped, a holo goes.
            var state = new HoloMapState();
            Press(state, 0f);
            state.Close();
            TestAssert.Eq(state.Stage, HoloStage.Mini, "Holo state: a close drops a waiting tap");
            TestAssert.Eq(state.Tick(.1f, false), HoloStep.None, "Holo state: ... a release in the pause opens no MFD map");
            TestAssert.Eq(state.Tick(.15f, true), HoloStep.None, "Holo state: ... and a key still held after the pause shows no holo");

            Press(state, 1f);
            state.Tick(1f + Tap, true);
            TestAssert.That(state.Holo, "Holo state: a held key shows the holo");
            state.Close();
            TestAssert.That(!state.Holo && state.Stage == HoloStage.Mini, "Holo state: a close takes the holo down");
            TestAssert.Eq(state.Tick(2f, true), HoloStep.None, "Holo state: ... and the key still held does not bring it back");
            TestAssert.Eq(state.Tick(2.1f, false), HoloStep.None, "Holo state: ... or open the MFD map on release");
            Press(state, 3f);
            TestAssert.Eq(state.Tick(3.05f, false), HoloStep.OpenMfd, "Holo state: the key works again after the pause");

            // The game minimising its map (a pause menu does) closes the holo and drops a tap.
            state = new HoloMapState();
            Press(state, 0f);
            state.Minimized();
            TestAssert.Eq(state.Tick(.1f, false), HoloStep.None, "Holo state: the game minimising its map drops a waiting tap");
            Press(state, 1f);
            state.Tick(1f + Tap, true);
            state.Minimized();
            TestAssert.That(!state.Holo, "Holo state: ... and closes the holo");

            state.Close();
            TestAssert.Eq(state.Stage, HoloStage.Mini, "Holo state: closing at MINI changes nothing");
            state.Maximized();
            state.Close();
            TestAssert.Eq(state.Stage, HoloStage.Mfd, "Holo state: the maximised map is not the holo's to close");

            // A scene change starts at MINI, even with a tap waiting or the holo up.
            state.Reset();
            TestAssert.Eq(state.Stage, HoloStage.Mini, "Holo state: a scene change starts at MINI");
            Press(state, 0f);
            state.Reset();
            TestAssert.Eq(state.Tick(.05f, false), HoloStep.None, "Holo state: ... with a tap waiting it is dropped");
            Press(state, 1f);
            state.Tick(1f + Tap, true);
            state.Reset();
            TestAssert.That(!state.Holo, "Holo state: ... and with the holo up it is gone");
        }

        private static void TestSync()
        {
            var state = new HoloMapState();
            Press(state, 0f);
            state.Sync(true);
            TestAssert.Eq(state.Stage, HoloStage.Mfd, "Holo state: a map that is maximised is MFD, and no tap waits with it");
            TestAssert.Eq(state.Tick(.05f, false), HoloStep.None, "Holo state: ... so a release opens nothing");

            state.Sync(false);
            TestAssert.Eq(state.Stage, HoloStage.Mini, "Holo state: MFD with the map not maximised (a Maximize the game refused) is MINI");

            Press(state, 1f);
            state.Sync(false);
            TestAssert.Eq(state.Stage, HoloStage.Pending, "Holo state: a tap waits while the map stays minimised");
            state.Tick(1f + Tap, true);
            state.Sync(false);
            TestAssert.That(state.Holo, "Holo state: the holo stays up while the map stays minimised");
        }

        // ------------------------------------------------------------------ the lattice

        private static void TestCellSizes()
        {
            TestAssert.Near(HoloGrid.CellMetres(20, 64), 625f, "Holo grid: 64 cells across 40 km are 625 m");
            TestAssert.Near(HoloGrid.CellMetres(10, 64), 312.5f, "Holo grid: ... 20 km are 312.5 m");
            TestAssert.Near(HoloGrid.CellMetres(40, 96), 833.3333f, "Holo grid: 96 cells across 80 km are 833 m (the denser grid of the BIG picture)");
            TestAssert.Near(HoloGrid.CellMetres(80, 96) / HoloGrid.CellMetres(40, 96), 2f, "Holo grid: a cell grows with the range, the cell count across stays the same");
            TestAssert.Near(HoloGrid.Radius(40), 40000f, "Holo grid: the radius is the range");

            HoloGrid.Snap(0f, 0f, 625f, out int x, out int z);
            TestAssert.That(x == 0 && z == 0, "Holo grid: the origin is the centre of cell 0");
            HoloGrid.Snap(311f, -311f, 625f, out x, out z);
            TestAssert.That(x == 0 && z == 0, "Holo grid: anywhere inside a cell keeps its centre (no refill)");
            HoloGrid.Snap(314f, -314f, 625f, out x, out z);
            TestAssert.That(x == 1 && z == -1, "Holo grid: past half a cell the next cell takes over");
            HoloGrid.Snap(-5000f, 12345f, 1250f, out x, out z);
            TestAssert.That(x == -4 && z == 10, "Holo grid: cells count from the global origin in the picture's own size (got " + x + ", " + z + ")");
            TestAssert.Near(HoloGrid.Anchor(3, 625f), 1875f, "Holo grid: a cell's centre is a whole number of cells from the origin");

            TestAssert.Eq(HoloGrid.Nodes(1, 1), 9, "Holo grid: a lattice one cell either side of the centre has 3 x 3 nodes");
            TestAssert.Eq(HoloGrid.Nodes(2, 3), 5 * 7, "Holo grid: ... and a rectangle has the product");
            TestAssert.That(HoloGrid.MaxNodes <= 65535, "Holo grid: the biggest lattice still fits 16-bit indices (" + HoloGrid.MaxNodes + " nodes)");
        }

        private static void TestHeights()
        {
            // 513 x 513 samples, 160 m apart, a ramp east of the middle, dry from sample 30 on.
            TerrainHeightField field = Field(201, 320f, (ix, iz) => 50f + ix + (iz % 7) * 10f, (ix, iz) => ix > 30);
            const int halfX = 5, halfZ = 3, sx = 2 * halfX + 1, sz = 2 * halfZ + 1;
            var v = new GhostVertex[HoloGrid.Nodes(halfX, halfZ)];
            float highest = HoloGrid.FillHeights(field, 625f, 2, -1, halfX, halfZ, float.MaxValue, v);
            float expected = field.SeaLevel;
            for (int j = 0; j < sz; j++)
            for (int i = 0; i < sx; i++)
            {
                GhostVertex a = v[j * sx + i];
                TestAssert.Near(a.X, (i - halfX) * 625f, "Holo grid: nodes are one cell apart along x, from the snapped centre");
                TestAssert.Near(a.Z, (j - halfZ) * 625f, "Holo grid: ... and along z");
                float gx = HoloGrid.Anchor(2, 625f) + a.X, gz = HoloGrid.Anchor(-1, 625f) + a.Z;
                TestAssert.Near(a.Y, field.IsLand(gx, gz) ? field.Sample(gx, gz) : field.SeaLevel, "Holo grid: a node reads the ground under it");
                if (a.Y > expected) expected = a.Y;
            }
            TestAssert.Near(highest, expected, "Holo grid: the highest elevation placed is reported");

            // Only the nodes within the reach count toward the highest ground in view.
            float within = HoloGrid.FillHeights(field, 625f, 2, -1, halfX, halfZ, 2000f, v);
            float seen = field.SeaLevel;
            foreach (GhostVertex a in v) if (a.X * a.X + a.Z * a.Z <= 2000f * 2000f && a.Y > seen) seen = a.Y;
            TestAssert.Near(within, seen, "Holo grid: the highest ground counts only what is within the reach");
            TestAssert.That(within <= highest, "Holo grid: ... so it is never above the highest of the whole lattice");
        }

        private static void TestIndices()
        {
            const int halfX = 1, halfZ = 2, sx = 3, sz = 5;
            var eastWest = new int[HoloGrid.MaxLineIndices];
            var northSouth = new int[HoloGrid.MaxLineIndices];
            var triangles = new int[HoloGrid.MaxTriangleIndices];
            HoloGrid.BuildIndices(halfX, halfZ, eastWest, northSouth, triangles, out int ew, out int ns, out int tri);
            TestAssert.Eq(ew, sz * (sx - 1) * 2, "Holo grid: the east-west lines are a segment between every two neighbours along a row");
            TestAssert.Eq(ns, sx * (sz - 1) * 2, "Holo grid: the north-south lines are a segment between every two neighbours along a column");
            TestAssert.Eq(tri, (sx - 1) * (sz - 1) * 6, "Holo grid: every cell is two triangles");
            for (int n = 0; n < ew; n += 2)
                TestAssert.That(eastWest[n] / sx == eastWest[n + 1] / sx && eastWest[n + 1] == eastWest[n] + 1, "Holo grid: an east-west segment joins two nodes of one row");
            for (int n = 0; n < ns; n += 2)
                TestAssert.That(northSouth[n + 1] == northSouth[n] + sx, "Holo grid: a north-south segment joins a node to the one north of it");
            var covered = new int[(sx - 1) * (sz - 1)];
            for (int n = 0; n < tri; n += 3)
            {
                int a = triangles[n], b = triangles[n + 1], c = triangles[n + 2];
                int column = Math.Min(a % sx, Math.Min(b % sx, c % sx)), row = Math.Min(a / sx, Math.Min(b / sx, c / sx));
                covered[row * (sx - 1) + column]++;
                TestAssert.That(a != b && b != c && a != c && Math.Max(a, Math.Max(b, c)) < HoloGrid.Nodes(halfX, halfZ), "Holo grid: a triangle has three nodes of the lattice");
            }
            foreach (int count in covered) TestAssert.Eq(count, 2, "Holo grid: each cell holds exactly two triangles");

            HoloGrid.BuildIndices(HoloGrid.MaxHalf, HoloGrid.MaxHalf, eastWest, northSouth, triangles, out ew, out ns, out tri);
            TestAssert.That(ew == HoloGrid.MaxLineIndices && ns == HoloGrid.MaxLineIndices && tri == HoloGrid.MaxTriangleIndices, "Holo grid: the biggest lattice fills its arrays exactly");
        }

        private static void TestColours()
        {
            const int halfX = 4, halfZ = 4, sx = 2 * halfX + 1;
            // A sea: every node the same faint ink, and no node is dark: the picture has no edge of its own.
            TerrainHeightField sea = Field(201, 320f, (ix, iz) => -200f, (ix, iz) => false);
            var v = new GhostVertex[HoloGrid.Nodes(halfX, halfZ)];
            float highest = HoloGrid.FillHeights(sea, 625f, 0, 0, halfX, halfZ, float.MaxValue, v);
            HoloGrid.FillColours(sea.SeaLevel, highest, Ink, v.Length, v);
            byte faint = (byte)(Ink.G * HoloGrid.SeaStrength + .5f);
            foreach (GhostVertex a in v)
            {
                TestAssert.Eq(a.A, (byte)255, "Holo colours: the light is premultiplied, so alpha is always full");
                TestAssert.Eq(a.G, faint, "Holo colours: every node of the sea is the same faint ink, to the very edge");
            }

            // Flat land 100 m up: dimmer than the ink (the span floor), not as bright as a peak.
            TerrainHeightField plain = Field(201, 320f, (ix, iz) => 100f, null);
            highest = HoloGrid.FillHeights(plain, 625f, 0, 0, halfX, halfZ, float.MaxValue, v);
            TestAssert.Near(highest, 100f, "Holo colours: the plain is its own highest ground");
            HoloGrid.FillColours(0f, highest, Ink, v.Length, v);
            GhostVertex near = v[halfZ * sx + halfX + 2];
            TestAssert.That(near.G < Ink.G && near.G > faint, "Holo colours: low land is brighter than the sea and dimmer than the ink (got " + near.G + ")");

            // A ramp east: the highest ground in view is the bright tone, and light rises with height.
            TerrainHeightField ramp = Field(201, 320f, (ix, iz) => (ix - 60) * 20f, (ix, iz) => ix > 60);
            highest = HoloGrid.FillHeights(ramp, 625f, 0, 0, halfX, halfZ, float.MaxValue, v);
            HoloGrid.FillColours(0f, highest, Ink, v.Length, v);
            byte brightG = (byte)(Ink.G + (255f - Ink.G) * HoloGrid.BrightLift + .5f);
            int previous = -1;
            bool rising = true;
            for (int i = halfX + 1; i < sx; i++)
            {
                GhostVertex a = v[halfZ * sx + i];
                int light = a.R + a.G + a.B;
                if (light < previous) rising = false;
                previous = light;
            }
            TestAssert.That(rising, "Holo colours: going up the ramp the light never falls");
            int brightest = 0;
            for (int n = 0; n < v.Length; n++)
                if (v[n].Y >= highest - .5f) brightest = Math.Max(brightest, v[n].G);
            TestAssert.That(brightest == 0 || brightest == brightG, "Holo colours: the highest ground in view is exactly the bright tone (got " + brightest + ", want " + brightG + ")");

            // The fill reports how many vertices carry light, and colours only the vertices it is told of.
            int lit = HoloGrid.FillColours(0f, highest, Ink, v.Length, v);
            int counted = 0;
            foreach (GhostVertex a in v) if (a.R + a.G + a.B > 0) counted++;
            TestAssert.Eq(lit, counted, "Holo colours: the fill counts the vertices that carry light");
            v[v.Length - 1].R = 7;
            HoloGrid.FillColours(0f, highest, Ink, v.Length - 1, v);
            TestAssert.Eq(v[v.Length - 1].R, (byte)7, "Holo colours: a vertex past the count is left alone");
        }

        private static GhostVertex[] Coast(int halfX, int halfZ, int firstLandColumn)
        {
            // A synthetic lattice: sea (at the sea's level) west of firstLandColumn, land 100 m up from it eastward.
            int sx = 2 * halfX + 1, sz = 2 * halfZ + 1;
            var v = new GhostVertex[sx * sz];
            for (int j = 0; j < sz; j++)
            for (int i = 0; i < sx; i++) v[j * sx + i] = new GhostVertex { X = (i - halfX) * 625f, Y = i >= firstLandColumn ? 100f : 0f, Z = (j - halfZ) * 625f };
            return v;
        }

        private static void TestWaterLines()
        {
            TestAssert.Eq(HoloGrid.WaterEvery, 8, "Holo water: every eighth line");
            TestAssert.Near(HoloGrid.WaterStrength, .35f, "Holo water: ... at 35 % of a land line");
            TestAssert.Near(HoloGrid.SeaStrength, HoloGrid.LandFloor, "Holo water: a sea node carries the land floor, so a land line that ends in one is as strong at the coast as inland");
            foreach (int line in new[] { 0, 8, 16, 800, -8, -16, -800 }) TestAssert.That(HoloGrid.WaterLineKept(line), "Holo water: line " + line + " is kept");
            foreach (int line in new[] { 1, 2, 7, 9, 15, 17, -1, -7, -9 }) TestAssert.That(!HoloGrid.WaterLineKept(line), "Holo water: line " + line + " is not");
            int kept = 0;
            for (int line = -40; line < 40; line++) if (HoloGrid.WaterLineKept(line)) kept++;
            TestAssert.Eq(kept, 10, "Holo water: one line in eight over any run of them");
            TestAssert.Near(HoloPresentation.LineGain(HoloShape.Mini), 1.15f, "Holo lines: the MINI's lines are 15 % stronger");
            TestAssert.Near(HoloPresentation.LineGain(HoloShape.Big), 1f, "Holo lines: ... the BIG map's are as they are");

            // A coast in a lattice of 9 x 9 nodes: sea in columns 0..5, land (100 m) from column 6. The shore cuts every row's segment between columns 5 and 6 (in the middle: no land mask here).
            const int half = 4, sx = 9, sz = 9, lattice = sx * sz;
            var buffers = new[] { new int[HoloGrid.MaxLineIndices], new int[HoloGrid.MaxLineIndices], new int[HoloGrid.MaxLineIndices], new int[HoloGrid.MaxLineIndices] };
            var cells = new int[HoloGrid.MaxLineIndices];
            GhostVertex[] v = Room(Coast(half, half, 6));
            HoloGrid.BuildLines(null, 625f, half, half, 0, 0, 0f, v, cells, buffers[0], buffers[1], buffers[2], buffers[3], out int landEw, out int landNs, out int water, out int coast, out int count);
            // Every land segment is there, whatever its row or column (none is thinned): per row the two between land nodes and the one cut at the shore; 3 columns of 8 north-south.
            TestAssert.Eq(landEw, sz * 3 * 2, "Holo water: every east-west segment on land is a land line, in every row, the one the shore cuts with it");
            TestAssert.Eq(landNs, 3 * (sz - 1) * 2, "Holo water: ... and every north-south one between land nodes");
            for (int n = 0; n < landEw; n += 2) TestAssert.That(buffers[0][n] % sx >= 6 && buffers[0][n] < lattice, "Holo water: an east-west land segment starts on a land node");
            for (int n = 0; n < landNs; n += 2) TestAssert.That(buffers[1][n] % sx >= 6, "Holo water: a north-south land segment lies inland");
            // The shore: one vertex per row, half way between the last sea node and the first land node and on the surface between them; the land line ends in it, and the shoreline joins them row to row.
            TestAssert.Eq(count, lattice + sz, "Holo coast: one vertex is added where the shore cuts each row");
            TestAssert.Eq(coast, (sz - 1) * 2, "Holo coast: the shoreline is one segment per cell it passes through");
            for (int n = lattice; n < count; n++)
            {
                TestAssert.Near(v[n].X, 1.5f * 625f, "Holo coast: the cut is half way between the sea node and the land node");
                TestAssert.Near(v[n].Y, 50f, "Holo coast: ... on the surface between them");
            }
            int cut = 0;
            for (int n = 0; n < landEw; n += 2) if (buffers[0][n + 1] >= lattice) cut++;
            TestAssert.Eq(cut, sz, "Holo coast: each row's last land line ends at the shore, not at the next node");
            for (int n = 0; n < coast; n += 2)
                TestAssert.That(buffers[3][n] >= lattice && buffers[3][n + 1] >= lattice && Math.Abs(v[buffers[3][n]].Z - v[buffers[3][n + 1]].Z) == 625f, "Holo coast: a shoreline segment joins the cuts of two neighbouring rows");
            // Water: the east-west line of row 4 (absolute line 0) over columns 0..5, and the north-south one of column 4 (absolute 0), the only kept lines of a lattice centred on cell 0.
            TestAssert.Eq(water, 5 * 2 + (sz - 1) * 2, "Holo water: only the kept lines remain over open water (1 of 8 each way)");
            for (int n = 0; n < water; n += 2)
            {
                int a = buffers[2][n], b = buffers[2][n + 1];
                bool eastWest = b == a + 1;
                TestAssert.That(eastWest ? a / sx == half : a % sx == half && b == a + sx, "Holo water: a water segment lies on a kept line");
                TestAssert.That((a % sx) <= 5 && (b % sx) <= 5, "Holo water: ... in the sea");
            }
            // The kept lines stay where they are in the world as the lattice moves: shifted a cell east, the kept column is one node west of before.
            HoloGrid.BuildLines(null, 625f, half, half, 1, 0, 0f, v, cells, buffers[0], buffers[1], buffers[2], buffers[3], out landEw, out landNs, out water, out coast, out count);
            bool columnKept = false;
            for (int n = 0; n < water; n += 2)
            {
                int a = buffers[2][n], b = buffers[2][n + 1];
                if (b == a + sx)
                {
                    TestAssert.Eq(a % sx, half - 1, "Holo water: the same absolute column is kept after the lattice moves a cell");
                    columnKept = true;
                }
            }
            TestAssert.That(columnKept, "Holo water: the kept column is still in the lattice");
            HoloGrid.BuildLines(null, 625f, half, half, -3, 5, 0f, v, cells, buffers[0], buffers[1], buffers[2], buffers[3], out landEw, out landNs, out water, out coast, out count);
            for (int n = 0; n < water; n += 2)
            {
                int a = buffers[2][n], b = buffers[2][n + 1];
                int line = b == a + 1 ? 5 + a / sx - half : -3 + a % sx - half;
                TestAssert.That(HoloGrid.WaterLineKept(line), "Holo water: a kept segment's absolute line number is a multiple of 8 (got " + line + ")");
            }
            // Land counts do not depend on where the lattice is.
            TestAssert.That(landEw == sz * 3 * 2 && landNs == 3 * (sz - 1) * 2 && coast == (sz - 1) * 2, "Holo water: the land lines and the shore are the same wherever the lattice stands");

            // All sea: nothing is a land line, and the water lines are all there is.
            var open = new GhostVertex[sx * sz];
            HoloGrid.BuildLines(null, 625f, half, half, 0, 0, 0f, open, cells, buffers[0], buffers[1], buffers[2], buffers[3], out landEw, out landNs, out water, out coast, out count);
            TestAssert.That(landEw == 0 && landNs == 0 && coast == 0 && count == lattice, "Holo water: open water has no land lines and no shore");
            TestAssert.Eq(water, 2 * (sx - 1) * 2, "Holo water: ... one row and one column of the eight shown here, each the whole width");
            // All land: nothing is thinned.
            HoloGrid.BuildLines(null, 625f, half, half, 0, 0, 0f, Coast(half, half, 0), cells, buffers[0], buffers[1], buffers[2], buffers[3], out landEw, out landNs, out water, out coast, out count);
            TestAssert.That(landEw == sz * (sx - 1) * 2 && landNs == sx * (sz - 1) * 2 && water == 0 && coast == 0, "Holo water: dry land keeps every line and has no shore");
            // A single land node: its four segments are cut, and the shore is a diamond round it.
            GhostVertex[] island = Room(Coast(half, half, sx));
            island[half * sx + half].Y = 50f;
            HoloGrid.BuildLines(null, 625f, half, half, 7, 7, 0f, island, cells, buffers[0], buffers[1], buffers[2], buffers[3], out landEw, out landNs, out water, out coast, out count);
            TestAssert.That(landEw == 2 * 2 && landNs == 2 * 2 && count == lattice + 4 && coast == 4 * 2, "Holo coast: one dry node is four half segments inside a diamond of shore (" + landEw + ", " + landNs + ", " + coast + ")");
            // Without room for the added vertices the shore is left uncut and nothing is written past the array.
            HoloGrid.BuildLines(null, 625f, half, half, 0, 0, 0f, Coast(half, half, 6), cells, buffers[0], buffers[1], buffers[2], buffers[3], out landEw, out landNs, out water, out coast, out count);
            TestAssert.That(count == lattice && coast == 0 && landEw == sz * 2 * 2, "Holo coast: a full vertex array leaves the shore uncut");

            // The relief's light and the surface's alpha: the sea is alpha 0 with its colour kept, level land keeps its colour, a slope facing the north-west sun is brighter than one facing away, the shore is the ink at its strength.
            GhostVertex[] ridge = Room(Coast(half, half, 2));
            for (int j = 0; j < sz; j++)
            for (int i = 2; i < sx; i++) ridge[j * sx + i].Y = 100f + 400f * (4 - Math.Abs(i - 5));
            HoloGrid.BuildLines(null, 625f, half, half, 0, 0, 0f, ridge, cells, buffers[0], buffers[1], buffers[2], buffers[3], out _, out _, out _, out _, out count);
            HoloGrid.FillColours(0f, 1700f, Ink, lattice, ridge);
            GhostVertex seaBefore = ridge[half * sx], westBefore = ridge[half * sx + 4], eastBefore = ridge[half * sx + 6];
            HoloGrid.Shade(half, half, 625f, 0f, Ink, count, ridge);
            GhostVertex seaAfter = ridge[half * sx], westAfter = ridge[half * sx + 4], eastAfter = ridge[half * sx + 6];
            TestAssert.That(seaAfter.A == 0 && seaAfter.G == seaBefore.G, "Holo shade: a sea node keeps its colour for the water lines and gets alpha 0 for the surface");
            TestAssert.That(westAfter.A == 255 && westAfter.G > westBefore.G && eastAfter.G < eastBefore.G, "Holo shade: the slope facing west is lit, the one facing east is in shade (" + westAfter.G + " vs " + eastAfter.G + ")");
            TestAssert.That(eastAfter.G >= (byte)(eastBefore.G * HoloGrid.ShadeFloor), "Holo shade: ... never below the floor");
            TestAssert.Eq(ridge[count - 1].G, (byte)(Ink.G * HoloGrid.CoastStrength + .5f), "Holo shade: the shore's vertices are the ink at the coast's strength");
            GhostVertex[] flat = Coast(half, half, 0);
            HoloGrid.FillColours(0f, 100f, Ink, lattice, flat);
            byte level = flat[half * sx + half].G;
            HoloGrid.Shade(half, half, 625f, 0f, Ink, lattice, flat);
            TestAssert.Eq(flat[half * sx + half].G, level, "Holo shade: level ground is left as it was");
        }

        /// <summary>The lattice with room after it for the vertices the shore adds.</summary>
        private static GhostVertex[] Room(GhostVertex[] lattice)
        {
            var roomy = new GhostVertex[lattice.Length + 64];
            Array.Copy(lattice, roomy, lattice.Length);
            return roomy;
        }

        private static void TestEdgeFade()
        {
            TestAssert.Near(HoloGrid.EdgeFade(0f), 0f, "Holo fade: nothing at the frame's edge");
            TestAssert.Near(HoloGrid.EdgeFade(HoloGrid.FadeShare), 1f, "Holo fade: full strength 5 % in");
            TestAssert.Near(HoloGrid.EdgeFade(HoloGrid.FadeShare * .5f), .5f, "Holo fade: half way, half strength (a smooth step)");
            TestAssert.Near(HoloGrid.EdgeFade(.5f), 1f, "Holo fade: the middle of the frame is untouched");
            TestAssert.Near(HoloGrid.EdgeFade(-.1f), 0f, "Holo fade: outside the frame is nothing");
            float last = -1f;
            for (float s = 0f; s <= .0501f; s += .0025f)
            {
                float f = HoloGrid.EdgeFade(s);
                TestAssert.That(f >= last, "Holo fade: it rises steadily across the last 5 %");
                last = f;
            }
            TestAssert.Near(HoloGrid.FadeShare, .05f, "Holo fade: the last 5 % of the frame");
        }

        private static void TestLineStrength()
        {
            // Looking north: the east-west lines run across the view, the north-south ones run away.
            TestAssert.Near(HoloGrid.LineStrength(90f, 0f), 1f, "Holo lines: a line across the view is at full strength");
            TestAssert.Near(HoloGrid.LineStrength(0f, 0f), HoloGrid.AwayStrength, "Holo lines: a line running away from the viewer is at 45 %");
            TestAssert.Near(HoloGrid.AwayStrength, .45f, "Holo lines: ... 45 % it is");
            TestAssert.Near(HoloGrid.LineStrength(180f, 0f), HoloGrid.AwayStrength, "Holo lines: a line has no sense, south is the same as north");
            TestAssert.Near(HoloGrid.LineStrength(270f, 0f), 1f, "Holo lines: ... and west the same as east");
            // Turn the table: heading east swaps which lines run across.
            TestAssert.Near(HoloGrid.LineStrength(0f, 90f), 1f, "Holo lines: looking east the north-south lines run across");
            TestAssert.Near(HoloGrid.LineStrength(90f, 90f), HoloGrid.AwayStrength, "Holo lines: ... and the east-west ones run away");
            TestAssert.Near(HoloGrid.LineStrength(90f, 45f), HoloGrid.LineStrength(0f, 45f), "Holo lines: on a diagonal both are alike");
            TestAssert.Near(HoloGrid.LineStrength(0f, 45f), HoloGrid.AwayStrength + (1f - HoloGrid.AwayStrength) * (float)Math.Sin(Math.PI / 4), "Holo lines: ... by the sine of the angle");
            for (float look = 0f; look < 360f; look += 7f)
            {
                float a = HoloGrid.LineStrength(90f, look), b = HoloGrid.LineStrength(0f, look);
                TestAssert.That(a >= HoloGrid.AwayStrength - 1e-4f && a <= 1f + 1e-4f && b >= HoloGrid.AwayStrength - 1e-4f && b <= 1f + 1e-4f, "Holo lines: a line is never fainter than 45 % nor brighter than full");
                // The two directions of the lattice are at right angles: what one gains in strength the other loses.
                TestAssert.Near(a + b, 2f * HoloGrid.AwayStrength + (1f - HoloGrid.AwayStrength) * (float)(Math.Abs(Math.Sin(look * Math.PI / 180)) + Math.Abs(Math.Cos(look * Math.PI / 180))), "Holo lines: the two directions share the angle between them");
            }
        }

        // ------------------------------------------------------------------ the camera

        private static HoloCamera Camera(HoloShape shape, bool northUp, float aspect, int rangeKm, float heading = 0f)
        {
            HoloCamera camera = HoloView.Fit(HoloPresentation.Tilt(shape), aspect, HoloPresentation.AircraftUp(shape, northUp), HoloGrid.Radius(rangeKm));
            camera.Heading = heading;
            return camera;
        }

        private static void TestMatrices()
        {
            var view = new float[HoloView.Elements];
            var proj = new float[HoloView.Elements];
            HoloCamera mini = Camera(HoloShape.Mini, true, 340f / 300f, 20);
            HoloView.View(mini, view);
            TestAssert.Near(view[0], 1f, "Holo view: north up leaves east on the right");
            TestAssert.Near(view[10] + (float)Math.Cos(68.0 * Math.PI / 180.0), 0f, "Holo view: depth grows with north by the tilt's cosine");
            TestAssert.That(Math.Abs(view[11] + mini.Distance) < 1f, "Holo view: north up, aircraft in the middle: the camera is its distance away");
            HoloView.Projection(mini, proj);
            float focal = (float)(1.0 / Math.Tan(12.0 * Math.PI / 180.0));
            TestAssert.Near(proj[5], focal, "Holo view: a 24 degree lens, vertically");
            TestAssert.Near(proj[0], focal / (340f / 300f), "Holo view: ... narrower by the frame's width over its height horizontally");
            TestAssert.Near(proj[14], -1f, "Holo view: a perspective divide by depth");
            TestAssert.That(HoloView.Focal > 4.7f && HoloView.Focal < 4.71f, "Holo view: the focal length of 24 degrees");

            // The matrices and the mark projection must agree for any point, heading, tilt and frame: they are the same picture.
            foreach (HoloShape shape in new[] { HoloShape.Mini, HoloShape.Big })
            foreach (bool northUp in new[] { true, false })
            foreach (float heading in new[] { 0f, 37f, 90f, 215f, 359f })
            foreach (int range in new[] { 10, 40 })
            {
                HoloCamera camera = Camera(shape, northUp, shape == HoloShape.Mini ? 340f / 300f : 1.3f, range, heading);
                HoloView.View(camera, view);
                HoloView.Projection(camera, proj);
                float[,] points = { { 0f, 0f, 0f }, { 6000f, 0f, -3000f }, { -9000f, 1500f, 8000f }, { 2000f, 3000f, 9500f } };
                for (int p = 0; p < points.GetLength(0); p++)
                {
                    float e = points[p, 0], u = points[p, 1], n = points[p, 2];
                    float vx = view[0] * e + view[1] * u + view[2] * n + view[3];
                    float vy = view[4] * e + view[5] * u + view[6] * n + view[7];
                    float vz = view[8] * e + view[9] * u + view[10] * n + view[11];
                    float cx = proj[0] * vx, cy = proj[5] * vy, cw = proj[14] * vz;
                    bool shown = HoloView.Project(camera, e, u, n, out float x, out float y);
                    TestAssert.That(shown && Math.Abs(cx / cw - x) < 1e-4f && Math.Abs(cy / cw - y) < 1e-4f,
                        "Holo view: the render's matrices and the marks' projection put a point in the same place (heading " + heading + ")");
                }
            }
        }

        private static void TestDepthOrder()
        {
            // The lines are drawn a little above the surface so they pass its depth test: a lifted point must be nearer (a smaller GL depth, z over w) than the point under it, at any heading and in both frames.
            var view = new float[HoloView.Elements];
            var proj = new float[HoloView.Elements];
            foreach (HoloShape shape in new[] { HoloShape.Mini, HoloShape.Big })
            foreach (float heading in new[] { 0f, 90f, 215f })
            {
                HoloCamera camera = Camera(shape, false, shape == HoloShape.Mini ? 340f / 300f : 1.3f, 20, heading);
                HoloView.View(camera, view);
                HoloView.Projection(camera, proj);
                float low = Depth(view, proj, 3000f, 400f, 5000f), high = Depth(view, proj, 3000f, 400f + HoloGrid.LineLift * 625f, 5000f);
                TestAssert.That(high < low, "Holo depth: a point lifted above the ground is nearer than the ground under it (smaller depth)");
                TestAssert.That(low > -1f && low < 1f, "Holo depth: the scene lies between the near and far planes");
            }
            TestAssert.Near(HoloGrid.DepthClear(false), 1f, "Holo depth: an ordinary buffer is cleared to 1, the far plane");
            TestAssert.Near(HoloGrid.DepthClear(true), 0f, "Holo depth: a reversed buffer is cleared to 0, the far plane");
        }

        /// <summary>The GL clip depth (z over w, -1 near .. 1 far) of a scene point under the camera's view and projection matrices.</summary>
        private static float Depth(float[] view, float[] proj, float east, float up, float north)
        {
            float vy = view[8] * east + view[9] * up + view[10] * north + view[11];
            float z = proj[10] * vy + proj[11], w = proj[14] * vy;
            return z / w;
        }

        private static void TestProjection()
        {
            float focal = HoloView.Focal;
            HoloCamera cam = Camera(HoloShape.Big, true, 1.3f, 40);
            float sin = (float)Math.Sin(55.0 * Math.PI / 180.0), cos = (float)Math.Cos(55.0 * Math.PI / 180.0);
            TestAssert.That(HoloView.Project(cam, 0f, 0f, 0f, out float x, out float y) && Math.Abs(x) < 1e-6f && Math.Abs(y) < 1e-6f,
                "Holo projection: the aircraft's own spot is the middle of the BIG frame");

            HoloView.Project(cam, 40000f, 0f, 0f, out x, out y);
            TestAssert.Near(x, 1f, "Holo projection: the range east of the aircraft is the frame's right edge");
            TestAssert.Near(y, 0f, "Holo projection: ... on the middle line");
            HoloView.Project(cam, -40000f, 0f, 0f, out x, out _);
            TestAssert.Near(x, -1f, "Holo projection: ... and the range west of it the left edge");

            HoloView.Project(cam, 0f, 0f, 20000f, out x, out y);
            TestAssert.Near(x, 0f, "Holo projection: ground dead ahead is straight above the middle");
            TestAssert.That(y > 0f, "Holo projection: ... above it");
            HoloView.Project(cam, 0f, 0f, 20000f, out _, out float far);
            HoloView.Project(cam, 0f, 0f, -20000f, out _, out float nearY);
            TestAssert.That(far > 0f && nearY < 0f && far < -nearY * 1.5f && -nearY < far * 1.5f, "Holo projection: ground behind is lower on the frame, a little farther apart for being nearer");
            float expectUp = focal * (20000f * sin) / (cam.Distance + 20000f * cos);
            TestAssert.Near(far, expectUp, "Holo projection: 20 km ahead lands where the lens puts it");

            HoloCamera turned = Camera(HoloShape.Big, false, 1.3f, 40, 90f);
            HoloView.Project(turned, 10000f, 0f, 0f, out x, out y);
            TestAssert.Near(x, 0f, "Holo projection: heading east, ground dead ahead (east) is straight above the middle");
            TestAssert.That(y > 0f, "Holo projection: ... above it");
            HoloView.Project(turned, 0f, 0f, 10000f, out x, out y);
            TestAssert.That(x < 0f && Math.Abs(y) < 1e-5f, "Holo projection: heading east, north is on the left");

            HoloView.Project(cam, 0f, 1000f, 0f, out x, out y);
            TestAssert.Near(x, 0f, "Holo projection: a height is straight up");
            TestAssert.Near(y, focal * (2500f * cos) / (cam.Distance - 2500f * sin), "Holo projection: ... stretched 2.5 times, and nearer for being up");

            TestAssert.That(!HoloView.Project(cam, 0f, 0f, -2000000f, out _, out _), "Holo projection: a point behind the camera is not shown");

            TestAssert.Near(HoloView.MarkHeight(1000f, 20000f), 1000f, "Holo marks: a low aircraft stands at its height");
            TestAssert.Near(HoloView.MarkHeight(10000f, 20000f), 3600f, "Holo marks: a high one is capped at 45% of the range on the table (3.6 km real)");
            TestAssert.Near(HoloView.MarkHeight(-50f, 20000f), 0f, "Holo marks: nothing stands below the sea");
        }

        private static void TestAircraftPlace()
        {
            // The aircraft's place in the frame, both sizes, both orientations.
            HoloPresentation.Aircraft(HoloShape.Mini, false, out float across, out float up);
            TestAssert.That(across == .5f && Math.Abs(up - .38f) < 1e-6f, "Tac place: the MINI heading up puts the aircraft in the middle across and 38 % up (got " + across + ", " + up + ")");
            HoloPresentation.Aircraft(HoloShape.Mini, true, out across, out up);
            TestAssert.That(across == .5f && up == .5f, "Tac place: ... the MINI north up in the middle");
            foreach (bool northUp in new[] { false, true })
            {
                HoloPresentation.Aircraft(HoloShape.Big, northUp, out across, out up);
                TestAssert.That(across == .5f && up == .5f, "Tac place: the BIG one has it in the middle, north up or heading up");
            }

            // And the camera really puts it there, at every range, tilt and frame.
            foreach (HoloShape shape in new[] { HoloShape.Mini, HoloShape.Big })
            foreach (bool northUp in new[] { false, true })
            foreach (float aspect in new[] { 340f / 300f, 1.2f, 1.3f, 1.5f })
            foreach (int range in new[] { 10, 20, 40, 80 })
            {
                HoloCamera camera = Camera(shape, northUp, aspect, range, 123f);
                HoloView.Project(camera, 0f, 0f, 0f, out float x, out float y);
                HoloPresentation.Aircraft(shape, northUp, out _, out float wanted);
                TestAssert.That(Math.Abs(x) < 1e-5f && Math.Abs((y * .5f + .5f) - wanted) < 1e-4f,
                    "Tac place: the fitted camera stands the aircraft where it is wanted (" + shape + ", north up " + northUp + ", aspect " + aspect + ": " + (y * .5f + .5f) + " vs " + wanted + ")");
            }
        }

        private static void TestFit()
        {
            // The range is half the frame across at the aircraft's own depth, whatever the tilt and frame.
            foreach (HoloShape shape in new[] { HoloShape.Mini, HoloShape.Big })
            foreach (bool northUp in new[] { false, true })
            foreach (float aspect in new[] { 340f / 300f, 1.3f })
            foreach (int range in new[] { 10, 20, 40, 80 })
            {
                HoloCamera camera = Camera(shape, northUp, aspect, range);
                HoloView.Project(camera, range * 1000f, 0f, 0f, out float x, out _);
                TestAssert.Near(x, 1f, "Holo fit: the range east of the aircraft is the frame's right edge (" + shape + ", " + range + " km)");

                // The four corners of the frame look at ground, and the frame's far corners see wider ground than the near ones (perspective).
                TestAssert.That(HoloView.GroundAt(camera, -1f, -1f, out float nearLeft, out float nearAhead) &
                                HoloView.GroundAt(camera, 1f, -1f, out float nearRight, out _) &
                                HoloView.GroundAt(camera, -1f, 1f, out float farLeft, out float farAhead) &
                                HoloView.GroundAt(camera, 1f, 1f, out float farRight, out _), "Holo fit: all four frame corners see ground");
                TestAssert.That(Math.Abs(nearLeft + nearRight) < 1f && Math.Abs(farLeft + farRight) < 1f, "Holo fit: the corners are mirrored across the aircraft");
                TestAssert.That(nearAhead < 0f && farAhead > 0f && -nearLeft < -farLeft, "Holo fit: the frame sees ground behind the aircraft and ahead of it, wider far off");

                // A corner's ground goes back to the same corner of the frame.
                HoloView.Project(camera, nearLeft, 0f, nearAhead, out float cx, out float cy);
                TestAssert.That(Math.Abs(cx + 1f) < 1e-3f && Math.Abs(cy + 1f) < 1e-3f, "Holo fit: the ground the lower-left corner sees projects back to it");
                HoloView.Project(camera, farRight, 0f, farAhead, out cx, out cy);
                TestAssert.That(Math.Abs(cx - 1f) < 1e-3f && Math.Abs(cy - 1f) < 1e-3f, "Holo fit: ... and the upper-right one too");

                // The scale at a row of the frame is the width of the ground across it.
                HoloView.GroundAt(camera, 1f, -.5f, out float rowRight, out _);
                TestAssert.That(Math.Abs(HoloView.WidthAt(camera, -.5f) - 2f * rowRight) < 2f, "Holo fit: the frame is twice the right edge's reach wide at any row");
            }
            TestAssert.That(!HoloView.GroundAt(Camera(HoloShape.Big, true, 1.3f, 40), 0f, 100f, out _, out _), "Holo fit: a point above the horizon sees no ground");
            HoloCamera flat = Camera(HoloShape.Big, true, 1.3f, 40);
            flat.Tilt = 10f;
            TestAssert.That(!HoloView.GroundAt(flat, 0f, 1f, out _, out _), "Holo fit: ... and neither does a frame whose top looks over the horizon");
            TestAssert.Near(HoloView.WidthAt(flat, 1f), 0f, "Holo fit: ... which has no scale there");
        }

        private static void TestExtent()
        {
            // The lattice made for a picture covers the frame at the smallest and the largest frame the picture is drawn with, at every heading, and the frame's near edge lies below the frame's bottom.
            int biggest = 0;
            foreach (HoloShape shape in new[] { HoloShape.Mini, HoloShape.Big })
            foreach (bool northUp in new[] { false, true })
            foreach (int range in shape == HoloShape.Mini ? new[] { 10, 20, 40 } : new[] { 20, 40, 80 })
            {
                float cell = HoloGrid.CellMetres(range, HoloPresentation.GridCells(shape));
                HoloCamera reference = Camera(shape, northUp, HoloPresentation.ReferenceAspect, range);
                HoloGrid.Extent(reference, cell, northUp, out int halfX, out int halfZ, out float reach);
                string label = shape + (northUp ? " north up " : " heading up ") + range + " km";
                TestAssert.That(halfX >= 2 && halfZ >= 2 && halfX < HoloGrid.MaxHalf && halfZ < HoloGrid.MaxHalf, "Holo tile: the lattice fits its limit (" + label + ": " + halfX + " x " + halfZ + ")");
                biggest = Math.Max(biggest, Math.Max(halfX, halfZ));
                if (range == 20 || range == 40) Console.WriteLine("Holo lattice {0}: {1} x {2} cells of {3:0} m, {4} nodes, reach {5:0.0} km", label, 2 * halfX, 2 * halfZ, cell, HoloGrid.Nodes(halfX, halfZ), reach / 1000f);

                foreach (float aspect in new[] { HoloPresentation.ReferenceAspect, 1.25f, 1.3f })
                foreach (float heading in northUp ? new[] { 0f } : new[] { 0f, 30f, 90f, 137f, 180f, 271f, 333f })
                foreach (float snap in new[] { -.5f, 0f, .5f })
                {
                    HoloCamera camera = Camera(shape, northUp, aspect, range, heading);
                    float s = (float)Math.Sin(heading * Math.PI / 180), c = (float)Math.Cos(heading * Math.PI / 180);
                    for (int corner = 0; corner < 4; corner++)
                    {
                        float x = (corner & 1) == 0 ? -1f : 1f, y = (corner & 2) == 0 ? -1f : 1f;
                        TestAssert.That(HoloView.GroundAt(camera, x, y, out float across, out float ahead), "Holo tile: the corner sees ground (" + label + ")");
                        // The table is turned by the heading: the ground east and north of the aircraft that the corner sees.
                        float east = across * c + ahead * s, north = -across * s + ahead * c;
                        // The lattice is centred within half a cell of the aircraft on either axis.
                        TestAssert.That(Math.Abs(east - snap * cell) < (halfX - 0.5f) * cell && Math.Abs(north - snap * cell) < (halfZ - 0.5f) * cell,
                            "Holo tile: the frame's corner is inside the lattice (" + label + ", heading " + heading + ", corner " + corner + ", aspect " + aspect + ": " + east + ", " + north + ")");
                        TestAssert.That(Math.Sqrt(east * east + north * north) < reach, "Holo tile: ... and within the reach the scans and the highest ground use (" + label + ")");
                    }
                }

                // North up the lattice's near edge is below the frame's bottom, even where hills rise (nothing of it is above the bottom), and the far edge is above the top and the sides outside.
                if (northUp)
                {
                    HoloCamera camera = Camera(shape, true, HoloPresentation.ReferenceAspect, range);
                    // (the lattice is centred within half a cell of the aircraft: the edges are at least half a cell nearer than their nominal places)
                    foreach (float across in new[] { -halfX * cell + cell, 0f, halfX * cell - cell })
                    foreach (float height in new[] { 0f, HoloView.MaxRelief })
                    {
                        HoloView.Project(camera, across, height, -halfZ * cell + .5f * cell, out _, out float nearY);
                        TestAssert.That(nearY <= -1f + 1e-3f, "Holo tile: the near edge of the lattice is not above the frame's bottom (" + label + ", hills " + height + " m: " + nearY + ")");
                    }
                    HoloView.Project(camera, 0f, 0f, halfZ * cell - .5f * cell, out _, out float farY);
                    TestAssert.That(farY >= 1f - 1e-3f, "Holo tile: the far edge is not below the frame's top (" + label + ")");
                    HoloView.Project(camera, halfX * cell - .5f * cell, 0f, 0f, out float sideX, out _);
                    TestAssert.That(sideX >= 1f - 1e-3f, "Holo tile: the side edges are not inside the frame (" + label + ")");
                }
            }
            TestAssert.That(biggest <= HoloGrid.MaxHalf, "Holo tile: no lattice needs more than the limit");

            // The extent grows with the range only through the cell: the same picture at twice the range is the same number of cells.
            HoloCamera small = Camera(HoloShape.Big, false, HoloPresentation.ReferenceAspect, 20), large = Camera(HoloShape.Big, false, HoloPresentation.ReferenceAspect, 40);
            HoloGrid.Extent(small, HoloGrid.CellMetres(20, 96), false, out int sx, out int sz, out _);
            HoloGrid.Extent(large, HoloGrid.CellMetres(40, 96), false, out int lx, out int lz, out _);
            TestAssert.That(sx == lx && sz == lz, "Holo tile: twice the range, twice the cell, the same lattice (" + sx + " vs " + lx + ")");

            // Heading up needs the square that holds a circle, north up only the rectangle that holds the frame.
            HoloCamera mini = Camera(HoloShape.Mini, false, HoloPresentation.ReferenceAspect, 20);
            HoloGrid.Extent(mini, 625f, false, out int hx, out int hz, out _);
            HoloCamera miniNorth = Camera(HoloShape.Mini, true, HoloPresentation.ReferenceAspect, 20);
            HoloGrid.Extent(miniNorth, 625f, true, out int nx, out int nz, out _);
            TestAssert.That(hx == hz && nx <= hx && nz <= hz && nx * nz < hx * hz, "Holo tile: north up the lattice is smaller than heading up (" + nx + " x " + nz + " vs " + hx + " x " + hz + ")");
        }

        private static void TestEdgePoint()
        {
            HoloView.EdgePoint(5f, 5f, 0f, 1f, 0f, 0f, 10f, 8f, out float x, out float y);
            TestAssert.That(Math.Abs(x - 5f) < 1e-4f && Math.Abs(y - 8f) < 1e-4f, "Holo north: a ray straight up leaves through the top edge");
            HoloView.EdgePoint(5f, 5f, -1f, 0f, 0f, 0f, 10f, 8f, out x, out y);
            TestAssert.That(Math.Abs(x) < 1e-4f && Math.Abs(y - 5f) < 1e-4f, "Holo north: ... left through the left edge");
            HoloView.EdgePoint(5f, 5f, .7071f, .7071f, 0f, 0f, 10f, 8f, out x, out y);
            TestAssert.That(Math.Abs(x - 8f) < 1e-2f && Math.Abs(y - 8f) < 1e-2f, "Holo north: a diagonal ray leaves where it first meets an edge");
            HoloView.EdgePoint(5f, 1f, 0f, -1f, 0f, 0f, 10f, 8f, out x, out y);
            TestAssert.That(Math.Abs(x - 5f) < 1e-4f && Math.Abs(y) < 1e-4f, "Holo north: a ray down leaves through the bottom edge");
            HoloView.EdgePoint(5f, 5f, 0f, 0f, 0f, 0f, 10f, 8f, out x, out y);
            TestAssert.That(x == 5f && y == 5f, "Holo north: no direction gives the point itself");
            foreach (float bearing in new[] { 0f, 40f, 90f, 150f, 200f, 270f, 330f })
            {
                float dx = (float)Math.Sin(bearing * Math.PI / 180), dy = (float)Math.Cos(bearing * Math.PI / 180);
                HoloView.EdgePoint(170f, 114f, dx, dy, 0f, 0f, 340f, 300f, out x, out y);
                bool onEdge = Math.Abs(x) < 1e-2f || Math.Abs(x - 340f) < 1e-2f || Math.Abs(y) < 1e-2f || Math.Abs(y - 300f) < 1e-2f;
                TestAssert.That(onEdge && x >= -1e-2f && x <= 340.01f && y >= -1e-2f && y <= 300.01f, "Holo north: the tick stands on the frame's edge at every bearing (" + bearing + ")");
            }
        }

        // ------------------------------------------------------------------ marks and words

        private static void TestOffer()
        {
            var list = new HoloMark[3];
            int count = 0;
            foreach (float range in new[] { 5f, 1f, 9f, 3f, 7f })
                HoloMarks.Offer(list, ref count, new HoloMark { Range = range });
            TestAssert.Eq(count, 3, "Holo marks: the list never outgrows its array");
            TestAssert.That(list[0].Range == 1f && list[1].Range == 3f && list[2].Range == 5f, "Holo marks: it keeps the nearest, nearest first");
            TestAssert.That(!HoloMarks.Offer(list, ref count, new HoloMark { Range = 6f }), "Holo marks: a mark farther than the farthest kept is refused");
            TestAssert.That(!HoloMarks.Offer(list, ref count, new HoloMark { Range = 5f }), "Holo marks: ... and so is one as far");
            TestAssert.That(HoloMarks.Offer(list, ref count, new HoloMark { Range = 2f, Colour = 7 }), "Holo marks: a nearer mark takes a place");
            TestAssert.That(list[0].Range == 1f && list[1].Range == 2f && list[1].Colour == 7 && list[2].Range == 3f, "Holo marks: ... in order, pushing the farthest out");

            list = new HoloMark[3];
            count = 0;
            HoloMarks.Offer(list, ref count, new HoloMark { Range = 4f, Colour = 1 });
            HoloMarks.Offer(list, ref count, new HoloMark { Range = 4f, Colour = 2 });
            TestAssert.That(list[0].Colour == 1 && list[1].Colour == 2, "Holo marks: equal ranges keep the order they came in");
            TestAssert.That(!HoloMarks.Offer(new HoloMark[0], ref count, new HoloMark { Range = 1f }), "Holo marks: an empty list keeps nothing");

            // The cap of the real list: the 96 nearest of many, in order.
            list = new HoloMark[HoloMarks.MaximumUnits];
            count = 0;
            var random = new Random(7);
            var all = new float[500];
            for (int i = 0; i < all.Length; i++)
            {
                all[i] = random.Next(1, 100000);
                HoloMarks.Offer(list, ref count, new HoloMark { Range = all[i] });
            }
            Array.Sort(all);
            TestAssert.Eq(count, 96, "Holo marks: at most 96 units are drawn");
            for (int i = 0; i < count; i++)
                TestAssert.Near(list[i].Range, all[i], "Holo marks: the 96 listed are the 96 nearest, nearest first");

            list = new HoloMark[3]; count = 0;
            foreach (float distance in new[] { 10f, 20f, 30f })
                HoloMarks.Offer(list, ref count, new HoloMark { Range = distance }, prioritizeAir: true);
            TestAssert.That(HoloMarks.Offer(list, ref count, new HoloMark { Kind = HoloKind.Air, Range = 500f }, prioritizeAir: true),
                "Holo marks: nearby ground clutter cannot evict a visible aircraft");
            TestAssert.That(HoloMarks.Offer(list, ref count, new HoloMark { Kind = HoloKind.Air, Range = 200f }, prioritizeAir: true),
                "Holo marks: another aircraft enters the bounded list");
            TestAssert.That(list[0].Range == 200f && list[1].Range == 500f && list[2].Range == 10f,
                "Holo marks: air contacts first, nearest first within each class");
            HoloMarks.Offer(list, ref count, new HoloMark { Kind = HoloKind.Air, Range = 900f }, prioritizeAir: true);
            TestAssert.That(!HoloMarks.Offer(list, ref count, new HoloMark { Range = 1f }, prioritizeAir: true),
                "Holo marks: an all-air list keeps its aircraft even over nearer ground");
        }

        private static void TestWords()
        {
            TestAssert.Eq(HoloMarks.Range(20000f), "20 KM", "Holo words: a whole number of kilometres");
            TestAssert.Eq(HoloMarks.Range(2500f), "2.5 KM", "Holo words: under 10 km one decimal");
            TestAssert.Eq(HoloMarks.Range(2000f), "2 KM", "Holo words: ... unless it is whole");
            TestAssert.Eq(HoloMarks.Range(12400f), "12 KM", "Holo words: from 10 km up, whole kilometres");
            TestAssert.Eq(HoloMarks.Range(850f), "850 M", "Holo words: under a kilometre in metres");
            TestAssert.Eq(HoloMarks.Range(999.8f), "1 KM", "Holo words: ... and the metres never read 1000");
            TestAssert.Eq(HoloMarks.Range(float.NaN), "--", "Holo words: a range that is not a number is two dashes");
            TestAssert.Eq(HoloMarks.Range(-5f), "--", "Holo words: ... and so is a negative one");

            TestAssert.Eq(HoloMarks.ShelfFlag(40, false), "40 KM  HDG UP", "Holo words: the shelf's right end, heading up");
            TestAssert.Eq(HoloMarks.ShelfFlag(80, true), "80 KM  N UP", "Holo words: ... and north up");
            TestAssert.Eq(HoloMarks.Caption, "HOLO MAP", "Holo words: the caption");

            TestAssert.Eq(HoloMarks.AirbaseLabel("Kodiak Island Naval Base"), "KODIAK ISLAND", "Holo words: an airbase name is upper case and clipped to 14 characters");
            TestAssert.Eq(HoloMarks.AirbaseLabel("  Alpha\tBase \n"), "ALPHA BASE", "Holo words: whitespace and control characters are tidied");
            TestAssert.Eq(HoloMarks.AirbaseLabel(null), "", "Holo words: no name is no label");
            TestAssert.Eq(HoloMarks.TargetLabel("Bearcat", 12400f), "BEARCAT  12 KM", "Holo words: the selected target's name and range");
            TestAssert.Eq(HoloMarks.TargetLabel(null, 850f), "850 M", "Holo words: ... or the range alone");
            TestAssert.Eq(HoloMarks.TargetLabel("A very long target name indeed", 3500f), "A VERY LONG TA  3.5 KM", "Holo words: a long name is clipped");
        }

        private static void TestScaleBar()
        {
            TestAssert.Near(HoloMarks.ScaleBar(40000f), 10000f, "Holo scale: a frame 40 km across gets a 10 km bar");
            TestAssert.Near(HoloMarks.ScaleBar(80000f), 20000f, "Holo scale: ... 80 km, 20 km");
            TestAssert.Near(HoloMarks.ScaleBar(20000f), 5000f, "Holo scale: ... 20 km, 5 km");
            TestAssert.Near(HoloMarks.ScaleBar(160000f), 50000f, "Holo scale: ... 160 km, 50 km");
            TestAssert.Eq(HoloMarks.Range(HoloMarks.ScaleBar(40000f)), "10 KM", "Holo scale: and it is written as kilometres");
            TestAssert.Near(HoloMarks.ScaleBar(0f), 0f, "Holo scale: no width, no bar");
            TestAssert.Near(HoloMarks.ScaleBar(float.NaN), 0f, "Holo scale: ... nor one that is not a number");
            TestAssert.Near(HoloMarks.ScaleBar(float.PositiveInfinity), 0f, "Holo scale: ... nor an endless one");

            // Every width the picture can have, tiny steps: the bar is a round number (1, 2, 3 or 5 times a power of ten) between 15 % and 35 % of the width.
            for (float width = 1000f; width < 400000f; width *= 1.003f)
            {
                float bar = HoloMarks.ScaleBar(width);
                float share = bar / width;
                TestAssert.That(share >= .15f - 1e-5f && share <= .35f + 1e-5f, "Holo scale: the bar is 15 to 35 % of the frame (width " + width + ": " + share + ")");
                double decade = Math.Pow(10, Math.Floor(Math.Log10(bar) + 1e-6));
                double mantissa = bar / decade;
                bool round = Math.Abs(mantissa - 1) < 1e-3 || Math.Abs(mantissa - 2) < 1e-3 || Math.Abs(mantissa - 3) < 1e-3 || Math.Abs(mantissa - 5) < 1e-3;
                TestAssert.That(round, "Holo scale: the bar is a round number (width " + width + ": " + bar + ")");
            }
            // The widths the pictures really have at the bar's row: each range, each size, both orientations.
            foreach (HoloShape shape in new[] { HoloShape.Mini, HoloShape.Big })
            foreach (bool northUp in new[] { false, true })
            foreach (int range in shape == HoloShape.Mini ? new[] { 10, 20, 40 } : new[] { 20, 40, 80 })
            {
                HoloCamera camera = Camera(shape, northUp, shape == HoloShape.Mini ? 340f / 300f : 1.3f, range);
                float row = shape == HoloShape.Mini ? 14f / 300f * 2f - 1f : 14f / 886f * 2f - 1f;
                float width = HoloView.WidthAt(camera, row), bar = HoloMarks.ScaleBar(width);
                TestAssert.That(width > range * 1000f && bar / width >= .15f - 1e-5f && bar / width <= .35f + 1e-5f && bar > 0f,
                    "Holo scale: the bar for the " + shape + " at " + range + " km is " + bar + " of " + width + " m");
            }
        }

        // ------------------------------------------------------------------ the TAC MAP: MINI, BIG, or the game's own corner map

        private static void TestPresentation()
        {
            // The standing MINI: the corner map showing, the TAC MAP on HOLO.
            TestAssert.Eq(HoloPresentation.Choose(HoloStage.Mini, true, true), HoloShape.Mini, "Tac map: the corner map showing on HOLO is the MINI, with no key");
            TestAssert.Eq(HoloPresentation.Choose(HoloStage.Pending, true, true), HoloShape.Mini, "Tac map: ... and it stays while a key press waits to be told a tap from a hold");
            TestAssert.Eq(HoloPresentation.Choose(HoloStage.Holo, true, true), HoloShape.Big, "Tac map: the held key grows the same hologram to the BIG one");

            // CLASSIC: the game's own corner map, the held key still raises the hologram.
            TestAssert.Eq(HoloPresentation.Choose(HoloStage.Mini, false, true), HoloShape.None, "Tac map: CLASSIC never draws the MINI");
            TestAssert.Eq(HoloPresentation.Choose(HoloStage.Pending, false, true), HoloShape.None, "Tac map: ... not while a key press waits");
            TestAssert.Eq(HoloPresentation.Choose(HoloStage.Holo, false, true), HoloShape.Big, "Tac map: ... but the held key still raises the BIG hologram");

            // The maximised map: nothing of the holo draws, whatever the mode; and without the gates the game's own corner map stays.
            foreach (bool mini in new[] { false, true })
            {
                TestAssert.Eq(HoloPresentation.Choose(HoloStage.Mfd, mini, true), HoloShape.None, "Tac map: while the MFD map is up nothing of the holo draws");
                foreach (HoloStage stage in new[] { HoloStage.Mini, HoloStage.Pending, HoloStage.Holo, HoloStage.Mfd })
                    TestAssert.Eq(HoloPresentation.Choose(stage, mini, false), HoloShape.None, "Tac map: without the gates the game's own corner map stays (" + stage + ")");
            }

            // A tap, through the machine: the MINI until the MFD map opens, then nothing, then the MINI again when the key minimises it.
            var state = new HoloMapState();
            TestAssert.Eq(HoloPresentation.Choose(state.Stage, true, true), HoloShape.Mini, "Tac map: a tap starts at the MINI");
            state.KeyDown(true, 1f);
            TestAssert.Eq(HoloPresentation.Choose(state.Stage, true, true), HoloShape.Mini, "Tac map: ... still the MINI on key down");
            state.Tick(1.05f, false);
            state.Maximized();
            TestAssert.Eq(HoloPresentation.Choose(state.Stage, true, true), HoloShape.None, "Tac map: ... nothing while the MFD map is open");
            state.Minimized();
            TestAssert.Eq(HoloPresentation.Choose(state.Stage, true, true), HoloShape.Mini, "Tac map: ... and the MINI returns when it closes");

            // A hold, through the machine: MINI, BIG while held, MINI after.
            state = new HoloMapState();
            state.KeyDown(true, 0f);
            TestAssert.Eq(state.Tick(Tap, true), HoloStep.ShowHolo, "Tac map: a hold shows the holo");
            TestAssert.Eq(HoloPresentation.Choose(state.Stage, true, true), HoloShape.Big, "Tac map: ... as the BIG one");
            state.Tick(1f, false);
            TestAssert.Eq(HoloPresentation.Choose(state.Stage, true, true), HoloShape.Mini, "Tac map: ... and shrinks back to the MINI on release");

            TestAssert.Near(HoloPresentation.Tilt(HoloShape.Mini), 68f, "Tac map: the MINI is seen from 68 degrees, close to a plan view");
            TestAssert.Near(HoloPresentation.Tilt(HoloShape.Big), 55f, "Tac map: the BIG one from 55, for more relief");
            TestAssert.Eq(HoloPresentation.GridCells(HoloShape.Mini), 48, "Tac map: the MINI's grid is 48 cells across");
            TestAssert.Eq(HoloPresentation.GridCells(HoloShape.Big), 96, "Tac map: ... the BIG one's 96");
        }

        private static void TestRanges()
        {
            foreach (int ok in new[] { 10, 20, 40 })
                TestAssert.Eq(HoloPresentation.MiniRange(ok), ok, "Tac map: the MINI reaches " + ok + " km");
            TestAssert.Eq(HoloPresentation.MiniRange(5), 10, "Tac map: the old 5 km option is the nearest new one, 10 km");
            foreach (int bad in new[] { 0, 7, 15, 80, -5 })
                TestAssert.Eq(HoloPresentation.MiniRange(bad), 20, "Tac map: any other saved range is the default, 20 km");
            foreach (int ok in new[] { 20, 40, 80 })
                TestAssert.Eq(HoloPresentation.HoloRange(ok), ok, "Tac map: the held holo reaches " + ok + " km");
            TestAssert.Eq(HoloPresentation.HoloRange(10), 20, "Tac map: the old 10 km option is the nearest new one, 20 km");
            foreach (int bad in new[] { 0, 5, 30, 160, -1 })
                TestAssert.Eq(HoloPresentation.HoloRange(bad), 40, "Tac map: any other saved range is the default, 40 km");

            TestAssert.Eq(HoloPresentation.BigRange(40, 20), 40, "Tac map: the big range is the HOLO RANGE");
            TestAssert.Eq(HoloPresentation.BigRange(20, 10), 20, "Tac map: ... 20 over a 10 km MINI");
            TestAssert.Eq(HoloPresentation.BigRange(20, 20), 20, "Tac map: ... equal to the MINI's is allowed");
            TestAssert.Eq(HoloPresentation.BigRange(20, 40), 40, "Tac map: ... but never less than the MINI's, a hologram that grows does not look at less ground");
            TestAssert.Eq(HoloPresentation.BigRange(80, 40), 80, "Tac map: ... 80 km over a 40 km MINI");
            TestAssert.Eq(HoloPresentation.BigRange(0, 0), 40, "Tac map: unusable saved ranges give the default pair, 40 over 20");

            // The one-time move of a saved range from the old disc's options: every pick keeps its place in the list.
            TestAssert.Eq(HoloPresentation.MigrateMiniRange(5), 10, "Tac map: a saved MINI 5 km becomes 10");
            TestAssert.Eq(HoloPresentation.MigrateMiniRange(10), 20, "Tac map: ... 10 becomes 20 (the old default is the new default)");
            TestAssert.Eq(HoloPresentation.MigrateMiniRange(20), 40, "Tac map: ... 20 becomes 40");
            TestAssert.Eq(HoloPresentation.MigrateMiniRange(7), 20, "Tac map: ... anything else is the default");
            TestAssert.Eq(HoloPresentation.MigrateHoloRange(10), 20, "Tac map: a saved HOLO RANGE 10 km becomes 20");
            TestAssert.Eq(HoloPresentation.MigrateHoloRange(20), 40, "Tac map: ... 20 becomes 40 (the old default is the new default)");
            TestAssert.Eq(HoloPresentation.MigrateHoloRange(40), 80, "Tac map: ... 40 becomes 80");
            TestAssert.Eq(HoloPresentation.MigrateHoloRange(0), 40, "Tac map: ... anything else is the default");
            foreach (int old in new[] { 5, 10, 20 })
                TestAssert.Eq(HoloPresentation.MiniRange(HoloPresentation.MigrateMiniRange(old)), HoloPresentation.MigrateMiniRange(old), "Tac map: a migrated MINI range is one of the options");
            foreach (int old in new[] { 10, 20, 40 })
                TestAssert.Eq(HoloPresentation.HoloRange(HoloPresentation.MigrateHoloRange(old)), HoloPresentation.MigrateHoloRange(old), "Tac map: a migrated HOLO range is one of the options");

            // Every range fills the grid's cell count across twice its length.
            foreach (int km in new[] { 10, 20, 40 })
                TestAssert.Near(HoloGrid.CellMetres(km, 64) * 64, km * 2000f, "Tac map: the MINI's grid spans twice the range exactly (" + km + " km)");
            foreach (int km in new[] { 20, 40, 80 })
                TestAssert.Near(HoloGrid.CellMetres(km, 96) * 96, km * 2000f, "Tac map: ... and the BIG one's (" + km + " km)");
        }

        private static void TestSizes()
        {
            // 1080p: the MINI 340 x 300 units, 16 in from the edges.
            HoloPresentation.Frame(0f, 1920f, 1080f, out float x, out float y, out float w, out float h);
            TestAssert.That(x == 16f && y == 16f && w == 340f && h == 300f, "Tac frame: the MINI is 340 x 300 units, 16 from the left and bottom edges (got " + x + ", " + y + ", " + w + " x " + h + ")");
            // The BIG one: 82 % of the height, 1.3 times as wide, centred.
            HoloPresentation.Frame(1f, 1920f, 1080f, out x, out y, out w, out h);
            TestAssert.That(Math.Abs(h - .82f * 1080f) <= .5f && Math.Abs(w - 1.3f * h) <= 1f, "Tac frame: the BIG one is 82 % of the screen height and 1.3 times as wide (got " + w + " x " + h + ")");
            TestAssert.That(Math.Abs(x - (1920f - w) * .5f) <= .5f && Math.Abs(y - (1080f - h) * .5f) <= .5f, "Tac frame: ... centred on the screen, both ways (got " + x + ", " + y + ")");
            // On a narrower screen the width is held to 92 % of it.
            HoloPresentation.Frame(1f, 1000f, 1080f, out x, out y, out w, out h);
            TestAssert.That(Math.Abs(w - 920f) <= .5f && Math.Abs(x - 40f) <= .5f && Math.Abs(h - .82f * 1080f) <= .5f, "Tac frame: on a narrow screen the width is clamped to 92 % of it (got " + w + " at " + x + ")");
            // The units follow the screen's height.
            HoloPresentation.Frame(0f, 3840f, 2160f, out x, out y, out w, out h);
            TestAssert.That(x == 32f && y == 32f && w == 680f && h == 600f, "Tac frame: the MINI scales with the screen height (4K)");
            HoloPresentation.Frame(0f, 1280f, 720f, out x, out y, out w, out h);
            TestAssert.That(w == 227f && h == 200f, "Tac frame: ... and 720p (got " + w + " x " + h + ")");

            // The frame grows steadily out of the corner and stays on the screen.
            float lastW = 0f, lastH = 0f;
            for (float t = 0f; t <= 1.0001f; t += .05f)
            {
                HoloPresentation.Frame(t, 1920f, 1080f, out x, out y, out w, out h);
                TestAssert.That(w >= lastW && h >= lastH && w >= 340f && h >= 300f && x >= 0f && y >= 0f && x + w <= 1920f && y + h <= 1080f, "Tac frame: it grows steadily between the two sizes and stays on the screen");
                lastW = w;
                lastH = h;
            }
            HoloPresentation.Frame(-1f, 1920f, 1080f, out x, out y, out w, out h);
            TestAssert.That(x == 16f && w == 340f, "Tac frame: a size below 0 is the MINI");
            HoloPresentation.Frame(3f, 1920f, 1080f, out _, out _, out w, out h);
            TestAssert.That(Math.Abs(h - 885.6f) <= .5f, "Tac frame: ... and above 1 the BIG one");

            // The growth: 180 ms from MINI to BIG and back, never past either end.
            float size = 0f;
            for (int frame = 0; frame < 5; frame++) size = HoloPresentation.Size(size, HoloShape.Big, .018f);
            TestAssert.Near(size, .5f, "Tac map: half the growth takes 90 ms");
            for (int frame = 0; frame < 5; frame++) size = HoloPresentation.Size(size, HoloShape.Big, .018f);
            TestAssert.Near(size, 1f, "Tac map: the growth takes 180 ms");
            TestAssert.Near(HoloPresentation.Size(size, HoloShape.Big, 1f), 1f, "Tac map: ... and stops at BIG");
            for (int frame = 0; frame < 10; frame++) size = HoloPresentation.Size(size, HoloShape.Mini, .018f);
            TestAssert.Near(size, 0f, "Tac map: releasing shrinks it back in 180 ms");
            TestAssert.Near(HoloPresentation.Size(.4f, HoloShape.None, .018f), .3f, "Tac map: a picture that is going (nothing) shrinks toward the MINI");
            TestAssert.Near(HoloPresentation.Size(.1f, HoloShape.Mini, 1f), 0f, "Tac map: ... and never below 0");

            // The part of the texture the picture uses.
            HoloPresentation.RenderPixels(340f, 300f, out int pw, out int ph);
            TestAssert.That(pw == 510 && ph == 450, "Tac map: a 340 x 300 pixel MINI is drawn with 510 x 450 pixels of the texture, not a shrunk big picture (got " + pw + " x " + ph + ")");
            HoloPresentation.RenderPixels(1151f, 886f, out pw, out ph);
            TestAssert.That(pw <= HoloPresentation.TextureWidth && ph <= HoloPresentation.TextureHeight && pw >= 1151 && Math.Abs(pw / (float)ph - 1151f / 886f) < .01f,
                "Tac map: the BIG frame at 1080p is drawn at least at its own size, within the texture, in its own shape (got " + pw + " x " + ph + ")");
            HoloPresentation.RenderPixels(5000f, 3000f, out pw, out ph);
            TestAssert.That(pw <= HoloPresentation.TextureWidth && ph <= HoloPresentation.TextureHeight, "Tac map: ... never more than the texture");
            HoloPresentation.RenderPixels(20f, 20f, out pw, out ph);
            TestAssert.That(pw == HoloPresentation.MinimumPixels && ph == HoloPresentation.MinimumPixels, "Tac map: ... and never fewer than the minimum");
            HoloPresentation.RenderPixels(0f, 0f, out pw, out ph);
            TestAssert.That(pw == HoloPresentation.MinimumPixels, "Tac map: a frame with no size gets the minimum");
            TestAssert.Near(HoloPresentation.Ease(0f), 0f, "Tac map: the shelf is at the MINI's place at size 0");
            TestAssert.Near(HoloPresentation.Ease(1f), 1f, "Tac map: ... and at the BIG one's at size 1");
            TestAssert.Near(HoloPresentation.Ease(.5f), .5f, "Tac map: ... half way at half");
        }

        private static void TestWedge()
        {
            double sixty = Math.Tan(30.0 * Math.PI / 180.0);
            TestAssert.Near(HoloView.HorizontalFov(60f, 16f / 9f), (float)(2.0 * Math.Atan(sixty * 16.0 / 9.0) * 180.0 / Math.PI), "Tac map: a 60 degree vertical lens at 16:9 sees about 91 degrees across");
            TestAssert.That(HoloView.HorizontalFov(60f, 16f / 9f) > 90f && HoloView.HorizontalFov(60f, 16f / 9f) < 93f, "Tac map: ... 91 and a half");
            TestAssert.Near(HoloView.HorizontalFov(60f, 1f), 60f, "Tac map: a square picture sees as wide as it is high");
            TestAssert.That(HoloView.HorizontalFov(40f, 21f / 9f) > HoloView.HorizontalFov(40f, 16f / 9f), "Tac map: a wider screen sees wider");
            TestAssert.Near(HoloView.HorizontalFov(0f, 1.78f), 0f, "Tac map: no lens, no field of view");
            TestAssert.Near(HoloView.HorizontalFov(60f, 0f), 0f, "Tac map: ... nor a picture without width");
            TestAssert.Near(HoloView.HorizontalFov(float.NaN, 1.78f), 0f, "Tac map: ... nor a reading that is not a number");

            const int n = 12;
            var east = new float[n + 1];
            var north = new float[n + 1];
            double quarter = Math.PI / 4;
            // Looking due north through a 90 degree lens: the arc's ends are 45 degrees either side, the middle straight ahead.
            HoloView.Wedge(0f, 90f, 5000f, n, east, north);
            TestAssert.Near(east[0], (float)(-5000.0 * Math.Sin(quarter)), "Tac map: the wedge's left end is 45 degrees left of north");
            TestAssert.Near(north[0], (float)(5000.0 * Math.Cos(quarter)), "Tac map: ... at the wedge's length");
            TestAssert.Near(east[n], (float)(5000.0 * Math.Sin(quarter)), "Tac map: the right end is 45 degrees right");
            TestAssert.Near(north[n], north[0], "Tac map: ... and as far ahead as the left");
            TestAssert.Near(east[n / 2], 0f, "Tac map: the middle of the arc is straight ahead");
            TestAssert.Near(north[n / 2], 5000f, "Tac map: ... at the wedge's length");
            for (int i = 0; i <= n; i++)
                TestAssert.That(Math.Abs(Math.Sqrt(east[i] * east[i] + north[i] * north[i]) - 5000.0) < .05, "Tac map: every point of the arc is on the circle of the wedge's length");

            // Looking east, then at a bearing just west of north: the arc is centred on the bearing.
            HoloView.Wedge(90f, 60f, 1000f, n, east, north);
            TestAssert.That(Math.Abs(east[n / 2] - 1000f) < .01f && Math.Abs(north[n / 2]) < .01f, "Tac map: looking east the middle of the arc is due east");
            TestAssert.That(north[0] > 0f && north[n] < 0f, "Tac map: ... the left end is to the north of it and the right end to the south");
            HoloView.Wedge(350f, 40f, 1000f, n, east, north);
            TestAssert.That(east[n / 2] < 0f && north[n / 2] > 0f, "Tac map: a bearing of 350 is just west of north");
            TestAssert.That(east[0] < east[n] && Math.Abs((east[0] + east[n]) * .5f - east[n / 2]) < 20f, "Tac map: ... and the arc is centred on it");

            // The lens is held within 8 and 160 degrees.
            HoloView.Wedge(0f, 0f, 1000f, n, east, north);
            TestAssert.Near(east[n] - east[0], (float)(2000.0 * Math.Sin(4.0 * Math.PI / 180.0)), "Tac map: a lens that reads 0 is drawn at the narrowest, 8 degrees");
            HoloView.Wedge(0f, 300f, 1000f, n, east, north);
            TestAssert.Near(east[n], (float)(1000.0 * Math.Sin(80.0 * Math.PI / 180.0)), "Tac map: ... and one that reads 300 at the widest, 160 degrees");

            // Seen through the MINI's camera (the wedge is only drawn there): at every heading and bearing the whole wedge, widest lens, stays inside the frame.
            foreach (bool northUp in new[] { false, true })
            foreach (int range in new[] { 10, 20, 40 })
            for (float heading = 0f; northUp ? heading < 1f : heading < 360f; heading += 30f)
            for (float bearing = 0f; bearing < 360f; bearing += 30f)
            {
                HoloCamera camera = Camera(HoloShape.Mini, northUp, 340f / 300f, range, heading);
                HoloView.Wedge(bearing, 160f, range * 1000f * .55f, n, east, north);
                for (int i = 0; i <= n; i++)
                {
                    bool shown = HoloView.Project(camera, east[i], 0f, north[i], out float x, out float y);
                    TestAssert.That(shown && Math.Abs(x) < 1f && Math.Abs(y) < 1f, "Tac map: the wedge stays inside the frame (range " + range + ", heading " + heading + ", bearing " + bearing + ", north up " + northUp + ": " + x + ", " + y + ")");
                }
            }
        }

        private static void TestMiniMarks()
        {
            TestAssert.Eq(HoloPresentation.UnitCap(HoloShape.Mini), 48, "Tac map: the MINI carries at most 48 units");
            TestAssert.Eq(HoloPresentation.UnitCap(HoloShape.Big), 96, "Tac map: ... the BIG one 96");
            TestAssert.Eq(HoloPresentation.AirbaseCap(HoloShape.Mini), 1, "Tac map: the MINI names the nearest airbase in view only");
            TestAssert.Eq(HoloPresentation.AirbaseCap(HoloShape.Big), 6, "Tac map: ... the BIG one six");

            // The scan's list is as long as the BIG one's; the MINI offers into it with its own limit.
            var list = new HoloMark[HoloMarks.MaximumUnits];
            int count = 0;
            var random = new Random(11);
            var all = new float[400];
            for (int i = 0; i < all.Length; i++)
            {
                all[i] = random.Next(1, 100000);
                HoloMarks.Offer(list, ref count, new HoloMark { Range = all[i], Kind = i % 3 == 0 ? HoloKind.Air : HoloKind.Surface }, HoloPresentation.UnitCap(HoloShape.Mini));
            }
            Array.Sort(all);
            TestAssert.Eq(count, 48, "Tac map: 400 units in range leave 48 on the MINI");
            for (int i = 0; i < count; i++)
                TestAssert.Near(list[i].Range, all[i], "Tac map: the 48 are the 48 nearest, nearest first");
            TestAssert.That(!HoloMarks.Offer(list, ref count, new HoloMark { Range = all[47] + 1f }, 48), "Tac map: a unit farther than the farthest of the 48 is refused");
            TestAssert.That(HoloMarks.Offer(list, ref count, new HoloMark { Range = 0f, Colour = 9 }, 48) && list[0].Colour == 9 && count == 48, "Tac map: a nearer one takes its place at the head and the farthest goes out");
            TestAssert.Near(list[47].Range, all[46], "Tac map: ... the 48th is now the old 47th");

            // The same scan taken to the BIG limit (a fresh scan, count restarted) holds 96.
            count = 0;
            for (int i = 0; i < all.Length; i++) HoloMarks.Offer(list, ref count, new HoloMark { Range = all[i] }, HoloPresentation.UnitCap(HoloShape.Big));
            TestAssert.Eq(count, 96, "Tac map: the same scan at the BIG limit keeps 96");

            // A limit smaller than what is already listed trims the list rather than overrunning it.
            count = 96;
            HoloMarks.Offer(list, ref count, new HoloMark { Range = -1f }, 48);
            TestAssert.Eq(count, 48, "Tac map: a list longer than the new limit is cut to it");
            TestAssert.That(!HoloMarks.Offer(list, ref count, new HoloMark { Range = 1f }, 0), "Tac map: a limit of 0 keeps nothing");

            // Airbases: the scan keeps the nearest six of those in reach, in order; the picture shows the first in view.
            var bases = new HoloMark[HoloMarks.MaximumAirbases];
            int baseCount = 0;
            foreach (float range in new[] { 8000f, 3000f, 5000f })
                HoloMarks.Offer(bases, ref baseCount, new HoloMark { Range = range, Id = (int)range }, HoloMarks.MaximumAirbases);
            TestAssert.That(baseCount == 3 && bases[0].Id == 3000 && bases[1].Id == 5000 && bases[2].Id == 8000, "Tac map: the airbases in reach are listed nearest first");

            TestAssert.Eq(HoloMarks.MiniCaption, "TAC MAP", "Tac map: the MINI's caption");
            TestAssert.Eq(HoloMarks.MiniFlag(20), "20 KM", "Tac map: the range at the shelf's right end");
            TestAssert.Eq(HoloMarks.MiniFlag(10), "10 KM", "Tac map: ... 10 km");
            TestAssert.Eq(HoloMarks.MiniFlag(40), "40 KM", "Tac map: ... 40 km");
            TestAssert.Eq(HoloMarks.Range(HoloGrid.Radius(80)), "80 KM", "Tac map: the range reads the same way");
        }

        // ------------------------------------------------------------------ cost

        private static void TestFillTime()
        {
            // The real sheet: 513 x 513 samples, 160 m apart, rolling ground with a coast. The two lattices that cost most: heading up (the square that holds the circle), the MINI's 64-cell grid and the BIG one's 96.
            TerrainHeightField field = Field(513, 160f,
                (ix, iz) => 600f + 500f * (float)(Math.Sin(ix * .05) * Math.Cos(iz * .04)) + 150f * (float)Math.Sin(ix * .31 + iz * .27),
                (ix, iz) => ix > 40);
            foreach (HoloShape shape in new[] { HoloShape.Mini, HoloShape.Big })
            {
                int range = shape == HoloShape.Mini ? 20 : 40;
                float cell = HoloGrid.CellMetres(range, HoloPresentation.GridCells(shape));
                HoloGrid.Extent(Camera(shape, false, HoloPresentation.ReferenceAspect, range), cell, false, out int halfX, out int halfZ, out float reach);
                var v = new GhostVertex[HoloGrid.MaxVertices];
                var eastWest = new int[HoloGrid.MaxLineIndices];
                var northSouth = new int[HoloGrid.MaxLineIndices];
                var triangles = new int[HoloGrid.MaxTriangleIndices];
                var water = new int[HoloGrid.MaxLineIndices];
                var coast = new int[HoloGrid.MaxLineIndices];
                var wetCells = new int[HoloGrid.MaxLineIndices];
                int nodes = HoloGrid.Nodes(halfX, halfZ);
                double heights = 0, colours = 0, indices = 0, lines = 0;
                const int warm = 20, runs = 200;
                for (int n = -warm; n < runs; n++)
                {
                    int cx = 10 + n % 37, cz = -20 + n % 23;
                    long a = Stopwatch.GetTimestamp();
                    float highest = HoloGrid.FillHeights(field, cell, cx, cz, halfX, halfZ, reach, v);
                    long b = Stopwatch.GetTimestamp();
                    HoloGrid.FillColours(field.SeaLevel, highest, Ink, nodes, v);
                    long c = Stopwatch.GetTimestamp();
                    HoloGrid.BuildIndices(halfX, halfZ, eastWest, northSouth, triangles, out _, out _, out _);
                    long d = Stopwatch.GetTimestamp();
                    HoloGrid.BuildLines(field, cell, halfX, halfZ, cx, cz, field.SeaLevel, v, wetCells, eastWest, northSouth, water, coast, out _, out _, out _, out _, out int count);
                    HoloGrid.Shade(halfX, halfZ, cell, field.SeaLevel, Ink, count, v);
                    long e = Stopwatch.GetTimestamp();
                    if (n < 0) continue;
                    heights += (b - a) * 1000.0 / Stopwatch.Frequency;
                    colours += (c - b) * 1000.0 / Stopwatch.Frequency;
                    indices += (d - c) * 1000.0 / Stopwatch.Frequency;
                    lines += (e - d) * 1000.0 / Stopwatch.Frequency;
                }
                heights /= runs;
                colours /= runs;
                indices /= runs;
                lines /= runs;
                Console.WriteLine("Holo lattice {0}: the land / water line sort after each fill {1:0.000} ms", shape, lines);
                Console.WriteLine("Holo lattice fill, {0} heading up at {1} km ({2} x {3} cells of {4:0} m, {5} nodes): heights {6:0.000} ms + colours {7:0.000} ms = {8:0.000} ms per cell change; index sets {9:0.000} ms (only when the size changes)",
                    shape, range, 2 * halfX, 2 * halfZ, cell, nodes, heights, colours, heights + colours, indices);
                TestAssert.That(heights + colours + lines < 4.0, "Holo grid: a full fill of the " + shape + " lattice with its line sort must stay a few milliseconds (took " + (heights + colours + lines) + " ms)");
            }
        }

        // ------------------------------------------------------------------ fixtures

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
    }
}
