using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The sky summary of a point (spec 8.2): the deck the natives follow, the layer the rain falls from, the opacity-weighted cover and the sampled class.</summary>
    internal static class SkySummaryTests
    {
        public static void Run()
        {
            NativeDeckRule();
            RainLayerRule();
            SkyCoverIsOpacityWeighted();
            ThroughTheField();
            ClassSampledSharesTheNominalFormula();
        }

        private static void NativeDeckRule()
        {
            SkySummary.NativeDeck(0.3f, 1500f, 2000f, 0.9f, 1f, 3300f, 4500f, 0f, 0f, 0f, 0f, out float b, out float t);
            TestAssert.That(b == 3300f && t == 4500f, "an opaque altostratus above a thin low deck is the deck a pilot is under (opacity 0.9 x 1.0)");
            SkySummary.NativeDeck(0.6f, 900f, 1400f, 0.9f, 1f, 3300f, 4500f, 0f, 0f, 0f, 0f, out b, out t);
            TestAssert.That(b == 900f && t == 1400f, "the lowest opaque layer wins");
            SkySummary.NativeDeck(0.3f, 1500f, 2000f, 0.4f, 0.5f, 4200f, 4700f, 0f, 0f, 0f, 0f, out b, out t);
            TestAssert.That(b == 1500f, "no opaque layer: the lowest layer with cover (0.4 x 0.5 = 0.2 is not opaque)");
            SkySummary.NativeDeck(0.02f, 1500f, 2000f, 0f, 0f, 0f, 0f, 0.2f, 0.25f, 8500f, 9400f, out b, out t);
            TestAssert.That(b == 8500f && t == 9400f, "a cirrostratus veil alone is the deck (the clamp lifts to 9500 m)");
            SkySummary.NativeDeck(0.01f, 1500f, 2000f, 0.01f, 1f, 3300f, 4500f, 0.01f, 1f, 8500f, 9400f, out b, out t);
            TestAssert.That(b == 1500f && t == 2000f, "clear sky: the nominal low base and top");
            SkySummary.NativeDeck(1f, 100f, 200f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, out b, out t);
            TestAssert.That(b == SkySummary.MinDeck, "the floor is 250 m");
            SkySummary.NativeDeck(0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 12000f, 12500f, out b, out t);
            TestAssert.That(b == SkySummary.MaxDeck && t >= b, "the ceiling is 9500 m and the top never falls under the base");
        }

        private static void RainLayerRule()
        {
            SkySummary.RainLayer(0f, 4f, 1f, 1f, 800f, 3300f, 3040f, 4500f, out float b, out float t);
            TestAssert.That(b == 800f && t == 3300f, "steady rain under a full low sheet falls from the low layer (embedded altostratus does not take it)");
            SkySummary.RainLayer(0f, 2.5f, 0f, 1f, 600f, 700f, 3300f, 4800f, out b, out t);
            TestAssert.That(b == 3300f && t == 4800f, "embedded showers with no low sheet fall from the altostratus");
            SkySummary.RainLayer(20f, 2.5f, 0f, 1f, 900f, 4000f, 3300f, 4800f, out b, out t);
            TestAssert.That(b == 900f && t == 4000f, "cell rain is the low layer even under an opaque altostratus");
            SkySummary.RainLayer(0f, 0f, 0f, 0.9f, 1500f, 2000f, 3300f, 4800f, out b, out t);
            TestAssert.That(b == 1500f && t == 2000f, "a dry point reports the low layer, the old default");
        }

        private static void SkyCoverIsOpacityWeighted()
        {
            TestAssert.Near(SkyMath.SkyCover(0f, 0f, 0f, 0.45f, 0.25f), 0.1125f, "a 0.45 cirrostratus veil of opacity 0.25 is 11% of the sky");
            TestAssert.That(SkyMath.SkyCover(0f, 1f, 1f, 0f, 0f) >= 0.99f, "an opaque altostratus covers the sky");
            TestAssert.That(SkyMath.SkyCover(0.5f, 0.5f, 0.6f, 0.5f, 0.2f) > SkyMath.SkyCover(0.5f, 0.5f, 0.3f, 0.5f, 0.2f), "more opacity, more cover");
        }

        private static WeatherPoint Centre(int sky, uint seed = 11u)
        {
            var field = new WeatherField();
            field.Build(new WeatherKey(seed, 0f, false, (byte)SkyLibrary.ClassOf(sky), skyPin: (byte)sky), 600f, 40960f, 40960f);
            return field.Sample(0f, 0f);
        }

        private static void ThroughTheField()
        {
            for (int sky = 0; sky < SkyLibrary.Count; sky++)
            {
                WeatherPoint p = Centre(sky);
                TestAssert.That(p.SkyCover >= 0f && p.SkyCover <= 1f && p.SkyCover + 1e-4f >= p.LowCover, "sky " + (sky + 1) + ": SkyCover is a cover and is at least the low cover");
                TestAssert.That(p.NativeDeck >= SkySummary.MinDeck && p.NativeDeck <= SkySummary.MaxDeck && p.NativeDeckTop >= p.NativeDeck, "sky " + (sky + 1) + ": a deck inside 250-9500 m");
                TestAssert.That(p.RainBase <= p.RainTop && !float.IsNaN(p.RainBase + p.RainTop), "sky " + (sky + 1) + ": a rain layer");
                TestAssert.That(p.Convective >= 0f && p.Convective <= 1f, "sky " + (sky + 1) + ": Convective is the row's share");
            }
            WeatherPoint veil = Centre(3), opaque = Centre(32), steady = Centre(34), embedded = Centre(37);
            TestAssert.That(veil.SkyCover < 0.6f, "the cirrostratus veil (sky 4) stays under the vanilla gate: the sun stays on (SkyCover " + veil.SkyCover + ")");
            TestAssert.That(opaque.SkyCover >= 0.6f && opaque.NativeDeck > 2500f, "altostratus opacus (sky 33) is overcast and its base is the deck (" + opaque.NativeDeck + " m)");
            TestAssert.That(Math.Abs(steady.NativeDeck - steady.CloudBase) < 1f, "steady rain (sky 35): the deck is the nimbostratus base");
            TestAssert.That(embedded.RainBase >= 2500f, "embedded showers (sky 38) fall from the altostratus, not from a low layer (RainBase " + embedded.RainBase + ")");
        }

        private static void ClassSampledSharesTheNominalFormula()
        {
            for (int sky = 0; sky < SkyLibrary.Count; sky++)
            {
                var field = new WeatherField();
                field.Build(new WeatherKey(5u, 0f, false, (byte)SkyLibrary.ClassOf(sky), skyPin: (byte)sky), 600f, 40960f, 40960f);
                WeatherPoint p = field.Sample(0f, 0f);
                SkyParams row = field.RowAt(0f, 0f);
                float nominal = SkyMath.ClassConditions(row);
                TestAssert.That(p.ClassSampled >= 0f && p.ClassSampled <= 1f, "sky " + (sky + 1) + ": ClassSampled is a conditions value");
                if (row.CumulusCover == 0f && row.Convective == 0f)
                    TestAssert.That(Math.Abs(p.ClassSampled - nominal) < 0.15f, "sky " + (sky + 1) + ": with no lattice the sampled class follows the nominal one (" + p.ClassSampled + " vs " + nominal + ")");
            }
            TestAssert.Near(SkyMath.ClassConditions(0.9f, 0f, 0f, 0f), 0.680f, "an overcast sheet of 0.90 is 0.680, the old Overcast row (spec 8.2)");
            TestAssert.Near(SkyMath.ClassConditions(1f, 3f, 0f, 0f), 0.837f, "steady rain of 3 mm/h under a full deck is 0.837");
        }
    }
}
