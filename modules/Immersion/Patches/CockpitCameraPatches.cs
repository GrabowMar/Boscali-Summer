using BoscaliSummer.Modules.Immersion.Runtime;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Patches
{
    /// <summary>
    /// Writes rotational head inertia to cameraPivot during cockpit view, and resets to identity
    /// upon leaving cockpit state.
    /// </summary>
    [HarmonyPatch(typeof(CameraCockpitState), "UpdateState")]
    internal static class CockpitHeadRotationPatch
    {
        private static void Postfix(CameraStateManager cam)
        {
            ImmersionManager manager = ImmersionManager.Live;
            if (manager == null || cam == null || cam.currentState != cam.cockpitState) return;
            cam.cameraPivot.localRotation = manager.HeadOffset;
        }
    }

    [HarmonyPatch(typeof(CameraCockpitState), "LeaveState")]
    internal static class CockpitHeadResetPatch
    {
        private static void Prefix(CameraStateManager cam)
        {
            if (cam != null && cam.cameraPivot != null)
            {
                cam.cameraPivot.localRotation = Quaternion.identity;
            }
            ImmersionManager.Live?.Head.Reset();
        }
    }

    /// <summary>
    /// Recoil shake on gunfire.
    /// Exits in one nanosecond for all foreign aircraft bullets, and when outside cockpit view.
    /// </summary>
    [HarmonyPatch(typeof(Gun), "SpawnBullet")]
    internal static class GunShotShakePatch
    {
        private static void Postfix(Gun __instance)
        {
            if (ImmersionManager.IsCockpitActive && ReferenceEquals(__instance.attachedUnit, ImmersionManager.FollowingUnit))
            {
                ImmersionManager.OnGunShot(__instance);
            }
        }
    }
}
