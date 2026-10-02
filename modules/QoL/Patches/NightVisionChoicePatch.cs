using HarmonyLib;

namespace BoscaliSummer.Modules.QoL.Patches
{
    // A camera change must not undo the player's explicit N-key choice.
    [HarmonyPatch(typeof(NightVision), "NightVis_OnSwitchCam")]
    internal static class NightVisionChoicePatch
    {
        private static bool Prefix() => false;
    }
}
