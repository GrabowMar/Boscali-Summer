using System;
using System.IO;
using System.Text.RegularExpressions;
using BoscaliSummer.Features.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>
    /// The deterministic field: same key and time give the same sky everywhere, nothing pops,
    /// the forecast is the live function sampled ahead, and the readouts say what it means.
    /// </summary>
    internal static class WeatherFieldTests
    {
        private const float HalfX = 40960f;
        private const float HalfZ = 40960f;

        public static void Run()
        {
            DomainStaysFreeOfUnity();
            KeyNormalisesOverrides();
            ScheduleIsContinuousAndBounded();
            ChainRespectsTransitions();
            CadenceFollowsHostSettings();
            HeldSkyNeverChanges();
            OverrideBlendsInWithoutAJump();
            FieldIsDeterministic();
            RainNeverPops();
            ForecastEqualsLiveField();
            CellsAreBoundedAndRainBeneathThem();
            FrontSignRunsForward();
            VisibilityFollowsRain();
            ClassificationAndConditions();
            MetarReadsLikeOne();
            CloudDensityMapsCover();
            FlightLevelPrecipitation();
        }

        private static void FlightLevelPrecipitation()
        {
            var point = new WeatherPoint { RainRate = 20f, CloudBase = 1000f, CloudTop = 3000f };
            TestAssert.That(FlightWeatherAirMass.Evaluate(point, 500f, false, null).Rain == 1f,
                "rain reaches aircraft below the cloud base");
            TestAssert.That(FlightWeatherAirMass.Evaluate(point, 3000f, false, null).Rain == 0f,
                "aircraft above the cloud top is dry");
            var inside = FlightWeatherAirMass.Evaluate(point, 1500f, true, null);
            TestAssert.That(inside.Rain > 0f && inside.CloudMoisture > 0f,
                "cloud entry carries rain and condensation together");
            TestAssert.That(FlightWeatherAirMass.Evaluate(point, 5000f, false, 0.8f).Rain == 0.8f,
                "manual rain override remains useful for flight checks");
        }

        private static WeatherKey Key(uint seed = 20260918u, bool dynamic = true, byte start = WeatherKey.AutoRegime)
            => new WeatherKey(seed, 0f, dynamic, start, WeatherFlags.All);

        private static void CadenceFollowsHostSettings()
        {
            var quick = new WeatherKey(17u, 0f, true, (byte)WeatherRegime.Fair,
                WeatherFlags.None, null, 1f, 0.5f);
            var slow = new WeatherKey(17u, 0f, true, (byte)WeatherRegime.Fair,
                WeatherFlags.None, null, 30f, 10f);
            TestAssert.That(RegimeSchedule.Evaluate(quick, 0f).NextChangeAt <
                RegimeSchedule.Evaluate(slow, 0f).NextChangeAt,
                "host cadence setting changes the next regional shift");
        }

        private static void DomainStaysFreeOfUnity()
        {
            string root = FindRepoRoot();
            foreach (string file in Directory.GetFiles(Path.Combine(root, "modules", "Weather", "Domain"), "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                TestAssert.That(!Regex.IsMatch(text, @"\bUnityEngine\b"), Path.GetFileName(file) + " must stay free of Unity types");
            }
        }

        private static void KeyNormalisesOverrides()
        {
            WeatherKey key = Key();
            key = key.WithOverride(100f, WeatherRegime.Storms)
                     .WithOverride(300f, WeatherRegime.Clear)
                     .WithOverride(200f, WeatherRegime.Severe);
            // The keyframe at 200 supersedes the later one at 300.
            TestAssert.That(key.OverrideCount == 2, "a new keyframe drops keyframes after it");
            TestAssert.That(key.Override(1).Time == 200f && key.Override(1).Regime == WeatherRegime.Severe, "latest keyframe kept");
            for (int i = 0; i < 10; i++) key = key.WithOverride(1000f + i * 100f, WeatherRegime.Fair);
            TestAssert.That(key.OverrideCount == WeatherKey.MaxOverrides, "keyframes are capped");
            TestAssert.That(key.Override(0).Time < key.Override(key.OverrideCount - 1).Time, "keyframes stay in time order");
            TestAssert.That(Key().Equals(Key()) && !Key().Equals(Key(7u)), "key equality is by value");
        }

        private static void ScheduleIsContinuousAndBounded()
        {
            WeatherKey key = Key();
            RegimeState previous = RegimeSchedule.Evaluate(key, 0f);
            for (float t = 5f; t < 6f * 3600f; t += 5f)
            {
                RegimeState s = RegimeSchedule.Evaluate(key, t);
                TestAssert.That(Math.Abs(s.Params.Overcast - previous.Params.Overcast) < 0.02f, "overcast jumped at t=" + t);
                TestAssert.That(Math.Abs(s.Params.CloudBase - previous.Params.CloudBase) < 40f, "cloud base jumped at t=" + t);
                TestAssert.That(Math.Abs(s.Params.WindSpeed - previous.Params.WindSpeed) < 0.5f, "wind jumped at t=" + t);
                TestAssert.That(Math.Abs(s.Params.Convective - previous.Params.Convective) < 0.03f, "convective jumped at t=" + t);
                TestAssert.That(s.Blend >= 0f && s.Blend <= 1f, "blend in range");
                TestAssert.That(s.FrontCount <= RegimeState.MaxFronts, "fronts bounded");
                for (int i = 0; i < s.FrontCount; i++)
                    TestAssert.That(s.GetFront(i).Strength > 0f && s.GetFront(i).Strength <= 1f, "front strength in (0,1]");
                previous = s;
            }
        }

        private static void ChainRespectsTransitions()
        {
            int visited = 0;
            bool[] seen = new bool[RegimeTable.Count];
            for (uint seed = 1; seed <= 20; seed++)
            {
                WeatherKey key = Key(seed);
                WeatherRegime last = RegimeSchedule.Evaluate(key, 0f).From;
                for (float t = 0f; t < 10f * 3600f; t += 30f)
                {
                    RegimeState s = RegimeSchedule.Evaluate(key, t);
                    if (s.From != last)
                    {
                        TestAssert.That(RegimeTable.TransitionWeight(last, s.From) > 0f,
                            "forbidden transition " + last + " -> " + s.From);
                        last = s.From;
                    }
                    if (!seen[(int)s.From])
                    {
                        seen[(int)s.From] = true;
                        visited++;
                    }
                }
            }
            TestAssert.That(visited == RegimeTable.Count, "the chain should visit every regime across seeds, saw " + visited);
            TestAssert.That(RegimeTable.TransitionWeight(WeatherRegime.Clear, WeatherRegime.Severe) == 0f, "clear never jumps to severe");
        }

        private static void HeldSkyNeverChanges()
        {
            WeatherKey key = Key(dynamic: false, start: (byte)WeatherRegime.Frontal);
            bool sawFront = false;
            for (float t = 0f; t < 4f * 3600f; t += 60f)
            {
                RegimeState s = RegimeSchedule.Evaluate(key, t);
                TestAssert.That(s.From == WeatherRegime.Frontal && s.To == WeatherRegime.Frontal, "held sky changed");
                TestAssert.That(float.IsPositiveInfinity(s.NextChangeAt), "held sky predicted a change");
                if (s.FrontCount > 0) sawFront = true;
            }
            TestAssert.That(sawFront, "a held FRONTAL sky still has fronts crossing it");
        }

        private static void OverrideBlendsInWithoutAJump()
        {
            WeatherKey key = Key().WithOverride(1500f, WeatherRegime.Severe);
            var field = new WeatherField();
            field.Build(key, 1499.5f, HalfX, HalfZ);
            WeatherPoint before = field.Sample(0f, 0f);
            field.Build(key, 1500.5f, HalfX, HalfZ);
            WeatherPoint after = field.Sample(0f, 0f);
            TestAssert.That(Math.Abs(after.Cover - before.Cover) < 0.05f, "override made the cover jump");

            RegimeState settled = RegimeSchedule.Evaluate(key, 1500f + RegimeSchedule.OverrideBlendSeconds + 1f);
            TestAssert.That(settled.From == WeatherRegime.Severe, "override regime should hold after its blend");

            RegimeState half = RegimeSchedule.Evaluate(key, 1500f + RegimeSchedule.OverrideBlendSeconds * 0.25f);
            TestAssert.That(half.To == WeatherRegime.Severe, "blending toward the forced regime");
        }

        private static void FieldIsDeterministic()
        {
            var a = new WeatherField();
            var b = new WeatherField();
            WeatherKey key = Key(99u);
            for (float t = 0f; t < 7200f; t += 611f)
            {
                a.Build(key, t, HalfX, HalfZ, 14f);
                b.Build(new WeatherKey(99u, 0f, true, WeatherKey.AutoRegime, WeatherFlags.All), t, HalfX, HalfZ, 14f);
                TestAssert.That(a.CellCount == b.CellCount, "cell count differs between peers");
                for (int i = -2; i <= 2; i++)
                {
                    float x = i * 15000f, z = -i * 9000f;
                    WeatherPoint pa = a.Sample(x, z);
                    WeatherPoint pb = b.Sample(x, z);
                    TestAssert.That(pa.RainRate == pb.RainRate && pa.Cover == pb.Cover && pa.WindX == pb.WindX,
                        "same key and time must give the same sky");
                }
            }
        }

        private static void RainNeverPops()
        {
            var field = new WeatherField();
            // Several seeds, several points, one-second steps across three hours.
            for (uint seed = 3; seed < 7; seed++)
            {
                WeatherKey key = Key(seed).WithOverride(5400f, WeatherRegime.Severe);
                float[] xs = { 0f, 12000f, -20000f };
                float[] zs = { 0f, -8000f, 15000f };
                float[] rain = new float[3];
                float[] cover = new float[3];
                float[] wind = new float[3];
                for (int step = 0; step < 3 * 3600; step++)
                {
                    field.Build(key, step, HalfX, HalfZ);
                    for (int k = 0; k < 3; k++)
                    {
                        WeatherPoint p = field.Sample(xs[k], zs[k]);
                        if (step > 0)
                        {
                            TestAssert.That(Math.Abs(p.RainRate - rain[k]) < 3f,
                                $"rain popped by {p.RainRate - rain[k]:F2} mm/h at t={step} seed {seed}");
                            TestAssert.That(Math.Abs(p.Cover - cover[k]) < 0.03f,
                                $"cover popped by {p.Cover - cover[k]:F3} at t={step} seed {seed}");
                            TestAssert.That(Math.Abs(p.WindSpeed - wind[k]) < 1.5f,
                                $"wind popped by {p.WindSpeed - wind[k]:F2} at t={step} seed {seed}");
                        }
                        rain[k] = p.RainRate;
                        cover[k] = p.Cover;
                        wind[k] = p.WindSpeed;
                        TestAssert.That(p.RainRate >= 0f && p.RainRate <= WeatherField.MaxRainRate, "rain in range");
                        TestAssert.That(p.FrontCover >= 0f && p.FrontCover <= 1f, "front cloud cover in range");
                        TestAssert.That(p.VisibilityKm >= 0.3f && p.VisibilityKm <= 50f, "visibility in range");
                    }
                }
            }
        }

        private static void ForecastEqualsLiveField()
        {
            WeatherKey key = Key(11u);
            var scratch = new WeatherField();
            var live = new WeatherField();
            var entries = new ForecastEntry[6];
            int n = WeatherWords.Forecast(scratch, key, 1000f, 600f, 5000f, -3000f, HalfX, HalfZ, 10f, entries);
            TestAssert.That(n == 6, "forecast fills its buffer");
            for (int i = 0; i < n; i++)
            {
                float at = 1000f + entries[i].OffsetSeconds;
                live.Build(key, at, HalfX, HalfZ, (10f + entries[i].OffsetSeconds / 3600f) % 24f);
                WeatherPoint p = live.Sample(5000f, -3000f);
                TestAssert.That(p.RainRate == entries[i].RainRate && p.Cover == entries[i].Cover,
                    "forecast must be the live field sampled ahead");
                TestAssert.That(live.Regime.Dominant == entries[i].Regime, "forecast regime matches");
            }
        }

        private static void CellsAreBoundedAndRainBeneathThem()
        {
            WeatherKey key = Key(5u, dynamic: false, start: (byte)WeatherRegime.Storms);
            var field = new WeatherField();
            bool sawMature = false;
            for (float t = 600f; t < 4f * 3600f; t += 97f)
            {
                field.Build(key, t, HalfX, HalfZ);
                TestAssert.That(field.CellCount <= StormCells.MaxCells, "cells bounded");
                for (int i = 0; i < field.CellCount; i++)
                {
                    StormCell c = field.Cell(i);
                    TestAssert.That(c.Radius >= 1000f && c.Radius <= 5000f, "cell radius in range");
                    TestAssert.That(c.PeakRain >= 15f && c.PeakRain <= 120f, "cell peak in range");
                    TestAssert.That(c.Age >= 0f && c.Age < 1f, "cell age in range");
                    if (c.Stage == StormStage.Mature && c.RainLevel > 0.9f)
                    {
                        sawMature = true;
                        WeatherPoint under = field.Sample(c.X, c.Z);
                        TestAssert.That(under.RainRate >= 0.5f * c.PeakRain, "it must rain hard under a mature core");
                        TestAssert.That(under.CoreDepth > 0.8f, "core depth reads the core");
                        TestAssert.That(under.Cover > 0.9f, "a mature cell is overcast overhead");
                    }
                }
            }
            TestAssert.That(sawMature, "a STORMS sky should grow mature cells within four hours");

            // A CLEAR held sky has no cells and no rain anywhere.
            field.Build(Key(5u, dynamic: false, start: (byte)WeatherRegime.Clear), 3000f, HalfX, HalfZ);
            TestAssert.That(field.CellCount == 0, "clear sky has no cells");
            for (int i = -3; i <= 3; i++)
                TestAssert.That(field.Sample(i * 12000f, i * 7000f).RainRate < 0.01f, "clear sky is dry");
        }

        private static void FrontSignRunsForward()
        {
            var source = new FrontSource { Seed = 3u, Id = 4, Regime = WeatherRegime.Frontal, Mid = 1000f, Strength = 1f };
            FrontState early = WeatherFronts.Resolve(source, 3u, 0f);
            FrontState late = WeatherFronts.Resolve(source, 3u, 2000f);
            float px = early.NormalX * 10000f, pz = early.NormalZ * 10000f;
            TestAssert.That(early.SignedDistance(px, pz) < 0f, "a point ahead of the front reads negative");
            TestAssert.That(early.SecondsUntil(px, pz) > 0f, "the front is still coming");
            TestAssert.That(late.SignedDistance(0f, 0f) > 0f, "after its midpoint the front has passed the centre");
            FrontEffect ahead = WeatherFronts.Profile(FrontKind.Cold, -20000f);
            FrontEffect behind = WeatherFronts.Profile(FrontKind.Cold, 2000f);
            TestAssert.That(behind.Rain > ahead.Rain * 5f, "a cold front rains behind its line");
            TestAssert.That(behind.VeerDegrees > 40f && ahead.VeerDegrees < 1f, "the wind veers as the front passes");
            FrontEffect warmAhead = WeatherFronts.Profile(FrontKind.Warm, -20000f);
            TestAssert.That(warmAhead.Rain > 1f, "a warm front rains ahead of its line");
        }

        private static void VisibilityFollowsRain()
        {
            WeatherKey key = Key(dynamic: false, start: (byte)WeatherRegime.Clear);
            var field = new WeatherField();
            field.Build(key, 100f, HalfX, HalfZ);
            WeatherPoint dry = field.Sample(0f, 0f);
            TestAssert.That(dry.VisibilityKm > 30f, "clear air sees far");

            // 25 mm/h in 45 km haze ≈ 2 km.
            float extinction = 3.912f / 45f + 0.25f * (float)Math.Pow(25, 0.66);
            float vis = 3.912f / extinction;
            TestAssert.That(vis > 1.5f && vis < 2.6f, "25 mm/h should give about 2 km, got " + vis);
            TestAssert.That(WeatherField.Reflectivity(10f) > 35f && WeatherField.Reflectivity(10f) < 42f, "10 mm/h ≈ 39 dBZ");
            TestAssert.That(RadarScale.Level(WeatherField.Reflectivity(0.5f)) <= 1, "drizzle is at most a light echo");
            TestAssert.That(RadarScale.Level(WeatherField.Reflectivity(100f)) == 5, "100 mm/h is extreme");
        }

        private static void ClassificationAndConditions()
        {
            TestAssert.That(WeatherField.Classify(0f) == PrecipitationKind.None, "none");
            TestAssert.That(WeatherField.Classify(0.3f) == PrecipitationKind.Drizzle, "drizzle");
            TestAssert.That(WeatherField.Classify(1f) == PrecipitationKind.Light, "light");
            TestAssert.That(WeatherField.Classify(5f) == PrecipitationKind.Moderate, "moderate");
            TestAssert.That(WeatherField.Classify(20f) == PrecipitationKind.Heavy, "heavy");
            TestAssert.That(WeatherField.Classify(80f) == PrecipitationKind.Violent, "violent");

            // Vanilla's five sets are floor(conditions × 5).
            TestAssert.That(RegimeTable.FromConditions(0.1f) == WeatherRegime.Clear, "clear band");
            TestAssert.That(RegimeTable.FromConditions(0.3f) == WeatherRegime.Fair, "scattered band");
            TestAssert.That(RegimeTable.FromConditions(0.5f) == WeatherRegime.Showers, "moderate band");
            TestAssert.That(RegimeTable.FromConditions(0.7f) == WeatherRegime.Overcast, "overcast band");
            TestAssert.That(RegimeTable.FromConditions(0.88f) == WeatherRegime.Storms, "thunderstorm band");
            TestAssert.That(RegimeTable.FromConditions(1f) == WeatherRegime.Severe, "top of the thunderstorm band");
        }

        private static void MetarReadsLikeOne()
        {
            var p = new WeatherPoint
            {
                RainRate = 30f, Cover = 0.95f, CloudBase = 250f, VisibilityKm = 1.8f,
                WindX = -7f, WindZ = 0f, Gust = 8f, Turbulence = 0.5f, LightningRate = 3f,
                CoreDepth = 0.7f, ConvectiveShare = 0.8f, Qnh = 998.4f, Temperature = 18.2f, Dewpoint = 17.4f,
            };
            string metar = WeatherWords.Metar(p, 18, 15.55f);
            TestAssert.That(metar.StartsWith("BSCL 181530Z "), "time group: " + metar);
            // Wind blowing toward the west comes FROM the east: 090.
            TestAssert.That(metar.Contains(" 09014G29KT "), "wind group: " + metar);
            TestAssert.That(metar.Contains(" 1800 "), "visibility group: " + metar);
            TestAssert.That(metar.Contains(" +TSRA "), "present weather: " + metar);
            TestAssert.That(metar.Contains(" OVC008CB "), "cloud group: " + metar);
            TestAssert.That(metar.EndsWith(" 18/17 Q0998"), "temperature and pressure: " + metar);
            TestAssert.That(WeatherWords.WindFrom(0f) == 180f, "a northward wind comes from the south");
            TestAssert.That(WeatherWords.Cardinal(44f) == "NE" && WeatherWords.Cardinal(350f) == "N", "cardinals");
            TestAssert.That(WeatherWords.Category(1f, 1000f) == FlightCategory.Lifr, "LIFR by visibility");
            TestAssert.That(WeatherWords.Category(20f, 2000f) == FlightCategory.Vfr, "VFR");
        }

        private static void CloudDensityMapsCover()
        {
            TestAssert.That(CloudDensity.Texel(0f, 0.9f) == 0f, "no cover, no cloud");
            TestAssert.That(CloudDensity.Texel(1f, 0.1f) == 1f, "full cover is solid");
            float last = -1f;
            for (float c = 0.05f; c <= 1f; c += 0.05f)
            {
                float d = CloudDensity.Texel(c, 0.6f);
                TestAssert.That(d >= last - 1e-5f, "density grows with cover");
                last = d;
            }
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "BoscaliSummer.sln")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "modules")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("repo root not found");
        }
    }
}
