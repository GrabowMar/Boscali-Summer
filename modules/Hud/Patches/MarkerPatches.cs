using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Hud.Runtime;
using HarmonyLib;
// Harmony calls these by reflection.
#pragma warning disable IDE0051

namespace BoscaliSummer.Modules.Hud.Patches
{
    /// <summary>
    /// One marker projection per frame (AV LAYER 2.1 step 2): <c>CombatHUD.UpdateMarkers</c> raises <see cref="HudLayer.InNativeMarkers"/> while it runs, and inside it the
    /// game's own <c>HUDUnitMarker.UpdatePosition</c>/<c>JammingDistortion</c> are skipped while <see cref="ThirdPersonHudCenter.OwnsMarkers"/> (the late repair does them with the
    /// rendered camera). Its audio, jamming decay and round-robin visibility still run. The constructor's own <c>UpdatePosition</c> is outside the flag. Fail open: a frame the
    /// repair did not finish hands placement back the next frame.
    /// </summary>
    [HarmonyPatch(typeof(CombatHUD), "UpdateMarkers")]
    internal static class MarkerUpdatePatch
    {
        private static void Prefix() => HudLayer.InNativeMarkers = true;

        // A finalizer runs even when the body throws, so the flag never sticks.
        private static void Finalizer() => HudLayer.InNativeMarkers = false;
    }

    [HarmonyPatch(typeof(HUDUnitMarker), nameof(HUDUnitMarker.UpdatePosition))]
    internal static class MarkerPositionPatch
    {
        private static bool Prefix() => !ThirdPersonHudCenter.OwnsMarkers;
    }

    [HarmonyPatch(typeof(HUDUnitMarker), nameof(HUDUnitMarker.JammingDistortion))]
    internal static class MarkerJammingPatch
    {
        private static bool Prefix() => !ThirdPersonHudCenter.OwnsMarkers;
    }

    /// <summary>The range ink's base colour: what the game set in <c>UpdateColor</c>, after the wing tint's own postfix (last priority).</summary>
    [HarmonyPatch(typeof(HUDUnitMarker), "UpdateColor")]
    internal static class MarkerColorPatch
    {
        // After Wing's WingHudTint.UpdateColorPatch: the ink captures the colour the tint wrote. Priority.Last keeps it after other plugins.
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("com.marci.boscalisummer.module.wing")]
        private static void Postfix(HUDUnitMarker __instance) => MarkerInk.Capture(__instance);
    }
}
