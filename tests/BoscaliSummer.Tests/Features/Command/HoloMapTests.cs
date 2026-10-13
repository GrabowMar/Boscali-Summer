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
            TestHoloFits();
            TestOffer();
            TestWords();
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
