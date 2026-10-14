using System;
using System.IO;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>
    /// Distant clouds must not boil: past the near limit the march samples from fixed
    /// positions (a cycling dither moves a far sample by kilometres), and the temporal
    /// resolve caches distant history harder than near. Both live only in the shipped
    /// shaders, so they are pinned by source scans, like NoiseTapTests.
    /// </summary>
    internal static class FarCloudStabilityTests
    {
        public static void Run()
        {
            string root = FindRepoRoot();
            string march = File.ReadAllText(Path.Combine(root, "modules", "Weather", "Assets", "Source", "FlightCloud.shader"));
            string resolve = File.ReadAllText(Path.Combine(root, "modules", "Weather", "Assets", "Source", "FlightCloudComposite.shader"));

            TestAssert.That(march.Contains("float segJitter = farSeg || (hero && h0 >= CLOUD_NEAR_LIMIT) ? 0.5 : jitter;"),
                "far segments and distant set-pieces march from fixed sample positions");
            TestAssert.That(march.Contains("lerp(sampleStart, sampleEnd, segJitter)"),
                "the segment sample position uses the distance-chosen jitter");
            TestAssert.That(march.Contains("frac(ign + _CloudChecker.z)"),
                "the near march keeps its cycling dither for the resolve to average out");
            TestAssert.That(march.Contains("phase, 0.5);") &&
                march.Contains("0.5, phase, cloudDistance);"),
                "thin layers and rain curtains sample at the fixed mid-step");

            TestAssert.That(resolve.Contains("smoothstep(45000.0, 120000.0, distance)"),
                "the resolve weights its history cache by cloud distance (near limit to 120 km)");
            TestAssert.That(resolve.Contains("lerp(0.15, 0.04, farCache)"),
                "distant history yields only 4 % toward the fresh clamp box per frame");
            TestAssert.That(resolve.Contains("lerp(0.08 + 0.17 * freshVal.a, 0.03, farCache)"),
                "a distant fresh texel enters at 3 % per visit instead of up to a quarter");
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "BoscaliSummer.sln")) && Directory.Exists(Path.Combine(directory.FullName, "modules"))) return directory.FullName;
                directory = directory.Parent;
            }
            throw new InvalidOperationException("repository root not found");
        }
    }
}
