using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The sea the ocean shader draws: wave heights by wind, the storm wind, the build-up, and when low aircraft throw spray.</summary>
    internal static class SeaStateTests
    {
        public static void Run()
        {
            var a = new float[SeaState.WaveCount];

            TestAssert.That(Math.Abs(SeaState.Wavelength(0) - 320.0) < 1e-6 && SeaState.Wavelength(SeaState.WaveCount - 1) < 6.0,
                "wavelengths run from 320 m down to a few metres");

            SeaState.Amplitudes(2f, a);
            float calm = SeaState.SignificantHeight(a);
            TestAssert.That(calm > 0.5f && calm < 0.9f, $"a calm sea is the background swell, not glass ({calm:F2} m)");
            TestAssert.That(Math.Abs(calm - SeaState.SwellHeight) < 0.01f,
                "spreading swell across directions preserves its significant height");
            double xx = 0, xz = 0, zz = 0;
            for (int i = 0; i < SeaState.SwellCount; i++)
            {
                TestAssert.That(a[i] > 0.05f, "each swell direction carries visible energy");
                double angle = SeaState.DirectionOffset(i) * Math.PI / 180.0;
                double slope = a[i] * 2.0 * Math.PI / SeaState.Wavelength(i);
                double x = Math.Sin(angle) * slope, z = Math.Cos(angle) * slope;
                xx += x * x; xz += x * z; zz += z * z;
            }
            double discriminant = Math.Sqrt((xx - zz) * (xx - zz) + 4.0 * xz * xz);
            TestAssert.That((xx + zz - discriminant) / (xx + zz + discriminant) > 0.08,
                "calm swell has cross-crest slope energy instead of nearly parallel bands");
            SeaState.Amplitudes(10f, a);
            float fresh = SeaState.SignificantHeight(a);
            TestAssert.That(fresh > 1.6f && fresh < 2.7f, $"10 m/s is about the Pierson-Moskowitz 2.1 m sea ({fresh:F2} m)");
            SeaState.Amplitudes(30f, a);
            float storm = SeaState.SignificantHeight(a);
            TestAssert.That(storm > 3.8f && storm <= SeaState.HeightCap + 1e-3f, $"a storm reaches but never passes the cap ({storm:F2} m)");
            float last = 0f;
            for (int u = 0; u <= 30; u += 2)
            {
                SeaState.Amplitudes(u, a);
                float hs = SeaState.SignificantHeight(a);
                TestAssert.That(hs >= last - 1e-4f, $"the sea never shrinks as the wind rises ({u} m/s)");
                last = hs;
            }

            TestAssert.That(Math.Abs(SeaState.SeaWind(3f, 1f, 1f) - 22f) < 1e-4f, "full rain under full cover is a 22 m/s gale");
            TestAssert.That(SeaState.SeaWind(6f, 0.2f, 0f) == 6f, "a fair sky leaves the mission wind");
            TestAssert.That(SeaState.SeaWind(25f, 1f, 1f) == 25f, "a stronger mission wind wins over the storm");
            TestAssert.That(SeaState.SeaWindFromConditions(4f, 0.5f) == 4f && SeaState.SeaWindFromConditions(4f, 1f) > 21f,
                "without the weather field the game's storm conditions raise the sea");

            TestAssert.That(SeaState.Ease(-1f, 12f, 0.016f) == 12f, "the first frame snaps to the sea");
            float eased = 3f;
            for (int i = 0; i < 60 * 45; i++) eased = SeaState.Ease(eased, 22f, 1f / 60f);
            TestAssert.That(eased > 14f && eased < 22f, $"the sea builds over about 45 s ({eased:F1} m/s after 45 s)");
            TestAssert.That(SeaState.Ease(10f, 4f, 1000f) >= 4f, "easing never overshoots");

            TestAssert.That(SeaState.Whitecaps(1f) < 0.001f && SeaState.Whitecaps(15f) > SeaState.Whitecaps(10f) && SeaState.Whitecaps(40f) <= 0.6f,
                "whitecaps grow with the wind and stay bounded");
            TestAssert.That(Math.Abs(SeaState.WhitecapSigma(0.5f)) < 1e-3f && Math.Abs(SeaState.WhitecapSigma(0.158655f) - 1f) < 1e-3f
                && Math.Abs(SeaState.WhitecapSigma(0.0013499f) - 3f) < 1e-3f, "whitecaps start at the normal quantile of their coverage");
            TestAssert.That(SeaState.Streaks(10f) == 0f && SeaState.Streaks(22f) == 1f, "streaks only in a gale");

            float ceiling = SeaState.SprayCeiling(8f);
            TestAssert.That(SeaState.SprayIntensity(ceiling + 1f, 250f, 8f) == 0f, "no spray above the ceiling");
            TestAssert.That(SeaState.SprayIntensity(3f, 15f, 8f) == 0f, "no spray from a slow aircraft (vanilla downwash covers hovering)");
            TestAssert.That(SeaState.SprayIntensity(3f, 250f, 8f) > 0.6f, "a fast pass just over the water throws heavy spray");
            TestAssert.That(SeaState.SprayIntensity(5f, 200f, 8f) > SeaState.SprayIntensity(15f, 200f, 8f), "lower is wetter");
            TestAssert.That(SeaState.WashIntensity(1.6f * ceiling + 1f, 8f) == 0f && SeaState.WashIntensity(5f, 8f) > 0.5f,
                "the wash under a low aircraft fades out above 1.6 ceilings");
        }
    }
}
