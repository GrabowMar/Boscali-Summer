using HarmonyLib;
using BoscaliSummer.Features.Hud.Runtime;

namespace BoscaliSummer.Features.Hud.Patches
{
    [HarmonyPatch]
    internal static class ThirdPersonHudPatches
    {
        [HarmonyPatch(typeof(HUDUnitMarker), nameof(HUDUnitMarker.UpdatePosition))]
        [HarmonyPostfix]
        private static void MarkerProjected(HUDUnitMarker __instance)
        {
            ThirdPersonHudController owner = ThirdPersonHudController.Instance;
            if (owner != null && owner.CollectProjection) owner.Projection.Record(__instance);
        }

        [HarmonyPatch(typeof(HUDUnitMarker), nameof(HUDUnitMarker.JammingDistortion))]
        [HarmonyPrefix]
        private static void BeforeDistortion(HUDUnitMarker __instance, out UnityEngine.Vector3 __state)
        { __state = __instance.image != null ? __instance.image.transform.position : UnityEngine.Vector3.zero; }

        [HarmonyPatch(typeof(HUDUnitMarker), nameof(HUDUnitMarker.JammingDistortion))]
        [HarmonyPostfix]
        private static void AfterDistortion(HUDUnitMarker __instance, UnityEngine.Vector3 __state)
        {
            ThirdPersonHudController owner = ThirdPersonHudController.Instance;
            if (owner != null && owner.CollectProjection && __instance.image != null)
                owner.Projection.RecordDistortion(__instance, __instance.image.transform.position - __state);
        }

        [HarmonyPatch(typeof(CameraStateManager), nameof(CameraStateManager.SwitchState))]
        [HarmonyPrefix]
        private static void SwitchStatePrefix() => ReleaseBeforeTransition();

        [HarmonyPatch(typeof(CameraStateManager), nameof(CameraStateManager.SetFollowingUnit))]
        [HarmonyPrefix]
        private static void FollowingUnitPrefix() => ReleaseBeforeTransition();

        [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.Maximize))]
        [HarmonyPrefix]
        private static void MaximizePrefix() => ReleaseBeforeTransition();

        [HarmonyPatch(typeof(GameplayUI), nameof(GameplayUI.PauseGame))]
        [HarmonyPrefix]
        private static void PausePrefix() => ReleaseBeforeTransition();

        private static void ReleaseBeforeTransition()
        {
            ThirdPersonHudController.Instance?.ReleaseVisibility();
            ThirdPersonHudController.Instance?.FlightCamera.Reset();
        }

        [HarmonyPatch(typeof(CameraStateManager), nameof(CameraStateManager.SwitchState))]
        [HarmonyPostfix]
        private static void SwitchStatePostfix()
        {
            ThirdPersonHudController.Instance?.ApplyVisibility();
        }

        [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.Minimize))]
        [HarmonyPostfix]
        private static void MinimizeMapPostfix()
        {
            ThirdPersonHudController.Instance?.ApplyVisibility();
        }

        [HarmonyPatch(typeof(GameplayUI), nameof(GameplayUI.ResumeGame))]
        [HarmonyPostfix]
        private static void ResumeGamePostfix()
        {
            ThirdPersonHudController.Instance?.ApplyVisibility();
        }

        [HarmonyPatch(typeof(CameraStateManager), nameof(CameraStateManager.SetFollowingUnit))]
        [HarmonyPostfix]
        private static void SetFollowingUnitPostfix()
        {
            ThirdPersonHudController.Instance?.ApplyVisibility();
        }
    }
}
