using BoscaliSummer.Features.Immersion.Runtime;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Features.Immersion.Patches
{
    /// <summary>
    /// Vanilla parents the cockpit camera to <c>cameraPivot</c>, sets the pivot's local rotation to
    /// identity once on entry and never touches it again (only its position). The head rotation is
    /// therefore written there after every cockpit update, and cleared before the state is left so
    /// the next camera state inherits the identity it expects.
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
            if (ImmersionManager.Live == null || cam == null) return;
            cam.cameraPivot.localRotation = Quaternion.identity;
            ImmersionManager.Live.Head.Reset();
        }
    }

    /// <summary>Once per round spawned, for the gun shake. Kept to a reference check and a return
    /// for everyone else's guns.</summary>
    [HarmonyPatch(typeof(Gun), "SpawnBullet")]
    internal static class GunShotShakePatch
    {
        private static void Postfix(Gun __instance)
        {
            ImmersionManager.Live?.OnShot(__instance);
        }
    }
}
