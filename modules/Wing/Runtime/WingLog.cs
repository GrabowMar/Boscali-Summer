using BepInEx.Logging;
using BoscaliSummer.Modules.Wing.Configuration;
using UnityEngine;

namespace BoscaliSummer.Modules.Wing.Runtime
{
    internal static class WingLog
    {
        internal static ManualLogSource Logger { get; private set; }

        internal static void Init(ManualLogSource logger) { Logger = logger; }

        /// <summary>Diagnostics shown only with Debug/WingVerboseLogging enabled.</summary>
        internal static void Verbose(string message)
        {
            WingConfig settings = WingSettings.Instance;
            if (Logger == null || settings == null || !settings.VerboseLogging.Value) return;
            Logger.LogInfo($"[frame={Time.frameCount}] {message}");
        }

        // Frozen wing logic version (was WMC 1.0.0): log headers + net handshake.
        internal const string FullVersion = "1.0.0";
    }
}
