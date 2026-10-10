using System;
using System.IO;
using System.Text.RegularExpressions;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>Source guards for the wire (spec 9.1): the clamp that turned 255 into Storm must not come back,
    /// and the removed fields must not linger anywhere.</summary>
    internal static class WeatherWireGuardTests
    {
        private static readonly Regex ClampToCount = new Regex(@"Clamp\([^;]*\bCount\s*-\s*1", RegexOptions.CultureInvariant);
        private static readonly Regex Removed = new Regex(
            @"\b(FieldStartRegime|FieldFrontTurn|HoldMinutes|BlendMinutes|TransitionProgress)\b", RegexOptions.CultureInvariant);

        public static void Run()
        {
            string weather = Path.Combine(FindRepoRoot(), "modules", "Weather");
            foreach (string dir in new[] { "Runtime", "Networking" })
                foreach (string file in Directory.GetFiles(Path.Combine(weather, dir), "*.cs", SearchOption.AllDirectories))
                {
                    string text = File.ReadAllText(file);
                    TestAssert.That(!ClampToCount.IsMatch(text), Path.GetFileName(file) + " clamps a byte that can be 255 to Count - 1");
                    // The manager's own TransitionProgress property is the on-screen fade, not the old wire field.
                    string checkedText = Path.GetFileName(file) == "WeatherManager.cs"
                        ? text.Replace("TransitionProgress => transitionProgress", string.Empty) : text;
                    TestAssert.That(!Removed.IsMatch(checkedText), Path.GetFileName(file) + " still mentions a removed wire field");
                }
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
