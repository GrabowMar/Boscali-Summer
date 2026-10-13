using System;
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
            TestHeightsMatchGhost();
            TestColours();
            TestMatrices();
            TestProjection();
            TestRings();
            TestDiscBox();
            TestHoloFits();
            TestOffer();
            TestWords();
            TestPresentation();
            TestRanges();
            TestSizes();
            TestWedge();
            TestMiniMarks();
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

        // ------------------------------------------------------------------ the grid, in any cell size

        private static void TestCellSizes()
        {
            TestAssert.Near(HoloGrid.CellMetres(10), 312.5f, "Holo grid: 64 cells across 20 km are 312.5 m");
            TestAssert.Near(HoloGrid.CellMetres(20), 625f, "Holo grid: ... 40 km are 625 m");
            TestAssert.Near(HoloGrid.CellMetres(40), 1250f, "Holo grid: ... 80 km are 1250 m");
            TestAssert.Near(HoloGrid.Radius(40), 40000f, "Holo grid: the radius is the range");
            TestAssert.Near(TerrainGhostGrid.Cells * HoloGrid.CellMetres(20) * .5f, HoloGrid.Radius(20), "Holo grid: the grid's half-width is the radius");

            HoloGrid.Snap(0f, 0f, 625f, out int x, out int z);
            TestAssert.That(x == 0 && z == 0, "Holo grid: the origin is the centre of cell 0");
            HoloGrid.Snap(311f, -311f, 625f, out x, out z);
            TestAssert.That(x == 0 && z == 0, "Holo grid: anywhere inside a cell keeps its centre (no refill)");
            HoloGrid.Snap(314f, -314f, 625f, out x, out z);
            TestAssert.That(x == 1 && z == -1, "Holo grid: past half a cell the next cell takes over");
            HoloGrid.Snap(-5000f, 12345f, 1250f, out x, out z);
            TestAssert.That(x == -4 && z == 10, "Holo grid: cells count from the global origin in the picture's own size (got " + x + ", " + z + ")");
            TestAssert.Near(HoloGrid.Anchor(3, 625f), 1875f, "Holo grid: a cell's centre is a whole number of cells from the origin");
        }

        private static void TestHeightsMatchGhost()
        {
            // At the ghost's own cell size the holo grid places and reads exactly what the ghost grid does.
            TerrainHeightField field = Field(201, 320f, (ix, iz) => 50f + ix + (iz % 7) * 10f, (ix, iz) => ix > 30);
            var ghost = new GhostVertex[TerrainGhostGrid.VertexCount];
            var holo = new GhostVertex[TerrainGhostGrid.VertexCount];
            TerrainGhostGrid.FillHeights(field, 5, -3, ghost);
            float highest = HoloGrid.FillHeights(field, TerrainGhostGrid.CellMetres, 5, -3, holo);
            float expected = field.SeaLevel;
            for (int n = 0; n < ghost.Length; n++)
            {
                TestAssert.That(ghost[n].X == holo[n].X && ghost[n].Y == holo[n].Y && ghost[n].Z == holo[n].Z,
                    "Holo grid: at the ghost's cell size every vertex lands where the ghost's does");
                if (ghost[n].Y > expected) expected = ghost[n].Y;
            }
            TestAssert.Near(highest, expected, "Holo grid: the highest elevation placed is reported");

            // At 625 m a vertex is 625 m from its neighbour and reads the ground under it.
            highest = HoloGrid.FillHeights(field, 625f, 2, 3, holo);
            const int side = TerrainGhostGrid.Side;
            for (int j = 0; j < side; j++)
            for (int i = 0; i < side; i++)
            {
                GhostVertex v = holo[j * side + i];
                TestAssert.Near(v.X, (i - 32) * 625f, "Holo grid: nodes are one cell apart along x");
                TestAssert.Near(v.Z, (j - 32) * 625f, "Holo grid: ... and along z");
                float gx = HoloGrid.Anchor(2, 625f) + v.X, gz = HoloGrid.Anchor(3, 625f) + v.Z;
                TestAssert.Near(v.Y, field.IsLand(gx, gz) ? field.Sample(gx, gz) : field.SeaLevel, "Holo grid: a vertex reads the ground under it");
                GhostVertex across = holo[TerrainGhostGrid.Nodes + i * side + j];
                TestAssert.That(across.X == v.X && across.Y == v.Y && across.Z == v.Z, "Holo grid: the two lines through a node agree");
            }
        }

        private static void TestColours()
        {
            const float cell = 625f, radius = TerrainGhostGrid.Cells * cell * .5f;
            const int side = TerrainGhostGrid.Side;
            // A sea: every line the same faint ink, no thinning, black past the edge.
            TerrainHeightField sea = Field(201, 320f, (ix, iz) => -200f, (ix, iz) => false);
            var v = new GhostVertex[TerrainGhostGrid.VertexCount];
            float highest = HoloGrid.FillHeights(sea, cell, 0, 0, v);
            HoloGrid.FillColours(cell, sea.SeaLevel, highest, Ink, v);
            byte faint = (byte)(Ink.G * HoloGrid.SeaStrength + .5f);
            for (int j = 0; j < side; j++)
            for (int i = 0; i < side; i++)
            {
                GhostVertex a = v[j * side + i];
                float d = (float)Math.Sqrt(a.X * a.X + a.Z * a.Z);
                TestAssert.Eq(a.A, (byte)255, "Holo colours: the light is premultiplied, so alpha is always full");
                if (d <= HoloGrid.EdgeStart * radius) TestAssert.Eq(a.G, faint, "Holo colours: every line of the sea is the same faint ink");
                if (d >= radius) TestAssert.That(a.R == 0 && a.G == 0 && a.B == 0, "Holo colours: nothing is lit at the edge or beyond, so the picture is a disc");
                TestAssert.That(a.G <= faint, "Holo colours: the edge only takes light away from the sea");
            }

            // Flat land 100 m up: dimmer than the ink (the span floor), not as bright as a peak.
            TerrainHeightField plain = Field(201, 320f, (ix, iz) => 100f, null);
            highest = HoloGrid.FillHeights(plain, cell, 0, 0, v);
            TestAssert.Near(highest, 100f, "Holo colours: the plain is its own highest ground");
            HoloGrid.FillColours(cell, 0f, highest, Ink, v);
            GhostVertex near = v[32 * side + 34];
            TestAssert.That(near.G < Ink.G && near.G > faint, "Holo colours: low land is brighter than the sea and dimmer than the ink (got " + near.G + ")");

            // A ramp east: the highest ground in view is the bright tone, and light rises with height.
            TerrainHeightField ramp = Field(201, 320f, (ix, iz) => (ix - 60) * 20f, (ix, iz) => ix > 60);
            highest = HoloGrid.FillHeights(ramp, cell, 0, 0, v);
            HoloGrid.FillColours(cell, 0f, highest, Ink, v);
            byte brightG = (byte)(Ink.G + (255f - Ink.G) * HoloGrid.BrightLift + .5f);
            GhostVertex peak = default;
            float peakY = -1f;
            int previous = -1;
            bool rising = true;
            for (int i = 33; i < side; i++)
            {
                GhostVertex a = v[32 * side + i];
                if (a.Y > peakY) { peakY = a.Y; peak = a; }
                int light = a.R + a.G + a.B;
                if (light < previous && Math.Abs(a.X) <= HoloGrid.EdgeStart * radius) rising = false;
                previous = light;
            }
            TestAssert.That(rising, "Holo colours: going up the ramp the light never falls inside the edge fade");
            TestAssert.That(peakY > 0f && peak.G <= brightG, "Holo colours: the highest ground never outshines the bright tone");
            int brightest = 0;
            for (int n = 0; n < v.Length; n++)
                if (v[n].Y >= highest - .5f && Math.Sqrt(v[n].X * v[n].X + v[n].Z * v[n].Z) <= HoloGrid.EdgeStart * radius) brightest = Math.Max(brightest, v[n].G);
            TestAssert.That(brightest == 0 || brightest == brightG, "Holo colours: the highest ground inside the fade is exactly the bright tone (got " + brightest + ", want " + brightG + ")");

            // The fill reports how many vertices carry light.
            int lit = HoloGrid.FillColours(cell, 0f, highest, Ink, v);
            int counted = 0;
            foreach (GhostVertex a in v) if (a.R + a.G + a.B > 0) counted++;
            TestAssert.Eq(lit, counted, "Holo colours: the fill counts the vertices that carry light");
        }

        // ------------------------------------------------------------------ the camera

        private static void TestMatrices()
        {
            var view = new float[HoloView.Elements];
            var proj = new float[HoloView.Elements];
            HoloView.View(0f, 20000f, view);
            TestAssert.Near(view[0], 1f, "Holo view: north up leaves east on the right");
            TestAssert.Near(view[10] + (float)Math.Cos(50.0 * Math.PI / 180.0), 0f, "Holo view: depth grows with north by the tilt's cosine");
            TestAssert.Near(view[11] / 20000f, -5.5f, "Holo view: the camera sits five and a half radii away");
            HoloView.Projection(20000f, proj);
            float focal = (float)(1.0 / Math.Tan(12.0 * Math.PI / 180.0));
            TestAssert.Near(proj[0], focal, "Holo view: a 24 degree lens");
            TestAssert.Near(proj[5], focal, "Holo view: ... square");
            TestAssert.Near(proj[14], -1f, "Holo view: a perspective divide by depth");

            // The matrices and the mark projection must agree for any point and heading: they are the same picture.
            foreach (float heading in new[] { 0f, 37f, 90f, 215f, 359f })
            foreach (float radius in new[] { 10000f, 40000f })
            {
                HoloView.View(heading, radius, view);
                HoloView.Projection(radius, proj);
                float[,] points = { { 0f, 0f, 0f }, { 6000f, 0f, -3000f }, { -9000f, 1500f, 8000f }, { 2000f, 3000f, 9500f } };
                for (int p = 0; p < points.GetLength(0); p++)
                {
                    float e = points[p, 0], u = points[p, 1], n = points[p, 2];
                    float vx = view[0] * e + view[1] * u + view[2] * n + view[3];
                    float vy = view[4] * e + view[5] * u + view[6] * n + view[7];
                    float vz = view[8] * e + view[9] * u + view[10] * n + view[11];
                    float cx = proj[0] * vx, cy = proj[5] * vy, cw = proj[14] * vz;
                    bool shown = HoloView.Project(heading, radius, e, u, n, out float x, out float y);
                    TestAssert.That(shown && Math.Abs(cx / cw - x) < 1e-4f && Math.Abs(cy / cw - y) < 1e-4f,
                        "Holo view: the render's matrices and the marks' projection put a point in the same place (heading " + heading + ")");
                }
            }
        }

        private static void TestProjection()
        {
            float focal = (float)(1.0 / Math.Tan(12.0 * Math.PI / 180.0));
            float sin = (float)Math.Sin(50.0 * Math.PI / 180.0), cos = (float)Math.Cos(50.0 * Math.PI / 180.0);
            TestAssert.That(HoloView.Project(0f, 20000f, 0f, 0f, 0f, out float x, out float y) && Math.Abs(x) < 1e-6f && Math.Abs(y) < 1e-6f,
                "Holo projection: the aircraft's own spot is the middle of the picture");

            HoloView.Project(0f, 20000f, 10000f, 0f, 0f, out x, out y);
            TestAssert.Near(x, focal * 10000f / 110000f, "Holo projection: 10 km east of the aircraft, north up, is right of centre by f * 0.1");
            TestAssert.Near(y, 0f, "Holo projection: ... on the middle line");

            HoloView.Project(0f, 20000f, 0f, 0f, 20000f, out x, out y);
            TestAssert.Near(x, 0f, "Holo projection: the far rim is straight above the middle");
            TestAssert.Near(y, focal * (20000f * sin) / (110000f + 20000f * cos), "Holo projection: ... higher on the picture, and smaller for being far");
            HoloView.Project(0f, 20000f, 0f, 0f, -20000f, out x, out y);
            TestAssert.Near(y, focal * (-20000f * sin) / (110000f - 20000f * cos), "Holo projection: the near rim is lower, and larger");

            HoloView.Project(90f, 20000f, 10000f, 0f, 0f, out x, out y);
            TestAssert.Near(x, 0f, "Holo projection: heading up, ground dead ahead is straight above the middle");
            TestAssert.That(y > 0f, "Holo projection: ... above it");
            HoloView.Project(90f, 20000f, 0f, 0f, 10000f, out x, out y);
            TestAssert.That(x < 0f && Math.Abs(y) < 1e-5f, "Holo projection: heading east, north is on the left");

            HoloView.Project(0f, 20000f, 0f, 1000f, 0f, out x, out y);
            TestAssert.Near(x, 0f, "Holo projection: a height is straight up");
            TestAssert.Near(y, focal * (2500f * cos) / (110000f - 2500f * sin), "Holo projection: ... stretched 2.5 times, and nearer for being up");

            TestAssert.That(!HoloView.Project(0f, 20000f, 0f, 0f, -200000f, out _, out _), "Holo projection: a point behind the camera is not shown");

            TestAssert.Near(HoloView.MarkHeight(1000f, 20000f), 1000f, "Holo marks: a low aircraft stands at its height");
            TestAssert.Near(HoloView.MarkHeight(10000f, 20000f), 3600f, "Holo marks: a high one is capped at 45% of the radius on the table (3.6 km real)");
            TestAssert.Near(HoloView.MarkHeight(-50f, 20000f), 0f, "Holo marks: nothing stands below the sea");
        }

        private static void TestRings()
        {
            var east = new float[65];
            var north = new float[65];
            HoloView.Ring(10000f, 64, east, north);
            TestAssert.Near(east[0], 0f, "Holo ring: the first point is due north");
            TestAssert.Near(north[0], 10000f, "Holo ring: ... at the radius");
            TestAssert.Near(east[16], 10000f, "Holo ring: a quarter round is due east");
            TestAssert.Near(north[16], 0f, "Holo ring: ... on the same latitude as the aircraft");
            TestAssert.Near(north[32], -10000f, "Holo ring: half round is due south");
            TestAssert.That(east[64] == east[0] && north[64] == north[0], "Holo ring: the last point closes the ring exactly");
            for (int i = 0; i <= 64; i++)
                TestAssert.That(Math.Abs(Math.Sqrt(east[i] * east[i] + north[i] * north[i]) - 10000.0) < .05, "Holo ring: every point is on the circle");
        }

        private static void TestDiscBox()
        {
            HoloView.DiscBox(out float x0, out float x1, out float y0, out float y1);
            TestAssert.Near(x0, -x1, "Holo box: the box is as wide to the left as to the right of the aircraft");
            TestAssert.That(x1 > .5f && x1 < 1f, "Holo box: the disc fills most of the picture's width but stays inside it (got " + x1 + ")");
            TestAssert.That(y0 > -1f && y1 < 1f && y1 > y0, "Holo box: ... and stays inside it vertically");
            TestAssert.That(y1 - y0 < x1 - x0, "Holo box: the tilted disc is wider than it is tall, so the frame no longer encloses an empty square");
            // The same rectangle holds the projected ring and the north tick's circle at any heading and radius.
            foreach (float radius in new[] { 5000f, 20000f, 40000f })
            for (float heading = 0f; heading < 360f; heading += 30f)
            for (int a = 0; a < 72; a++)
            {
                float angle = a * 5f * (float)Math.PI / 180f;
                foreach (float reach in new[] { 1f, HoloView.NorthTickRadii })
                {
                    HoloView.Project(heading, radius, (float)Math.Sin(angle) * radius * reach, 0f, (float)Math.Cos(angle) * radius * reach, out float x, out float y);
                    TestAssert.That(x >= x0 - .002f && x <= x1 + .002f && y >= y0 - .002f && y <= y1 + .002f,
                        "Holo box: the ring and the north tick are inside the box (heading " + heading + ", radius " + radius + ")");
                }
            }
            HoloView.Project(0f, 1f, 0f, 0f, HoloView.NorthTickRadii, out _, out float farTop);
            HoloView.Project(0f, 1f, 0f, 0f, -HoloView.NorthTickRadii, out _, out float nearBottom);
            TestAssert.Near(y1, farTop, "Holo box: its top is the north tick's far point");
            TestAssert.Near(y0, nearBottom, "Holo box: its bottom is the near point of the same circle");
        }

        private static void TestHoloFits()
        {
            // Every rim point at the sea, and a peak 4 km high at the rim, stays inside the square at every heading and range.
            foreach (float radius in new[] { 10000f, 20000f, 40000f })
            for (float heading = 0f; heading < 360f; heading += 15f)
            for (int a = 0; a < 72; a++)
            {
                float angle = a * 5f * (float)Math.PI / 180f;
                float e = (float)Math.Sin(angle) * radius, n = (float)Math.Cos(angle) * radius;
                foreach (float h in new[] { 0f, 2000f, 4000f })
                {
                    bool shown = HoloView.Project(heading, radius, e, HoloView.MarkHeight(h, radius), n, out float x, out float y);
                    TestAssert.That(shown && Math.Abs(x) < 1f && Math.Abs(y) < 1f,
                        "Holo fit: the disc stays inside the picture (radius " + radius + ", heading " + heading + ", got " + x + ", " + y + ")");
                }
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

            TestAssert.Eq(HoloMarks.ShelfFlag(20, false), "20 KM  HDG UP", "Holo words: the shelf's right end, heading up");
            TestAssert.Eq(HoloMarks.ShelfFlag(10, true), "10 KM  N UP", "Holo words: ... and north up");
            TestAssert.Eq(HoloMarks.Caption, "HOLO MAP", "Holo words: the caption");

            TestAssert.Eq(HoloMarks.AirbaseLabel("Kodiak Island Naval Base"), "KODIAK ISLAND", "Holo words: an airbase name is upper case and clipped to 14 characters");
            TestAssert.Eq(HoloMarks.AirbaseLabel("  Alpha\tBase \n"), "ALPHA BASE", "Holo words: whitespace and control characters are tidied");
            TestAssert.Eq(HoloMarks.AirbaseLabel(null), "", "Holo words: no name is no label");
            TestAssert.Eq(HoloMarks.TargetLabel("Bearcat", 12400f), "BEARCAT  12 KM", "Holo words: the selected target's name and range");
            TestAssert.Eq(HoloMarks.TargetLabel(null, 850f), "850 M", "Holo words: ... or the range alone");
            TestAssert.Eq(HoloMarks.TargetLabel("A very long target name indeed", 3500f), "A VERY LONG TA  3.5 KM", "Holo words: a long name is clipped");
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
        }

        private static void TestRanges()
        {
            TestAssert.Eq(HoloPresentation.MiniRange(5), 5, "Tac map: the MINI reaches 5 km");
            TestAssert.Eq(HoloPresentation.MiniRange(10), 10, "Tac map: ... 10 km");
            TestAssert.Eq(HoloPresentation.MiniRange(20), 20, "Tac map: ... or 20 km");
            foreach (int bad in new[] { 0, 7, 15, 40, -5 })
                TestAssert.Eq(HoloPresentation.MiniRange(bad), 10, "Tac map: any other saved range is the default, 10 km");
            TestAssert.Eq(HoloPresentation.HoloRange(10), 10, "Tac map: the held holo reaches 10 km");
            TestAssert.Eq(HoloPresentation.HoloRange(40), 40, "Tac map: ... or 40 km");
            TestAssert.Eq(HoloPresentation.HoloRange(5), 20, "Tac map: ... anything else is the default, 20 km");

            TestAssert.Eq(HoloPresentation.BigRange(20, 10), 20, "Tac map: the big radius is the HOLO RANGE");
            TestAssert.Eq(HoloPresentation.BigRange(10, 5), 10, "Tac map: ... 10 over a 5 km MINI");
            TestAssert.Eq(HoloPresentation.BigRange(10, 10), 10, "Tac map: ... equal to the MINI's is allowed");
            TestAssert.Eq(HoloPresentation.BigRange(10, 20), 20, "Tac map: ... but never less than the MINI's, a hologram that grows does not look at less ground");
            TestAssert.Eq(HoloPresentation.BigRange(40, 20), 40, "Tac map: ... 40 km over a 20 km MINI");
            TestAssert.Eq(HoloPresentation.BigRange(0, 0), 20, "Tac map: unusable saved ranges give the default pair, 20 over 10");

            // Every MINI range fills the holo mesh's 64 cells exactly across its diameter.
            foreach (int km in new[] { 5, 10, 20 })
                TestAssert.Near(HoloGrid.CellMetres(km) * TerrainGhostGrid.Cells, km * 2000f, "Tac map: the MINI's grid spans the picture's diameter exactly (" + km + " km)");
        }

        private static void TestSizes()
        {
            // 1080p: 300 units, 16 in from the edges; the held one 46 % of the height and 24 in.
            TestAssert.Near(HoloPresentation.Side(0f, 1080f), 300f, "Tac map: the MINI is a square of 300 units");
            TestAssert.Near(HoloPresentation.Margin(0f, 1080f), 16f, "Tac map: ... 16 units from the left and bottom edges");
            TestAssert.Near(HoloPresentation.Side(1f, 1080f), 497f, "Tac map: the BIG one is 46 % of the screen height");
            TestAssert.Near(HoloPresentation.Margin(1f, 1080f), 24f, "Tac map: ... 24 units in");
            TestAssert.Near(HoloPresentation.Side(0f, 2160f), 600f, "Tac map: the MINI scales with the screen height (4K)");
            TestAssert.Near(HoloPresentation.Margin(0f, 2160f), 32f, "Tac map: ... and so does its margin");
            TestAssert.Near(HoloPresentation.Side(0f, 720f), 200f, "Tac map: ... and 720p");
            float last = 0f;
            for (float t = 0f; t <= 1.0001f; t += .05f)
            {
                float side = HoloPresentation.Side(t, 1080f);
                TestAssert.That(side >= last && side >= 300f && side <= 497f, "Tac map: the frame grows steadily between the two sizes");
                last = side;
            }
            TestAssert.Near(HoloPresentation.Side(-1f, 1080f), 300f, "Tac map: a size below 0 is the MINI");
            TestAssert.Near(HoloPresentation.Side(3f, 1080f), 497f, "Tac map: ... and above 1 the BIG one");

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

            // The picture's rate and the part of the texture it uses.
            TestAssert.Near(HoloPresentation.RenderInterval(HoloShape.Mini, false), .1f, "Tac map: the MINI is drawn at 10 Hz");
            TestAssert.Near(HoloPresentation.RenderInterval(HoloShape.Big, false), .05f, "Tac map: the BIG one at 20 Hz");
            TestAssert.Near(HoloPresentation.RenderInterval(HoloShape.Mini, true), 0f, "Tac map: ... and every frame while it changes size");
            TestAssert.Eq(HoloPresentation.RenderPixels(300f), 450, "Tac map: a 300 pixel MINI is drawn with 450 pixels of the texture, not a shrunk big picture");
            TestAssert.Eq(HoloPresentation.RenderPixels(497f), 746, "Tac map: ... the BIG one with 746");
            TestAssert.Eq(HoloPresentation.RenderPixels(900f), 768, "Tac map: ... never more than the texture");
            TestAssert.Eq(HoloPresentation.RenderPixels(40f), 256, "Tac map: ... and never fewer than 256");
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

            // Seen through the table: at every heading, bearing and radius the whole wedge, widest lens, stays inside the picture.
            foreach (float radius in new[] { 5000f, 10000f, 20000f })
            for (float heading = 0f; heading < 360f; heading += 30f)
            for (float bearing = 0f; bearing < 360f; bearing += 30f)
            {
                HoloView.Wedge(bearing, 160f, radius * .55f, n, east, north);
                for (int i = 0; i <= n; i++)
                {
                    bool shown = HoloView.Project(heading, radius, east[i], 0f, north[i], out float x, out float y);
                    TestAssert.That(shown && Math.Abs(x) < 1f && Math.Abs(y) < 1f, "Tac map: the wedge stays inside the picture (radius " + radius + ", heading " + heading + ", bearing " + bearing + ")");
                }
            }
        }

        private static void TestMiniMarks()
        {
            TestAssert.Eq(HoloPresentation.UnitCap(HoloShape.Mini), 48, "Tac map: the MINI carries at most 48 units");
            TestAssert.Eq(HoloPresentation.UnitCap(HoloShape.Big), 96, "Tac map: ... the BIG one 96");
            TestAssert.Eq(HoloPresentation.AirbaseCap(HoloShape.Mini), 1, "Tac map: the MINI names the nearest airbase only");
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

            // Airbases: of three in range the MINI keeps the nearest.
            var bases = new HoloMark[HoloMarks.MaximumAirbases];
            int baseCount = 0;
            foreach (float range in new[] { 8000f, 3000f, 5000f })
                HoloMarks.Offer(bases, ref baseCount, new HoloMark { Range = range, Id = (int)range }, HoloPresentation.AirbaseCap(HoloShape.Mini));
            TestAssert.That(baseCount == 1 && bases[0].Id == 3000, "Tac map: the MINI names the nearest of the airbases in range");

            TestAssert.Eq(HoloMarks.MiniCaption, "TAC MAP", "Tac map: the MINI's caption");
            TestAssert.Eq(HoloMarks.MiniFlag(10), "10 KM", "Tac map: the radius at the shelf's right end");
            TestAssert.Eq(HoloMarks.MiniFlag(5), "5 KM", "Tac map: ... 5 km");
            TestAssert.Eq(HoloMarks.MiniFlag(20), "20 KM", "Tac map: ... 20 km");
            TestAssert.Eq(HoloMarks.Range(HoloGrid.Radius(5)), "5 KM", "Tac map: the ring's range reads the same way");
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
