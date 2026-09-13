using System;
using BoscaliSummer.Features.Autopilot.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Features.Autopilot.Patches
{
    /// <summary>Keeps the Boscali Summer root slice present across native wheel rebuilds and
    /// injects it on open only when the wheel is the stock root, never a foreign submenu.</summary>
    [HarmonyPatch(typeof(RadialMenuMain))]
    internal static class RadialMenuLifecyclePatches
    {
        [HarmonyPatch("SetupMain")]
        [HarmonyPrefix]
        private static void SetupMain_Prefix(RadialMenuMain __instance)
        {
            try { BoscaliRadialMenu.EnsureRootInjected(__instance); }
            catch (Exception e) { Plugin.Logger?.LogError("Autopilot: radial injection failed: " + e); }
        }

        [HarmonyPatch(nameof(RadialMenuMain.OpenMenu))]
        [HarmonyPostfix]
        private static void OpenMenu_Postfix(RadialMenuMain __instance)
        {
            try
            {
                if (BoscaliRadialMenu.EnsureRootInjected(__instance, openingRoot: true))
                    RadialMenuAccess.SetupMain(__instance);
            }
            catch (Exception e) { Plugin.Logger?.LogError("Autopilot: radial injection failed while opening: " + e); }
        }

        [HarmonyPatch("OnDestroy")]
        [HarmonyPostfix]
        private static void OnDestroy_Postfix() => BoscaliRadialMenu.Reset();
    }

    /// <summary>Dispatches delegate-backed slices because the native radial methods are
    /// non-virtual.</summary>
    [HarmonyPatch(typeof(RadialMenuAction))]
    internal static class BoscaliMenuActionPatches
    {
        [HarmonyPatch(nameof(RadialMenuAction.AllowedOnAircraft))]
        [HarmonyPrefix]
        private static bool AllowedOnAircraft_Prefix(RadialMenuAction __instance, Aircraft aircraft, ref bool __result)
        {
            if (!(__instance is BoscaliMenuAction action)) return true;
            __result = action.IsAllowed(aircraft);
            return false;
        }

        [HarmonyPatch(nameof(RadialMenuAction.TriggerAction))]
        [HarmonyPrefix]
        private static bool TriggerAction_Prefix(RadialMenuAction __instance, Aircraft aircraft)
        {
            if (!(__instance is BoscaliMenuAction action)) return true;
            action.Invoke(aircraft);
            return false;
        }
    }
}
