using System;
using System.IO;
using System.Text.RegularExpressions;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>
    /// Static weather states: one state per interval, neighbour steps only, a smooth fade, a
    /// cloud layout that never moves, and readouts that say what it means.
    /// </summary>
    internal static class WeatherFieldTests
    {
        private const float HalfX = 40960f;
        private const float HalfZ = 40960f;

        public static void Run()
        {
            DomainStaysFreeOfUnity();
            TimelineStepsOnTheInterval();
            StatesMoveOnlyToNeighbours();
            HeldSkyNeverChanges();
            SkyIsStaticBetweenFades();
            FadesNeverPop();
            LayoutOnlyChangesUnderAnEmptySky();
            StatesBuildRealisticSkies();
            CoverGrowsUpTheLadder();
            FairCloudGroupsHaveSeparateCores();
            SuperstructuresAreStaticScenery();
            FrontSplitsTheSky();
            ConsoleFormationsFollowTheKey();
            FrontCloudProfilesHaveVerticalStructure();
            FieldIsDeterministic();
            VisibilityFollowsRain();
            ClassificationAndConditions();
            WordsBasics();
            CloudDensityMapsCover();
            FlightLevelPrecipitation();
        }

        private static WeatherKey Key(uint seed = 20260918u, bool dynamic = true,
            WeatherRegimeType? start = null, float interval = 5f, float fade = 60f)
            => new WeatherKey(seed, 0f, dynamic, start.HasValue ? (byte)start.Value : WeatherKey.AutoState, interval, fade);

        private static WeatherKey Held(WeatherRegimeType state, uint seed = 24u) => Key(seed, false, state);

        private static void TimelineStepsOnTheInterval()
        {
            WeatherKey key = Key(7u);
            TimelineState a = WeatherTimeline.Evaluate(key, 299f);
            TimelineState b = WeatherTimeline.Evaluate(key, 301f);
            TestAssert.That(a.Step == 0 && b.Step == 1, "a new state every five minutes");
            TestAssert.That(Math.Abs(b.NextChangeAt - 600f) < 0.01f, "the next change is one interval on");
            TestAssert.That(WeatherTimeline.Evaluate(key, 361f).Blend == 1f, "the fade lasts sixty seconds");
            TestAssert.That(WeatherTimeline.Evaluate(key, 330f).Blend > 0f && WeatherTimeline.Evaluate(key, 330f).Blend < 1f,
                "the change fades in");
            WeatherKey slow = Key(7u, interval: 12f);
            TestAssert.That(WeatherTimeline.Evaluate(slow, 700f).Step == 0, "the interval follows the host setting");
        }

        private static void StatesMoveOnlyToNeighbours()
        {
            bool[] seen = new bool[StateTable.Count];
            for (uint seed = 1; seed <= 20; seed++)
            {
                WeatherKey key = Key(seed);
                for (int step = 1; step < 400; step++)
                {
                    TimelineState s = WeatherTimeline.Evaluate(key, step * 300f + 1f);
                    TestAssert.That(Math.Abs((int)s.To - (int)s.From) <= 1,
                        $"jumped {s.From} -> {s.To} at step {step} seed {seed}");
                    seen[(int)s.To] = true;
                }
            }
            for (int i = 0; i < seen.Length; i++)
                TestAssert.That(seen[i], "the walk should reach " + (WeatherRegimeType)i);
        }

        private static void HeldSkyNeverChanges()
        {
            WeatherKey key = Held(WeatherRegimeType.Overcast);
            for (float t = 0f; t < 4f * 3600f; t += 97f)
            {
                TimelineState s = WeatherTimeline.Evaluate(key, t);
                TestAssert.That(s.To == WeatherRegimeType.Overcast && s.Level == (int)WeatherRegimeType.Overcast,
                    "held sky changed");
                TestAssert.That(float.IsPositiveInfinity(s.NextChangeAt), "held sky predicted a change");
            }
        }

        private static void SkyIsStaticBetweenFades()
        {
            var field = new WeatherField();
            for (uint seed = 1; seed <= 8; seed++)
            {
                WeatherKey key = Key(seed);
                for (int step = 0; step < 24; step++)
                {
                    float settled = step * 300f + WeatherTimeline.SettleSeconds(key) + 1f, late = step * 300f + 299f;
                    field.Build(key, settled, HalfX, HalfZ);
                    WeatherPoint a = field.Sample(9000f, -14000f);
                    int cells = field.CellCount;
                    field.Build(key, late, HalfX, HalfZ);
                    WeatherPoint b = field.Sample(9000f, -14000f);
                    TestAssert.That(a.Cover == b.Cover && a.RainRate == b.RainRate && cells == field.CellCount,
                        $"the sky moved during a hold (seed {seed}, step {step})");
                }
            }
        }

        private static void FadesNeverPop()
        {
            var field = new WeatherField();
            float[] xs = { 0f, 12000f, -20000f, 26000f };
            float[] zs = { 0f, -8000f, 15000f, 21000f };
            float worstCover = 0f;
            for (uint seed = 3; seed < 9; seed++)
            {
                WeatherKey key = Key(seed);
                var cover = new float[xs.Length];
                var rain = new float[xs.Length];
                var wind = new float[xs.Length];
                for (int t = 0; t < 2 * 3600; t++)
                {
                    field.Build(key, t, HalfX, HalfZ);
                    for (int k = 0; k < xs.Length; k++)
                    {
                        WeatherPoint p = field.Sample(xs[k], zs[k]);
                        if (t > 0)
                        {
                            worstCover = Math.Max(worstCover, Math.Abs(p.Cover - cover[k]));
                            TestAssert.That(Math.Abs(p.RainRate - rain[k]) < 3f,
                                $"rain popped by {p.RainRate - rain[k]:F2} mm/h at t={t} seed {seed}");
                            TestAssert.That(Math.Abs(p.WindSpeed - wind[k]) < 1.5f,
                                $"wind popped by {p.WindSpeed - wind[k]:F2} at t={t} seed {seed}");
                        }
                        cover[k] = p.Cover;
                        rain[k] = p.RainRate;
                        wind[k] = p.WindSpeed;
                        TestAssert.That(p.RainRate >= 0f && p.RainRate <= WeatherField.MaxRainRate, "rain in range");
                        TestAssert.That(p.VisibilityKm >= 0.3f && p.VisibilityKm <= 50f, "visibility in range");
                    }
                }
            }
            TestAssert.That(worstCover < 0.05f, $"cover popped by {worstCover:F3} in one second");
        }

        private static void LayoutOnlyChangesUnderAnEmptySky()
        {
            var field = new WeatherField();
            int swaps = 0;
            for (uint seed = 1; seed <= 12; seed++)
            {
                WeatherKey key = Key(seed, start: WeatherRegimeType.Clear);
                uint layout = WeatherTimeline.Evaluate(key, 0f).Layout;
                for (float t = 0f; t < 10f * 3600f; t += 5f)
                {
                    TimelineState s = WeatherTimeline.Evaluate(key, t);
                    if (s.Layout == layout) continue;
                    swaps++;
                    layout = s.Layout;
                    TestAssert.That(s.Level == 0f && s.GrowthLevel == 0f, "the layout swapped with cloud on the map");
                    field.Build(key, t, HalfX, HalfZ);
                    TestAssert.That(field.CellCount == 0 && field.FrontCount == 0 && field.CloudClusterCount == 0,
                        "a clear sky has nothing left to swap");
                }
            }
            TestAssert.That(swaps > 3, "layouts are re-rolled through clear skies, saw " + swaps);
        }

        private static void StatesBuildRealisticSkies()
        {
            var field = new WeatherField();
            field.Build(Held(WeatherRegimeType.Clear), 600f, HalfX, HalfZ);
            TestAssert.That(field.CellCount == 0 && field.FrontCount == 0, "clear sky has no cells or fronts");
            for (int i = -3; i <= 3; i++)
            {
                WeatherPoint p = field.Sample(i * 12000f, i * 7000f);
                TestAssert.That(p.RainRate < 0.01f && p.VisibilityKm > 30f, "clear sky is dry and sees far");
            }

            field.Build(Held(WeatherRegimeType.Scattered), 600f, HalfX, HalfZ);
            for (int i = 0; i < field.CellCount; i++)
            {
                StormCell cell = field.Cell(i);
                TestAssert.That(cell.TopMax - cell.Base < 4000f && cell.LightningPeak == 0f,
                    "scattered cumulus stays shallow and silent");
            }

            field.Build(Held(WeatherRegimeType.Storm), 600f, HalfX, HalfZ);
            TestAssert.That(field.CellCount >= 8 && field.FrontCount == WeatherFronts.MaxFronts,
                "a storm fires most cell sites and both front bands");
            bool deep = false, lightning = false;
            for (int i = 0; i < field.CellCount; i++)
            {
                StormCell c = field.Cell(i);
                deep |= c.TopMax - c.Base > 5000f;
                lightning |= c.LightningRate > 0f;
                WeatherPoint under = field.Sample(c.X, c.Z);
                TestAssert.That(under.RainRate >= 0.5f * c.PeakRain * c.RainLevel, "it rains under a storm core");
                TestAssert.That(under.Cover > 0.9f, "a mature cell is overcast overhead");
            }
            TestAssert.That(deep && lightning, "storm cells are deep and electric");

            field.Build(Held(WeatherRegimeType.Overcast), 600f, HalfX, HalfZ);
            FrontState band = field.Front(0);
            float across = band.OffsetAtAlong(0f) - 2000f;
            WeatherPoint underBand = field.Sample(band.NormalX * across, band.NormalZ * across);
            TestAssert.That(field.FrontCount == 1 && underBand.FrontCover > 0.5f,
                "overcast carries one connected frontal band");
        }

        private static void CoverGrowsUpTheLadder()
        {
            var field = new WeatherField();
            float previous = -1f;
            for (int state = 0; state <= (int)WeatherRegimeType.Overcast; state++)
            {
                field.Build(Held((WeatherRegimeType)state, 11u), 600f, HalfX, HalfZ);
                float mean = field.MeanCover(7);
                TestAssert.That(mean > previous, $"{(WeatherRegimeType)state} must be cloudier than the state below ({mean:F2})");
                previous = mean;
            }
        }

        private static void FairCloudGroupsHaveSeparateCores()
        {
            var field = new WeatherField();
            field.Build(Held(WeatherRegimeType.Scattered), 600f, HalfX, HalfZ);
            bool found = false;
            for (int i = 0; i < field.CloudClusterCount; i++)
            {
                DryCloudCluster cloud = field.CloudCluster(i);
                if (cloud.Strength < 0.35f) continue;
                WeatherPoint core = field.Sample(cloud.X, cloud.Z);
                TestAssert.That(core.ClusterCover > 0.75f && core.CellShape > 0.75f,
                    "cumulus cores are discrete towers in both weather and render fields");
                TestAssert.That(cloud.CoverAt(cloud.X + cloud.Radius * 1.5f, cloud.Z + cloud.Radius * 1.5f) == 0f,
                    "separate cumulus cores leave clear air between groups");
                found = true;
            }
            TestAssert.That(found, "scattered sky forms cumulus groups");
        }

        private static void SuperstructuresAreStaticScenery()
        {
            var field = new WeatherField();
            field.Build(Held(WeatherRegimeType.Clear), 600f, HalfX, HalfZ);
            TestAssert.That(field.SuperstructureCount == 0, "a clear sky has no storm set-pieces");
            field.Build(Held(WeatherRegimeType.Scattered), 600f, HalfX, HalfZ);
            TestAssert.That(field.SuperstructureCount == 1 &&
                field.SuperstructureAt(0).Kind == SuperstructureKind.Supercell,
                "a convective sky shows one distant cumulonimbus");

            field.Build(Held(WeatherRegimeType.Storm), 600f, HalfX, HalfZ);
            TestAssert.That(field.SuperstructureCount == 3, "a storm shows a squall line and two cells");
            TestAssert.That(field.SuperstructureAt(0).Kind == SuperstructureKind.ShelfLine, "the squall line comes first");
            var first = new Superstructure[Superstructures.MaxCount];
            for (int i = 0; i < field.SuperstructureCount; i++)
            {
                Superstructure s = field.SuperstructureAt(i);
                first[i] = s;
                float distance = (float)Math.Sqrt(s.X * s.X + s.Z * s.Z);
                TestAssert.That(distance > Math.Max(HalfX, HalfZ) + 25000f && distance < 220000f,
                    $"set-piece {i} sits outside the theater and inside the march ({distance / 1000f:F0} km)");
                TestAssert.That(s.Top > 8000f && s.Top < 16000f && s.Strength > 0.99f, "a storm set-piece is built and tall");
            }
            field.Build(Held(WeatherRegimeType.Storm), 3000f, HalfX, HalfZ);
            for (int i = 0; i < field.SuperstructureCount; i++)
                TestAssert.That(field.SuperstructureAt(i).X == first[i].X && field.SuperstructureAt(i).Z == first[i].Z,
                    "set-pieces never move");

            // The console can force any set-piece into a clear sky, and re-roll where they sit.
            WeatherKey forced = Held(WeatherRegimeType.Clear).WithSets(Superstructures.SquallLineSet);
            field.Build(forced, 600f, HalfX, HalfZ);
            TestAssert.That(field.SuperstructureCount == 1 && field.SuperstructureAt(0).Set == Superstructures.SquallLineSet &&
                field.SuperstructureAt(0).Strength == 1f, "a forced squall line stands in a clear sky");
            float x0 = field.SuperstructureAt(0).X;
            field.Build(forced.WithLayoutSalt(1), 600f, HalfX, HalfZ);
            TestAssert.That(field.SuperstructureCount == 1 && field.SuperstructureAt(0).X != x0, "a re-roll moves the set-piece");
            TestAssert.That(WeatherTimeline.Evaluate(forced.WithLayoutSalt(1), 600f).To == WeatherRegimeType.Clear,
                "a re-roll leaves the weather state alone");
            TestAssert.That(!forced.Equals(forced.WithLayoutSalt(1)) && !forced.Equals(Held(WeatherRegimeType.Clear)),
                "set-pieces and layout are part of the synced key");
        }

        private static void FrontSplitsTheSky()
        {
            var field = new WeatherField();
            field.Build(Held(WeatherRegimeType.Scattered, 11u), 600f, HalfX, HalfZ);
            TestAssert.That(field.Split.Amount == 0f, "fair-weather skies have no frontal boundary");

            field.Build(Held(WeatherRegimeType.Overcast, 11u), 600f, HalfX, HalfZ);
            SkySplit split = field.Split;
            TestAssert.That(split.Amount > 0.5f, "an overcast sky is a front passing over the map");
            // Walk across the boundary along its normal: open ahead, full deck behind.
            float ahead = 0f, behind = 0f;
            for (int i = -2; i <= 2; i++)
            {
                float along = i * 12000f;
                float bx = -split.NormalZ * along, bz = split.NormalX * along;
                float line = split.Offset;
                ahead += field.Sample(bx + split.NormalX * (line + 60000f), bz + split.NormalZ * (line + 60000f)).BackgroundCover;
                behind += field.Sample(bx + split.NormalX * (line - 60000f), bz + split.NormalZ * (line - 60000f)).BackgroundCover;
            }
            TestAssert.That(behind > ahead * 2f && behind / 5f > 0.5f,
                $"the deck lies behind the front ({behind / 5f:F2}) and opens ahead of it ({ahead / 5f:F2})");
            FrontState band = field.Front(0);
            TestAssert.That(Math.Abs(band.NormalX - split.NormalX) < 1e-5f && Math.Abs(band.Offset - split.Offset) < 1e-3f,
                "the main front band lies on the boundary");
        }

        private static void ConsoleFormationsFollowTheKey()
        {
            var field = new WeatherField();
            WeatherKey key = Held(WeatherRegimeType.Clear)
                .WithSets(Superstructures.StormEyeSet | Superstructures.LenticularSet | Superstructures.FogBankSet)
                .WithAnchor(12000f, -8000f);
            field.Build(key, 600f, HalfX, HalfZ);
            Superstructure eye = default, lens = default;
            for (int i = 0; i < field.SuperstructureCount; i++)
            {
                Superstructure s = field.SuperstructureAt(i);
                if (s.Kind == SuperstructureKind.StormEye) eye = s;
                if (s.Kind == SuperstructureKind.Lenticulars) lens = s;
            }
            TestAssert.That(eye.Strength == 1f && eye.X == 12000f && eye.Z == -8000f, "the storm eye stands on its anchor");
            float apart = (float)Math.Sqrt((lens.X - eye.X) * (lens.X - eye.X) + (lens.Z - eye.Z) * (lens.Z - eye.Z));
            TestAssert.That(lens.Strength == 1f && Math.Abs(apart - 30000f) < 1f, "lenticulars stand beside the eye, not in it");
            TestAssert.That(eye.Top > 12000f && eye.Size > 8000f, "an eyewall is tall around a wide eye");

            field.Build(key.WithoutAnchor(), 600f, HalfX, HalfZ);
            TestAssert.That(field.SuperstructureAt(0).X != 12000f, "without an anchor set-pieces take their default spot");

            field.Build(Held(WeatherRegimeType.Overcast, 11u), 600f, HalfX, HalfZ);
            float heading = field.Split.Heading;
            field.Build(Held(WeatherRegimeType.Overcast, 11u).WithFrontTurn(2), 600f, HalfX, HalfZ);
            TestAssert.That(Math.Abs(field.Split.Heading - heading - 90f) < 0.01f, "the console turns the front by 45 degree steps");
            TestAssert.That(Held(WeatherRegimeType.Clear).WithFrontTurn(9).FrontTurn == 1 &&
                Held(WeatherRegimeType.Clear).WithFrontTurn(-1).FrontTurn == 7, "front turns wrap around");
            TestAssert.That(!key.Equals(key.WithoutAnchor()) && !key.Equals(key.WithFrontTurn(1)),
                "placement and front turn are part of the synced key");
        }

        private static void FieldIsDeterministic()
        {
            var a = new WeatherField();
            var b = new WeatherField();
            for (float t = 0f; t < 7200f; t += 611f)
            {
                a.Build(Key(99u), t, HalfX, HalfZ, 14f);
                b.Build(new WeatherKey(99u, 0f, true, WeatherKey.AutoState), t, HalfX, HalfZ, 14f);
                TestAssert.That(a.CellCount == b.CellCount, "cell count differs between peers");
                for (int i = -2; i <= 2; i++)
                {
                    WeatherPoint pa = a.Sample(i * 15000f, -i * 9000f);
                    WeatherPoint pb = b.Sample(i * 15000f, -i * 9000f);
                    TestAssert.That(pa.RainRate == pb.RainRate && pa.Cover == pb.Cover && pa.WindX == pb.WindX,
                        "same key and time must give the same sky");
                }
            }
        }

        private static void ClassificationAndConditions()
        {
            TestAssert.That(WeatherField.Classify(0f) == PrecipitationKind.None, "none");
            TestAssert.That(WeatherField.Classify(0.3f) == PrecipitationKind.Drizzle, "drizzle");
            TestAssert.That(WeatherField.Classify(1f) == PrecipitationKind.Light, "light");
            TestAssert.That(WeatherField.Classify(5f) == PrecipitationKind.Moderate, "moderate");
            TestAssert.That(WeatherField.Classify(20f) == PrecipitationKind.Heavy, "heavy");
            TestAssert.That(WeatherField.Classify(80f) == PrecipitationKind.Violent, "violent");
            TestAssert.That(StateTable.FromConditions(0.05f) == WeatherRegimeType.Clear, "clear band");
            TestAssert.That(StateTable.FromConditions(0.7f) == WeatherRegimeType.Overcast, "overcast band");
            TestAssert.That(StateTable.FromConditions(1f) == WeatherRegimeType.Storm, "storm band");
            TestAssert.That(StateTable.At(2.5f).Overcast > StateTable.Get(WeatherRegimeType.Scattered).Overcast &&
                StateTable.At(2.5f).Overcast < StateTable.Get(WeatherRegimeType.Broken).Overcast,
                "a fade sits between its neighbours");
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

        private static void VisibilityFollowsRain()
        {
            WeatherKey key = Held(WeatherRegimeType.Clear);
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

        private static void WordsBasics()
        {
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

        private static void FrontCloudProfilesHaveVerticalStructure()
        {
            FrontEffect warmFar = WeatherFronts.Profile(FrontKind.Warm, -65000f);
            FrontEffect warmRain = WeatherFronts.Profile(FrontKind.Warm, -10000f);
            TestAssert.That(warmFar.BaseOffset > warmRain.BaseOffset + 4500f &&
                warmFar.Depth < warmRain.Depth * 0.4f,
                "warm front leads with a high thin shield and lowers into deep rain cloud");
            FrontEffect coldLine = WeatherFronts.Profile(FrontKind.Cold, 2000f);
            FrontEffect coldRear = WeatherFronts.Profile(FrontKind.Cold, 22000f);
            FrontEffect squallLine = WeatherFronts.Profile(FrontKind.Squall, 1200f);
            FrontEffect squallRear = WeatherFronts.Profile(FrontKind.Squall, 22000f);
            TestAssert.That(coldLine.Depth > coldRear.Depth + 2500f &&
                squallLine.Depth > squallRear.Depth + 4000f &&
                squallLine.Depth > coldLine.Depth + 1500f,
                "cold and squall fronts concentrate ascent near the line and trail a shallower shield");
            foreach (FrontKind kind in new[] { FrontKind.Warm, FrontKind.Cold, FrontKind.Squall })
            {
                FrontEffect previous = WeatherFronts.Profile(kind, -90000f);
                for (float distance = -89900f; distance <= 50000f; distance += 100f)
                {
                    FrontEffect current = WeatherFronts.Profile(kind, distance);
                    TestAssert.That(current.Depth >= 900f && current.Depth <= 8500f &&
                        current.BaseOffset >= -350f && current.BaseOffset <= 5200f,
                        "frontal altitude profiles are bounded");
                    TestAssert.That(Math.Abs(current.BaseOffset - previous.BaseOffset) < 30f &&
                        Math.Abs(current.Depth - previous.Depth) < 130f,
                        "flying across the front must not meet altitude steps");
                    previous = current;
                }
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
