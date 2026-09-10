using System.Reflection;
using HarmonyLib;

namespace BoscaliSummer.Garrisons
{
    [HarmonyPatch]
    internal static class AirbaseCapturePatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Airbase), "CaptureFaction");
        private static bool Prepare() => TargetMethod() != null;

        private static void Postfix(Airbase __instance, FactionHQ newHQ)
        {
            if (Plugin.Settings.UrbanCombat.GarrisonsEnabled.Value)
                ZoneGarrisonManager.Instance?.ScheduleCapture(__instance, newHQ);
        }
    }

    [HarmonyPatch]
    internal static class GarrisonClientVisualPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Building), "OnStartClient");
        private static bool Prepare() => TargetMethod() != null;

        private static void Postfix(Building __instance)
        {
            if (__instance != null &&
                !string.IsNullOrEmpty(__instance.NetworkUniqueName) &&
                __instance.NetworkUniqueName.StartsWith(ZoneGarrisonManager.NamePrefix, System.StringComparison.Ordinal))
                GarrisonVisual.Apply(__instance);
            else if (__instance != null &&
                !string.IsNullOrEmpty(__instance.NetworkUniqueName) &&
                __instance.NetworkUniqueName.StartsWith(MakeshiftFortificationBuilder.NamePrefix, System.StringComparison.Ordinal))
                MakeshiftFortificationBuilder.ApplyPresentation(__instance);
        }
    }
}
