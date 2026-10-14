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
            TestAssert.That(!march.Contains("_CloudChecker.z"),
                "the dither is frozen per texel: no per-frame cycling anywhere in the march");
            TestAssert.That(march.Contains("phase, 0.5);") &&
                march.Contains("0.5, phase, cloudDistance);"),
                "thin layers and rain curtains sample at the fixed mid-step");

            TestAssert.That(march.Contains("gFoot = max(t * _CloudPixelAngle, stepLength * 0.35);"),
                "far noise is filtered to a third of the step: sharper far noise drew the steps as horizontal slices");
            TestAssert.That(resolve.Contains("float spread = lerp(0.45, 0.15, far);") && !resolve.Contains("float2(x, y) * (1.0 + far)"),
                "the composite blurs distant cloud with a wide kernel on adjacent taps (spaced taps drew blocks at depth edges)");
            TestAssert.That(march.Contains("lerp(0.7, 1.6, smoothstep(10000.0, 60000.0, distanceToSample))") &&
                march.Contains("float sampleAir = CloudAir(t,") && march.Contains("float air = CloudAir(depth,"),
                "distant cloud and thin layers take more haze with distance (0.7 of the ground haze to 1.6 by 60 km)");
            TestAssert.That(march.Contains("return lerp(shaded, _CloudFogColor, smoothstep(10000.0, 60000.0, distance));"),
                "distant haze is plain airlight, not fog shaded by the cloud's own light (dark smudges on the horizon)");
            TestAssert.That(march.Contains("return min(air, exp(-max(0.0, distanceToSample - 8000.0) / 40000.0));"),
                "distant cloud has a visibility floor independent of the air's extinction (clear air left it unhazed)");

            TestAssert.That(resolve.Contains("float rest = occludedHistory ? 0.0 : 1.0 - saturate(motion * 40.0);") &&
                resolve.Contains("lerp(entry, 1.0, rest)"),
                "sky still on screen is frozen: a fresh texel is kept as marched and history is not pulled toward the box");
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
