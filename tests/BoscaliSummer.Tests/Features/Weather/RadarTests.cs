using System;
using BoscaliSummer.Features.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>
    /// The synthetic reflectivity picture: the pure field the WEA radar paints its texture from.
    ///
    /// <para>These are the parts a screenshot cannot check — that a squall line reads as a band
    /// across the boundary rather than as a row of dots, that the picture is a closed-form function
    /// of the same storm field every peer derives, that the grid is chart-oriented rather than
    /// mirrored, and that a scrubbed forecast is another instant of one schedule rather than a
    /// mutation of the live field.</para>
    /// </summary>
    internal static class RadarTests
    {
        public static void Run()
        {
            EmptySkyPaintsNothing();
            CellCoreReadsHotterThanItsEdge();
            TallerCellsPaintHotter();
            SquallLinePaintsABandAcrossTheFront();
            TheGridIsDeterministicAndChartOriented();
            ResolutionIsCappedAndDegenerateGridsAreRefused();
            DegenerateInputsClampInsteadOfThrowing();
            TheFutureIsDeterministicAndLeavesTheLiveFieldAlone();
        }

        private static void EmptySkyPaintsNothing()
        {
            var image = new RadarImage();
            image.Resize(32, 16);
            image.Sample(null, 0, WeatherFront.None, 40000f, 20000f);

            TestAssert.That(AllZero(image), "no cells and no front is a blank picture");
        }

        private static void CellCoreReadsHotterThanItsEdge()
        {
            var cell = new StormCell(0, StormKind.Supercell, 0f, 0f, 5000f, 0.9f, 100f, 720f, 900f, 9000f, 0f, 0f);
            var cells = new[] { cell };

            float core = RadarImage.ReflectivityAt(cells, 1, WeatherFront.None, 0f, 0f);
            float edge = RadarImage.ReflectivityAt(cells, 1, WeatherFront.None, 4500f, 0f);
            TestAssert.That(core > edge, "a cell's core paints harder than its edge");
            TestAssert.That(core > RadarImage.NoEcho, "a strong cell's core is a real echo");
        }

        private static void TallerCellsPaintHotter()
        {
            // Same place, same influence, same intensity: only kind and tower height differ, so the
            // picture has to separate a supercell's core from fair-weather cumulus.
            var cumulus = new StormCell(0, StormKind.Cumulus, 0f, 0f, 5000f, 0.6f, 100f, 720f, 900f, 2500f, 0f, 0f);
            var supercell = new StormCell(0, StormKind.Supercell, 0f, 0f, 5000f, 0.6f, 100f, 720f, 900f, 9000f, 0f, 0f);

            TestAssert.That(
                RadarImage.ReflectivityAt(new[] { supercell }, 1, WeatherFront.None, 0f, 0f) >
                RadarImage.ReflectivityAt(new[] { cumulus }, 1, WeatherFront.None, 0f, 0f),
                "a supercell paints hotter than a cumulus of the same intensity");
        }

        private static void SquallLinePaintsABandAcrossTheFront()
        {
            const float mapSize = 40000f;
            int seed = WeatherModel.Seed("Boscali Summer");
            var cells = new StormCell[StormField.MaxCells];
            Atmosphere air = UnstableAir(0.85f, 0.80f);
            WeatherFront front = ColdFront(4000f, 0.90f);

            int count = StormField.Fill(cells, seed, 400f, mapSize, 0.95f, 90f, 15f, front, air, out StormMode mode);
            TestAssert.That(mode == StormMode.SquallLine && count > 1,
                "the squall-line context really lays a line along the boundary");

            float on = RadarImage.ReflectivityAt(cells, count, front, front.Position, 0f);
            float off = RadarImage.ReflectivityAt(cells, count, front, front.Position + 3f * front.Width, 0f);
            TestAssert.That(on > 0.5f, "the boundary itself paints a hard echo");
            TestAssert.That(off < RadarImage.NoEcho,
                "well off the boundary the picture is clear — the band is a line, not a wash");
        }

        private static void TheGridIsDeterministicAndChartOriented()
        {
            var first = new RadarImage();
            var again = new RadarImage();
            first.Resize(48, 24);
            again.Resize(48, 24);

            var cell = new StormCell(0, StormKind.Supercell, 15000f, 10000f, 5000f, 1f, 100f, 720f, 900f, 9000f, 0f, 0f);
            var cells = new[] { cell };
            first.Sample(cells, 1, WeatherFront.None, 40000f, 20000f);
            again.Sample(cells, 1, WeatherFront.None, 40000f, 20000f);

            TestAssert.That(SameValues(first.Values, again.Values), "the same field and window paint the same grid");
            TestAssert.That(first.Values[6 * 48 + 33] > 0.5f, "a cell in the north-east lands in the north-east");
            TestAssert.That(first.Values[18 * 48 + 14] < RadarImage.NoEcho, "the opposite corner stays clear");
        }

        private static void ResolutionIsCappedAndDegenerateGridsAreRefused()
        {
            var image = new RadarImage();
            image.Resize(4096, 4096);
            TestAssert.That(image.Width <= RadarImage.MaxResolution && image.Height <= RadarImage.MaxResolution,
                "the grid never exceeds its named resolution cap");

            int capped = image.Width;
            TestAssert.That(!image.Resize(0, 0) && !image.Resize(RadarImage.MinResolution - 1, 16) &&
                            image.Width == capped,
                "a degenerate grid is refused and leaves the previous grid in place");
        }

        private static void DegenerateInputsClampInsteadOfThrowing()
        {
            var loud = new StormCell(0, StormKind.Supercell, 0f, 0f, 5000f, 4f, 100f, 720f, 900f, 9000f, 0f, 0f);
            var loudCells = new[] { loud };
            float clamped = RadarImage.ReflectivityAt(loudCells, 1, WeatherFront.None, 0f, 0f);
            TestAssert.That(clamped >= 0f && clamped <= 1f,
                "a cell that claims more than full intensity is clamped to the scale");

            TestAssert.That(RadarImage.ReflectivityAt(loudCells, 1, WeatherFront.None, float.NaN, float.NaN) == 0f,
                "an unreadable point is clear air, never NaN");

            var broken = new StormCell(
                0, StormKind.Cumulus, float.NaN, float.NaN, float.NaN, float.NaN,
                0f, 0f, float.NaN, float.NaN, float.NaN, float.NaN);
            TestAssert.That(RadarImage.ReflectivityAt(new[] { broken }, 1, WeatherFront.None, 0f, 0f) == 0f,
                "a degenerate cell paints nothing rather than throwing");

            var image = new RadarImage();
            image.Resize(16, 8);
            image.Sample(loudCells, 1, WeatherFront.None, 40000f, 20000f);
            image.Sample(loudCells, 1, WeatherFront.None, 0f, float.NaN);
            TestAssert.That(AllZero(image), "a degenerate window clears the picture instead of leaving the last one");
        }

        private static void TheFutureIsDeterministicAndLeavesTheLiveFieldAlone()
        {
            const float mapSize = 40000f;
            int seed = WeatherModel.Seed("Boscali Summer");
            Atmosphere air = UnstableAir(0.85f, 0.80f);
            WeatherFront liveFront = ColdFront(4000f, 0.90f);

            var liveCells = new StormCell[StormField.MaxCells];
            int liveCount = StormField.Fill(
                liveCells, seed, 400f, mapSize, 0.95f, 90f, 15f, liveFront, air, out _);

            float liveX = liveCells[0].X;
            float liveZ = liveCells[0].Z;
            float liveIntensity = liveCells[0].Intensity;

            var image = new RadarImage();
            image.Resize(32, 16);
            image.Sample(liveCells, liveCount, liveFront, 40000f, 20000f);
            var liveGrid = new float[image.Values.Length];
            Array.Copy(image.Values, liveGrid, liveGrid.Length);

            // +30 minutes: the boundary translated along its own normal, the schedule sampled at the
            // same instant. This is the page's forecast path, minus the page.
            WeatherFront futureFront = ColdFront(
                liveFront.Position + liveFront.Speed * 1800f, liveFront.Activity);
            var futureCells = new StormCell[StormField.MaxCells];
            var futureAgain = new StormCell[StormField.MaxCells];
            int futureCount = StormField.Fill(
                futureCells, seed, 2200f, mapSize, 0.95f, 90f, 15f, futureFront, air, out _);
            int secondCount = StormField.Fill(
                futureAgain, seed, 2200f, mapSize, 0.95f, 90f, 15f, futureFront, air, out _);

            image.Sample(futureCells, futureCount, futureFront, 40000f, 20000f);
            var futureGrid = new float[image.Values.Length];
            Array.Copy(image.Values, futureGrid, futureGrid.Length);

            var check = new RadarImage();
            check.Resize(32, 16);
            check.Sample(futureAgain, secondCount, futureFront, 40000f, 20000f);
            TestAssert.That(SameValues(futureGrid, check.Values),
                "sampling a future offset twice paints the same grid");
            TestAssert.That(!SameValues(liveGrid, futureGrid),
                "a scrubbed forecast is a different picture, not the live one relabelled");

            TestAssert.That(liveCells[0].X == liveX && liveCells[0].Z == liveZ &&
                            liveCells[0].Intensity == liveIntensity,
                "sampling a future offset never mutates the live field");

            image.Sample(liveCells, liveCount, liveFront, 40000f, 20000f);
            TestAssert.That(SameValues(liveGrid, image.Values),
                "the live picture is still the live picture after a forecast was sampled");
        }

        private static bool AllZero(RadarImage image)
        {
            float[] values = image.Values;
            if (values == null) return false;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != 0f) return false;
            }
            return true;
        }

        private static bool SameValues(float[] first, float[] second)
        {
            if (first == null || second == null || first.Length != second.Length) return false;
            for (int i = 0; i < first.Length; i++)
            {
                if (first[i] != second[i]) return false;
            }
            return true;
        }

        private static WeatherFront ColdFront(float position, float activity)
        {
            return new WeatherFront(
                true, FrontKind.Cold, 1f, 0f, position, 15f, FrontKinds.Width(FrontKind.Cold),
                activity, AirMassKind.ContinentalPolar, AirMassKind.MaritimeTropical);
        }

        private static Atmosphere UnstableAir(float cape, float shear)
        {
            return new Atmosphere(
                true, AirMassKind.MaritimeTropical, FrontKind.Cold,
                28f, 24f, 900f, cape, 0.10f, shear, 22f, 0.50f, 9000f, 1500f, 0.40f, 0.60f);
        }
    }
}
