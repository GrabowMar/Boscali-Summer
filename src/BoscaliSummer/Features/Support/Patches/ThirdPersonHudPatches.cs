using HarmonyLib;
using BoscaliSummer.Features.Support.Runtime;

namespace BoscaliSummer.Features.Support.Patches
{
    [HarmonyPatch]
    internal static class ThirdPersonHudPatches
    {
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

        [HarmonyPatch(typeof(GameplayUI), nameof(GameplayUI.SelectAircraft))]
        [HarmonyPostfix]
        private static void SelectAircraftPostfix()
        {
            ThirdPersonHudController.Instance?.SetSpectating(false);
        }

        [HarmonyPatch(typeof(CameraStateManager), nameof(CameraStateManager.SetFollowingUnit))]
        [HarmonyPostfix]
        private static void SetFollowingUnitPostfix(Unit unit)
        {
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            bool isLocal = hud != null && hud.aircraft != null && hud.aircraft == unit;
            ThirdPersonHudController.Instance?.SetSpectating(!isLocal);
        }
    }
}
