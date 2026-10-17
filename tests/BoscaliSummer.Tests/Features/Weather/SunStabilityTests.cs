using System;
using System.IO;

namespace BoscaliSummer.Tests.Features.Weather
{
    // The Unity-only sight sampler cannot be linked into the pure runner. Pin its sampling contract;
    // Run-SunUnityCheck exercises the actual disc/shafts shaders and camera presenters separately.
    internal static class SunStabilityTests
    {
        public static void Run()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BoscaliSummer.sln"))) directory = directory.Parent;
            if (directory == null) throw new InvalidOperationException("Repository root not found");
            string visual = Path.Combine(directory.FullName, "modules", "Weather", "Visuals");
            string sampler = File.ReadAllText(Path.Combine(visual, "WeatherVolumeDressing.cs"));
            TestAssert.That(!sampler.Contains("sightLine") && sampler.Contains("sightStep * (sightTap + 0.5f)"),
                "Successive sun rays sample identical fixed midpoints instead of publishing alternating cloud depths.");
            TestAssert.That(sampler.Contains("sightOrigin + sightDirection") && sampler.Contains("if (sightTap == 0)"),
                "The batched sun ray holds one observer, direction and spacing until completion.");
            TestAssert.That(sampler.Contains("sightDepth = nextSight = 0f; sightOpen = 1f;"),
                "Discard partial sun-ray state on restore/hide instead of carrying old scene cloud into the next ray.");
            string pass = File.ReadAllText(Path.Combine(visual, "WeatherCloudPass.cs"));
            TestAssert.That(pass.Contains("Shader.SetGlobalFloat(CloudLowRes.CoverFrameId, 0f)") &&
                File.ReadAllText(Path.Combine(visual, "CloudLowRes.cs")).Contains("cmd.SetGlobalFloat(CoverFrameId, 1f)"),
                "Only this camera's executed cloud pass makes its cloud cover valid for the sun.");
        }
    }
}
