using HarmonyLib;
using BoscaliSummer.Features.QoL.Runtime;

namespace BoscaliSummer.Features.QoL.Patches
{
    [HarmonyPatch]
    internal static class ThirdPersonHudPatches
    {
        [HarmonyPatch(typeof(FlightHud), "Update")]
        [HarmonyPrefix]
        private static bool FlightHudUpdatePrefix() => RunNativeHud();

        [HarmonyPatch(typeof(HeadMountedDisplay), "Update")]
        [HarmonyPrefix]
        private static bool HelmetUpdatePrefix() => RunNativeHud();

        [HarmonyPatch(typeof(CombatHUD), "LateUpdate")]
        [HarmonyPrefix]
        private static bool CombatHudUpdatePrefix() => RunNativeHud();

        private static bool RunNativeHud() => ThirdPersonHudController.Instance?.DeferNativeHud != true;

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
