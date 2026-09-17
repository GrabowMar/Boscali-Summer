using System;
using System.Globalization;
using System.Reflection;
using BoscaliSummer.Features.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>
    /// The deterministic weather schedule and every string the panel prints, without Unity.
    ///
    /// <para>These are the parts a screenshot cannot check: that every peer derives the same
    /// sky from the mission name and clock, that a blend crosses the short way round, that a
    /// foreign write always wins, that the unit conversions are exact and culture-proof, that
    /// the severity ramps only ever climb, and that an unknown reading renders as a dash rather
    /// than a confident zero.</para>
    /// </summary>
    internal static class WeatherTests
    {
        public static void Run()
        {
            SeedDeterminism();
            FrontArithmetic();
            FrontsStayInsideTheirBands();
            TheOpeningHalfHourActuallyChanges();
            RegimesMatchTheBandTable();
            BlendsTakeTheShortArc();
            HazeNeverInventsAStorm();
            ForecastStaysBounded();
            DrivesAdoptForeignWrites();
            DrivesRampWithoutOvershoot();
            HeadingsTakeTheShortArc();
            ReadoutsNeverInventPrecision();
            SnapshotsReadTheLocalWind();
            UnitConversionsAreExactAndCultureInvariant();
            TrendsCarryTheirOwnSign();
            FlightCategoriesMatchTheirThresholds();
            NewFormattersSurviveUnreadableInput();
            TheBannerNamesTheRightCell();
            StormTests.Run();
            DomainStaysFreeOfUnityTypes();
        }

        /// <summary>
        /// The regression guard for the first real bug found in play. Weather fronts were
        /// twenty-five minutes long and the shipped mission's opening ran HAZE then CLEAR, so a
        /// player who flew for half an hour saw the sky do nothing at all. The schedule has to
        /// visibly move inside one sortie.
        /// </summary>
        private static void TheOpeningHalfHourActuallyChanges()
        {
            int seed = WeatherModel.Seed("Boscali Summer");
            int fronts = (int)(1800f / WeatherModel.FrontSeconds) + 1;

            var seen = new System.Collections.Generic.HashSet<WeatherRegime>();
            float lowest = float.MaxValue;
            float highest = float.MinValue;
            for (int front = 0; front <= fronts; front++)
            {
                WeatherState state = WeatherModel.Front(seed, front);
                seen.Add(state.Regime);
                if (state.Conditions < lowest) lowest = state.Conditions;
                if (state.Conditions > highest) highest = state.Conditions;
            }

            TestAssert.That(seen.Count >= 3,
                "the first half hour of the shipped mission must cross at least three regimes, saw " + seen.Count);
            TestAssert.That(highest - lowest >= 0.5f,
                "conditions must swing by at least half the scale inside the first half hour, swing was " +
                (highest - lowest).ToString("0.00"));
            TestAssert.That(WeatherModel.FrontSeconds <= 600f,
                "a front longer than ten minutes cannot deliver a visible change inside one sortie");
        }

        private static void SeedDeterminism()
        {
            TestAssert.That(WeatherModel.Seed("Boscali Summer") == WeatherModel.Seed("Boscali Summer"),
                "the same mission identity seeds the same schedule");
            TestAssert.That(WeatherModel.Seed("a") != WeatherModel.Seed("b"),
                "different mission identities seed different schedules");
            TestAssert.That(WeatherModel.Seed(null) == 0x5EED && WeatherModel.Seed("") == 0x5EED,
                "an absent mission identity falls back to the documented seed");
        }

        private static void FrontArithmetic()
        {
            const float frontSeconds = WeatherModel.FrontSeconds;
            const float blendSeconds = WeatherModel.FrontBlendSeconds;

            TestAssert.That(WeatherModel.FrontIndex(0f) == 0, "mission start is inside front zero");
            TestAssert.That(WeatherModel.FrontIndex(frontSeconds - 1f) == 0,
                "the last second of a front is still that front");
            TestAssert.That(WeatherModel.FrontIndex(frontSeconds) == 1,
                "the front boundary advances the index");
            TestAssert.That(WeatherModel.FrontStart(3) == 3f * frontSeconds,
                "fronts start on exact multiples of the front length");

            float blendStart = frontSeconds - blendSeconds;
            TestAssert.That(WeatherModel.BlendWeight(0f) == 0f, "a fresh front blends nothing");
            TestAssert.That(WeatherModel.BlendWeight(blendStart) == 0f,
                "the blend window opens at zero weight");
            float middle = WeatherModel.BlendWeight(blendStart + blendSeconds * 0.5f);
            TestAssert.That(middle > 0f && middle < 1f,
                "the middle of the blend window is strictly partial");
            TestAssert.That(WeatherModel.BlendWeight(frontSeconds - 1e-3f) >= 0.9999f,
                "the blend is complete by the end of the front");

            TestAssert.That(WeatherModel.PhaseIndex(WeatherModel.FrontsPerPhase) == 1,
                "the first front of the next phase opens a new day character");
        }

        private static void FrontsStayInsideTheirBands()
        {
            string[] seeds = { "Boscali Summer", "Boscali Winter" };
            foreach (string name in seeds)
            {
                int seed = WeatherModel.Seed(name);
                int clear = 0;
                int storm = 0;
                for (int front = 0; front <= 200; front++)
                {
                    WeatherState state = WeatherModel.Front(seed, front);
                    TestAssert.That(state.Conditions >= 0f && state.Conditions <= 1f,
                        "conditions stay in 0..1 for " + name);
                    TestAssert.That(state.CloudBase >= WeatherModel.MinCloudBase &&
                                    state.CloudBase <= WeatherModel.MaxCloudBase,
                        "the cloud base stays in the model band for " + name);
                    TestAssert.That(state.WindSpeed >= 0f && state.WindSpeed <= 30f,
                        "wind speed stays in 0..30 for " + name);
                    TestAssert.That(state.Turbulence >= 0f && state.Turbulence <= 1f,
                        "turbulence stays in 0..1 for " + name);
                    TestAssert.That(state.WindHeading >= 0f && state.WindHeading < 360f,
                        "the wind heading stays on the compass for " + name);
                    if (state.Regime == WeatherRegime.Clear) clear++;
                    if (state.Regime == WeatherRegime.Storm) storm++;
                }
                TestAssert.That(clear > 0, "the schedule produces clear fronts for " + name);
                TestAssert.That(storm > 0, "the schedule produces storm fronts for " + name);
            }
        }

        private static void RegimesMatchTheBandTable()
        {
            for (int i = 0; i < WeatherRegimes.Count; i++)
            {
                TestAssert.That(
                    WeatherRegimes.FromConditions(WeatherRegimes.ConditionsHi(i) - 1e-4f) == (WeatherRegime)i,
                    "the top of a conditions band still renders as that band");
                TestAssert.That(
                    WeatherRegimes.FromIndex(WeatherRegimes.Index((WeatherRegime)i)) == (WeatherRegime)i,
                    "a regime survives the index round trip");
                TestAssert.That(!string.IsNullOrEmpty(WeatherRegimes.Label(WeatherRegimes.FromIndex(i))),
                    "every regime index has a non-empty label");
            }
            TestAssert.That(WeatherRegimes.FromConditions(1f) == WeatherRegime.Storm,
                "saturated conditions are a storm");
            TestAssert.That(
                WeatherRegimes.IsSevere(WeatherRegime.Storm) && !WeatherRegimes.IsSevere(WeatherRegime.Overcast),
                "only squall and storm are severe");
            TestAssert.That(
                WeatherRegimes.FromIndex(-3) == WeatherRegime.Clear &&
                WeatherRegimes.FromIndex(99) == WeatherRegime.Storm,
                "out-of-range indices clamp instead of throwing");
        }

        private static void BlendsTakeTheShortArc()
        {
            var source = new WeatherState(WeatherRegime.Haze, 0.3f, 2000f, 8f, 90f, 0.2f);
            TestAssert.That(SameState(source, WeatherState.Blend(source, source, 0.37f)),
                "blending a sky with itself changes nothing");

            var target = new WeatherState(WeatherRegime.Storm, 0.9f, 600f, 18f, 200f, 0.8f);
            TestAssert.That(SameState(WeatherState.Blend(source, target, 0f), source),
                "zero weight keeps the source sky");
            TestAssert.That(SameState(WeatherState.Blend(source, target, 1f), target),
                "full weight lands on the target sky");

            var from = new WeatherState(WeatherRegime.Fair, 0.2f, 2400f, 4f, 350f, 0.05f);
            var to = new WeatherState(WeatherRegime.Overcast, 0.5f, 1800f, 9f, 10f, 0.3f);
            float heading = WeatherState.Blend(from, to, 0.5f).WindHeading;
            TestAssert.That(CircularDistance(heading, 0f) < 1f,
                "350 to 10 degrees at half weight crosses north, not the long way round");
        }

        private static void HazeNeverInventsAStorm()
        {
            var clear = new WeatherState(WeatherRegime.Clear, 0f, 2600f, 3f, 0f, 0.05f);
            TestAssert.That(SameState(WeatherState.WithHaze(clear, 0f), clear),
                "no fire is no haze");

            WeatherState hazed = WeatherState.WithHaze(clear, 1f);
            TestAssert.That(hazed.Conditions > clear.Conditions, "haze thickens a clear day");
            TestAssert.That(hazed.Conditions <= 0.15f,
                "haze lifts conditions by at most the 0.15 constant");
            TestAssert.That(hazed.CloudBase < clear.CloudBase, "haze drops the cloud base");
            TestAssert.That(hazed.CloudBase >= WeatherModel.MinCloudBase,
                "haze never drops the cloud base below the model floor");
            TestAssert.That(!hazed.IsSevere, "haze never invents a storm out of a clear day");
            TestAssert.That(WeatherState.WithHaze(clear, 2f).Conditions <= 0.15f,
                "haze above the scale clamps instead of stacking");

            var saturated = new WeatherState(WeatherRegime.Storm, 0.99f, 600f, 20f, 0f, 0.9f);
            WeatherState stirred = WeatherState.WithHaze(saturated, 1f);
            TestAssert.That(stirred.Conditions <= 1f && stirred.Turbulence <= 1f,
                "haze cannot push a saturated sky past its ceilings");
        }

        private static void ForecastStaysBounded()
        {
            int seed = WeatherModel.Seed("Boscali Summer");
            float now = 120f;

            WeatherForecast forecast = WeatherForecast.Build(seed, now, 4, 60f);
            TestAssert.That(forecast.Count == 4, "a four-step forecast has four entries");
            TestAssert.That(forecast[0].AtSeconds == now + 60f,
                "the first entry sits one step ahead");
            for (int i = 1; i < forecast.Count; i++)
            {
                TestAssert.That(forecast[i].AtSeconds > forecast[i - 1].AtSeconds,
                    "forecast entries move strictly forward in time");
            }

            WeatherForecast clamped = WeatherForecast.Build(seed, now, 999, 0.001f);
            TestAssert.That(clamped.Count == WeatherForecast.MaxEntries,
                "an oversized forecast clamps to the entry ceiling");
            TestAssert.That(Near(clamped[1].AtSeconds - clamped[0].AtSeconds, WeatherForecast.MinStepSeconds),
                "a sub-minimum step clamps to the step floor");

            TestAssert.That(WeatherForecast.Build(seed, now, 0).Count == 0,
                "an empty forecast asks for nothing");
            TestAssert.That(
                clamped.NextChangeSeconds >= 0f && clamped.NextChangeSeconds <= WeatherModel.FrontSeconds,
                "the next change is inside one front from now");
            TestAssert.That(
                WeatherForecast.SecondsToRegime(WeatherRegime.Clear, seed, now, 0f) == -1f,
                "a zero horizon cannot reach any regime");

            float horizon = 5000f;
            for (int i = 0; i < WeatherRegimes.Count; i++)
            {
                float at = WeatherForecast.SecondsToRegime((WeatherRegime)i, seed, now, horizon);
                TestAssert.That(at == -1f || (at >= 0f && at <= horizon),
                    "a forecast never points past the horizon it was given");
            }
        }

        private static void DrivesAdoptForeignWrites()
        {
            const float baseline = 0.5f;
            var drive = new WeatherDrive();

            TestAssert.That(!drive.Observe(baseline), "the first reading is a baseline, not a foreign write");
            TestAssert.That(!drive.Holding, "a baseline does not hold the schedule off");
            TestAssert.That(!drive.Observe(baseline + WeatherDrive.AdoptEpsilon * 0.5f),
                "drift inside the adoption window is treated as our own ramp");
            TestAssert.That(!drive.Holding, "a small drift does not trigger a hold");
            TestAssert.That(drive.Observe(baseline + WeatherDrive.AdoptEpsilon * 4f),
                "a move past the adopted baseline is somebody else's write");
            TestAssert.That(drive.Holding, "a foreign write holds the schedule off");

            drive.Advance(WeatherDrive.HoldSeconds + 1f);
            TestAssert.That(!drive.Holding, "the hold expires");
            TestAssert.That(drive.ShouldWrite, "the schedule resumes after the hold");

            drive.Reset();
            TestAssert.That(drive.ShouldWrite && !drive.Holding,
                "a reset releases the drive immediately");
        }

        private static void DrivesRampWithoutOvershoot()
        {
            var drive = new WeatherDrive();

            TestAssert.That(Near(drive.Step(0f, 1f, 0.1f, 1f), 0.1f),
                "a ramp moves at the rate for the elapsed time");
            TestAssert.That(drive.Step(0.95f, 1f, 0.1f, 1f) == 1f,
                "a target closer than the step lands exactly on target");
            TestAssert.That(Near(drive.Step(1f, 0f, 0.1f, 1f), 0.9f),
                "a downward ramp moves at the same rate");
            TestAssert.That(drive.Step(float.NaN, 0.42f, 0.1f, 1f) == 0.42f,
                "an unreadable live value snaps to the target");
            TestAssert.That(drive.Step(0.3f, 0.9f, 0f, 1f) == 0.3f,
                "no elapsed time cannot move a value");
            TestAssert.That(drive.Step(0.3f, 0.9f, 0.1f, 0f) == 0.3f,
                "a zero rate cannot move a value");

            float value = 0f;
            for (int i = 0; i < 100; i++)
            {
                value = drive.Step(value, 1f, 0.01f, 1f);
                TestAssert.That(value <= 1f, "a ramp never overshoots its target");
            }
            TestAssert.That(Near(value, 1f, 0.001f), "a hundred small steps arrive at the target");
        }

        private static void HeadingsTakeTheShortArc()
        {
            var drive = new WeatherDrive();

            TestAssert.That(Near(drive.StepAngle(350f, 10f, 10f, 2f), 10f),
                "a heading ramp from 350 to 10 crosses north instead of sweeping back through 180");
            TestAssert.That(Near(drive.StepAngle(10f, 350f, 10f, 2f), 350f),
                "a heading ramp from 10 to 350 backs through north");
            TestAssert.That(Near(drive.StepAngle(350f, 10f, 1f, 2f), 352f),
                "a heading ramp still moves at the rate for the elapsed time");
            TestAssert.That(Near(drive.StepAngle(359f, 1f, 1f, 2f), 1f),
                "a heading ramp that can reach the target lands exactly on it");
            TestAssert.That(drive.StepAngle(90f, 45f, 0f, 2f) == 90f,
                "no elapsed time cannot move a heading");
            TestAssert.That(drive.StepAngle(90f, 45f, 1f, 0f) == 90f,
                "a zero rate cannot move a heading");

            float heading = 350f;
            for (int i = 0; i < 40; i++)
            {
                heading = drive.StepAngle(heading, 10f, 0.5f, 2f);
                TestAssert.That(heading >= 0f && heading < 360f, "a heading ramp stays inside 0..360");
            }
            TestAssert.That(Near(heading, 10f, 0.001f), "a heading ramp arrives at the target");
        }

        private static void ReadoutsNeverInventPrecision()
        {
            TestAssert.That(WeatherReadout.Compass16(0f) == "N", "zero degrees reads north");
            TestAssert.That(WeatherReadout.Compass16(90f) == "E", "ninety degrees reads east");
            TestAssert.That(WeatherReadout.Compass16(180f) == "S", "one eighty reads south");
            TestAssert.That(WeatherReadout.Compass16(270f) == "W", "two seventy reads west");
            TestAssert.That(WeatherReadout.Compass16(359f) == "N", "the last degree wraps to north");
            TestAssert.That(WeatherReadout.Compass16(float.NaN) == WeatherReadout.Unknown,
                "an unreadable heading is a dash, not north");

            TestAssert.That(WeatherReadout.Percent01(0.5f) == "50%", "a half reads as 50%");
            TestAssert.That(WeatherReadout.Percent01(1.5f) == "100%", "over-full clamps to 100%");
            TestAssert.That(WeatherReadout.Percent01(-1f) == "0%", "below empty clamps to 0%");
            TestAssert.That(WeatherReadout.Percent01(float.NaN) == WeatherReadout.Unknown,
                "an unknown ratio is a dash, never 0%");
            TestAssert.That(WeatherReadout.Percent01(float.PositiveInfinity) == WeatherReadout.Unknown,
                "an infinite ratio is a dash");

            TestAssert.That(WeatherReadout.Clock(0f) == "00:00", "zero seconds reads as zero");
            TestAssert.That(WeatherReadout.Clock(65f) == "01:05", "minutes and seconds roll over");
            TestAssert.That(WeatherReadout.Clock(3600f) == "1:00:00", "an hour switches to h:mm:ss");
            TestAssert.That(WeatherReadout.Clock(-5f) == "00:00", "a negative clock reads as zero");
            TestAssert.That(WeatherReadout.Clock(float.NaN) == WeatherReadout.Unknown,
                "an unreadable clock is a dash");

            TestAssert.That(WeatherReadout.Meters(float.NaN) == WeatherReadout.Unknown,
                "an unreadable altitude is a dash");

            TestAssert.That(WeatherReadout.Trend(0.2f, 0.5f) == "BUILDING", "rising conditions build");
            TestAssert.That(WeatherReadout.Trend(0.5f, 0.2f) == "EASING", "falling conditions ease");
            TestAssert.That(WeatherReadout.Trend(0.4f, 0.41f) == "STEADY",
                "a move inside the dead band is steady");

            TestAssert.That(
                WeatherReadout.Severity(WeatherRegime.Clear) == 1 &&
                WeatherReadout.Severity(WeatherRegime.Fair) == 1,
                "a clear or fair sky is nominal");
            TestAssert.That(
                WeatherReadout.Severity(WeatherRegime.Haze) == 2 &&
                WeatherReadout.Severity(WeatherRegime.Overcast) == 2,
                "haze and overcast are caution");
            TestAssert.That(
                WeatherReadout.Severity(WeatherRegime.Squall) == 3 &&
                WeatherReadout.Severity(WeatherRegime.Storm) == 3,
                "squall and storm are danger");

            for (int i = 0; i < WeatherRegimes.Count; i++)
            {
                string rail = WeatherReadout.RailClass((WeatherRegime)i);
                TestAssert.That(rail == "rail danger" || rail == "rail contested" || rail == "rail ready",
                    "every regime gets one of the three documented rail classes");
            }
            TestAssert.That(WeatherReadout.RailClass(WeatherRegime.Clear) == "rail ready",
                "a clear sky is ready");
            TestAssert.That(WeatherReadout.RailClass(WeatherRegime.Overcast) == "rail contested",
                "an overcast sky is contested");
            TestAssert.That(WeatherReadout.RailClass(WeatherRegime.Storm) == "rail danger",
                "a storm is danger");
        }

        private static void SnapshotsReadTheLocalWind()
        {
            var live = new WeatherState(WeatherRegime.Fair, 0.2f, 2400f, 4f, 123.5f, 0.05f);
            var snapshot = new WeatherSnapshot(true, 600f, live, live, 3f, 0f, 4f, 0.1f, 1f, false, true, null, 0, 0f, StormWarning.None, default);

            TestAssert.That(Near(snapshot.LocalWindSpeed, 5f),
                "three and four make a five metre local wind");
            TestAssert.That(snapshot.LocalWindHeading >= 0f && snapshot.LocalWindHeading < 360f,
                "a local wind heading stays on the compass");
            TestAssert.That(Near(snapshot.LocalWindHeading, 36.87f, 0.01f),
                "the local heading is atan2(x, z), not a zero vector");

            var calm = new WeatherSnapshot(true, 600f, live, live, 0f, 0f, 0f, 0f, 1f, false, true, null, 0, 0f, StormWarning.None, default);
            TestAssert.That(calm.LocalWindHeading == live.WindHeading,
                "a still local sample falls back to the synced mean heading");

            TestAssert.That(!WeatherSnapshot.Unavailable.Available,
                "the unavailable snapshot says so");
            TestAssert.That(WeatherSnapshot.Unavailable.LocalWindSpeed == 0f &&
                            !float.IsNaN(WeatherSnapshot.Unavailable.LocalWindHeading),
                "an unavailable snapshot computes without throwing");
        }

        /// <summary>
        /// The panel's new units. Feet, kilometres, metres above ground and tenths of a metre
        /// per second all have to be exact and identical under every culture: a comma decimal
        /// separator must not turn 3281 feet into something a pilot cannot read.
        /// </summary>
        private static void UnitConversionsAreExactAndCultureInvariant()
        {
            TestAssert.That(WeatherReadout.Feet(1000f) == "3281 FT",
                "a thousand metres reads 3281 feet");
            TestAssert.That(WeatherReadout.Feet(3400f) == "11155 FT",
                "the model's ceiling clamp reads 11155 feet");
            TestAssert.That(WeatherReadout.Feet(0f) == "0 FT", "zero metres is zero feet, not a dash");
            TestAssert.That(WeatherReadout.MetersAgL(1040f) == "1040 M AGL",
                "metres above ground say so");
            TestAssert.That(WeatherReadout.Kilometres(8000f) == "8.0 KM",
                "eight thousand metres is 8.0 km");
            TestAssert.That(WeatherReadout.Kilometres(10400f) == "10 KM",
                "four figures of kilometres drop the decimal");
            TestAssert.That(WeatherReadout.Kilometres(800f) == "0.8 KM",
                "poor visibility keeps its decimal");
            TestAssert.That(WeatherReadout.Celsius(14f) == "14°C", "fourteen degrees reads fourteen");
            TestAssert.That(WeatherReadout.Celsius(-3f) == "-3°C", "a negative temperature keeps its sign");
            TestAssert.That(WeatherReadout.Speed(17.04f) == "17.0 M/S", "a gust reads to a tenth");

            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                // A comma-decimal culture. Every one of these must ignore it.
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                TestAssert.That(WeatherReadout.Feet(1000f) == "3281 FT",
                    "feet ignore the current culture");
                TestAssert.That(WeatherReadout.Kilometres(8000f) == "8.0 KM",
                    "kilometres ignore the current culture");
                TestAssert.That(WeatherReadout.Speed(17.04f) == "17.0 M/S",
                    "wind speeds ignore the current culture");
                TestAssert.That(WeatherReadout.SignedDecimal(5.66f, 1) == "+5.7",
                    "signed decimals ignore the current culture");
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        /// <summary>
        /// A trend has to say which way it points. The arrow, the signed delta and the veer all
        /// carry their own direction, and a level reading is a level mark rather than an arrow.
        /// </summary>
        private static void TrendsCarryTheirOwnSign()
        {
            TestAssert.That(WeatherReadout.SignedPercent(0.183f) == "+18%", "a rise reads with a plus");
            TestAssert.That(WeatherReadout.SignedPercent(-0.24f) == "-24%", "a fall reads with a minus");
            TestAssert.That(WeatherReadout.SignedPercent(0f) == "0%", "no change reads as zero, unsigned");
            TestAssert.That(WeatherReadout.SignedPercent(0.004f) == "0%",
                "a change under half a percent is level, never -0%");
            TestAssert.That(WeatherReadout.SignedPercent(-0.004f) == "0%",
                "a fall under half a percent is level, never -0%");

            TestAssert.That(WeatherReadout.SignedDecimal(5.66f, 1) == "+5.7", "a wind rise carries its plus");
            TestAssert.That(WeatherReadout.SignedDecimal(-2.34f, 1) == "-2.3", "a wind fall carries its minus");
            TestAssert.That(WeatherReadout.SignedDecimal(0f, 1) == "0.0", "a level wind delta is unsigned");

            TestAssert.That(WeatherReadout.TrendMark(0.5f, WeatherReadout.TrendDeadband) == "▲",
                "a rise above the dead band is an up arrow");
            TestAssert.That(WeatherReadout.TrendMark(-0.5f, WeatherReadout.TrendDeadband) == "▼",
                "a fall below the dead band is a down arrow");
            TestAssert.That(WeatherReadout.TrendMark(0.01f, WeatherReadout.TrendDeadband) == "=",
                "a move inside the dead band is a level mark");
            TestAssert.That(WeatherReadout.TrendMark(0f, 0f) == "=", "a zero change is never an arrow");

            TestAssert.That(WeatherReadout.Veer(350f, 10f) == "+20°",
                "a north-crossing heading change veers twenty degrees, not three hundred and forty");
            TestAssert.That(WeatherReadout.Veer(10f, 350f) == "-20°", "the same change reversed backs");
            TestAssert.That(WeatherReadout.Veer(90f, 90f) == "0°", "no veer reads as zero");

            TestAssert.That(WeatherReadout.ShearLabel(0f) == "LIGHT", "still air is light shear");
            TestAssert.That(WeatherReadout.ShearLabel(0.3f) == "MODERATE", "a third of the scale is moderate");
            TestAssert.That(WeatherReadout.ShearLabel(0.9f) == "STRONG", "near the top is strong");
            TestAssert.That(WeatherReadout.ShearLabel(-2f) == "LIGHT",
                "negative shear clamps rather than throwing");
        }

        /// <summary>
        /// The standard ceiling-and-visibility ladder, and the panel's ramp on top of it. Every
        /// boundary is asserted on both sides, because a category one rung off is a different
        /// decision in the cockpit.
        /// </summary>
        private static void FlightCategoriesMatchTheirThresholds()
        {
            TestAssert.That(Atmospheres.Rank((FlightCategory)(-4)) == 0 &&
                            Atmospheres.Rank((FlightCategory)99) == 3,
                "an out-of-range category clamps instead of throwing");

            TestAssert.That(CategoryAt(900f, 99999f) == FlightCategory.Vfr,
                "a 900 m ceiling is still visual");
            TestAssert.That(CategoryAt(899f, 99999f) == FlightCategory.Mvfr,
                "a ceiling just under 900 m is marginal visual");
            TestAssert.That(CategoryAt(300f, 99999f) == FlightCategory.Mvfr,
                "a 300 m ceiling is still marginal visual");
            TestAssert.That(CategoryAt(299f, 99999f) == FlightCategory.Ifr,
                "a ceiling under 300 m is instrument");
            TestAssert.That(CategoryAt(150f, 99999f) == FlightCategory.Ifr,
                "a 150 m ceiling is instrument, not low");
            TestAssert.That(CategoryAt(149f, 99999f) == FlightCategory.Lifr,
                "a ceiling under 150 m is low instrument");
            TestAssert.That(CategoryAt(99999f, 8000f) == FlightCategory.Vfr,
                "eight km visibility is visual");
            TestAssert.That(CategoryAt(99999f, 7999f) == FlightCategory.Mvfr,
                "visibility just under eight km is marginal");
            TestAssert.That(CategoryAt(99999f, 5000f) == FlightCategory.Mvfr,
                "five km visibility is still marginal");
            TestAssert.That(CategoryAt(99999f, 4999f) == FlightCategory.Ifr,
                "visibility just under five km is instrument");
            TestAssert.That(CategoryAt(99999f, 1600f) == FlightCategory.Ifr,
                "sixteen hundred metres of visibility is instrument");
            TestAssert.That(CategoryAt(99999f, 1599f) == FlightCategory.Lifr,
                "visibility under sixteen hundred metres is low instrument");

            TestAssert.That(Atmospheres.Label(FlightCategory.Vfr) == "VFR" &&
                            Atmospheres.Label(FlightCategory.Mvfr) == "MVFR" &&
                            Atmospheres.Label(FlightCategory.Ifr) == "IFR" &&
                            Atmospheres.Label(FlightCategory.Lifr) == "LIFR",
                "every category has its standard label");

            string[] expectedRail = { "rail ready", "rail info", "rail contested", "rail danger" };
            string[] expectedChip = { "chip live", "chip info", "chip warn", "chip danger" };
            int previousSeverity = -1;
            for (int i = 0; i < 4; i++)
            {
                var category = (FlightCategory)i;
                int severity = WeatherReadout.FlightSeverity(category);
                TestAssert.That(severity == Atmospheres.Rank(category),
                    "the panel's severity is exactly the atmosphere rank");
                TestAssert.That(severity > previousSeverity,
                    "the severity ramp is strictly monotonic in the rank");
                previousSeverity = severity;
                TestAssert.That(WeatherReadout.FlightRailClass(category) == expectedRail[i],
                    "the rail ramp runs ready to danger in rank order");
                TestAssert.That(WeatherReadout.FlightChipClass(category) == expectedChip[i],
                    "the chip ramp runs live to danger in rank order");
            }

            string[] expectedRegimeChip = { "chip live", "chip live", "chip warn", "chip warn", "chip danger", "chip danger" };
            for (int i = 0; i < WeatherRegimes.Count; i++)
            {
                TestAssert.That(WeatherReadout.ChipClass((WeatherRegime)i) == expectedRegimeChip[i],
                    "every regime wears the chip class its severity names");
            }
        }

        /// <summary>
        /// Every new formatter, fed the readings a broken sample actually produces. A dash is an
        /// answer; "NaN" is a bug report, and an exception here is a dead cockpit panel.
        /// </summary>
        private static void NewFormattersSurviveUnreadableInput()
        {
            float[] poison = { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f, -1234.5f, float.MaxValue };
            foreach (float value in poison)
            {
                AssertClean(WeatherReadout.Feet(value), "feet");
                AssertClean(WeatherReadout.MetersAgL(value), "metres AGL");
                AssertClean(WeatherReadout.Kilometres(value), "kilometres");
                AssertClean(WeatherReadout.Celsius(value), "celsius");
                AssertClean(WeatherReadout.Speed(value), "wind speed");
                AssertClean(WeatherReadout.SignedDecimal(value, 1), "signed decimal");
                AssertClean(WeatherReadout.SignedPercent(value), "signed percent");
                AssertClean(WeatherReadout.TrendMark(value, WeatherReadout.TrendDeadband), "trend mark");
                AssertClean(WeatherReadout.ShearLabel(value), "shear label");
                AssertClean(WeatherReadout.Veer(value, 90f), "veer from");
                AssertClean(WeatherReadout.Veer(90f, value), "veer to");
            }
        }

        /// <summary>
        /// The cockpit banner names one cell, and it has to be the right one: the cell already
        /// raising a warning first, then the nearest supercell, and nothing at all otherwise.
        /// </summary>
        private static void TheBannerNamesTheRightCell()
        {
            var supercell = new StormCell(0, StormKind.Supercell, 30000f, 0f, 5000f, 0.9f, 10f, 600f, 800f, 9000f, 0f, 0f);
            var warnedCumulus = new StormCell(1, StormKind.Cumulus, 3000f, 0f, 1000f, 0.5f, 10f, 600f, 800f, 2000f, 0f, 0f);
            var quietCumulus = new StormCell(2, StormKind.Cumulus, 1000f, 0f, 1000f, 0.05f, 10f, 600f, 800f, 2000f, 0f, 0f);
            var cells = new[] { supercell, warnedCumulus, quietCumulus };

            TestAssert.That(StormReadout.Nearest(cells, 3, 0f, 0f, out StormCell nearest, out float distance) &&
                            nearest.Slot == 1,
                "a warned cell is named ahead of a farther supercell");
            TestAssert.That(Near(distance, 3000f), "the named cell's distance is its own distance");

            var distant = new[] { supercell, quietCumulus };
            TestAssert.That(StormReadout.Nearest(distant, 2, 0f, 0f, out nearest, out distance) &&
                            nearest.Slot == 0,
                "with nothing warned, the nearest supercell is named");
            TestAssert.That(Near(distance, 30000f), "the supercell's distance is the far one");

            TestAssert.That(!StormReadout.Nearest(new[] { quietCumulus }, 1, 0f, 0f, out _, out _),
                "a cell too weak to warn and too small to matter is not named");
            TestAssert.That(!StormReadout.Nearest(null, 0, 0f, 0f, out _, out _), "no buffer names nothing");
            TestAssert.That(!StormReadout.Nearest(cells, 0, 0f, 0f, out _, out _), "an empty buffer names nothing");
            TestAssert.That(!StormReadout.Nearest(cells, 3, float.NaN, 0f, out _, out _),
                "an unreadable reader position names nothing");

            TestAssert.That(Near(StormReadout.BearingDegrees(0f, 0f, 0f, 1f), 0f), "due north is zero degrees");
            TestAssert.That(Near(StormReadout.BearingDegrees(0f, 0f, 1f, 0f), 90f), "due east is ninety");
            TestAssert.That(Near(StormReadout.BearingDegrees(0f, 0f, -1f, 0f), 270f), "due west is two seventy");
            TestAssert.That(StormReadout.BearingDegrees(float.NaN, 0f, 1f, 0f) == 0f,
                "an unreadable bearing falls back to north rather than throwing");
        }

        private static FlightCategory CategoryAt(float ceilingMetres, float visibilityMetres)
        {
            var atmosphere = new Atmosphere(true, AirMassKind.MaritimePolar, FrontKind.None,
                12f, 8f, ceilingMetres, 0f, 0f, 0f, 0f, 0f, visibilityMetres, 0f, 0.5f, 0f);
            return Atmospheres.Category(atmosphere);
        }

        private static void AssertClean(string rendered, string what)
        {
            TestAssert.That(!string.IsNullOrEmpty(rendered), what + " must render something");
            TestAssert.That(rendered.IndexOf("NaN", StringComparison.Ordinal) < 0,
                what + " must never print NaN, printed '" + rendered + "'");
            TestAssert.That(rendered.IndexOf("Infinity", StringComparison.Ordinal) < 0,
                what + " must never print Infinity, printed '" + rendered + "'");
        }

        private static void DomainStaysFreeOfUnityTypes()
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
                inspected++;
                foreach (FieldInfo field in type.GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    TestAssert.That(!IsUnityType(field.FieldType),
                        "domain field " + type.Name + "." + field.Name + " must not be a Unity type");
                }
                foreach (PropertyInfo property in type.GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    TestAssert.That(!IsUnityType(property.PropertyType),
                        "domain property " + type.Name + "." + property.Name + " must not be a Unity type");
                }
            }
            TestAssert.That(inspected > 0, "the weather domain types were actually inspected");
        }

        private static bool IsUnityType(Type type)
        {
            Type owner = type.IsArray ? type.GetElementType() : type;
            string ns = owner != null ? owner.Namespace : null;
            return ns != null && ns.StartsWith("UnityEngine", StringComparison.Ordinal);
        }

        private static bool SameState(WeatherState a, WeatherState b) =>
            a.Regime == b.Regime &&
            Near(a.Conditions, b.Conditions) &&
            Near(a.CloudBase, b.CloudBase) &&
            Near(a.WindSpeed, b.WindSpeed) &&
            Near(a.Turbulence, b.Turbulence) &&
            CircularDistance(a.WindHeading, b.WindHeading) < 1e-3f;

        private static float CircularDistance(float a, float b)
        {
            float delta = Math.Abs(WeatherState.WrapHeading(a - b));
            return delta > 180f ? 360f - delta : delta;
        }

        private static bool Near(float a, float b) => Near(a, b, 1e-4f);

        private static bool Near(float a, float b, float tolerance) =>
            Math.Abs(a - b) <= tolerance;
    }
}
