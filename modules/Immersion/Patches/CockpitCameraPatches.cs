using BoscaliSummer.Modules.Immersion.Runtime;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Patches
{
    /// <summary>
    /// Composes the mod's owned rotational offset on the cockpit pivot and removes that
    /// write on leaving. Native free look, head tracking and translation remain untouched.
    /// </summary>
    [HarmonyPatch(typeof(CameraCockpitState), "UpdateState")]
    internal static class CockpitHeadRotationPatch
    {
        private static void Postfix(CameraStateManager cam)
        {
            ImmersionManager manager = ImmersionManager.Live;
            if (manager == null || cam == null || cam.currentState != cam.cockpitState) return;
            manager.ApplyCameraOffset(cam);
        }
    }

    [HarmonyPatch(typeof(CameraCockpitState), "LeaveState")]
    internal static class CockpitHeadResetPatch
    {
        private static void Prefix(CameraStateManager cam)
        {
            ImmersionManager.Live?.RemoveCameraOffset();
        }
    }

    /// <summary>
    /// Recoil shake on gunfire.
    /// A static cockpit flag and reference comparison skip foreign gunfire immediately.
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
