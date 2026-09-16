using HarmonyLib;

namespace BoscaliSummer.Features.Support.Patches
{
    /// <summary>
    /// While the station uplink covers the screen, the maximised map under it must not pan or
    /// zoom. <c>MapControls</c> reads the mouse through Rewired, not uGUI, so the uplink's
    /// full-screen blocker cannot consume the wheel or a drag; the frame is skipped instead and
    /// map control returns the moment the uplink closes.
    /// </summary>
    [HarmonyPatch(typeof(DynamicMap), "MapControls")]
    internal static class UplinkMapControlsGuardPatch
    {
        private static bool Prefix() => !Presentation.PlatformUplink.IsOpen;
    }

    /// <summary>
    /// A cursor over the uplink is not a map position: armed call-ins and other modules' map
    /// orders resolve through this method, so a click on the feed never drops an order on the
    /// terrain hidden behind it.
    /// </summary>
    [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.TryGetCursorCoordinates))]
    internal static class UplinkMapCursorGuardPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!Presentation.PlatformUplink.IsOpen) return true;
            __result = false;
            return false;
        }
    }
}
