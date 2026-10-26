using HarmonyLib;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Core.Fx
{
    // Adapted from qweaeqew/Boscali-Summer-Feed-Stack-Fix (MIT; see LICENSE).
    // The release fix prevents dead cache entries; this also recovers one left by another camera.
    [HarmonyPatch(typeof(UniversalAdditionalCameraData), "GetOrCreateVolumeStack")]
    internal static class FeedStackRecoveryPatch
    {
        [HarmonyPostfix]
        private static void Postfix(UniversalAdditionalCameraData __instance)
        {
            VolumeStack stack = __instance.volumeStack;
            if (stack == null || stack.GetComponent<DepthOfField>() != null) return;
            __instance.volumeStack = VolumeManager.instance.CreateStack();
            Plugin.Logger?.LogWarning($"Replaced a disposed VolumeStack on camera '{__instance.name}'.");
        }
    }
}
