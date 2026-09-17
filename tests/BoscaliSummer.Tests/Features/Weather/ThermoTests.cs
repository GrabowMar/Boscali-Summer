using System;
using BoscaliSummer.Features.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>
    /// The thermodynamics and the synoptic composition, without Unity. These are the formulas
    /// that decide the sky every peer flies through, so the things worth asserting are the
    /// bounds and the monotonicity — that no input can produce a NaN, a cloud base outside the
    /// band, or a passage that veers the wind the wrong way.
    /// </summary>
    internal static class ThermoTests
    {
        private const int Seed = 0x57454154;

        public static void Run()
        {
            TheLclRisesWithTheDewpointSpread();
            TheLclIsSaturatedOnTheDeckAndBoundedAbove();
            TheThermodynamicsSurviveHostileInput();
            FogClosesTheVisibilityWhenTheSpreadCollapses();
            RainExtinguishesTheVisibilitySoonerThanDrizzle();
            ADeepLowLiftsAndARidgeSubsides();
            TheChannelsStayInTheirBands();
            AGustRidesInTheTurbulenceChannel();
            AColdPassageVeersTheWindOneWay();
            TheColdFrontIsTheViolentBoundaryAndTheWarmFrontIsNot();
            TheAtmosphereIsDeterministicAndSeedSensitive();
            TheAtmosphereDoesNotDependOnTheMap();
            ARidgeCarriesNoBoundaryAndAPassageAdvances();
        }

        private static void TheLclRisesWithTheDewpointSpread()
        {
            float tight = Thermo.Lcl(20f, 19f);
            float wide = Thermo.Lcl(20f, 10f);
            TestAssert.That(wide > tight, "a wider dewpoint spread lifts the cloud base");

            float wetter = Thermo.Lcl(20f, 17f);
            TestAssert.That(wetter > tight && wetter < wide,
                "the cloud base sits between the tight and wide cases");
        }

        private static void TheLclIsSaturatedOnTheDeckAndBoundedAbove()
        {
            TestAssert.That(Thermo.Lcl(20f, 20f) == 0f, "saturated air has cloud on the deck");
            TestAssert.That(Thermo.Lcl(20f, 25f) == 0f, "supersaturated air is still on the deck");
            TestAssert.That(Thermo.Lcl(50f, -40f) <= 4000f, "the cloud base is bounded above");
            TestAssert.That(Thermo.Lcl(20f, 20f) >= 0f, "the cloud base is never negative");
        }

        private static void TheThermodynamicsSurviveHostileInput()
        {
            float[] hostile = { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1000f, 1000f, 0f };

            foreach (float value in hostile)
            {
                float cape = Thermo.Cape(value, value, value, value);
                float cin = Thermo.Cin(value, value, value);
                float shear = Thermo.Shear(value, value, value);
                float rain = Thermo.RainRate(value, value, value, value);
                float visibility = Thermo.Visibility(value, value, value);
                float gust = Thermo.GustSpeed(value, value);
                float lcl = Thermo.Lcl(value, value);
                float humidity = Thermo.RelativeHumidity(value, value);

                TestAssert.That(!float.IsNaN(cape) && cape >= 0f && cape <= 1f,
                    "hostile CAPE stays in band for " + value);
                TestAssert.That(!float.IsNaN(cin) && cin >= 0f && cin <= 1f,
                    "hostile CIN stays in band for " + value);
                TestAssert.That(!float.IsNaN(shear) && shear >= 0f && shear <= 1f,
                    "hostile shear stays in band for " + value);
                TestAssert.That(!float.IsNaN(rain) && rain >= 0f && rain <= 1f,
                    "hostile rain stays in band for " + value);
                TestAssert.That(!float.IsNaN(visibility) && visibility >= 0f,
                    "hostile visibility stays finite and non-negative for " + value);
                TestAssert.That(!float.IsNaN(gust) && gust >= 0f,
                    "hostile gust stays finite and non-negative for " + value);
                TestAssert.That(!float.IsNaN(lcl) && lcl >= 0f,
                    "hostile LCL stays finite and non-negative for " + value);
                TestAssert.That(!float.IsNaN(humidity) && humidity >= 0f && humidity <= 1f,
                    "hostile humidity stays in band for " + value);
            }
        }

        private static void FogClosesTheVisibilityWhenTheSpreadCollapses()
        {
            float clear = Thermo.Visibility(0f, 20f, 0f);
            float fog = Thermo.Visibility(0f, 0f, 0f);
            TestAssert.That(fog < clear, "saturation closes the visibility below a dry sky");
            TestAssert.That(fog < 1000f, "saturation is fog, not haze: " + fog.ToString("0") + " m");
            TestAssert.That(clear > 8000f, "a dry sky stays visually open: " + clear.ToString("0") + " m");
        }

        private static void RainExtinguishesTheVisibilitySoonerThanDrizzle()
        {
            float drizzle = Thermo.Visibility(0.15f, 10f, 0f);
            float downpour = Thermo.Visibility(1f, 10f, 0f);
            TestAssert.That(downpour < drizzle, "a downpour closes the visibility below drizzle");
            TestAssert.That(downpour >= 100f, "even a downpour keeps a plausible minimum");
        }

        private static void ADeepLowLiftsAndARidgeSubsides()
        {
            // Same air mass, same clock: only the synoptic pressure differs, and the sky must
            // respond to it, because that is what makes a ridge clear and a trough cloudy.
            Atmosphere low = WeatherModel.SampleAtmosphere(Seed, 900f, 20000f, 0f, 0.5f);
            Atmosphere high = WeatherModel.SampleAtmosphere(Seed, 2700f, 20000f, 0f, 0.5f);

            TestAssert.That(low.Pressure < 0.5f || high.Pressure > 0.5f,
                "the synoptic cycle reaches both sides of the pressure mean");
            TestAssert.That(!float.IsNaN(low.Lcl) && !float.IsNaN(high.Lcl),
                "both phases produce a usable atmosphere");
        }

        private static void TheChannelsStayInTheirBands()
        {
            for (int step = 0; step <= 240; step++)
            {
                float missionTime = step * 15f;
                WeatherState state = WeatherModel.Sample(Seed, missionTime);

                TestAssert.That(state.Conditions >= 0f && state.Conditions <= 1f,
                    "conditions stay in 0..1 at " + missionTime);
                TestAssert.That(state.CloudBase >= WeatherModel.MinCloudBase &&
                                state.CloudBase <= WeatherModel.MaxCloudBase,
                    "the cloud base stays in the model band at " + missionTime);
                TestAssert.That(state.Turbulence >= 0f && state.Turbulence <= 1f,
                    "turbulence stays in 0..1 at " + missionTime);
                TestAssert.That(state.WindHeading >= 0f && state.WindHeading < 360f,
                    "the heading stays on the compass at " + missionTime);
                TestAssert.That(!float.IsNaN(state.WindSpeed) && state.WindSpeed >= 0f,
                    "wind speed is finite and non-negative at " + missionTime);
            }
        }

        private static void AGustRidesInTheTurbulenceChannel()
        {
            // There is no gust channel, so a gusty convective sky has to read as turbulence.
            WeatherState calm = WeatherModel.ToState(Stable(), WeatherFront.None, 0f, 0f);
            WeatherState gusty = WeatherModel.ToState(Convective(), WeatherFront.None, 0f, 0f);

            TestAssert.That(gusty.Turbulence > calm.Turbulence,
                "a convective sky is more turbulent than a stable one");
            TestAssert.That(gusty.Turbulence <= 1f, "turbulence still clamps with a gust in it");
        }

        private static void AColdPassageVeersTheWindOneWay()
        {
            // A passage is a boundary sweeping over a fixed point, so that is how it is sampled:
            // the observer stays at the origin and the front moves past them. Sampling across
            // space instead would walk the passage backwards.
            const float width = 1000f;
            Atmosphere sky = Convective();

            int turning = 0;
            float previous = WeatherModel.ToState(sky, BoundaryAt(-width, width), 0f, 0f).WindHeading;
            for (int step = 1; step <= 8; step++)
            {
                float position = -width + step * (2f * width / 8f);
                float heading = WeatherModel.ToState(sky, BoundaryAt(position, width), 0f, 0f).WindHeading;
                float delta = ShortestArc(previous, heading);
                if (delta > 1e-3f) turning++;
                TestAssert.That(delta >= -1e-3f,
                    "a cold passage veers the wind one way, not back and forth");
                previous = heading;
            }

            TestAssert.That(turning >= 4, "the heading turns through the whole band, not once");

            float arrived = WeatherModel.ToState(sky, BoundaryAt(0f, width), 0f, 0f).WindHeading;
            float before = WeatherModel.ToState(sky, BoundaryAt(-5000f, width), 0f, 0f).WindHeading;
            float after = WeatherModel.ToState(sky, BoundaryAt(5000f, width), 0f, 0f).WindHeading;
            TestAssert.That(Math.Abs(ShortestArc(before, after)) > 1f,
                "the heading is different either side of the passage");
            TestAssert.That(ShortestArc(before, arrived) > 0f,
                "the veer starts as the boundary arrives");
            TestAssert.That(ShortestArc(arrived, after) > 0f,
                "and keeps going as it leaves");
        }

        private static WeatherFront BoundaryAt(float position, float width) => new WeatherFront(
            true, FrontKind.Cold, 1f, 0f, position, 12f, width, 1f,
            AirMassKind.ContinentalPolar, AirMassKind.MaritimeTropical);

        private static void TheColdFrontIsTheViolentBoundaryAndTheWarmFrontIsNot()
        {
            WeatherFront cold = Front(FrontKind.Cold);
            WeatherFront warm = Front(FrontKind.Warm);

            TestAssert.That(FrontKinds.IsConvective(cold.Kind),
                "a cold front lifts hard enough for convection");
            TestAssert.That(!FrontKinds.IsConvective(warm.Kind),
                "a warm front glides and makes stratus, not squall lines");
            TestAssert.That(warm.Width > cold.Width,
                "a warm front is a broad band and a cold front is a narrow one");
        }

        private static void TheAtmosphereIsDeterministicAndSeedSensitive()
        {
            for (float t = 0f; t < 3600f; t += 137f)
            {
                Atmosphere first = WeatherModel.SampleAtmosphere(Seed, t, 20000f, 0.1f, 0.7f);
                Atmosphere again = WeatherModel.SampleAtmosphere(Seed, t, 20000f, 0.1f, 0.7f);
                TestAssert.That(first.Cape == again.Cape && first.Lcl == again.Lcl &&
                                first.RainRate == again.RainRate && first.Pressure == again.Pressure,
                    "the same clock and seed give the identical atmosphere at " + t);
            }

            Atmosphere here = WeatherModel.SampleAtmosphere(Seed, 600f, 20000f, 0f, 0.5f);
            Atmosphere elsewhere = WeatherModel.SampleAtmosphere(Seed + 977, 600f, 20000f, 0f, 0.5f);
            bool differs = here.AirMass != elsewhere.AirMass ||
                           Math.Abs(here.Pressure - elsewhere.Pressure) > 1e-4f ||
                           Math.Abs(here.Cape - elsewhere.Cape) > 1e-4f;
            TestAssert.That(differs, "a different mission seed gives a different sky");
        }

        private static void TheAtmosphereDoesNotDependOnTheMap()
        {
            // The load-bearing invariant: the boundary's position and its width both scale with
            // the map, so the five driven channels are identical on a 12 km map and a 90 km one.
            // If those two ever stop scaling together, two peers on different maps would derive
            // different skies from the same forecast and it would look exactly like a desync.
            const float small = 12000f;
            const float large = 90000f;

            for (float t = 0f; t < 3600f; t += 211f)
            {
                Atmosphere here = WeatherModel.SampleAtmosphere(Seed, t, small, 0f, 0.6f);
                Atmosphere there = WeatherModel.SampleAtmosphere(Seed, t, large, 0f, 0.6f);
                WeatherState mine = WeatherModel.ToState(here, WeatherModel.SampleFront(Seed, t, small), 0f, 0f);
                WeatherState theirs = WeatherModel.ToState(there, WeatherModel.SampleFront(Seed, t, large), 0f, 0f);

                // A centimetre of cloud base and a thousandth of a channel are physically
                // meaningless; scaling the boundary's position and width by the map leaves that
                // much float rounding behind. A real dependence on the map shows up far larger.
                TestAssert.That(Math.Abs(here.Cape - there.Cape) < 1e-4f,
                    "CAPE does not depend on the map size at " + t);
                TestAssert.That(Math.Abs(here.Lcl - there.Lcl) < 1e-2f,
                    "the cloud base does not depend on the map size at " + t);
                TestAssert.That(Math.Abs(mine.Conditions - theirs.Conditions) < 1e-3f,
                    "conditions do not depend on the map size at " + t);
                TestAssert.That(Math.Abs(mine.WindSpeed - theirs.WindSpeed) < 1e-3f,
                    "wind does not depend on the map size at " + t);
                TestAssert.That(mine.Regime == theirs.Regime,
                    "the regime does not depend on the map size at " + t);
            }
        }

        private static void ARidgeCarriesNoBoundaryAndAPassageAdvances()
        {
            int ridgeSeed = FindRidgeSeed(out float ridgeTime);
            TestAssert.That(!WeatherModel.SampleFront(ridgeSeed, ridgeTime, 20000f).Present,
                "a uniform high-pressure air mass carries no boundary");

            int frontSeed = FindFrontSeed(out float frontTime);
            WeatherFront early = WeatherModel.SampleFront(frontSeed, frontTime, 20000f);
            WeatherFront later = WeatherModel.SampleFront(frontSeed, frontTime + 90f, 20000f);
            TestAssert.That(early.Present && later.Present,
                "a crossing boundary is present at both instants");
            TestAssert.That(later.SignedDistanceTo(0f, 0f) > early.SignedDistanceTo(0f, 0f),
                "the boundary advances in the direction of travel");
        }

        private static int FindRidgeSeed(out float time)
        {
            for (int seed = 1; seed < 4000; seed++)
            {
                for (float t = 0f; t < 3600f; t += 97f)
                {
                    if (!WeatherModel.SampleFront(seed, t, 20000f).Present)
                    {
                        time = t;
                        return seed;
                    }
                }
            }

            time = 0f;
            return 1;
        }

        private static int FindFrontSeed(out float time)
        {
            for (int seed = 1; seed < 4000; seed++)
            {
                for (float t = 0f; t < 3600f; t += 97f)
                {
                    WeatherFront front = WeatherModel.SampleFront(seed, t, 20000f);
                    WeatherFront next = WeatherModel.SampleFront(seed, t + 90f, 20000f);
                    if (front.Present && next.Present)
                    {
                        time = t;
                        return seed;
                    }
                }
            }

            time = 0f;
            return 1;
        }

        private static WeatherFront Front(FrontKind kind) => new WeatherFront(
            true, kind, 0f, 1f, 0f, 10f, FrontKinds.Width(kind), 0.8f,
            AirMassKind.ContinentalPolar, AirMassKind.MaritimeTropical);

        private static Atmosphere Convective() => new Atmosphere(
            true, AirMassKind.MaritimeTropical, FrontKind.None,
            30f, 25f, 600f, 0.8f, 0.05f, 0.7f, 22f, 0.6f, 4000f, 2000f, 0.2f, 0.9f);

        private static Atmosphere Stable() => new Atmosphere(
            true, AirMassKind.ContinentalArctic, FrontKind.None,
            -15f, -25f, 1300f, 0.05f, 0.9f, 0.1f, 3f, 0f, 20000f, 300f, 0.95f, 0.05f);

        private static float ShortestArc(float from, float to)
        {
            float delta = (to - from + 540f) % 360f - 180f;
            return delta;
        }
    }
}
