using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Core.Game
{
    /// <summary>
    /// Boscali Summer carries the wing in-tree (the Wing module). The retired stand-alone Wing Command plugin must
    /// not run beside it: two wing AIs, two radial menus and the same Harmony targets twice. Owner ruling
    /// 2026-10-05: the built-in wing wins. When WingCommand.dll loads (the chainloader loads it after this plugin),
    /// its plugin's Awake is skipped and the component removed, with one warning naming why.
    /// </summary>
    internal static class ExternalWingGuard
    {
        private const string AssemblyName = "WingCommand";
        private const string PluginType = "WingCommand.Plugin";

        private static ManualLogSource log;
        private static Harmony harmony;
        private static bool patched;

        public static void Install(ManualLogSource logger)
        {
            log = logger;
            try
            {
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                    TryPatch(a);
                if (!patched) AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }
            catch (Exception e)
            {
                log?.LogWarning("[Wing] external Wing Command guard failed to install: " + e.Message);
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args) => TryPatch(args.LoadedAssembly);

        private static void TryPatch(Assembly assembly)
        {
            if (patched || assembly == null || assembly.GetName().Name != AssemblyName) return;
            try
            {
                Type plugin = assembly.GetType(PluginType, false);
                MethodInfo awake = plugin?.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (awake == null)
                {
                    log?.LogWarning("[Wing] stand-alone Wing Command found but its Awake was not; it may run beside the built-in wing.");
                    return;
                }
                harmony ??= new Harmony(Plugin.PluginGuid + ".external-wing-guard");
                harmony.Patch(awake, prefix: new HarmonyMethod(typeof(ExternalWingGuard), nameof(SkipAwake)));
                patched = true;
                AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            }
            catch (Exception e)
            {
                log?.LogWarning("[Wing] could not stop the stand-alone Wing Command: " + e.Message);
            }
        }

        private static bool SkipAwake(MonoBehaviour __instance)
        {
            log?.LogWarning("[Wing] the stand-alone Wing Command plugin is installed; Boscali Summer's built-in wing replaces it, " +
                "so Wing Command is disabled for this session. Remove BepInEx/plugins/WingCommand to silence this warning.");
            if (__instance != null)
            {
                __instance.enabled = false;
                UnityEngine.Object.Destroy(__instance);
            }
            return false;
        }
    }
}
