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
            MaxCellsIsEightAndCapacityIsRespected();
            ContextFillIsDeterministicAndSeedSensitive();
            LegacyOverloadKeepsItsOldResults();
            StableAirRaisesNoCells();
            MarginalInstabilityRaisesOneCell();
            ClusterGroupsItsCells();
            SquallLineLiesAlongTheFront();
            FrontProximityDrivesIntensity();
            ForecastCarriesTrendsAndSeverity();
        }

        private static void MaxCellsIsEightAndCapacityIsRespected()
        {
            TestAssert.That(StormField.MaxCells == 8, "the field is sized for eight storm slots");
            TestAssert.That(Near(StormField.SlotStagger, StormField.Period / 8f),
                "eight slots stagger a full period apart");

            int seed = WeatherModel.Seed("Boscali Summer");
            Atmosphere air = UnstableAir(0.90f, 0.80f);
            WeatherFront front = ColdFront(4000f, 0.90f);

            for (int size = 1; size <= 12; size++)
            {
                var cells = new StormCell[size];
                int count = StormField.Fill(
                    cells, seed, 400f, 40000f, 0.95f, 90f, 12f, front, air, out StormMode mode);
                TestAssert.That(count <= size && count <= StormField.MaxCells,
                    "a " + size + "-cell buffer never overflows and mode=" + StormModes.Label(mode));
            }

            var full = new StormCell[StormField.MaxCells];
            int filled = StormField.Fill(full, seed, 400f, 40000f, 0.95f, 90f, 12f, front, air, out _);
            TestAssert.That(filled > 3,
                "the wider field actually populates more than the old three slots");
        }

        private static void ContextFillIsDeterministicAndSeedSensitive()
        {
            int seed = WeatherModel.Seed("Boscali Summer");
            var first = new StormCell[StormField.MaxCells];
            var again = new StormCell[StormField.MaxCells];
            var other = new StormCell[StormField.MaxCells];
            Atmosphere air = UnstableAir(0.85f, 0.80f);
            WeatherFront front = ColdFront(4000f, 0.90f);

            int count = StormField.Fill(first, seed, 400f, 40000f, 0.95f, 90f, 12f, front, air, out StormMode mode);
            int second = StormField.Fill(again, seed, 400f, 40000f, 0.95f, 90f, 12f, front, air, out StormMode againMode);
            TestAssert.That(count == second && mode == againMode,
                "the same context fills the same number of cells in the same mode");
            for (int i = 0; i < count; i++)
            {
                TestAssert.That(first[i].Slot == again[i].Slot &&
                                first[i].X == again[i].X && first[i].Z == again[i].Z &&
                                first[i].Intensity == again[i].Intensity &&
                                first[i].Kind == again[i].Kind && first[i].Radius == again[i].Radius,
                    "the same context puts the same cell in the same place with the same strength");
            }

            int otherCount = StormField.Fill(other, seed + 1, 400f, 40000f, 0.95f, 90f, 12f, front, air, out _);
            bool differs = otherCount != count;
            for (int i = 0; i < otherCount && i < count && !differs; i++)
            {
                differs = other[i].X != first[i].X || other[i].Z != first[i].Z ||
                          other[i].Intensity != first[i].Intensity;
            }
            TestAssert.That(differs, "a different seed puts the population somewhere else");
        }

        private static void LegacyOverloadKeepsItsOldResults()
        {
            int seed = WeatherModel.Seed("Boscali Summer");
            var legacy = new StormCell[StormField.MaxCells];
            var context = new StormCell[StormField.MaxCells];

            int legacyCount = StormField.Fill(legacy, seed, 400f, 40000f, 0.95f, 90f, 12f);
            int contextCount = StormField.Fill(
                context, seed, 400f, 40000f, 0.95f, 90f, 12f,
                WeatherFront.None, Atmosphere.Unavailable, out StormMode mode);
            TestAssert.That(legacyCount == contextCount && legacyCount > 0,
                "the legacy overload delegates to the context overload with default context");
            TestAssert.That(mode == StormMode.Scattered,
                "no front and no atmosphere is the scattered population, never a line");
            for (int i = 0; i < legacyCount; i++)
            {
                TestAssert.That(legacy[i].Slot == context[i].Slot &&
                                legacy[i].X == context[i].X && legacy[i].Z == context[i].Z &&
                                legacy[i].Intensity == context[i].Intensity,
                    "the legacy overload and the defaulted context overload agree cell for cell");
            }

            // Golden values captured from the field before this change for slot zero, whose birth
            // phase the wider population does not move. This is the integration pass's safety net:
            // the legacy call path still puts its first cell in the same place, the same strength.
            AssertLegacySlotZero(seed, 400f, 0.95f, StormKind.Cumulus,
                -10262.552f, 9319.793f, 1670.3689f, 0.3190344f, 1721.4484f, 4247.419f);
            AssertLegacySlotZero(seed, 1000f, 0.95f, StormKind.Supercell,
                -8869.982f, 11945.095f, 6897.9736f, 0.9634037f, 754.8944f, 9652.868f);
            AssertLegacySlotZero(seed, 400f, 0.55f, StormKind.Cumulus,
                -10262.552f, 9319.793f, 1670.3689f, 0.2593752f, 1810.9371f, 4336.9077f);
        }

        private static void AssertLegacySlotZero(
            int seed,
            float missionTime,
            float frontConditions,
            StormKind kind,
            float x,
            float z,
            float radius,
            float intensity,
            float cloudBase,
            float topHeight)
        {
            var cells = new StormCell[StormField.MaxCells];
            int count = StormField.Fill(cells, seed, missionTime, 40000f, frontConditions, 90f, 12f);

            StormCell slotZero = default;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (cells[i].Slot != 0) continue;
                slotZero = cells[i];
                found = true;
            }

            string at = " at t=" + missionTime.ToString("0") + " c=" + frontConditions.ToString("0.00");
            TestAssert.That(found, "the legacy field still raises its first slot" + at);
            TestAssert.That(slotZero.Kind == kind, "the first cell's kind is unchanged" + at);
            TestAssert.That(slotZero.X == x && slotZero.Z == z,
                "the first cell is in the same place as before this change" + at);
            TestAssert.That(slotZero.Radius == radius, "the first cell has the same radius as before" + at);
            TestAssert.That(slotZero.Intensity == intensity,
                "the first cell has the same intensity as before" + at);
            TestAssert.That(slotZero.CloudBase == cloudBase,
                "the first cell has the same cloud base as before" + at);
            TestAssert.That(slotZero.TopHeight == topHeight,
                "the first cell tops out exactly where it did before" + at);
        }

        private static void StableAirRaisesNoCells()
        {
            int seed = WeatherModel.Seed("Boscali Summer");
            var cells = new StormCell[StormField.MaxCells];
            WeatherFront front = ColdFront(0f, 1f);

            var arctic = new Atmosphere(
                true, AirMassKind.ContinentalArctic, FrontKind.Cold,
                -12f, -18f, 900f, 0.05f, 0.80f, 0.15f, 30f, 0f, 20000f, 400f, 0.70f, 0.20f);
            TestAssert.That(
                StormField.Fill(cells, seed, 400f, 40000f, 0.95f, 90f, 12f, front, arctic, out StormMode arcticMode) == 0 &&
                arcticMode == StormMode.None,
                "a stable arctic air mass raises no cells even under a severe front");

            var capped = new Atmosphere(
                true, AirMassKind.MaritimePolar, FrontKind.Cold,
                12f, 8f, 900f, 0.10f, 0.90f, 0.60f, 12f, 0.20f, 8000f, 900f, 0.50f, 0.40f);
            TestAssert.That(
                StormField.Fill(cells, seed, 400f, 40000f, 0.95f, 90f, 12f, front, capped, out StormMode cappedMode) == 0 &&
                cappedMode == StormMode.None,
                "a low-CAPE air mass raises no cells no matter how strong the front");

            TestAssert.That(
                StormField.Fill(cells, seed, 400f, 40000f, 0.95f, 90f, 12f, front, UnstableAir(0.85f, 0.80f), out _) > 0,
                "the same front over unstable air raises cells");
        }

        private static void MarginalInstabilityRaisesOneCell()
        {
            int seed = WeatherModel.Seed("Boscali Summer");
            var cells = new StormCell[StormField.MaxCells];
            var air = new Atmosphere(
                true, AirMassKind.MaritimePolar, FrontKind.Cold,
                14f, 10f, 900f, 0.30f, 0.20f, 0.30f, 10f, 0.10f, 9000f, 1200f, 0.50f, 0.50f);

            int count = StormField.Fill(
                cells, seed, 400f, 40000f, 0.35f, 90f, 10f, ColdFront(4000f, 0.90f), air, out StormMode mode);
            TestAssert.That(mode == StormMode.Isolated, "marginal instability is an isolated cell");
            TestAssert.That(count == 1, "an isolated cell really is alone in the sky");
        }

        private static void ClusterGroupsItsCells()
        {
            const float mapSize = 40000f;
            int seed = WeatherModel.Seed("Boscali Summer");
            var cells = new StormCell[StormField.MaxCells];

            int count = StormField.Fill(
                cells, seed, 400f, mapSize, 0.70f, 90f, 0f,
                WeatherFront.None, UnstableAir(0.70f, 0.70f), out StormMode mode);
            TestAssert.That(mode == StormMode.Cluster,
                "high CAPE and shear without a convective boundary is a cluster");
            TestAssert.That(count > 1, "a cluster is more than one cell");

            float meanX = 0f;
            float meanZ = 0f;
            for (int i = 0; i < count; i++)
            {
                meanX += cells[i].X;
                meanZ += cells[i].Z;
            }
            meanX /= count;
            meanZ /= count;

            float worst = 0f;
            for (int i = 0; i < count; i++)
            {
                float dx = cells[i].X - meanX;
                float dz = cells[i].Z - meanZ;
                float distance = (float)Math.Sqrt(dx * dx + dz * dz);
                if (distance > worst) worst = distance;
            }

            // With no wind a cluster cell sits at centre + spread, and every spread is within
            // ClusterSpreadFraction of the drawn spawn radius. Two spreads can oppose, so the
            // widest possible offset from the mean is twice the spread cap.
            float bound = 2f * StormField.ClusterSpreadFraction * StormField.SpawnRadiusMax * mapSize;
            TestAssert.That(worst <= bound,
                "every cluster cell sits within a fraction of the spawn radius of the population, worst=" +
                worst.ToString("0") + " bound=" + bound.ToString("0"));
        }

        private static void SquallLineLiesAlongTheFront()
        {
            const float mapSize = 40000f;
            int seed = WeatherModel.Seed("Boscali Summer");
            var cells = new StormCell[StormField.MaxCells];
            Atmosphere air = UnstableAir(0.85f, 0.80f);
            WeatherFront front = ColdFront(4000f, 0.90f);

            int count = StormField.Fill(cells, seed, 400f, mapSize, 0.95f, 90f, 15f, front, air, out StormMode mode);
            TestAssert.That(mode == StormMode.SquallLine,
                "a convective front under high CAPE and shear is a squall line");
            TestAssert.That(count >= 2, "a squall line is more than one cell");

            float lead = mapSize * 0.05f;
            float minAlong = float.MaxValue;
            float maxAlong = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                StormCell cell = cells[i];
                float signed = front.SignedDistanceTo(cell.X, cell.Z);
                TestAssert.That(signed < 0f,
                    "every squall-line cell sits on the leading side of the boundary");
                TestAssert.That(-signed <= lead + 1f,
                    "the line lies along the boundary, not out in the map");
                TestAssert.That(Near(cell.X * front.NormalX + cell.Z * front.NormalZ, front.Position + lead, 1f),
                    "the line's position is the front's position");
                TestAssert.That(Near(cell.VelocityX, front.NormalX * front.Speed) &&
                                Near(cell.VelocityZ, front.NormalZ * front.Speed),
                    "a squall-line cell moves with the front, not with the wind");

                float along = -front.NormalZ * cell.X + front.NormalX * cell.Z;
                if (along < minAlong) minAlong = along;
                if (along > maxAlong) maxAlong = along;
            }
            TestAssert.That(maxAlong - minAlong > mapSize * 0.05f,
                "the line is spread across the map instead of stacked on one point");

            StormCell strongest = StormField.StrongestAt(cells, count, cells[0].X, cells[0].Z, out float influence);
            TestAssert.That(influence > 0f && influence <= 1f,
                "the strongest cell over a cell's own position has real influence");
            StormWarning warning = StormField.WarningAt(cells, count, cells[0].X, cells[0].Z, out StormCell source);
            TestAssert.That(warning == StormWarning.Warning && source.Intensity > 0f,
                "standing in a squall line is a warning from the cell that imposed it");

            // Move the boundary: the line is on the new position, still on the leading side.
            var moved = new WeatherFront(
                true, FrontKind.Cold, front.NormalX, front.NormalZ,
                front.Position + front.Speed * 120f, front.Speed, front.Width, front.Activity,
                front.Behind, front.Ahead);
            var later = new StormCell[StormField.MaxCells];
            int laterCount = StormField.Fill(later, seed, 520f, mapSize, 0.95f, 90f, 15f, moved, air, out StormMode laterMode);
            TestAssert.That(laterMode == StormMode.SquallLine && laterCount == count,
                "the same line re-forms as the front moves");
            for (int i = 0; i < laterCount; i++)
            {
                float signed = moved.SignedDistanceTo(later[i].X, later[i].Z);
                TestAssert.That(signed < 0f && -signed <= lead + 1f,
                    "the line has moved with the front and kept its leading side");
                TestAssert.That(Near(later[i].X * moved.NormalX + later[i].Z * moved.NormalZ, moved.Position + lead, 1f),
                    "the line's new position is the front's new position");
            }
        }

        private static void FrontProximityDrivesIntensity()
        {
            const float mapSize = 40000f;
            int seed = WeatherModel.Seed("Boscali Summer");
            var far = new StormCell[StormField.MaxCells];
            var near = new StormCell[StormField.MaxCells];
            Atmosphere air = UnstableAir(0.35f, 0.20f);

            // Moderate instability with low shear is the scattered population: the front changes
            // the strength of the cells, not where they are.
            int farCount = StormField.Fill(
                far, seed, 400f, mapSize, 0.70f, 90f, 10f, ColdFront(-30000f, 0.90f), air, out StormMode farMode);
            TestAssert.That(farMode == StormMode.Scattered, "moderate instability scatters the cells");
            TestAssert.That(farCount > 0, "the scattered population is not empty");

            float line = far[0].X;
            float width = FrontKinds.Width(FrontKind.Cold);
            var ahead = new StormCell[StormField.MaxCells];
            var behind = new StormCell[StormField.MaxCells];
            int nearCount = StormField.Fill(
                near, seed, 400f, mapSize, 0.70f, 90f, 10f, ColdFront(line, 0.90f), air, out _);
            int aheadCount = StormField.Fill(
                ahead, seed, 400f, mapSize, 0.70f, 90f, 10f, ColdFront(line - 2f * width, 0.90f), air, out _);
            int behindCount = StormField.Fill(
                behind, seed, 400f, mapSize, 0.70f, 90f, 10f, ColdFront(line + 2f * width, 0.90f), air, out _);
            TestAssert.That(nearCount == farCount && aheadCount == farCount && behindCount == farCount,
                "the front's position does not change which slots are alive");
            for (int i = 0; i < farCount; i++)
            {
                TestAssert.That(near[i].X == far[i].X && near[i].Z == far[i].Z,
                    "a scattered cell's position does not depend on the front");
            }
            TestAssert.That(near[0].Intensity > far[0].Intensity,
                "a cell on the boundary is stronger than the same cell with the boundary far away");
            TestAssert.That(ahead[0].Intensity == far[0].Intensity && behind[0].Intensity == far[0].Intensity,
                "a boundary outside its band leaves a cell at its base strength");

            // The boundary sweeping in: strength grows as it closes and peaks over the cell.
            float previous = -1f;
            for (int step = 3; step >= 0; step--)
            {
                var sweep = new StormCell[StormField.MaxCells];
                float distance = width * step * 0.3f;
                StormField.Fill(sweep, seed, 400f, mapSize, 0.70f, 90f, 10f, ColdFront(line - distance, 0.90f), air, out _);
                TestAssert.That(sweep[0].Intensity > previous,
                    "a cell strengthens every step as the front closes from " + distance.ToString("0") + " m");
                previous = sweep[0].Intensity;
            }
            TestAssert.That(Near(previous, near[0].Intensity),
                "the sweep peaks exactly on the boundary");
        }

        private static void ForecastCarriesTrendsAndSeverity()
        {
            int seed = WeatherModel.Seed("Boscali Summer");

            int rising = -1;
            int falling = -1;
            for (int front = 0; front < 40 && (rising < 0 || falling < 0); front++)
            {
                float before = WeatherModel.Front(seed, front).Conditions;
                float after = WeatherModel.Front(seed, front + 1).Conditions;
                if (rising < 0 && after - before > 0.05f) rising = front;
                if (falling < 0 && before - after > 0.05f) falling = front;
            }
            TestAssert.That(rising >= 0 && falling >= 0,
                "the schedule has both a thickening and an easing transition to forecast");

            AssertTransitionTrend(seed, rising, 1);
            AssertTransitionTrend(seed, falling, -1);
        }

        private static void AssertTransitionTrend(int seed, int front, int sign)
        {
            float start = WeatherModel.FrontStart(front + 1) - WeatherModel.FrontBlendSeconds - 120f;
            WeatherForecast forecast = WeatherForecast.Build(seed, start, 4, 60f);
            TestAssert.That(forecast.Count == 4, "the trend forecast has the steps it asked for");

            bool sawTrend = false;
            WeatherState previous = WeatherModel.Sample(seed, start);
            for (int i = 0; i < forecast.Count; i++)
            {
                WeatherForecastEntry entry = forecast[i];
                TestAssert.That(entry.Severity == WeatherRegimes.Index(entry.State.Regime) &&
                                entry.Severity >= 0 && entry.Severity < WeatherRegimes.Count,
                    "every entry carries its regime's severity rank");
                TestAssert.That(Near(entry.ConditionsDelta, entry.State.Conditions - previous.Conditions),
                    "the conditions delta is measured against the previous entry");
                TestAssert.That(Near(entry.WindDelta, entry.State.WindSpeed - previous.WindSpeed),
                    "the wind delta is measured against the previous entry");
                TestAssert.That(entry.ConditionsDelta >= -1f && entry.ConditionsDelta <= 1f &&
                                entry.WindDelta >= -WeatherForecastEntry.WindDeltaLimit &&
                                entry.WindDelta <= WeatherForecastEntry.WindDeltaLimit,
                    "a forecast trend is clamped to a printable range");
                if (sign > 0 && entry.ConditionsDelta > 0.001f) sawTrend = true;
                if (sign < 0 && entry.ConditionsDelta < -0.001f) sawTrend = true;
                previous = entry.State;
            }

            TestAssert.That(sawTrend, sign > 0
                ? "the trend column shows the sky thickening"
                : "the trend column shows the sky easing");
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

        private static bool Near(float a, float b) => Near(a, b, 1e-4f);

        private static bool Near(float a, float b, float tolerance) => Math.Abs(a - b) <= tolerance;
    }
}
