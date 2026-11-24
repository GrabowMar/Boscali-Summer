#if !PUBLIC_RELEASE
using System;
using BoscaliSummer.Core.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Patches
{
    /// <summary>
    /// Sim-only stand-in for head shake and roll (tests/ingame/hud-sync.json): a postfix on the cockpit camera's own UpdateState adds a sinusoidal yaw and roll, and a
    /// FOV breath, to the pose the game just wrote (it writes localRotation fresh every frame, so nothing accumulates). Applied by hand the first time
    /// <c>HudAutomation.Spin</c> runs, never through a module's patch list: a normal run never patches the method, so it costs nothing and verify has nothing new to check.
    /// </summary>
    internal static class SimSpinPatch
    {
        /// <summary>Yaw peak angular rate (deg/s), roll amplitude (deg), FOV breath as a fraction of the FOV.</summary>
        public static float YawDegS, RollDeg, Zoom;
        public static bool Active;

        private const float YawHz = 0.5f, RollHz = 0.37f, ZoomHz = 0.23f;
        private static float startedAt, fovDelta;
        private static bool installed;

        public static void Start(float yawDegS, float rollDeg, float zoom)
        {
            YawDegS = yawDegS;
            RollDeg = rollDeg;
            Zoom = zoom;
            startedAt = Time.time;
            Active = yawDegS != 0f || rollDeg != 0f || zoom != 0f;
            if (installed) return;
            installed = true;
            // After Immersion's CockpitHeadRotationPatch: both compose cam.transform.localRotation, and the spin goes on top
            // (tools/arch/harmony-shared.json).
            new Harmony("boscali.sim.spin").Patch(AccessTools.Method(typeof(CameraCockpitState), "UpdateState"),
                postfix: new HarmonyMethod(typeof(SimSpinPatch), nameof(Postfix)) { after = new[] { "com.marci.boscalisummer.module.immersion" } });
        }

        public static void Stop() => Active = false;

        private static void Postfix(CameraStateManager cam)
        {
            try
            {
                if (cam == null || cam.currentState != cam.cockpitState) return;
                float t = Time.time - startedAt;
                float yaw = Active ? YawDegS / (2f * Mathf.PI * YawHz) * Mathf.Sin(2f * Mathf.PI * YawHz * t) : 0f;
                float roll = Active ? RollDeg * Mathf.Sin(2f * Mathf.PI * RollHz * t) : 0f;
                if (yaw != 0f || roll != 0f) cam.transform.localRotation *= Quaternion.Euler(0f, yaw, roll);
                // The game lerps the FOV from its own last value: take off last frame's breath, put on this frame's.
                Camera main = cam.mainCamera;
                float delta = Active && Zoom != 0f ? main.fieldOfView * Zoom * Mathf.Sin(2f * Mathf.PI * ZoomHz * t) : 0f;
                if (delta != fovDelta || delta != 0f)
                {
                    main.fieldOfView += delta - fovDelta;
                    if (cam.cockpitCamRender != null) cam.cockpitCamRender.fieldOfView = main.fieldOfView;
                    fovDelta = delta;
                }
            }
            catch (Exception e)
            {
                PatchGuard.Report("Hud.SimSpin", e);
            }
        }
    }
}
#endif
