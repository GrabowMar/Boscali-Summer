using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>Altitude bounds from the baked tape (spec 7.3): by potential extent, the far bounds contain the near ones, and both reach the row's own deck top.</summary>
    internal static class TapeBoundsTests
    {
        public static void Run() { BoundsContainTheTape(); BoundsContainTheClouds(); }

        private static void BoundsContainTheTape()
        {
            WeatherTape tape = WeatherTape.For(new WeatherKey(9u, 0f, false, 1, story: 2, headingDeg: 250f));
            BakedTape baked = tape.Baked;
            for (float s = 0f; s < 2800000f; s += 150000f)
            {
                TapeBounds.Compute(baked, s, TapeBounds.NearHalfWidth, out float nb, out float nt);
                TapeBounds.Compute(baked, s, TapeBounds.FarHalfWidth, out float fb, out float ft);
                TestAssert.That(fb <= nb && ft >= nt, "the far bounds contain the near bounds");
                SkyParams row = baked.Texel((int)(s / baked.TexelMetres));
                float lowTop = row.CloudBase + Math.Max(row.PuffDepth, row.DeckDepth);
                TestAssert.That(nt >= lowTop || (row.SheetCover < 0.02f && row.CumulusCover < 0.02f && row.Convective < 0.02f), "the bounds reach the row's own deck top");
            }
        }

        /// <summary>The contract the density check also pins on the GPU: every sampled cloud base and top lies inside the far bounds (rain lowers a base, a storm crown rises).</summary>
        private static void BoundsContainTheClouds()
        {
            foreach (WeatherRegimeType state in new[] { WeatherRegimeType.Scattered, WeatherRegimeType.Broken, WeatherRegimeType.Overcast, WeatherRegimeType.RainSquall, WeatherRegimeType.Storm })
            for (uint seed = 70; seed < 76; seed++)
            {
                var field = new WeatherField();
                field.Build(new WeatherKey(seed, 0f, false, (byte)state), 600f, 40000f, 40000f);
                TapeBounds.Compute(field.Tape.Tape, field.Frame.S(0f, 0f), TapeBounds.FarHalfWidth, out float bottom, out float top);
                for (int z = -30000; z <= 30000; z += 7500)
                for (int x = -30000; x <= 30000; x += 7500)
                {
                    WeatherPoint p = field.Sample(x, z);
                    SkyParams row = field.RowAt(x, z);
                    // A clear row's sub-percent noise cover (a few hundredths) is not a cloud the march must reach.
                    if (p.Cover < 0.02f || (row.SheetCover < 0.02f && row.CumulusCover < 0.02f && row.Convective < 0.02f && row.HorizonCover < 0.02f)) continue;
                    TestAssert.That(p.CloudBase >= bottom - 0.5f && Math.Max(p.CloudTop, p.LowTop) <= top + 0.5f,
                        state + " " + seed + " at " + x + "," + z + ": " + p.CloudBase + ".." + p.LowTop + " outside " + bottom + ".." + top);
                }
            }
        }
    }
}
