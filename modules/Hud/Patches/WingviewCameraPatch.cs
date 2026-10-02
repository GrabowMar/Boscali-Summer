using System;
using BoscaliSummer.Modules.Hud.Presentation;
using BoscaliSummer.Core.Diagnostics;
using HarmonyLib;

namespace BoscaliSummer.Modules.Hud.Patches
{
    /// <summary>
    /// One postfix on vanilla's own orbit-camera update
    /// (docs/superpowers/specs/2026-09-28-wingview-third-person-design.md, "Camera"). Runs after
    /// vanilla has placed the camera and read this frame's pan/tilt input, so
    /// <see cref="Runtime.WingviewCameraState"/> either lets that placement stand (yielding to
    /// look-at-target or free-look) or overwrites it with Wingview's own pose. The field pattern
    /// mirrors the deleted <c>ThirdPersonOrbitPatch</c> (git history) exactly.
    /// </summary>
    [HarmonyPatch(typeof(CameraOrbitState), nameof(CameraOrbitState.UpdateState))]
    internal static class WingviewCameraPatch
    {
        private static void Postfix(CameraOrbitState __instance, CameraStateManager cam,
            ref float ___panView, ref float ___tiltView, float ___viewDistAdjust, float ___lookAtTargetLerp)
        {
            try
            {
                if (cam.currentState != __instance) return;
                HudBoard.Instance?.TickWingview(cam, ref ___panView, ref ___tiltView, ___viewDistAdjust, ___lookAtTargetLerp);
            }
            catch (Exception e)
            {
                PatchGuard.Report("Hud.WingviewCamera", e);
            }
        }
    }
}
