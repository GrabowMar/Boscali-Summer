using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>Tier 2 of the no-pop contract (spec 9.6.2) for the lattice features, and rigidity (9.6.1) of the lattice and the base field
    /// under a moving frame. The tape does not move in this slice, so a slide of the field at v m/s is measured as a step of v metres along
    /// the tape axis between two samples: |Sample(p + u * v) - Sample(p)| is what a fixed point sees in one second.</summary>
    internal static class NoPopTierTwoTests
    {
        private const float V = 45f; // m/s: pace 6 at wind 12, the contract's v_max

        public static void Run()
        {
            OneCumulusGroupStaysUnderItsCap();
            FullLatticeStaysUnderItsCap();
            StormCellTable();
            FieldSlideStaysUnderTheCaps();
            LatticeAndBaseFieldAreRigid();
        }

        private static void OneCumulusGroupStaysUnderItsCap()
        {
            float worst = 0f;
            foreach ((LowGenus genus, float streets) in new[] { (LowGenus.Humilis, 0f), (LowGenus.Mediocris, 0f), (LowGenus.Congestus, 0f), (LowGenus.Cumulonimbus, 0f), (LowGenus.Humilis, 0.8f) })
            {
                LatticeSource src = CumulusLatticeTests.Source(61u, new SkyParams { CumulusLevel = 1f, Streets = streets, PuffDepth = 500f }, genus);
                int groups = 0;
                for (int gz = -3; gz < 3; gz++)
                for (int gx = -3; gx < 3; gx++)
                for (int k = 0; k < 6; k++)
                {
                    if (!CumulusLattice.Resolve(src, gx, gz, k, out CumulusGroup g)) break;
                    g.Strength = 1f;
                    groups++;
                    for (int d = 0; d < 6; d++)
                    {
                        float angle = d * 0.5236f, dx = (float)Math.Cos(angle), dz = (float)Math.Sin(angle);
                        for (float t = -g.Reach; t <= g.Reach; t += 6f)
                        {
                            float a = g.CoverAt(g.X + dx * t, g.Z + dz * t), b = g.CoverAt(g.X + dx * (t + V), g.Z + dz * (t + V));
                            worst = Math.Max(worst, Math.Abs(b - a));
                        }
                    }
                }
                TestAssert.That(groups > 20, "groups tested for " + genus);
            }
            Console.WriteLine("  one cumulus group, worst change in one second at 45 m/s: " + worst.ToString("0.0000") + " (cap 0.07)");
            TestAssert.That(worst <= 0.0701f, "one cumulus group changes at most 0.07 per second at 45 m/s: " + worst);
        }

        private static void FullLatticeStaysUnderItsCap()
        {
            float worst = 0f;
            foreach ((LowGenus genus, float streets) in new[] { (LowGenus.Humilis, 0f), (LowGenus.Congestus, 0f), (LowGenus.Cumulonimbus, 0f), (LowGenus.Humilis, 0.8f) })
            for (uint seed = 1; seed <= 3; seed++)
            {
                LatticeSource src = CumulusLatticeTests.Source(seed * 313u, new SkyParams { CumulusLevel = 1f, Streets = streets, PuffDepth = 500f }, genus, seed * 40f);
                var cache = new CellCache<CumulusGroup>();
                for (int line = 0; line < 4; line++)
                {
                    float angle = line * 0.8f + seed, dx = (float)Math.Cos(angle), dz = (float)Math.Sin(angle);
                    var cover = new float[8000];
                    for (int i = 0; i < cover.Length; i++)
                    {
                        float t = i * 5f;
                        CumulusLattice.Sample(src, cache, dx * t - 20000f, dz * t, CumulusLattice.Half, out float clear, out _, out _);
                        cover[i] = 1f - clear;
                    }
                    int step = (int)(V / 5f); // 9 samples of 5 m: one second at 45 m/s
                    for (int i = step; i < cover.Length; i++) worst = Math.Max(worst, Math.Abs(cover[i] - cover[i - step]));
                }
            }
            Console.WriteLine("  full cumulus lattice, worst change in one second at 45 m/s: " + worst.ToString("0.0000") + " (cap 0.10)");
            TestAssert.That(worst <= 0.1001f, "the full lattice changes at most 0.10 per second at 45 m/s: " + worst);
        }

        private static void StormCellTable()
        {
            Console.WriteLine("  storm cell rain, worst mm/h change in one second at 45 m/s (cap 3):");
            foreach (float radiusKm in new[] { 3.0f, 4.0f, 5.3f })
            foreach (float severity in new[] { 0f, 0.6f, 1f })
            {
                var cell = new StormCell { Radius = radiusKm * 1000f, Strength = 1f, PeakRain = 30f + 90f * severity };
                float rain = 0f, cover = 0f, shape = 0f, wind = 0f;
                for (float x = -6.2f * cell.Radius; x <= 6.2f * cell.Radius; x += 5f)
                {
                    rain = Math.Max(rain, Math.Abs(cell.RainAt(x + V, 0f) - cell.RainAt(x, 0f)));
                    cover = Math.Max(cover, Math.Abs(cell.CoverAt(x + V, 0f) - cell.CoverAt(x, 0f)));
                    shape = Math.Max(shape, Math.Abs(cell.ShapeAt(x + V, 0f) - cell.ShapeAt(x, 0f)));
                    cell.WindAt(x + V, 0f, out float ox, out float oz, out float up, out _);
                    cell.WindAt(x, 0f, out float px, out float pz, out float pu, out _);
                    wind = Math.Max(wind, Math.Max(Math.Abs(ox - px), Math.Abs(up - pu)));
                }
                float bound = 0.858f * V * cell.PeakRain / cell.Radius * 1.06f; // the Gaussian core's steepest slope, plus the faint halo
                Console.WriteLine("    R " + radiusKm.ToString("0.0") + " km, P " + cell.PeakRain.ToString("0") + ": rain " + rain.ToString("0.00") + " (bound " + bound.ToString("0.00") + "), cover " +
                    cover.ToString("0.000") + ", shape " + shape.ToString("0.000") + ", wind " + wind.ToString("0.00") + " m/s");
                TestAssert.That(rain <= bound && rain <= 3f, "a storm cell's rain stays under its bound and the 3 mm/h/s ceiling: " + rain);
                TestAssert.That(cover <= 1.54f * V / cell.Radius + 1e-4f && cover <= 0.0232f, "its cover changes at most 1.54 * 45 / R per second: " + cover);
                TestAssert.That(shape <= 1.54f * V / cell.Radius + 1e-4f, "so does its shape: " + shape);
                TestAssert.That(wind < 2.5f, "its outflow changes gently: " + wind);
            }
        }

        private static void FieldSlideStaysUnderTheCaps()
        {
            // The whole sample, features and the legacy sheet together, for a held tape: a sky with cumulus only, and a storm with cells.
            foreach ((WeatherKey key, string name, bool cells) in new[]
            {
                (new WeatherKey(81u, 0f, false, skyPin: 14), "sky 15 (mediocris)", false),
                (new WeatherKey(82u, 0f, false, skyPin: 7), "sky 8 (humilis)", false),
                (new WeatherKey(83u, 0f, false, skyPin: 43), "sky 44 (supercell day)", true),
                (new WeatherKey(84u, 0f, false, (byte)WeatherRegimeType.Storm), "the Storm class sky", true),
            })
            {
                var field = new WeatherField();
                field.Build(key, 600f, 40960f, 40960f);
                float ux = field.Frame.UX, uz = field.Frame.UZ;
                float cluster = 0f, cell = 0f, rain = 0f, baseStep = 0f;
                for (int i = 0; i < 500; i++)
                {
                    float x = (i * 7919 % 80000) - 40000f, z = (i * 104729 % 70000) - 35000f;
                    WeatherPoint a = field.Sample(x, z), b = field.Sample(x + ux * V, z + uz * V);
                    cluster = Math.Max(cluster, Math.Abs(a.ClusterCover - b.ClusterCover));
                    cell = Math.Max(cell, Math.Abs(a.CellCover - b.CellCover));
                    rain = Math.Max(rain, Math.Abs(a.RainRate - b.RainRate));
                    baseStep = Math.Max(baseStep, Math.Abs(a.CloudBase - b.CloudBase));
                }
                Console.WriteLine("  " + name + ": cumulus " + cluster.ToString("0.000") + ", cell cover " + cell.ToString("0.000") + ", rain " + rain.ToString("0.00") + ", base " + baseStep.ToString("0.0") + " m per second");
                TestAssert.That(cluster <= 0.1001f, name + ": cumulus cover changes at most 0.10 per second: " + cluster);
                TestAssert.That(cell <= 0.0701f, name + ": storm-cell cover changes at most 0.07 per second: " + cell);
                TestAssert.That(rain <= 3f, name + ": rain changes at most 3 mm/h per second: " + rain);
                if (!cells) TestAssert.That(baseStep <= 30f, name + ": the base changes at most 30 m per second: " + baseStep);
            }
        }

        private static void LatticeAndBaseFieldAreRigid()
        {
            // The frame slides: the same sky at D and at D + 37 km is the same picture moved by 37 km along u (spec 9.6.1). Sky 8 has no front,
            // no rain and no sheet, so the outputs of the lattice and the base-height field are functions of the tape position alone.
            var key = new WeatherKey(91u, 0f, false, skyPin: 7);
            var a = new WeatherField(); var b = new WeatherField();
            a.Build(key, 600f, 40960f, 40960f);
            b.Build(key, 600f, 40960f, 40960f);
            const float slide = 37000f;
            b.UseFixtureTape(b.Tape, new TapeFrame(key.HeadingDeg, slide, noise: a.Frame.Noise));
            int checkedPoints = 0;
            for (int i = 0; i < 200; i++)
            {
                float x = (i * 7919 % 80000) - 40000f, z = (i * 104729 % 70000) - 35000f;
                WeatherPoint p = a.Sample(x, z), q = b.Sample(x - a.Frame.UX * slide, z - a.Frame.UZ * slide);
                TestAssert.That(Math.Abs(p.ClusterCover - q.ClusterCover) < 1e-3f && Math.Abs(p.CellShape - q.CellShape) < 1e-3f &&
                    Math.Abs(p.CloudBase - q.CloudBase) < 0.5f && Math.Abs(p.CellCover - q.CellCover) < 1e-3f,
                    "the lattice and the base field slide rigidly with the frame (point " + i + ")");
                if (p.ClusterCover > 0.05f) checkedPoints++;
            }
            TestAssert.That(checkedPoints > 20, "the comparison saw cloud: " + checkedPoints);
        }
    }
}
