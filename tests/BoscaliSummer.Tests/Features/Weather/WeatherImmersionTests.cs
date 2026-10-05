using System;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Visuals;

namespace BoscaliSummer.Tests.Features.Weather
{
    internal static class WeatherImmersionTests
    {
        public static void Run()
        {
            var point = new WeatherPoint { CloudBase = 1000f, CloudTop = 3000f };
            var cloud = FlightWeatherAirMass.Evaluate(point, 1500f, 0.6f, null);
            TestAssert.That(cloud.CloudMoisture > 0.8f && cloud.VisualRain == 0f && cloud.Atmosphere == 0f,
                "A dry dense cloud condenses on glass without rain or duplicate atmospheric extinction");
            TestAssert.That(FlightWeatherAirMass.Evaluate(point, 1500f, 0.1f, null).CloudMoisture < cloud.CloudMoisture,
                "Cloud edge moisture is weaker than core moisture");
            TestAssert.That(FlightWeatherAirMass.Evaluate(point, 9000f, 0f, 0.4f).Rain == 0.4f,
                "Explicit rain override preserves its altitude-independent semantics");
            TestAssert.That(Math.Abs(FlightWeatherAirMass.VerticalRain(point, 1000f) -
                FlightWeatherAirMass.VerticalRain(point, 1000.01f)) < 0.001f, "Precipitation is continuous at base");
            TestAssert.That(Math.Abs(LightningMath.ThunderDelay(6860f) - 20f) < 0.001f,
                "Distant thunder is not artificially clamped to eight seconds");
            TestAssert.That(AtmosphericSurfaceMath.ColdTarget(-30f, 0f) == 0f,
                "Dry cold altitude cannot create frost");
            TestAssert.That(AtmosphericSurfaceMath.ColdTarget(5f, 1f) == 0f &&
                AtmosphericSurfaceMath.ColdTarget(-8f, 1f) > 0f, "Cold dressing needs subzero moist exposure");
            TestAssert.That(Math.Abs(AtmosphericSurfaceMath.AdvanceWetness(0f, 1f, 35f) - 1f) < 0.001f &&
                AtmosphericSurfaceMath.AdvanceWetness(1f, 0f, 90f) > 0f,
                "Surface histories wet over 35 seconds and retain residual water while drying");
            var field = new WeatherField();
            field.Build(new WeatherKey(73, 0f, false, (byte)WeatherRegimeType.Storm), 60f, 40000f, 40000f);
            int events = 0;
            for (int slot = 0; slot < 300; slot++)
            for (int source = 0; source < field.CellCount + field.SuperstructureCount; source++)
            {
                bool a = StormLightning.TryEvent(field, source, slot, out LightningEvent first);
                bool b = StormLightning.TryEvent(field, source, slot, out LightningEvent second);
                TestAssert.That(a == b && first.Time == second.Time && first.X == second.X,
                    "Storm events derive identically from field and mission-time slot");
                if (!a) continue;
                events++;
                TestAssert.That(first.Time >= slot * StormLightning.SlotSeconds &&
                    first.Time < (slot + 1) * StormLightning.SlotSeconds && first.Y > first.BottomY,
                    "Strike event remains inside its time slot and has a descending bolt");
            }
            TestAssert.That(events > 0, "Storm field produces spatial strikes without observer-rain gating");

            // Isolate hanging rain cloud from the deck with constant body/detail noise.
            byte[] noise = new byte[2 * 2 * 2 * 4];
            for (int i = 0; i < noise.Length; i += 4)
            { noise[i] = 255; noise[i + 1] = 128; noise[i + 2] = 255; noise[i + 3] = 255; }
            var scud = new CloudBodies(noise, 2, new StateParams { LayerDepth = 1200f }, nearDetail: true);
            var rainyColumn = new WeatherPoint { BackgroundCover = 1f, CloudBase = 1000f, CloudTop = 2500f, RainRate = 20f };
            TestAssert.That(scud.Density(rainyColumn, 0f, 500f, 0f, 0f) > 0.2f &&
                scud.Density(rainyColumn, 0f, 200f, 0f, 0f) == 0f,
                "View occupation includes scud 500 m beneath a rainy base, with a bounded lower edge");
            TestAssert.That(scud.Density(rainyColumn, 0f, 1700f, 0f, 1200f) > 0.2f,
                "Cloud altitude shift moves scud and its occupation together");

            field.Build(new WeatherKey(73, 0f, false, (byte)WeatherRegimeType.Clear, 5f, 60f,
                Superstructures.SupercellSet), 60f, 40000f, 40000f);
            Superstructure hero = field.SuperstructureAt(0);
            var settledHero = new CloudBodies(noise, 2, field.Params, field: field);
            var hiddenHero = new CloudBodies(noise, 2, field.Params, field: field,
                shownHeroes: new float[Superstructures.MaxCount], nearDetail: true);
            TestAssert.That(settledHero.Heroes(hero.X, 3000f, hero.Z) > 0.5f &&
                hiddenHero.Heroes(hero.X, 3000f, hero.Z) == 0f,
                "Cloud moisture follows displayed set-piece fade, not its final field strength");

            byte[] structure = new byte[16], profile = new byte[16];
            structure[0] = 255;
            profile[3] = 51;
            WeatherPoint displayed = CloudBodies.SampleMap(structure, profile, 2, 0.5f, 0.5f);
            TestAssert.That(Math.Abs(displayed.BackgroundCover - 0.25f) < 0.001f &&
                Math.Abs(displayed.RainRate - 5f) < 0.001f,
                "Displayed map probes bilinearly sample texel centers like the GPU");
            TestAssert.That(CloudBodies.SampleMap(structure, profile, 2, -1f, 0.25f).BackgroundCover == 1f,
                "Displayed map probes clamp texture edges like the GPU");
            WeatherPoint oldClear = default;
            TestAssert.That(CloudBodies.BlendMaps(oldClear, displayed, 0f).BackgroundCover == 0f &&
                Math.Abs(CloudBodies.BlendMaps(oldClear, displayed, 0.5f).BackgroundCover - 0.125f) < 0.001f,
                "Cloud entry follows held clear maps and their displayed fade instead of the arriving field");
            CloudBodies.MapWeights(96f, 0f, 100f, 400f, out float nearWeight, out float farFade);
            TestAssert.That(Math.Abs(nearWeight - 0.5f) < 0.001f && farFade == 1f,
                "Displayed near and far maps blend continuously through the GPU border band");
            WeatherPoint overlap = CloudBodies.BlendMaps(new WeatherPoint { BackgroundCover = 0.4f, CloudBase = 1000f },
                new WeatherPoint { BackgroundCover = 0.8f, CloudBase = 2000f }, nearWeight);
            TestAssert.That(Math.Abs(overlap.BackgroundCover - 0.6f) < 0.001f && Math.Abs(overlap.CloudBase - 1500f) < 1f,
                "Near/far mixing preserves the same coverage and height interpolation");
            CloudBodies.MapWeights(368f, 0f, 100f, 400f, out nearWeight, out farFade);
            TestAssert.That(nearWeight == 0f && Math.Abs(farFade - 0.5f) < 0.001f,
                "Far map coverage fades without collapsing its heights");
            CloudBodies.MapWeights(401f, 0f, 100f, 400f, out nearWeight, out farFade);
            TestAssert.That(farFade == 0f, "Outside displayed far maps cannot invent regular cloud occupation");
            float sampledX = float.NaN, sampledZ = float.NaN;
            var heldWarp = new CloudBodies(noise, 2, new StateParams { LayerDepth = 1200f }, nearDetail: true,
                weatherSampler: (x, z) => { sampledX = x; sampledZ = z; return rainyColumn; });
            TestAssert.That(heldWarp.Density(default, 100f, 500f, 200f, 0f) > 0.2f &&
                Math.Abs(sampledX - 1500f) < 0.1f && sampledZ > 200f && sampledZ < 210f,
                "Boundary warp resamples the displayed map, preserving held precipitation and cloud occupation");
        }
    }
}
