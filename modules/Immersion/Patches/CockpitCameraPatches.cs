using System;
using System.Reflection;
using BoscaliSummer.Modules.Immersion.Runtime;
using HarmonyLib;

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

    /// <summary>
    /// Hits on the followed aircraft raise pilot stress (faster breathing). The client body of the
    /// Mirage RPC is Aircraft.UserCode_RpcDamage_&lt;hash&gt;; the hash changes per game build, so it is
    /// resolved by prefix. Missing target: Harmony skips the patch and hits are simply not counted.
    /// </summary>
    [HarmonyPatch]
    internal static class PlayerHitPatch
    {
        private static MethodBase target;
        private static bool resolved;

        private static MethodBase TargetMethod()
        {
            if (resolved) return target;
            resolved = true;
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Aircraft)))
                if (method.Name.StartsWith("UserCode_RpcDamage_", StringComparison.Ordinal)) { target = method; break; }
            if (target == null) Plugin.Logger?.LogWarning("[Immersion] Aircraft.UserCode_RpcDamage_* not found; pilot stress ignores hits.");
            return target;
        }

        private static bool Prepare() => TargetMethod() != null;

        private static void Postfix(Aircraft __instance)
        {
            if (ImmersionManager.IsCockpitActive && ReferenceEquals(__instance, ImmersionManager.FollowingUnit))
                ImmersionManager.Live?.OnPlayerHit();
        }
    }
}
