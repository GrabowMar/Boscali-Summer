using BoscaliSummer.Core.Game;
using HarmonyLib;

namespace BoscaliSummer.Modules.QoL.Patches
{
    // Vanilla sets the gain from ambient light once a second; while the mod meters the picture it must not fight it.
    [HarmonyPatch(typeof(NightVision), "UpdateGain")]
    internal static class NightVisionGainPatch
    {
        private static bool Prefix() => !NightVisionLink.OwnsGain;
    }
}
