using System;
using System.Collections.Generic;
using System.Text;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The 47 skies (spec 4.4): every library rule, the class table, the pools, the derived columns and the jitter.</summary>
    internal static class SkyLibraryTests
    {
        private static readonly float[] BandEdges = { 0f, 0.12f, 0.28f, 0.45f, 0.60f, 0.75f, 0.88f, 1.0001f };

        public static void Run()
        {
            LibraryShape();
            ClassTableAndPools();
            ExtremesStayInsideTheWidenedBands();
            OpeningInstanceKeepsTheClass();
            SeparationEmbeddingAndBands();
            RainRules();
            CumulusCapacityRule();
            RolesAgreeWithClasses();
            OneLowGenusAndRungsOnTheLadder();
            DerivedColumnsEqualTheirFormulas();
            CoverRulesAndVanillaGate();
            ThinLayersAreVisibleOnEverySky();
            JitterRules();
            OpeningPickIsMissionLed();
        }

        private static int Count(WeatherRegimeType cls)
        {
            int n = 0;
            for (int i = 0; i < SkyLibrary.Count; i++) if (SkyLibrary.ClassOf(i) == cls) n++;
            return n;
        }

        private static void LibraryShape()
        {
            TestAssert.That(SkyLibrary.Count == 47 && WeatherKey.SkyCount == SkyLibrary.Count, "47 skies, and the wire validates against the same count");
            var names = new HashSet<string>();
            for (int i = 0; i < SkyLibrary.Count; i++)
            {
                SkyDef d = SkyLibrary.Get(i);
                TestAssert.That(d.Index == i && d.Number == i + 1 && !string.IsNullOrEmpty(d.Name) && names.Add(d.Name), "sky " + (i + 1) + " has a unique name");
            }
            int[] expected = { 7, 7, 7, 7, 6, 7, 6 };
            for (int c = 0; c < 7; c++) TestAssert.That(Count((WeatherRegimeType)c) == expected[c], "class " + c + " holds " + expected[c] + " skies");
            TestAssert.That(SkyLibrary.Get(0).OpeningWeight == 0.1f && SkyLibrary.Get(5).OpeningWeight == 0.1f && SkyLibrary.Get(1).OpeningWeight == 1f,
                "Blue CAVOK and Summer haze weigh 0.1");
            TestAssert.That(SkyLibrary.Get(15).Patchy && SkyLibrary.Get(16).Patchy && SkyLibrary.Get(20).Patchy && !SkyLibrary.Get(18).Patchy, "skies 16, 17 and 21 are the patchy ones");
            TestAssert.That(SkyLibrary.Nominal(9).Streets == 0.8f && SkyLibrary.Nominal(7).Streets == 0f, "only cloud streets carry streets");
            TestAssert.That(Math.Abs(SkyLibrary.Nominal(5).Haze - StateTable.Get(WeatherRegimeType.Clear).Haze / 2f) < 1e-4f, "summer haze doubles the haze");
            SkyLayer under = SkyLibrary.Nominal(17).Mid; // altocumulus ripples over cumulus
            TestAssert.That(under.RippleWavelength == 900f && under.Ripples == 0.8f && under.Cells == 0.2f, "undulatus ripples are 900 m apart");
            TestAssert.That(SkyLibrary.Nominal(2).High.Aspect == 4f && SkyLibrary.Nominal(2).High.Curl == 1f && SkyLibrary.Nominal(1).High.Aspect == 5f, "hooks curl, fibres are long");
            TestAssert.That(SkyLibrary.Nominal(26).Mid.Base == 3200f && SkyLibrary.Nominal(26).CloudBase == 700f, "sky 27: fractus at 700 m, altostratus at 3200 m");
        }

        private static void ClassTableAndPools()
        {
            var table = new StringBuilder("Sky library, nominal ClassConditions and opening pools:");
            for (int c = 0; c < 7; c++)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                int pool = 0;
                for (int i = 0; i < SkyLibrary.Count; i++)
                {
                    if (SkyLibrary.ClassOf(i) != (WeatherRegimeType)c) continue;
                    float cc = SkyMath.ClassConditions(SkyLibrary.Nominal(i));
                    lo = Math.Min(lo, cc); hi = Math.Max(hi, cc);
                    TestAssert.That(cc >= BandEdges[c] && cc < BandEdges[c + 1], "sky " + (i + 1) + " reads " + cc + " inside the " + (WeatherRegimeType)c + " band");
                    TestAssert.That(SkyMath.ClassOf(SkyLibrary.Nominal(i)) == (WeatherRegimeType)c, "sky " + (i + 1) + " reads as its own class");
                    if (!SkyLibrary.Get(i).Patchy) pool++;
                }
                TestAssert.That(SkyLibrary.OpeningPool((WeatherRegimeType)c).Length == pool && pool >= 3, "class " + c + " has at least three non-patchy skies");
                table.Append("\n  ").Append(((WeatherRegimeType)c).ToString().PadRight(10)).Append(lo.ToString("0.000")).Append("-").Append(hi.ToString("0.000"))
                    .Append("  pool ").Append(pool);
            }
            Console.WriteLine(table.ToString());
            int[] pools = { 7, 7, 4, 7, 6, 7, 6 };
            for (int c = 0; c < 7; c++)
                TestAssert.That(SkyLibrary.OpeningPool((WeatherRegimeType)c).Length == pools[c], "the pool table of 4.4: class " + c + " has " + pools[c]);
            // Spec 4.4 hand checks of the nominal range.
            TestAssert.That(Math.Abs(SkyMath.ClassConditions(SkyLibrary.Nominal(41)) - 0.936f) < 0.001f, "sky 42, the multicell cluster, reads 0.936");
            TestAssert.That(Math.Abs(SkyMath.ClassConditions(SkyLibrary.Nominal(16)) - 0.297f) < 0.001f, "sky 17, the isolated Cb, reads 0.297");
            TestAssert.That(SkyMath.ClassConditions(SkyLibrary.Nominal(46)) == 1f && SkyMath.ClassConditions(SkyLibrary.Nominal(42)) == 1f, "skies 47 and 43 cap at 1.000");
        }

        private static void ExtremesStayInsideTheWidenedBands()
        {
            for (int i = 0; i < SkyLibrary.Count; i++)
            {
                int c = (int)SkyLibrary.ClassOf(i);
                float low = SkyMath.ClassConditions(SkyLibrary.Extreme(i, -1)), high = SkyMath.ClassConditions(SkyLibrary.Extreme(i, +1));
                float nominal = SkyMath.ClassConditions(SkyLibrary.Nominal(i));
                TestAssert.That(low <= nominal + 1e-6f && high >= nominal - 1e-6f, "sky " + (i + 1) + ": the extremes bracket the nominal sky");
                TestAssert.That(low >= BandEdges[c] - 0.1f && high <= BandEdges[c + 1] + 0.1f,
                    "sky " + (i + 1) + ": extremes " + low.ToString("0.000") + ".." + high.ToString("0.000") + " stay inside the band widened by 0.1");
            }
        }

        private static void OpeningInstanceKeepsTheClass()
        {
            int clamped = 0, runs = 0;
            for (int c = 0; c < 7; c++)
                foreach (int i in SkyLibrary.OpeningPool((WeatherRegimeType)c))
                    for (uint seed = 1; seed <= 64; seed++)
                    {
                        SkyParams sky = SkyLibrary.OpeningInstance(i, seed * 2654435761u, 0f, out float scale);
                        runs++;
                        if (scale < 1f) clamped++;
                        TestAssert.That(SkyMath.ClassOf(sky) == (WeatherRegimeType)c, "sky " + (i + 1) + " seed " + seed + " opens as its class");
                        TestAssert.That(scale == 1f || scale == 0.5f || scale == 0.25f || scale == 0f, "the amplitude steps are 1, 0.5, 0.25, 0");
                    }
            Console.WriteLine("Opening instances: " + clamped + " of " + runs + " needed a clamped jitter amplitude.");
        }

        private static float DeckTopOf(in SkyParams s) => SkyArchetypes.DeckTop(s);

        private static void SeparationEmbeddingAndBands()
        {
            float[] embedded = { 3640f, 3040f, 3240f, 2940f }; // skies 34, 35, 40, 43
            int[] embeddedAt = { 33, 34, 39, 42 };
            for (int k = 0; k < embedded.Length; k++)
                TestAssert.That(Math.Abs(SkyLibrary.Nominal(embeddedAt[k]).Mid.Base - embedded[k]) < 0.5f, "embedded sky " + (embeddedAt[k] + 1) + " puts its altostratus on the nimbostratus top - 300 m");
            TestAssert.That(Math.Abs(SkyLibrary.Nominal(37).Mid.Base - 3300f) < 0.5f && !SkyLibrary.Get(37).HasLow, "sky 38 has no low layer and keeps its altostratus at 3300");
            foreach (float openingBase in new[] { 0f, 400f, 900f, 1500f, 2400f, 9000f })
            for (uint seed = 1; seed <= 12; seed++)
            for (int i = 0; i < SkyLibrary.Count; i++)
            {
                SkyDef d = SkyLibrary.Get(i);
                SkyParams s = SkyLibrary.Instance(i, seed * 40503u, openingBase, 1f);
                string who = "sky " + (i + 1) + " base " + openingBase + " seed " + seed;
                if (d.HasLow)
                {
                    TestAssert.That(s.CloudBase >= d.Genus.BandMin - 0.01f && s.CloudBase <= d.Genus.BandMax + 0.01f, who + ": low base inside its genus band");
                    if (d.HasMid && d.Embedded)
                        TestAssert.That(Math.Abs(s.Mid.Base - Math.Min(d.MidArchetype.BandMax, Math.Max(d.MidArchetype.BandMin, DeckTopOf(s) - 300f))) < 0.5f, who + ": embedded mid base follows the nimbostratus top");
                    else if (d.HasMid && !d.Genus.Convective)
                        TestAssert.That(DeckTopOf(s) < s.Mid.Base - 300f + 0.5f || s.Mid.Base >= d.MidArchetype.BandMax - 0.5f, who + ": stratiform low top below mid base - 300 m");
                }
                if (d.HasMid) TestAssert.That(s.Mid.Base >= d.MidArchetype.BandMin - 0.01f && s.Mid.Base <= d.MidArchetype.BandMax + 0.01f, who + ": mid base inside its band");
                if (d.HasHigh) TestAssert.That(s.High.Base >= d.HighArchetype.BandMin - 0.01f && s.High.Base <= d.HighArchetype.BandMax + 0.01f, who + ": high base inside its band");
            }
            TestAssert.That(Math.Abs(SkyLibrary.Instance(7, 5u, 2400f, 0f).CloudBase - 2400f) < 0.01f &&
                Math.Abs(SkyLibrary.Instance(28, 5u, 2400f, 0f).CloudBase - 900f) < 0.01f,
                "the mission altitude 2400 m stays 2400 on a Humilis and becomes 900 on a Stratus (each genus' own band)");
            TestAssert.That(SkyLibrary.Instance(0, 5u, 2400f, 0f).CloudBase == SkyLibrary.Nominal(0).CloudBase, "a sky with no low layer ignores the opening base");
        }

        private static void RainRules()
        {
            int[] cellSkies = { 15, 16, 20, 27, 36, 40, 41, 42, 43, 44, 45, 46 };
            float[] convective = { 0.15f, 0.25f, 0.20f, 0.30f, 0.60f, 0.45f, 0.7f, 0.6f, 0.8f, 0.9f, 0.7f, 0.8f };
            for (int k = 0; k < cellSkies.Length; k++)
                TestAssert.That(Math.Abs(SkyLibrary.Nominal(cellSkies[k]).Convective - convective[k]) < 1e-6f, "sky " + (cellSkies[k] + 1) + " carries Convective " + convective[k]);
            for (int i = 0; i < SkyLibrary.Count; i++)
            {
                SkyDef d = SkyLibrary.Get(i);
                SkyParams s = d.Nominal;
                if (s.StratRain > 0.1f)
                    TestAssert.That(SkyMath.RainCover(s) >= 0.45f || d.Genus.DeckDepth > 0f && d.HasLow, "sky " + (i + 1) + ": stratiform rain needs cover 0.45 or a deck genus");
                if (Array.IndexOf(cellSkies, i) < 0) TestAssert.That(s.Convective == 0f, "sky " + (i + 1) + " has no cells");
                if (s.StratRain > 0.1f || s.Convective > 0.3f) TestAssert.That(s.RainBase > 0f, "sky " + (i + 1) + " declares its RainBase");
                TestAssert.That(s.StratRain == 0f || SkyMath.Gate(SkyMath.RainCover(s)) > 0.01f, "sky " + (i + 1) + " rain is not gated away");
            }
            float[] rb = { 800f, 350f, 900f, 3300f, 600f, 1000f, 1200f, 700f };
            int[] rbAt = { 34, 35, 36, 37, 38, 39, 40, 42 };
            for (int k = 0; k < rb.Length; k++) TestAssert.That(SkyLibrary.Nominal(rbAt[k]).RainBase == rb[k], "sky " + (rbAt[k] + 1) + " RainBase " + rb[k]);
            TestAssert.That(SkyLibrary.Nominal(15).RainBase == 1400f, "a cell sky without its own value rains from the base of its cell layer");
            float[] storm = { 0.6f, 0.7f, 1.0f, 0.9f, 0.7f, 0.6f };
            for (int k = 0; k < 6; k++) TestAssert.That(SkyLibrary.Nominal(41 + k).Severity == storm[k], "storm sky " + (42 + k) + " lists Severity " + storm[k]);
            TestAssert.That(SkyLibrary.Nominal(36).Severity == 0.15f && SkyLibrary.Nominal(15).Severity == 0f && SkyLibrary.Nominal(27).Severity == 0f,
                "other cell skies inherit their class row's severity");
        }

        private static void CumulusCapacityRule()
        {
            int tested = 0;
            var table = new StringBuilder("Cumulus capacity rule (mu at the sky's cover, mu at cover + 0.08):");
            for (int i = 0; i < SkyLibrary.Count; i++)
            {
                SkyParams s = SkyLibrary.Nominal(i);
                if (s.CumulusCover <= 0f) continue;
                tested++;
                double mu = Math.Log(1.0 - s.CumulusCover) / Math.Log(1.0 - s.CapacityCover);
                double jittered = Math.Log(1.0 - (s.CumulusCover + 0.08)) / Math.Log(1.0 - s.CapacityCover);
                TestAssert.That(mu <= 0.75, "sky " + (i + 1) + ": mu " + mu.ToString("0.000") + " leaves room");
                TestAssert.That(jittered < 1.0, "sky " + (i + 1) + ": mu " + jittered.ToString("0.000") + " at the largest jitter never clamps the lattice");
                table.Append("\n  sky ").Append(i + 1).Append(" ").Append(mu.ToString("0.000")).Append(" ").Append(jittered.ToString("0.000"));
            }
            TestAssert.That(tested == 20, "20 skies carry a CumulusCover, got " + tested);
            Console.WriteLine(table.ToString());
            TestAssert.That(Math.Abs(Math.Log(0.5) / Math.Log(0.36) - 0.678) < 0.001, "the worst nominal sky is 28 at 0.678");
            for (int i = 0; i < SkyLibrary.Count; i++)
            {
                SkyDef d = SkyLibrary.Get(i);
                if (!d.HasLow) continue;
                float capacity = d.Genus.Convective ? (d.Nominal.Streets >= CumulusLattice.StreetThreshold ? SkyArchetypes.StreetCapacityHumilis : d.Genus.Capacity) : 0f;
                TestAssert.That(Math.Abs(d.Nominal.CapacityCover - capacity) < 1e-6f && d.Genus.Convective == (d.Genus.Candidates > 0),
                    "sky " + (i + 1) + ": capacity is a cumulus-genus constant");
            }
        }

        private static void RolesAgreeWithClasses()
        {
            for (int i = 0; i < SkyLibrary.Count; i++)
            {
                SkyDef d = SkyLibrary.Get(i);
                int cls = 1 << (int)d.Class;
                foreach (Role role in (Role[])Enum.GetValues(typeof(Role)))
                    if (role != Role.None && (d.Roles & role) != 0)
                        TestAssert.That((RoleRules.AllowedClasses(role) & cls) != 0, "sky " + (i + 1) + " is " + d.Class + " and may not take role " + role);
                TestAssert.That(d.Roles != Role.None || i == 5, "every sky but summer haze has a role");
            }
        }

        private static void OneLowGenusAndRungsOnTheLadder()
        {
            for (int i = 0; i < SkyLibrary.Count; i++)
            {
                SkyDef d = SkyLibrary.Get(i);
                SkyParams s = d.Nominal;
                if (d.HasLow)
                    TestAssert.That(s.LobeRung == d.Genus.LobeRung && s.PuffDepth == d.Genus.PuffDepth && s.DeckDepth == d.Genus.DeckDepth &&
                        s.DeckSmooth == d.Genus.DeckSmooth && s.Anvil == d.Genus.Anvil && s.BaseWobble == d.Genus.BaseWobble,
                        "sky " + (i + 1) + ": the low columns are one genus row");
                TestAssert.That(s.LobeRung == Math.Floor(s.LobeRung) && s.LobeRung >= 0f && s.LobeRung <= 6f, "sky " + (i + 1) + ": lobe on a rung");
                foreach (SkyLayer l in new[] { s.Mid, s.High })
                    if (l.Cover > 0f)
                        TestAssert.That(l.PeriodRung == Math.Floor(l.PeriodRung) && l.PeriodRung >= 0f && l.PeriodRung <= 12f && l.Thick >= 250f, "sky " + (i + 1) + ": period on a rung");
            }
            TestAssert.That(Ladder.LobeMetres(SkyLibrary.Nominal(7).LobeRung) < Ladder.LobeMetres(SkyLibrary.Nominal(43).LobeRung), "a humilis is a smaller lump than a cumulonimbus");
        }

        private static void DerivedColumnsEqualTheirFormulas()
        {
            for (int i = 0; i < SkyLibrary.Count; i++)
            {
                SkyDef d = SkyLibrary.Get(i);
                SkyParams s = d.Nominal;
                string who = "sky " + (i + 1);
                TestAssert.That(Math.Abs(s.Mid.Strength - 0.6f * s.OpacityMid) < 1e-6f && Math.Abs(s.High.Strength - 0.8f * s.OpacityHigh) < 1e-6f, who + ": strength");
                float cumulus = Math.Min(1f, 1.5f * s.CumulusCover + 0.2f * s.Convective);
                TestAssert.That(Math.Abs(s.HorizonCover - Math.Max(0.9f * s.SheetCover, cumulus)) < 1e-6f, who + ": horizon cover");
                TestAssert.That(Math.Abs(s.HorizonDepth - Math.Max(300f, Math.Max(s.DeckDepth, 0.5f * s.PuffDepth))) < 1e-4f, who + ": horizon depth");
                if (d.HasLow) TestAssert.That(s.BaseVariation == d.Genus.BaseVariation && s.BaseVariation >= 150f && s.BaseVariation <= 400f, who + ": base variation is the genus constant");
                double mu = s.CumulusCover > 0f && s.CapacityCover > 0f ? Math.Log(1.0 - s.CumulusCover) / Math.Log(1.0 - s.CapacityCover) : 0.0;
                TestAssert.That(Math.Abs(CumulusLevels.MeanStrength(s.CumulusLevel) - Math.Min(1.0, mu)) < 0.002, who + ": cumulus level inverts the mean-strength curve");
                var tape = TapeSampler.Constant(s);
                TestAssert.That(tape.At(0f).CumulusLevel == s.CumulusLevel && tape.At(0f).HorizonCover == s.HorizonCover, who + ": the baked row carries the derived columns");
            }
            float[] horizon = { 0.225f, 0.525f, 0.855f, 0.90f, 0.59f };
            int[] at = { 7, 14, 28, 31, 41 };
            for (int k = 0; k < at.Length; k++)
                TestAssert.That(Math.Abs(SkyLibrary.Nominal(at[k]).HorizonCover - horizon[k]) < 0.001f, "sky " + (at[k] + 1) + " horizon cover " + horizon[k]);
            float[] depth = { 300f, 500f, 800f, 2500f, 1000f, 1750f };
            int[] depthAt = { 7, 28, 18, 33, 36, 15 };
            for (int k = 0; k < depth.Length; k++)
                TestAssert.That(Math.Abs(SkyLibrary.Nominal(depthAt[k]).HorizonDepth - depth[k]) < 0.5f, "sky " + (depthAt[k] + 1) + " horizon depth " + depth[k]);
            TestAssert.That(Math.Abs(SkyLibrary.Nominal(7).CumulusLevel - 0.430f) < 0.001f && Math.Abs(SkyLibrary.Nominal(11).CumulusLevel - 0.305f) < 0.001f &&
                Math.Abs(SkyLibrary.Nominal(17).CumulusLevel - 0.626f) < 0.001f, "cumulus levels of skies 8, 12 and 18");
        }

        private static void CoverRulesAndVanillaGate()
        {
            for (int i = 0; i < SkyLibrary.Count; i++)
            {
                SkyParams s = SkyLibrary.Nominal(i);
                float sky = SkyMath.SkyCover(SkyMath.LowCover(s), s.Mid.Cover, s.OpacityMid, s.High.Cover, s.OpacityHigh);
                if (SkyLibrary.ClassOf(i) == WeatherRegimeType.Clear) TestAssert.That(sky < 0.6f, "Clear sky " + (i + 1) + " keeps SkyCover under 0.6");
                if (SkyLibrary.ClassOf(i) == WeatherRegimeType.Overcast) TestAssert.That(sky >= 0.6f, "Overcast sky " + (i + 1) + " reaches 0.6");
            }
        }

        private static void ThinLayersAreVisibleOnEverySky()
        {
            // The cover law (Global Constraints): a layer shows about its Cover of the sky. Judged on the CPU twin and the real noise, before any
            // render; until the review pass the contact sheets were the only place a layer that draws nothing could be noticed (and they did not
            // look: cirrus at cover 0.30 covered 2-4 % of the sky, at 0.10-0.12 nothing). Nominal skies; a jittered instance moves a cover by at most 0.08.
            byte[] noise = BoscaliSummer.Modules.Weather.Visuals.CloudNoise3D.Generate(64, 47);
            for (int i = 0; i < SkyLibrary.Count; i++)
            {
                SkyParams sky = SkyLibrary.Nominal(i);
                var bodies = new BoscaliSummer.Modules.Weather.Visuals.CloudBodies(noise, 64, sky);
                foreach (SkyLayer layer in new[] { sky.Mid, sky.High })
                {
                    if (!layer.Present) continue;
                    float share = ThinLayerTests.AreaShare(bodies, layer);
                    TestAssert.That(share >= 0.5f * layer.Cover && share <= 4f * layer.Cover,
                        "sky " + (i + 1) + ": a layer of cover " + layer.Cover.ToString("0.00") + " covers " + share.ToString("0.000") + " of the sky");
                }
            }
        }

        private static void JitterRules()
        {
            for (int i = 0; i < SkyLibrary.Count; i++)
            {
                SkyDef d = SkyLibrary.Get(i);
                SkyParams nominal = d.Nominal;
                SkyParams none = SkyLibrary.Instance(i, 99u, 0f, 0f);
                TestAssert.That(none.CloudBase == nominal.CloudBase && none.SheetCover == nominal.SheetCover && none.Mid.Cover == nominal.Mid.Cover &&
                    none.LobeRung == nominal.LobeRung && none.Mid.Base == nominal.Mid.Base, "sky " + (i + 1) + ": jitter scale 0 is the nominal sky");
                SkyParams a = SkyLibrary.Instance(i, 1u, 0f, 1f), b = SkyLibrary.Instance(i, 1u, 0f, 1f), other = SkyLibrary.Instance(i, 2u, 0f, 1f);
                TestAssert.That(a.CloudBase == b.CloudBase && a.CumulusCover == b.CumulusCover && a.High.Cover == b.High.Cover, "sky " + (i + 1) + ": an instance is a pure function of its seed");
                bool anyDiffers = false;
                for (uint seed = 2; seed < 12 && !anyDiffers; seed++)
                {
                    SkyParams t = SkyLibrary.Instance(i, seed, 0f, 1f);
                    anyDiffers = t.CloudBase != a.CloudBase || t.CumulusCover != a.CumulusCover || t.SheetCover != a.SheetCover || t.High.Cover != a.High.Cover ||
                        t.Mid.Cover != a.Mid.Cover || t.LobeRung != a.LobeRung;
                }
                TestAssert.That(anyDiffers || (!d.HasLow && !d.HasMid && d.Nominal.High.Cover == 0f), "sky " + (i + 1) + ": other seeds give other instances");
                for (uint seed = 1; seed <= 20; seed++)
                {
                    SkyParams s = SkyLibrary.Instance(i, seed * 977u, 0f, 1f);
                    TestAssert.That(s.SheetCover >= 0f && s.SheetCover <= 1f && s.CumulusCover >= 0f && s.CumulusCover <= 1f && s.Mid.Cover >= 0f && s.Mid.Cover <= 1f &&
                        s.High.Cover >= 0f && s.High.Cover <= 1f, "sky " + (i + 1) + ": covers stay in 0..1");
                    TestAssert.That((nominal.SheetCover > 0f || s.SheetCover == 0f) && (nominal.CumulusCover > 0f || s.CumulusCover == 0f) &&
                        (nominal.Mid.Cover > 0f || s.Mid.Cover == 0f) && (nominal.High.Cover > 0f || s.High.Cover == 0f), "sky " + (i + 1) + ": jitter never creates a layer the sky does not have");
                    TestAssert.That(Math.Abs(s.LobeRung - nominal.LobeRung) <= (d.LowJitter.Rungs) + 1e-6f, "sky " + (i + 1) + ": the lobe moves at most its allowed rungs");
                    TestAssert.That(Math.Abs(s.Mid.PeriodRung - nominal.Mid.PeriodRung) <= 1f && Math.Abs(s.High.PeriodRung - nominal.High.PeriodRung) <= 1f, "sky " + (i + 1) + ": the periods move at most one rung");
                    if (d.HasLow)
                    {
                        float step = d.Nominal.CloudBase * d.LowJitter.BaseFrac;
                        TestAssert.That(Math.Abs(s.CloudBase - nominal.CloudBase) <= step + 0.5f, "sky " + (i + 1) + ": the base moves at most its fraction");
                        TestAssert.That(Math.Abs(s.CumulusCover - nominal.CumulusCover) <= d.LowJitter.Cover + 1e-5f && Math.Abs(s.SheetCover - nominal.SheetCover) <= d.LowJitter.Cover + 1e-5f,
                            "sky " + (i + 1) + ": low cover moves at most its amount");
                    }
                }
                SkyParams up = SkyLibrary.Extreme(i, +1), down = SkyLibrary.Extreme(i, -1);
                TestAssert.That(up.CloudBase == nominal.CloudBase && down.LobeRung == nominal.LobeRung && up.Mid.Base == nominal.Mid.Base, "sky " + (i + 1) + ": extremes move covers only");
            }
            TestAssert.That(SkyLibrary.Get(7).LowJitter.BaseFrac == 0.20f && SkyLibrary.Get(18).LowJitter.BaseFrac == 0.12f && SkyLibrary.Get(28).LowJitter.BaseFrac == 0.08f &&
                SkyLibrary.Get(33).LowJitter.Cover == 0.02f && SkyLibrary.Get(21).LowJitter.Cover == 0.05f, "jitter defaults per archetype group (4.1)");
        }

        private static void OpeningPickIsMissionLed()
        {
            int[] counts = new int[SkyLibrary.Count];
            for (uint seed = 1; seed <= 20000; seed++)
            {
                int pick = SkyLibrary.PickOpening(WeatherRegimeType.Clear, seed * 2246822519u);
                TestAssert.That(Array.IndexOf(SkyLibrary.OpeningPool(WeatherRegimeType.Clear), pick) >= 0, "a pick is always a pool sky");
                counts[pick]++;
            }
            TestAssert.That(counts[0] > 100 && counts[0] < 700 && counts[5] > 100 && counts[5] < 700, "Blue CAVOK and Summer haze are rare, not absent: " + counts[0] + ", " + counts[5]);
            TestAssert.That(counts[1] > 2500 && counts[1] < 4500, "an ordinary clear sky is picked about one time in five: " + counts[1]);
            TestAssert.That(SkyLibrary.PickOpening(WeatherRegimeType.Fair, 77u) == SkyLibrary.PickOpening(WeatherRegimeType.Fair, 77u) &&
                SkyLibrary.Get(SkyLibrary.PickOpening(WeatherRegimeType.Storm, 77u)).Class == WeatherRegimeType.Storm, "the pick is deterministic and in the class");
        }
    }
}
