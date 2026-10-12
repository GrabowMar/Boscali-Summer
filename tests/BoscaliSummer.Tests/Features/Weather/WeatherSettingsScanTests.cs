using System;
using System.IO;
using System.Linq;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>
    /// The settings plumbing of spec 8.3, pinned by source scans (the manager and the host table are Unity code): <c>Pace</c> has one owner, <c>TapeMoves</c> and <c>Pace</c> are host-authoritative and
    /// sit on the SET SERVER row, the 1 Hz reconcile goes through <c>TapeReconcile</c> and <c>Rebased</c>, and the old key survives only as a migration.
    /// </summary>
    internal static class WeatherSettingsScanTests
    {
        public static void Run()
        {
            string root = FindRepoRoot();
            string authority = File.ReadAllText(Path.Combine(root, "Core", "Config", "HostAuthority.cs"));
            TestAssert.That(authority.Contains("s.Weather.TapeMoves") && authority.Contains("s.Weather.Pace"), "HostAuthority carries TapeMoves and Pace, so a client's locked page shows the host's values");

            string module = File.ReadAllText(Path.Combine(root, "modules", "Weather", "WeatherModule.cs"));
            TestAssert.That(module.Contains(".Toggle(1, weather.TapeMoves") && module.Contains(".Number(2, weather.Pace"), "SET SERVER > EFFECTS has the TapeMoves toggle and the Pace row");

            string[] sources = Directory.GetFiles(Path.Combine(root, "modules"), "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(Path.Combine(root, "Core"), "*.cs", SearchOption.AllDirectories)).ToArray();
            string[] writers = sources.Where(f => File.ReadAllText(f).Contains("Pace.Value =") ).Select(Path.GetFileName).ToArray();
            TestAssert.That(writers.Length == 1 && writers[0] == "WeatherManager.Console.cs", "Pace has one owner: only the console stepper writes settings.Pace.Value (" + string.Join(", ", writers) + ")");
            string[] moveWriters = sources.Where(f => File.ReadAllText(f).Contains("TapeMoves.Value =")).Select(Path.GetFileName).ToArray();
            TestAssert.That(moveWriters.Length == 1 && moveWriters[0] == "WeatherManager.Console.cs", "TapeMoves is written only by the console's Hold switch (" + string.Join(", ", moveWriters) + ")");

            string manager = File.ReadAllText(Path.Combine(root, "modules", "Weather", "Runtime", "WeatherManager.cs"));
            TestAssert.That(manager.Contains("TapeReconcile.Needed(fieldKey, wantDynamic, wantSpeed)") && manager.Contains("fieldKey.Rebased(missionTime, wantDynamic, wantSpeed)"),
                "the 1 Hz reconcile compares TapeMoves and the pace speed with the key and calls Rebased");

            string[] oldKey = sources.Where(f => File.ReadAllText(f).Contains("DynamicWeatherEnabled")).Select(Path.GetFileName).ToArray();
            TestAssert.That(oldKey.All(n => n == "LegacyConfigMigration.cs" || n == "WeatherSettings.cs"), "DynamicWeatherEnabled lives only in the migration and in the comment that explains the new key (" + string.Join(", ", oldKey) + ")");
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
