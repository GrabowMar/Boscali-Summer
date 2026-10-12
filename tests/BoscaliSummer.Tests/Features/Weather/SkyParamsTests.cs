using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The sky row (spec 4.1): its column table, the blend, the ladders, the derived columns, the cumulus level and the class measure.</summary>
    internal static class SkyParamsTests
    {
        public static void Run()
        {
            ColumnTableCoversEveryField();
            GpuSlotsFollowTheTextureLayout();
            LerpInheritsAnAbsentLayersGeometry();
            LadderValuesAndSplit();
            LadderMixKeepsContrast();
            DerivedColumnsFollowTheirFormulas();
            MeanStrengthCurve();
            ClassConditionsHitsItsAnchors();
            ClassConditionsIsMonotone();
        }

        private static List<FieldInfo> FloatFields(Type type)
        {
            var list = new List<FieldInfo>();
            foreach (FieldInfo f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (f.FieldType == typeof(float)) list.Add(f);
            return list;
        }

        /// <summary>Fills every float of a boxed SkyParams (the two layers included) with distinct values.</summary>
        private static SkyParams Filled(float seed)
        {
            object box = new SkyParams();
            int n = 0;
            foreach (FieldInfo f in FloatFields(typeof(SkyParams))) f.SetValue(box, seed + (++n) * 0.37f);
            object mid = new SkyLayer(), high = new SkyLayer();
            foreach (FieldInfo f in FloatFields(typeof(SkyLayer))) { f.SetValue(mid, seed + (++n) * 0.37f); f.SetValue(high, seed + (++n) * 0.37f); }
            typeof(SkyParams).GetField("Mid").SetValue(box, mid);
            typeof(SkyParams).GetField("High").SetValue(box, high);
            return (SkyParams)box;
        }

        private static void ColumnTableCoversEveryField()
        {
            int fields = FloatFields(typeof(SkyParams)).Count + 2 * FloatFields(typeof(SkyLayer)).Count;
            TestAssert.That(fields == SkyColumns.Count,
                "every float of SkyParams and both layers is a column: " + fields + " fields, " + SkyColumns.Count + " columns");
            TestAssert.That(SkyColumns.Names.Length == SkyColumns.Count && SkyColumns.GpuRow.Length == SkyColumns.Count &&
                SkyColumns.GpuChannel.Length == SkyColumns.Count, "the name and slot tables are as long as the column table");

            SkyParams filled = Filled(1f);
            var row = new float[SkyColumns.Count];
            SkyColumns.Write(filled, row, 0);
            SkyParams back = SkyColumns.Read(row, 0);
            var seen = new HashSet<float>();
            foreach (float v in row) TestAssert.That(seen.Add(v), "Write puts each field in exactly one column");
            foreach (FieldInfo f in FloatFields(typeof(SkyParams)))
                TestAssert.That((float)f.GetValue(filled) == (float)f.GetValue(back), "round trip " + f.Name);
            foreach (string layer in new[] { "Mid", "High" })
            {
                object a = typeof(SkyParams).GetField(layer).GetValue(filled), b = typeof(SkyParams).GetField(layer).GetValue(back);
                foreach (FieldInfo f in FloatFields(typeof(SkyLayer)))
                    TestAssert.That((float)f.GetValue(a) == (float)f.GetValue(b), "round trip " + layer + "." + f.Name);
            }
            // The names agree with the layout the generator wrote: the first and last of each group.
            TestAssert.That(SkyColumns.Names[0] == "LobeRung" && SkyColumns.Names[11] == "HorizonDepth" && SkyColumns.Names[12] == "MidBase" &&
                SkyColumns.Names[26] == "HighBase" && SkyColumns.Names[40] == "CloudBase", "column groups sit where the texture layout expects them");
        }

        private static void GpuSlotsFollowTheTextureLayout()
        {
            var used = new HashSet<int>();
            int gpu = 0;
            for (int c = 0; c < SkyColumns.Count; c++)
            {
                int row = SkyColumns.GpuRow[c], channel = SkyColumns.GpuChannel[c];
                if (row < 0) { TestAssert.That(channel < 0 && c >= 40, "CPU-only columns have no slot"); continue; }
                gpu++;
                TestAssert.That(row <= 10 && channel >= 0 && channel <= 3, "slot inside the 11 x 4 texture");
                TestAssert.That(used.Add(row * 4 + channel), "no two columns share a slot: " + SkyColumns.Names[c]);
            }
            TestAssert.That(gpu == 40, "12 low genus columns and 14 per thin layer reach the shader, got " + gpu);
            int At(string name) => Array.IndexOf(SkyColumns.Names, name);
            TestAssert.That(SkyColumns.GpuRow[At("LobeRung")] == 0 && SkyColumns.GpuChannel[At("LobeRung")] == 0 &&
                SkyColumns.GpuRow[At("Dome")] == 1 && SkyColumns.GpuRow[At("DeckSmooth")] == 2 && SkyColumns.GpuChannel[At("HorizonDepth")] == 3,
                "rows 0-2 are LobeRung.. / Dome.. / DeckSmooth.. (spec 7.2)");
            TestAssert.That(SkyColumns.GpuRow[At("MidBase")] == 3 && SkyColumns.GpuRow[At("MidAspect")] == 6 && SkyColumns.GpuRow[At("HighBase")] == 7 &&
                SkyColumns.GpuRow[At("HighRippleWavelength")] == 10 && SkyColumns.GpuChannel[At("HighRippleWavelength")] == 1,
                "rows 3-6 are the mid layer and 7-10 the high layer, two slots spare");
        }

        private static void LerpInheritsAnAbsentLayersGeometry()
        {
            SkyParams none = default;
            SkyParams deck = default;
            deck.Mid = new SkyLayer { Base = 4200f, Thick = 500f, Cover = 0.5f };
            SkyParams quarter = SkyParams.Lerp(none, deck, 0.25f);
            TestAssert.That(quarter.Mid.Base == 4200f && quarter.Mid.Thick == 500f,
                "a layer fading in keeps its altitude, it does not sweep up from the ground");
            TestAssert.That(Math.Abs(quarter.Mid.Cover - 0.125f) < 1e-6f, "its cover fades linearly");
            SkyParams out3 = SkyParams.Lerp(deck, none, 0.75f);
            TestAssert.That(out3.Mid.Base == 4200f && Math.Abs(out3.Mid.Cover - 0.125f) < 1e-6f, "and a layer fading out keeps it too");
            SkyParams lowDeck = default;
            lowDeck.Mid = new SkyLayer { Base = 3300f, Thick = 1800f, Cover = 1f };
            SkyParams between = SkyParams.Lerp(deck, lowDeck, 0.5f);
            TestAssert.That(Math.Abs(between.Mid.Base - 3750f) < 1e-3f && Math.Abs(between.Mid.Thick - 1150f) < 1e-3f,
                "two present layers blend their altitudes");
            SkyParams plain = SkyParams.Blend(none, deck, 0.25f);
            TestAssert.That(Math.Abs(plain.Mid.Base - 1050f) < 1e-3f, "Blend is the plain lerp the texture does, with no inheritance");
            TestAssert.That(SkyParams.Lerp(deck, lowDeck, 0f).Mid.Base == 4200f && SkyParams.Lerp(deck, lowDeck, 1f).Mid.Base == 3300f,
                "the ends are exact");
            SkyParams streaked = default;
            streaked.Mid = new SkyLayer { Base = 8800f, Thick = 500f, Cover = 0.3f, PeriodRung = 10f, Aspect = 5f, RippleWavelength = 900f };
            SkyParams fade = SkyParams.Lerp(none, streaked, 0.1f);
            TestAssert.That(fade.Mid.PeriodRung == 10f && fade.Mid.Aspect == 5f && fade.Mid.RippleWavelength == 900f,
                "a layer fading in keeps its feature size and aspect: no sweep through scales, no divide by an absent layer's zero aspect");
            SkyParams other = streaked;
            other.Mid.Aspect = 3f; other.Mid.RippleWavelength = 600f;
            TestAssert.That(SkyParams.Lerp(streaked, other, 0.49f).Mid.Aspect == 5f && SkyParams.Lerp(streaked, other, 0.51f).Mid.Aspect == 3f &&
                SkyParams.Lerp(streaked, other, 0.3f).Mid.RippleWavelength == 900f && SkyParams.Lerp(streaked, other, 0.7f).Mid.RippleWavelength == 600f,
                "two present layers hold aspect and ripple wavelength at the nearer side (spec 6: never sample a scale that varies)");
            TestAssert.That(Math.Abs(SkyParams.Blend(streaked, other, 0.5f).Mid.Aspect - 4f) < 1e-6f, "the texel lerp is a plain lerp");
        }

        private static void LadderValuesAndSplit()
        {
            float[] lobes = { 700f, 990f, 1400f, 1980f, 2800f, 3960f, 5600f };
            for (int i = 0; i < lobes.Length; i++)
                TestAssert.That(Math.Abs(Ladder.LobeMetres(i) - lobes[i]) < lobes[i] * 0.005f, "lobe rung " + i + " is " + lobes[i] + " m");
            float[] periods = { 250f, 354f, 500f, 707f, 1000f, 1414f, 2000f, 2828f, 4000f, 5657f, 8000f, 11314f, 16000f };
            for (int i = 0; i < periods.Length; i++)
                TestAssert.That(Math.Abs(Ladder.PeriodMetres(i) - periods[i]) < periods[i] * 0.005f, "period rung " + i + " is " + periods[i] + " m");
            Ladder.Split(2f, out float r0, out float w);
            TestAssert.That(r0 == 2f && w == 0f, "an integer rung is one rung");
            Ladder.Split(2.5f, out r0, out w);
            TestAssert.That(r0 == 2f && Math.Abs(w - 0.5f) < 1e-6f, "the middle of a ramp weighs both rungs equally");
            Ladder.Split(2.3f, out _, out w);
            TestAssert.That(w == 0f && !Ladder.NeedsSecond(w), "below frac 0.35 the second rung has no weight");
            Ladder.Split(2.7f, out _, out w);
            TestAssert.That(w == 1f && !Ladder.NeedsSecond(w), "above frac 0.65 the second rung is the only one");
            float previous = 0f;
            for (float i = 2f; i < 2.995f; i += 0.01f)
            {
                Ladder.Split(i, out _, out float wi);
                TestAssert.That(wi >= previous - 1e-6f && wi - previous < 0.06f, "the weight rises smoothly");
                previous = wi;
            }
            TestAssert.That(Ladder.NearestLobeRung(1400f) == 2f && Ladder.NearestLobeRung(4600f) == 5f && Ladder.NearestLobeRung(50f) == 0f &&
                Ladder.NearestLobeRung(90000f) == 6f, "nearest rung clamps to the ladder");
            TestAssert.That(Ladder.NearestPeriodRung(2600f) == 7f && Ladder.NearestPeriodRung(5000f) == 9f, "the old slab scales sit on rungs 7 and 9");
            TestAssert.That(Math.Abs(Ladder.EdgeScale(700f) - 0.504f) < 1e-4f && Ladder.EdgeScale(5600f) == 2f &&
                Math.Abs(Ladder.EdgeScale(1400f) - 1.008f) < 1e-3f, "edge texture follows the lobe: 600..2500 m over 1250");
        }

        private static void LadderMixKeepsContrast()
        {
            TestAssert.That(Ladder.Mix(0.3f, 0.9f, 0f) == 0.3f && Ladder.Mix(0.3f, 0.9f, 0.01f) == 0.3f &&
                Ladder.Mix(0.3f, 0.9f, 1f) == 0.9f && Ladder.Mix(0.3f, 0.9f, 0.99f) == 0.9f, "outside 0.02..0.98 one rung decides");
            // Two independent hashed fields of equal spread: the mid-ramp mix must keep that spread, not lose 30 %.
            double sum0 = 0, sum2 = 0, sumM = 0, sumMLinear = 0;
            const int n = 4000;
            for (int i = 0; i < n; i++)
            {
                float a = BoscaliSummer.Core.Math.Deterministic.UnitFloat(BoscaliSummer.Core.Math.Deterministic.Hash(i, 1, 7)) ;
                float b = BoscaliSummer.Core.Math.Deterministic.UnitFloat(BoscaliSummer.Core.Math.Deterministic.Hash(i, 2, 7));
                sum0 += (a - 0.5) * (a - 0.5);
                float mixed = Ladder.Mix(a, b, 0.5f);
                sumM += (mixed - 0.5) * (mixed - 0.5);
                float linear = (a + b) * 0.5f;
                sumMLinear += (linear - 0.5) * (linear - 0.5);
                sum2 += (b - 0.5) * (b - 0.5);
            }
            double spread = Math.Sqrt(sum0 / n), mixSpread = Math.Sqrt(sumM / n), linearSpread = Math.Sqrt(sumMLinear / n);
            TestAssert.That(Math.Abs(mixSpread / spread - 1.0) < 0.06, "the mid-ramp mix keeps the field's contrast (" + mixSpread / spread + ")");
            TestAssert.That(linearSpread / spread < 0.8, "a plain lerp would lose it (" + linearSpread / spread + ")");
            TestAssert.That(Ladder.Mix(0.5f, 0.5f, 0.5f, 0.52f) != 0.5f, "the mean is a parameter (0.52 for the thin layers)");
        }

        private static void DerivedColumnsFollowTheirFormulas()
        {
            var sky = new SkyParams { OpacityMid = 0.7f, OpacityHigh = 0.15f, SheetCover = 0f, CumulusCover = 0.15f, CapacityCover = 0.36f,
                PuffDepth = 500f, DeckDepth = 0f, Convective = 0f };
            SkyDerived.Apply(ref sky);
            TestAssert.That(Math.Abs(sky.Mid.Strength - 0.42f) < 1e-6f && Math.Abs(sky.High.Strength - 0.12f) < 1e-6f,
                "Strength is 0.6 x OpacityMid and 0.8 x OpacityHigh: today's 0.42 and 0.12 at the old opacities");
            TestAssert.That(Math.Abs(sky.HorizonCover - 0.795f) < 1e-6f, "horizon cover of sky 8 is HorizonStack 5.3 x 0.15");
            TestAssert.That(sky.HorizonDepth == 300f, "Humilis horizon depth is the 300 m floor");
            TestAssert.That(Math.Abs(sky.CumulusLevel - 0.430f) < 0.001f, "sky 8's cumulus level is 0.430, got " + sky.CumulusLevel);

            sky = new SkyParams { SheetCover = 0.60f, CumulusCover = 0.30f, Convective = 0.7f, PuffDepth = 2000f, DeckDepth = 0f, CapacityCover = 0.65f };
            SkyDerived.Apply(ref sky);
            TestAssert.That(Math.Abs(sky.HorizonCover - 1f) < 1e-6f, "sky 42: min(1, 5.3 x 0.30 + 0.14) = 1");
            TestAssert.That(sky.HorizonDepth == 1000f, "a Cb deck is half its puff depth deep");
            sky = new SkyParams { SheetCover = 1f, DeckDepth = 500f, PuffDepth = 500f };
            SkyDerived.Apply(ref sky);
            TestAssert.That(Math.Abs(sky.HorizonCover - 0.9f) < 1e-6f && sky.HorizonDepth == 500f, "a sheet keeps the old 0.9 floor");
            sky = new SkyParams();
            SkyDerived.Apply(ref sky);
            TestAssert.That(sky.HorizonCover == 0f && sky.CumulusLevel == 0f, "a keyframe with no sheet, cumulus or convective term gets 0");

            TestAssert.That(Math.Abs(CumulusLevels.Mu(0.15f, 0.36f) - 0.364f) < 0.001f && Math.Abs(CumulusLevels.Mu(0.08f, 0.36f) - 0.187f) < 0.001f,
                "mu = ln(1 - Cu) / ln(1 - Cap)");
            TestAssert.That(Math.Abs(CumulusLevels.For(0.08f, 0.36f) - 0.305f) < 0.001f && Math.Abs(CumulusLevels.For(0.25f, 0.36f) - 0.626f) < 0.001f,
                "levels of sky 12 and sky 18");
            TestAssert.That(CumulusLevels.Mu(0.99f, 0.36f) == 1f, "a cover past the capacity clamps mu to 1 (cover is capped at 0.95)");
        }

        private static void MeanStrengthCurve()
        {
            TestAssert.That(Math.Abs(CumulusLevels.MeanStrength(0.3f) - 0.180f) < 0.001f && Math.Abs(CumulusLevels.MeanStrength(0.5f) - 0.464f) < 0.001f &&
                Math.Abs(CumulusLevels.MeanStrength(0.7f) - 0.750f) < 0.001f && Math.Abs(CumulusLevels.MeanStrength(1f) - 0.9986f) < 0.0005f,
                "M(0.3) 0.180, M(0.5) 0.464, M(0.7) 0.750, M(1) 0.9986");
            foreach (float level in new[] { 0.2f, 0.5f, 0.9f })
            {
                // Mean strength over uniform ranks, by numeric integration of smoothstep(rank*0.7, rank*0.7 + 0.35, level).
                double total = 0;
                const int n = 20000;
                for (int i = 0; i < n; i++)
                {
                    float rank = (i + 0.5f) / n;
                    total += BoscaliSummer.Modules.Weather.Domain.WeatherMath.Smoothstep(rank * 0.7f, rank * 0.7f + 0.35f, level);
                }
                TestAssert.That(Math.Abs(total / n - CumulusLevels.MeanStrength(level)) < 0.002, "M matches the numeric mean at level " + level);
            }
            for (float x = 0.05f; x <= 1f; x += 0.05f)
                TestAssert.That(Math.Abs(CumulusLevels.Inverse(CumulusLevels.MeanStrength(x)) - x) < 0.002f, "Minv(M(x)) = x at " + x);
        }

        private static SkyParams Row(float sheet = 0f, float cumulus = 0f, float mid = 0f, float high = 0f, float rain = 0f, float conv = 0f, float sev = 0f)
        {
            var s = new SkyParams { SheetCover = sheet, CumulusCover = cumulus, StratRain = rain, Convective = conv, Severity = sev, OpacityMid = 0.7f, OpacityHigh = 0.2f };
            s.Mid.Cover = mid; s.High.Cover = high;
            return s;
        }

        private static void ClassConditionsHitsItsAnchors()
        {
            var thin = new SkyParams { OpacityHigh = 1f };
            thin.High.Cover = 0.04f;
            TestAssert.That(Math.Abs(SkyMath.ClassConditions(thin) - 0.052f) < 0.001f, "SkyCover 0.04 reads 0.052 (the old Clear 0.05)");
            TestAssert.That(Math.Abs(SkyMath.ClassConditions(Row(sheet: 0.90f)) - 0.680f) < 0.001f, "an overcast sheet of 0.90 reads 0.680 (the old Overcast 0.68)");
            TestAssert.That(Math.Abs(SkyMath.ClassConditions(Row(sheet: 1f, rain: 3f)) - 0.837f) < 0.001f, "steady rain of 3 mm/h under a full deck reads 0.837 (the old Rain 0.82)");
            TestAssert.That(Math.Abs(SkyMath.ClassConditions(Row(sheet: 0.55f, conv: 1f, sev: 0.65f)) - 0.963f) < 0.001f, "the old Storm row reads 0.963 (the old Storm 0.95)");
            TestAssert.That(SkyMath.ClassOf(Row(sheet: 0.90f)) == WeatherRegimeType.Overcast && SkyMath.ClassOf(Row(sheet: 1f, rain: 3f)) == WeatherRegimeType.RainSquall &&
                SkyMath.ClassOf(Row()) == WeatherRegimeType.Clear, "bands are the unchanged FromConditions edges");
            TestAssert.That(SkyMath.ClassConditions(Row(sheet: 1f, rain: 120f, conv: 1f, sev: 1f)) == 1f, "the measure caps at 1");
            TestAssert.That(Math.Abs(SkyMath.Gate(0.3f)) < 1e-6f && Math.Abs(SkyMath.Gate(0.6f) - 1f) < 1e-6f && Math.Abs(SkyMath.Gate(0.33f) - 0.028f) < 0.001f,
                "the rain gate is smoothstep(0.3, 0.6)");
            // Rain needs a closed sky: the same rain under 20 % cover adds nothing.
            TestAssert.That(Math.Abs(SkyMath.ClassConditions(Row(sheet: 0.2f, rain: 5f)) - SkyMath.ClassConditions(Row(sheet: 0.2f))) < 1e-6f,
                "no class credit for rain under open sky");
        }

        private static void ClassConditionsIsMonotone()
        {
            var rng = new Random(11);
            for (int i = 0; i < 3000; i++)
            {
                SkyParams r = Row((float)rng.NextDouble(), (float)rng.NextDouble() * 0.6f, (float)rng.NextDouble(), (float)rng.NextDouble(),
                    (float)rng.NextDouble() * 6f, (float)rng.NextDouble(), (float)rng.NextDouble());
                float before = SkyMath.ClassConditions(r);
                var moved = new[]
                {
                    Row(Math.Min(1f, r.SheetCover + 0.05f), r.CumulusCover, r.Mid.Cover, r.High.Cover, r.StratRain, r.Convective, r.Severity),
                    Row(r.SheetCover, Math.Min(1f, r.CumulusCover + 0.05f), r.Mid.Cover, r.High.Cover, r.StratRain, r.Convective, r.Severity),
                    Row(r.SheetCover, r.CumulusCover, Math.Min(1f, r.Mid.Cover + 0.05f), r.High.Cover, r.StratRain, r.Convective, r.Severity),
                    Row(r.SheetCover, r.CumulusCover, r.Mid.Cover, Math.Min(1f, r.High.Cover + 0.05f), r.StratRain, r.Convective, r.Severity),
                    Row(r.SheetCover, r.CumulusCover, r.Mid.Cover, r.High.Cover, r.StratRain + 0.5f, r.Convective, r.Severity),
                    Row(r.SheetCover, r.CumulusCover, r.Mid.Cover, r.High.Cover, r.StratRain, Math.Min(1f, r.Convective + 0.05f), r.Severity),
                    Row(r.SheetCover, r.CumulusCover, r.Mid.Cover, r.High.Cover, r.StratRain, r.Convective, Math.Min(1f, r.Severity + 0.05f)),
                };
                foreach (SkyParams m in moved)
                    TestAssert.That(SkyMath.ClassConditions(m) >= before - 1e-6f, "the class measure rises with every input");
            }
        }
    }
}
