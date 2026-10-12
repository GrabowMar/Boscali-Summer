using System;
using System.IO;
using System.Text.RegularExpressions;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>
    /// The manager is Unity code, so its native wiring (spec 8.1) is pinned by a source scan: the drive gate, the explicit targets, the old easing and the read-back of the cloud height are gone,
    /// and the pure natives, the handshake gate and the ForceWeather mapping are what it calls.
    /// </summary>
    internal static class WeatherNativeScanTests
    {
        public static void Run()
        {
            string manager = Read("Runtime", "WeatherManager.cs");
            foreach (string gone in new[] { "bool drive", "explicitTargets", "UpdateHostProgression", "NativeSkyConditions", "TransitionRateMultiplier", "transitionDuration", "transitionProgress", "NativeTransitionSeconds", "WeatherTimeline" })
                TestAssert.That(!manager.Contains(gone), "WeatherManager no longer mentions " + gone);
            foreach (string wired in new[] { "NativeWeather.Server(field, baseline)", "NativeWeather.Client(field,", "NativeWeather.Ease(", "SyncGate.Decide(", "ForceWeatherMap.Key(", "UpdateNativeTargets();", "PendingSync<WeatherSyncMessage>", "sunFlips++", "NativeWeather.MissionFromTargets(", "GameAccess.IsServer() && !ownNatives" })
                TestAssert.That(manager.Contains(wired), "WeatherManager calls " + wired);

            int update = manager.IndexOf("private void Update()", StringComparison.Ordinal);
            int targets = manager.IndexOf("UpdateNativeTargets();", update, StringComparison.Ordinal);
            int ready = manager.IndexOf("if (targetsReady)", update, StringComparison.Ordinal);
            TestAssert.That(update > 0 && targets > update && ready > targets, "Update derives the targets and then follows them, with no gate on Dynamic or a manual hold in between");
            string between = manager.Substring(update, ready - update);
            TestAssert.That(!Regex.IsMatch(between, @"fieldKey\.Dynamic\s*\|\||isManualOverride\)\s*\n?\s*\{?\s*\n?\s*if \(GameAccess"), "the native block has no Dynamic or manual-override gate");

            int readBack = 0;
            foreach (Match m in Regex.Matches(manager, @"currentCloudHeight\s*=\s*level\.cloudHeight;")) readBack++;
            TestAssert.That(readBack == 1, "level.cloudHeight is read back only where no target exists yet (the not-ready branch), found " + readBack);
            TestAssert.That(!manager.Contains("targetCloudHeight = Regional().CloudBase"), "the base no longer comes from the low layer alone");
            TestAssert.That(manager.Contains("Mathf.Min(NativeWeather.ConditionsCap, currentConditions)"), "ApplyNative caps what it writes to vanilla");
        }

        private static string Read(params string[] parts)
        {
            string root = FindRepoRoot();
            string[] path = new string[parts.Length + 2];
            path[0] = root; path[1] = Path.Combine("modules", "Weather");
            Array.Copy(parts, 0, path, 2, parts.Length);
            return File.ReadAllText(Path.Combine(path));
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
