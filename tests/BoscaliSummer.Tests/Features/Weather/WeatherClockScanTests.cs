using System;
using System.IO;
using System.Text.RegularExpressions;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>Invariant 5 (spec 9.6): the weather runtime never derives the field from Time.time or a
    /// local clock; its only time input is the gated mission clock.</summary>
    internal static class WeatherClockScanTests
    {
        private static readonly Regex WallClock = new Regex(@"\bTime\.time\b", RegexOptions.CultureInvariant);

        public static void Run()
        {
            string root = FindRepoRoot();
            string runtime = Path.Combine(root, "modules", "Weather", "Runtime");
            int renderOnly = 0; // the terrain ripple phase is the one allowed read of Time.time
            foreach (string file in Directory.GetFiles(runtime, "*.cs", SearchOption.AllDirectories))
            {
                foreach (string raw in File.ReadAllLines(file))
                {
                    // A render-only animation phase must follow the game's time scale; it never feeds the field.
                    if (raw.Contains("// render-only")) { renderOnly++; continue; }
                    string code = raw;
                    int comment = code.IndexOf("//", StringComparison.Ordinal);
                    if (comment >= 0) code = code.Substring(0, comment);
                    TestAssert.That(!WallClock.IsMatch(code), Path.GetFileName(file) + " reads Time.time: " + raw.Trim());
                }
            }
            TestAssert.That(renderOnly <= 1, "only the terrain ripple phase may be marked render-only, found " + renderOnly);
            string manager = File.ReadAllText(Path.Combine(runtime, "WeatherManager.cs"));
            TestAssert.That(!manager.Contains("MissionManager>.i?.MissionTime"),
                "WeatherManager must read the mission clock only through TryMissionTime");
            TestAssert.That(manager.Contains("MissionTimeSource.TryNow"), "TryMissionTime must use the shared gate");
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "BoscaliSummer.sln")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "modules"))) return directory.FullName;
                directory = directory.Parent;
            }
            throw new InvalidOperationException("repository root not found");
        }
    }
}
