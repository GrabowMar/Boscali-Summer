using System;
using System.Reflection;
using BoscaliSummer.Features.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>
    /// The spatial storm model: the deterministic cell field, the influence and warning
    /// ladders, and every string the storm half of the panel prints.
    ///
    /// <para>These are the parts a screenshot cannot check: that the same seed puts the same
    /// storms in the same places forever, that advection follows the front wind, that a cell
    /// is born and dies rather than popping, and that an unreadable range reads as a dash
    /// rather than as zero.</para>
    /// </summary>
    internal static class StormTests
    {
        public static void Run()
        {
            FillIsBoundedAndDeterministic();
            CellsAreSpatiallyBoundedAndMoveWithWind();
            CellsFormAndDieWithTheFront();
            InfluenceAndWarningsEscalateWithProximity();
            StrongestAndWorstPickTheRightCell();
            StormReadoutNeverInventsPrecision();
            SnapshotCarriesTheStormPicture();
            StormDomainStaysFreeOfUnityTypes();
        }

        private static void FillIsBoundedAndDeterministic()
        {
            int seed = WeatherModel.Seed("Boscali Summer");
            var cells = new StormCell[StormField.MaxCells];
            var again = new StormCell[StormField.MaxCells];

            int first = StormField.Fill(cells, seed, 400f, 40000f, 0.95f, 90f, 12f);
            int second = StormField.Fill(again, seed, 400f, 40000f, 0.95f, 90f, 12f);
            TestAssert.That(first == second, "the same storm seed fills the same number of cells");
            TestAssert.That(first > 0, "a severe front raises at least one cell");
            for (int i = 0; i < first; i++)
            {
                TestAssert.That(cells[i].X == again[i].X && cells[i].Z == again[i].Z,
                    "the same storm seed puts the same cell in the same place");
                TestAssert.That(cells[i].Intensity == again[i].Intensity,
                    "the same storm seed gives the same cell the same intensity");
                TestAssert.That(cells[i].Kind == again[i].Kind,
                    "the same storm seed gives the same cell the same kind");
            }

            TestAssert.That(StormField.Fill(null, seed, 400f, 40000f, 0.95f, 90f, 12f) == 0,
                "a null destination is not a crash");
            foreach (float mapSize in new[] { 0f, -1f, 500f, float.NaN })
            {
                TestAssert.That(StormField.Fill(cells, seed, 400f, mapSize, 0.95f, 90f, 12f) == 0,
                    "a map below the 1000 m floor raises no storms");
            }
            TestAssert.That(StormField.Fill(cells, seed, -50f, 40000f, 0.95f, 90f, 12f) >= 0,
                "a negative mission time does not throw");
            TestAssert.That(StormField.Fill(cells, seed, float.NaN, 40000f, 0.95f, 90f, 12f) >= 0,
                "an unreadable mission time does not throw");

            int[] seeds = { seed, 0, 12345 };
            foreach (int other in seeds)
            {
                for (float t = 0f; t <= 4000f; t += 37f)
                {
                    int count = StormField.Fill(cells, other, t, 40000f, 0.95f, 90f, 12f);
                    TestAssert.That(count <= StormField.MaxCells,
                        "the field never raises more than MaxCells storms at t=" + t.ToString("0"));
                }
            }
        }

        private static void CellsAreSpatiallyBoundedAndMoveWithWind()
        {
            const float mapSize = 40000f;
            const float windSpeed = 30f;
            int[] seeds = { WeatherModel.Seed("Boscali Summer"), 0, 12345 };
            var cells = new StormCell[StormField.MaxCells];

            foreach (int seed in seeds)
            {
                for (float t = 0f; t <= 4000f; t += 37f)
                {
                    int count = StormField.Fill(cells, seed, t, mapSize, 0.95f, 90f, windSpeed);
                    for (int i = 0; i < count; i++)
                    {
                        StormCell cell = cells[i];
                        TestAssert.That(!float.IsNaN(cell.X) && !float.IsInfinity(cell.X) &&
                                        !float.IsNaN(cell.Z) && !float.IsInfinity(cell.Z),
                            "a storm cell is always in a real place");
                        TestAssert.That(Math.Abs(cell.X) <= mapSize && Math.Abs(cell.Z) <= mapSize,
                            "the spawn ring plus advection cannot leave the map");
                        TestAssert.That(cell.Radius > 0f, "a storm cell has a real radius");
                        TestAssert.That(cell.TopHeight > cell.CloudBase,
                            "a cell's tower top stays above its cloud base (t=" + t.ToString("0") +
                            ", intensity=" + cell.Intensity.ToString("0.000") +
                            ", base=" + cell.CloudBase.ToString("0") +
                            ", top=" + cell.TopHeight.ToString("0") + ")");
                        TestAssert.That(cell.Intensity > 0f && cell.Intensity <= 1f,
                            "a live cell has an intensity inside 0..1");
                        TestAssert.That(cell.Kind == StormKind.Cumulus ||
                                        cell.Kind == StormKind.ToweringCumulus ||
                                        cell.Kind == StormKind.Supercell,
                            "a cell is always one of the defined kinds");
                    }
                }
            }

            int seed0 = WeatherModel.Seed("Boscali Summer");
            var calm = new StormCell[StormField.MaxCells];
            var blown = new StormCell[StormField.MaxCells];
            int calmCount = StormField.Fill(calm, seed0, 400f, mapSize, 0.95f, 90f, 0f);
            int blownCount = StormField.Fill(blown, seed0, 400f, mapSize, 0.95f, 90f, windSpeed);
            TestAssert.That(calmCount == blownCount && calmCount > 0,
                "wind changes where the cells are, not which slot is alive");

            float windX = (float)Math.Sin(90.0 * Math.PI / 180.0);
            float windZ = (float)Math.Cos(90.0 * Math.PI / 180.0);
            for (int i = 0; i < calmCount; i++)
            {
                float dx = blown[i].X - calm[i].X;
                float dz = blown[i].Z - calm[i].Z;
                TestAssert.That(dx != 0f || dz != 0f,
                    "a front wind pushes the same slot's cell to a different place");
                TestAssert.That(dx * windX + dz * windZ > 0f,
                    "every cell under wind has drifted with the wind");
            }
        }

        private static void CellsFormAndDieWithTheFront()
        {
            var cells = new StormCell[StormField.MaxCells];
            int seed = 12345;

            TestAssert.That(StormField.Fill(cells, seed, 400f, 40000f, 0f, 90f, 10f) == 0,
                "a clear front raises no storms");
            TestAssert.That(StormField.Fill(cells, seed, 400f, 40000f, 0.05f, 90f, 10f) == 0,
                "a light front still raises no storms");
            TestAssert.That(StormField.Fill(cells, seed, 400f, 40000f, 0.95f, 90f, 10f) > 0,
                "a severe front raises storms");

            // Slot zero is born at mission time zero, so one period is exactly one life: the
            // intensity must grow out of nothing, hold a peak and decay back to nothing. If
            // the envelope ever inverts, the sky pops instead of building.
            float peak = 0f;
            float first = -1f;
            float last = -1f;
            for (float t = 0f; t < StormField.Period; t += 5f)
            {
                int count = StormField.Fill(cells, seed, t, 40000f, 0.95f, 90f, 10f);
                float intensity = 0f;
                for (int i = 0; i < count; i++)
                {
                    if (cells[i].Slot == 0) intensity = cells[i].Intensity;
                }
                if (intensity > peak) peak = intensity;
                if (first < 0f) first = intensity;
                last = intensity;
            }

            TestAssert.That(peak > 0.4f, "a storm cell has to grow into something");
            TestAssert.That(first < 0.2f * peak && last < 0.2f * peak,
                "a storm cell is born and dies near nothing, not at full strength");
        }

        private static void InfluenceAndWarningsEscalateWithProximity()
        {
            var cell = new StormCell(0, StormKind.Supercell, 0f, 0f, 5000f, 1f, 100f, 720f,
                900f, 8000f, 0f, 0f);

            TestAssert.That(Near(cell.InfluenceAt(0f, 0f), 1f),
                "standing in the eye of a full-strength cell is full influence");

            float previous = float.MaxValue;
            foreach (float distance in new[] { 0f, 1000f, 2500f, 4900f, 5000f })
            {
                float influence = cell.InfluenceAt(0f, distance);
                TestAssert.That(influence < previous,
                    "influence strictly decreases on the way out of a storm");
                previous = influence;
            }
            TestAssert.That(cell.InfluenceAt(0f, 5000f) == 0f && cell.InfluenceAt(0f, 9000f) == 0f,
                "a storm has no influence at or beyond its radius");

            var spent = new StormCell(0, StormKind.Supercell, 0f, 0f, 5000f, 0f, 100f, 720f,
                900f, 8000f, 0f, 0f);
            TestAssert.That(spent.InfluenceAt(0f, 0f) == 0f,
                "a cell with no intensity owns no sky");

            var point = new StormCell(0, StormKind.Supercell, 0f, 0f, 0f, 1f, 100f, 720f,
                900f, 8000f, 0f, 0f);
            TestAssert.That(point.InfluenceAt(0f, 0f) == 0f,
                "a zero-radius cell cannot divide by its radius");

            TestAssert.That(cell.WarningAt(0f, 0f) == StormWarning.Warning,
                "point blank inside the rain is a warning");
            TestAssert.That(cell.WarningAt(0f, 5000f * 1.2f) == StormWarning.Watch,
                "the watch ring sits outside the rain");
            TestAssert.That(cell.WarningAt(0f, 5000f * 3f) == StormWarning.Advisory,
                "the advisory ring is the outer forecast horizon");
            TestAssert.That(cell.WarningAt(0f, 5000f * 5f) == StormWarning.None,
                "far outside every ring there is nothing to warn about");

            var whisper = new StormCell(0, StormKind.Supercell, 0f, 0f, 5000f,
                StormField.MinWarningIntensity * 0.5f, 100f, 720f, 900f, 8000f, 0f, 0f);
            TestAssert.That(whisper.WarningAt(0f, 0f) == StormWarning.None,
                "a cell below the warning floor raises nothing even at point blank");

            int previousTier = (int)StormWarning.Warning;
            foreach (float distance in new[] { 0f, 1000f, 2500f, 4000f, 5000f, 7000f, 9000f, 15000f, 25000f })
            {
                int tier = (int)cell.WarningAt(0f, distance);
                TestAssert.That(tier <= previousTier,
                    "a warning tier never escalates as the distance grows");
                previousTier = tier;
            }
        }

        private static void StrongestAndWorstPickTheRightCell()
        {
            var nearAndWeak = new StormCell(0, StormKind.Cumulus, 0f, 2500f, 1000f, 0.2f, 100f,
                720f, 900f, 2000f, 0f, 0f);
            var farAndStrong = new StormCell(1, StormKind.Supercell, 0f, 4500f, 8000f, 1f, 100f,
                720f, 900f, 8000f, 0f, 0f);
            var cells = new[] { nearAndWeak, farAndStrong };

            TestAssert.That(nearAndWeak.WarningAt(0f, 0f) == StormWarning.Advisory,
                "the nearer cell is only an advisory");
            TestAssert.That(farAndStrong.WarningAt(0f, 0f) == StormWarning.Warning,
                "the farther cell is the real warning");

            StormCell strongest = StormField.StrongestAt(cells, cells.Length, 0f, 0f, out float influence);
            TestAssert.That(strongest.Slot == farAndStrong.Slot,
                "the strongest cell at a point wins on influence, not on distance");
            TestAssert.That(influence == farAndStrong.InfluenceAt(0f, 0f),
                "StrongestAt returns exactly the winning cell's influence");

            StormWarning warning = StormField.WarningAt(cells, cells.Length, 0f, 0f, out StormCell source);
            TestAssert.That(warning == StormWarning.Warning,
                "the highest warning tier wins, not the nearest cell's");
            TestAssert.That(source.Slot == farAndStrong.Slot,
                "the warning source is the cell that imposed the tier");

            StormCell none = StormField.StrongestAt(cells, 0, 0f, 0f, out float noInfluence);
            TestAssert.That(noInfluence == 0f && none.Intensity == 0f,
                "no cells means no influence and a default source");
            TestAssert.That(StormField.WarningAt(cells, 0, 0f, 0f, out StormCell noSource) == StormWarning.None &&
                            noSource.Intensity == 0f,
                "no cells means no warning and a default source");

            StormCell nullCell = StormField.StrongestAt(null, 0, 0f, 0f, out float nullInfluence);
            TestAssert.That(nullInfluence == 0f && nullCell.Intensity == 0f && nullCell.Radius == 0f,
                "a null field is no influence and a default source, not a crash");
            TestAssert.That(StormField.WarningAt(null, 0, 0f, 0f, out _) == StormWarning.None,
                "a null field is no warning, not a crash");
        }

        private static void StormReadoutNeverInventsPrecision()
        {
            for (int i = 0; i <= (int)StormKind.Supercell; i++)
            {
                TestAssert.That(!string.IsNullOrEmpty(StormReadout.Kind((StormKind)i)),
                    "every defined storm kind has a name");
            }
            for (int i = 0; i <= (int)StormWarning.Warning; i++)
            {
                TestAssert.That(!string.IsNullOrEmpty(StormReadout.Warning((StormWarning)i)),
                    "every defined warning tier has a name");
            }
            TestAssert.That(StormReadout.Kind((StormKind)99) == StormReadout.Kind(StormKind.Supercell) &&
                            StormReadout.Kind((StormKind)(-5)) == StormReadout.Kind(StormKind.Cumulus),
                "an out-of-range storm kind clamps to a defined label");
            TestAssert.That(StormReadout.Warning((StormWarning)99) == StormReadout.Warning(StormWarning.Warning) &&
                            StormReadout.Warning((StormWarning)(-5)) == StormReadout.Warning(StormWarning.None),
                "an out-of-range warning tier clamps to a defined label");

            TestAssert.That(StormReadout.NauticalMiles(float.NaN) == WeatherReadout.Unknown &&
                            StormReadout.NauticalMiles(-1f) == WeatherReadout.Unknown,
                "an unreadable storm range is a dash, not a confident zero");
            TestAssert.That(StormReadout.NauticalMiles(1852f) == "1.0 NM",
                "1852 metres reads as one nautical mile");

            TestAssert.That(StormReadout.BearingTo(float.NaN, 0f, 0f, 0f) == WeatherReadout.Unknown,
                "an unreadable bearing is a dash");
            TestAssert.That(StormReadout.BearingTo(0f, 0f, 0f, 100f) == "N 0°",
                "a cell to the north reads as north zero degrees");
            TestAssert.That(StormReadout.BearingTo(0f, 0f, 100f, 0f) == "E 90°",
                "a cell to the east reads as east ninety degrees");
            TestAssert.That(StormReadout.BearingTo(0f, 0f, 0f, -100f) == "S 180°",
                "a cell to the south reads as south one eighty");
            TestAssert.That(StormReadout.BearingTo(0f, 0f, -100f, 0f) == "W 270°",
                "a cell to the west reads as west two seventy");

            TestAssert.That(StormReadout.HazardLine(StormWarning.None, "x", 0f, "N") == "NO STORM IN RANGE",
                "a clear sky says so instead of printing an empty hazard line");
            TestAssert.That(StormReadout.HazardLine(StormWarning.Warning, null, 0f, "N") == WeatherReadout.Unknown &&
                            StormReadout.HazardLine(StormWarning.Warning, "", 0f, "N") == WeatherReadout.Unknown,
                "a hazard line without a kind degrades to a dash");
            TestAssert.That(StormReadout.HazardLine(StormWarning.Warning, "SUPERCELL", 1852f, null) == WeatherReadout.Unknown &&
                            StormReadout.HazardLine(StormWarning.Warning, "SUPERCELL", 1852f, "") == WeatherReadout.Unknown,
                "a hazard line without a bearing degrades to a dash");

            for (int i = -1; i <= 4; i++)
            {
                string rail = StormReadout.RailClass((StormWarning)i);
                TestAssert.That(rail == "rail danger" || rail == "rail contested" ||
                                rail == "rail info" || rail == "rail inert",
                    "every warning tier gets one of the four documented rail classes");
                string chip = StormReadout.ChipClass((StormWarning)i);
                TestAssert.That(chip == "danger" || chip == "warn" || chip == "info" || chip == "inert",
                    "every warning tier gets one of the four documented chip classes");
            }
        }

        private static void SnapshotCarriesTheStormPicture()
        {
            var live = new WeatherState(WeatherRegime.Storm, 0.9f, 700f, 20f, 180f, 0.8f);
            var empty = new WeatherSnapshot(true, 600f, live, live, 0f, 0f, 0f, 0f, 1f,
                false, true, null, 0, 0f, StormWarning.None, default);

            TestAssert.That(empty.RainIntensity == 0f && empty.CellCount == 0,
                "a sky with no cells has no rain");
            TestAssert.That(empty.LocalWindSpeed == 0f && !float.IsNaN(empty.LocalWindHeading),
                "an empty storm picture still computes without throwing");

            var drenched = new WeatherSnapshot(true, 600f, live, live, 0f, 0f, 0f, 0f, 1f,
                false, true, new StormCell[StormField.MaxCells], 1, 0.5f, StormWarning.Warning, default);
            TestAssert.That(drenched.RainIntensity == 0.5f, "half influence is half rain");

            var flood = new WeatherSnapshot(true, 600f, live, live, 0f, 0f, 0f, 0f, 1f,
                false, true, new StormCell[StormField.MaxCells], 1, 2f, StormWarning.Warning, default);
            TestAssert.That(flood.RainIntensity == 1f,
                "rain clamps to full rather than overflowing the scale");

            var broken = new WeatherSnapshot(true, 600f, live, live, 0f, 0f, 0f, 0f, 1f,
                false, true, new StormCell[StormField.MaxCells], 1, float.NaN, StormWarning.None, default);
            TestAssert.That(broken.RainIntensity == 0f && !float.IsNaN(broken.RainIntensity),
                "an unreadable influence reads as no rain, never as NaN");
        }

        private static void StormDomainStaysFreeOfUnityTypes()
        {
            const string domainNamespace = "BoscaliSummer.Features.Weather.Domain";
            int inspected = 0;
            foreach (Type type in typeof(WeatherModel).Assembly.GetTypes())
            {
                if (type.Namespace == null ||
                    !type.Namespace.StartsWith(domainNamespace, StringComparison.Ordinal))
                {
                    continue;
                }
                foreach (FieldInfo field in type.GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    inspected++;
                    TestAssert.That(!IsUnityType(field.FieldType),
                        "storm domain field " + type.Name + "." + field.Name + " must not be a Unity type");
                }
                foreach (PropertyInfo property in type.GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    inspected++;
                    TestAssert.That(!IsUnityType(property.PropertyType),
                        "storm domain property " + type.Name + "." + property.Name + " must not be a Unity type");
                }
            }
            TestAssert.That(inspected > 0, "the storm domain's members were actually inspected");
        }

        private static bool IsUnityType(Type type)
        {
            Type owner = type.IsArray ? type.GetElementType() : type;
            string ns = owner != null ? owner.Namespace : null;
            return ns != null && ns.StartsWith("UnityEngine", StringComparison.Ordinal);
        }

        private static bool Near(float a, float b) => Math.Abs(a - b) <= 1e-4f;
    }
}
