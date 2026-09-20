using System;
using System.Reflection;
using HarmonyLib;
using BoscaliSummer.Features.QoL.Runtime;

namespace BoscaliSummer.Features.QoL.Patches
{
    [HarmonyPatch(typeof(CameraOrbitState), nameof(CameraOrbitState.UpdateState))]
    internal static class ThirdPersonOrbitPatch
    {
        private static void Postfix(CameraOrbitState __instance, CameraStateManager cam,
            ref float ___panView, ref float ___tiltView, float ___viewDistAdjust, float ___lookAtTargetLerp)
        {
            if (cam.currentState != __instance) return;
            ThirdPersonHudController.Instance?.FlightCamera.Orbit(cam, ref ___panView, ref ___tiltView,
                ___viewDistAdjust, ___lookAtTargetLerp);
        }
    }

    [HarmonyPatch(typeof(CameraChaseState), nameof(CameraChaseState.UpdateState))]
    internal static class ThirdPersonChasePatch
    {
        private static readonly AccessTools.FieldRef<CameraChaseState, int> PositionRef =
            AccessTools.FieldRefAccess<CameraChaseState, int>("currentPos");
        private static void Postfix(CameraChaseState __instance, CameraStateManager cam, float ___viewDistAdjust)
        {
            if (cam.currentState != __instance || PositionRef == null) return;
            // Back is the verified native enum value 0. Leave wing, belly and camera-tool presets alone.
            ThirdPersonHudController.Instance?.FlightCamera.Chase(cam, ___viewDistAdjust,
                PositionRef(__instance) == 0);
        }
    }
}
