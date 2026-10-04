using System;
using UnityEngine.SceneManagement;

namespace BoscaliSummer.Core.Diagnostics
{
    /// <summary>
    /// The nomodkit bridge's mod-telemetry contract: a public static class whose full name ends
    /// in ".NOModKitTelemetry", with <c>string Snapshot()</c> returning one JSON object and
    /// <c>string Control(string key, string value)</c> returning one JSON object. The bridge
    /// finds it by reflection, so Boscali takes no reference on nomodkit. Both run on the
    /// Unity main thread (the bridge pumps requests from Update).
    /// Controls: profiling=on|deep|off, reset.
    /// </summary>
    public static class NOModKitTelemetry
    {
        public static string Snapshot() => FootprintProfiler.SnapshotJson();

        public static string Control(string key, string value)
        {
            try
            {
                switch ((key ?? "").Trim().ToLowerInvariant())
                {
                    case "profiling":
                        string wanted = (value ?? "").Trim().ToLowerInvariant();
                        if (wanted == "off" || wanted == "false" || wanted == "0")
                        {
                            FootprintProfiler.Stop();
                            return "{\"ok\":true,\"profiling\":false}";
                        }
                        string mode = FootprintProfiler.Start(wanted == "deep");
                        return "{\"ok\":true,\"profiling\":true,\"mode\":\"" + mode + "\"}";
                    case "reset":
                        FootprintProfiler.ResetWindow();
                        return "{\"ok\":true}";
                    default:
                        return "{\"ok\":false,\"error\":\"unknown key; use profiling=on|deep|off or reset\"}";
                }
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":\"" + ex.GetType().Name + "\"}";
            }
        }

        /// <summary>Starts the profiler from config once the first scene loads, after every module patched.</summary>
        internal static void StartWhenReady(bool deep)
        {
            void OnLoaded(Scene scene, LoadSceneMode loadMode)
            {
                SceneManager.sceneLoaded -= OnLoaded;
                if (!FootprintProfiler.Running) FootprintProfiler.Start(deep);
            }
            SceneManager.sceneLoaded += OnLoaded;
        }
    }
}
