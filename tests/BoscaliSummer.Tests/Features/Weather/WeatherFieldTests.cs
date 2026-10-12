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
            StatesBuildRealisticSkies();
            CoverGrowsUpTheLadder();
            FairCloudGroupsHaveSeparateCores();
            SuperstructuresAreStaticScenery();
            ConsoleFormationsFollowTheKey();
            FieldIsDeterministic();
            VisibilityFollowsRain();
            ClassificationAndConditions();
            WordsBasics();
            CloudDensityMapsCover();
            FlightLevelPrecipitation();
        }

        private static WeatherKey Key(uint seed = 20260918u, bool dynamic = true, WeatherRegimeType? start = null)
            => new WeatherKey(seed, 0f, dynamic, start.HasValue ? (byte)start.Value : WeatherKey.AutoState);

        private static WeatherKey Held(WeatherRegimeType state, uint seed = 24u)
        {
            int pin = 0;
            for (int i = 0; i < SkyLibrary.Count; i++) if (SkyLibrary.ClassOf(i) == state) { pin = i; break; }
            return new WeatherKey(seed, 0f, false, (byte)state, skyPin: (byte)pin);
        }

        private static void StatesBuildRealisticSkies()
        {
            var field = new WeatherField();
            var cells = new StormCell[StormLattice.EnumerateCap];
            field.Build(Held(WeatherRegimeType.Clear), 600f, HalfX, HalfZ);
            TestAssert.That(field.ActiveCellCount(0f, 0f, 80000f) == 0, "clear sky has no cells");
            for (int i = -3; i <= 3; i++)
            {
                WeatherPoint p = field.Sample(i * 12000f, i * 7000f);
                TestAssert.That(p.RainRate < 0.01f && p.VisibilityKm > 30f, "clear sky is dry and sees far");
            }

            field.Build(Held(WeatherRegimeType.Scattered), 600f, HalfX, HalfZ);
            int scattered = StormLattice.Enumerate(field.Lattice, 0f, 0f, 120000f, cells);
            for (int i = 0; i < scattered; i++)
                TestAssert.That(cells[i].TopMax - cells[i].Base < 4000f && cells[i].LightningPeak == 0f,
                    "scattered cumulus stays shallow and silent");

            field.Build(Held(WeatherRegimeType.Storm), 600f, HalfX, HalfZ);
            TestAssert.That(field.ActiveCellCount(0f, 0f, 60000f) >= 8, "a storm fires many cells");
            int count = StormLattice.Enumerate(field.Lattice, 0f, 0f, 120000f, cells);
            bool deep = false, lightning = false;
            int mature = 0;
            for (int i = 0; i < count; i++)
            {
                StormCell c = cells[i];
                deep |= c.TopMax - c.Base > 5000f;
                lightning |= c.LightningRate > 0f;
                if (c.Strength < 0.95f) continue;
                mature++;
                field.Frame.FromTape(c.X, c.Z, out float wx, out float wz);
                WeatherPoint under = field.Sample(wx, wz);
                TestAssert.That(under.RainRate >= 0.5f * c.PeakRain * c.Strength, "it rains under a storm core");
                TestAssert.That(under.LowCover > 0.9f, "a mature cell is overcast overhead");
            }
            TestAssert.That(deep && lightning && mature > 3, "storm cells are deep and electric");

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
            for (int i = -3; i <= 3; i++)
            {
                if (!CumulusLattice.FindNearest(field.Lattice, null, i * 17000f, -i * 9000f, 3, 0.8f, out CumulusGroup group)) continue;
                group.BlobCentre(0, out float cx, out float cz);
                field.Frame.FromTape(cx, cz, out float wx, out float wz);
                WeatherPoint core = field.Sample(wx, wz);
                TestAssert.That(core.ClusterCover >= 0.79f && core.CellShape >= 0.79f,
                    "cumulus cores are discrete towers in both weather and render fields");
                found = true;
            }
            TestAssert.That(found, "scattered sky forms cumulus groups");
            int clear = 0, total = 0;
            for (float z = -35000f; z <= 35000f; z += 2500f)
            for (float x = -35000f; x <= 35000f; x += 2500f)
            {
                total++;
                if (field.Sample(x, z).ClusterCover <= 0f) clear++;
            }
            TestAssert.That(clear > total * 0.15f && clear < total * 0.85f,
                $"separate cumulus groups leave clear air between them ({clear} of {total} samples are clear)");
        }

        private static void SuperstructuresAreStaticScenery()
        {
            var field = new WeatherField();
            field.Build(Held(WeatherRegimeType.Clear), 600f, HalfX, HalfZ);
            TestAssert.That(field.SuperstructureCount == 0, "a clear sky has no storm set-pieces");
            // Sky 17, the isolated cumulonimbus (Convective 0.25): the class's first sky, 15, is dry cumulus and shows no distant cell.
            field.Build(new WeatherKey(24u, 0f, false, (byte)WeatherRegimeType.Scattered, skyPin: 16), 600f, HalfX, HalfZ);
            TestAssert.That(field.SuperstructureCount == 1 &&
                field.SuperstructureAt(0).Kind == SuperstructureKind.Supercell,
                "a convective sky shows one distant cumulonimbus");

            // Sky 45, the cold-front storm line (Convective 0.9): the class's first sky, 42, is a multicell cluster at 0.7, too weak for a full-strength squall line.
            WeatherKey storm = new WeatherKey(24u, 0f, false, (byte)WeatherRegimeType.Storm, skyPin: 44);
            field.Build(storm, 600f, HalfX, HalfZ);
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
                TestAssert.That(s.Top > 8000f && s.Top < 16000f && s.Strength > 0.85f, "a storm set-piece is built and tall (the supercell scales with Convective 0.9)");
            }
            field.Build(storm, 3000f, HalfX, HalfZ);
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
            TestAssert.That(SkyMath.ClassOf(field.RowAt(0f, 0f)) == WeatherRegimeType.Clear,
                "a re-roll leaves the weather state alone");
            TestAssert.That(!forced.Equals(forced.WithLayoutSalt(1)) && !forced.Equals(Held(WeatherRegimeType.Clear)),
                "set-pieces and layout are part of the synced key");
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

            TestAssert.That(!key.Equals(key.WithoutAnchor()) && !key.Equals(key.WithLayoutSalt(1)),
                "placement and layout are part of the synced key");
        }

        private static void FieldIsDeterministic()
        {
            var a = new WeatherField();
            var b = new WeatherField();
            for (float t = 0f; t < 7200f; t += 611f)
            {
                a.Build(Key(99u), t, HalfX, HalfZ, 14f);
                b.Build(new WeatherKey(99u, 0f, true, WeatherKey.AutoState), t, HalfX, HalfZ, 14f);
                TestAssert.That(a.ActiveCellCount(0f, 0f, 60000f) == b.ActiveCellCount(0f, 0f, 60000f), "cell count differs between peers");
                for (int i = -2; i <= 2; i++)
                {
                    WeatherPoint pa = a.Sample(i * 15000f, -i * 9000f);
                    WeatherPoint pb = b.Sample(i * 15000f, -i * 9000f);
                    TestAssert.That(pa.RainRate == pb.RainRate && pa.LowCover == pb.LowCover && pa.WindX == pb.WindX,
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
