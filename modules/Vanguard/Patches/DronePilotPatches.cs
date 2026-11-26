using System;
using System.Reflection;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Modules.Vanguard.Runtime;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Patches
{
    [HarmonyPatch]
    // Run after AutopilotLandInputPatch so the unattended aircraft has a single final flight command.
    [HarmonyAfter("com.marci.boscalisummer.module.autopilot")]
    internal static class DronePilotAxesPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(PilotPlayerState), nameof(PilotPlayerState.FixedUpdateState));
        private static bool Prepare() => TargetMethod() != null && DronePilot.Supported;

        private static bool Prefix(PilotPlayerState __instance)
        {
            DronePilot pilot = DronePilot.Active;
            if (pilot == null || !pilot.Controls(__instance)) return true;
            try { pilot.ReadPlayerAxes(__instance); return false; }
            catch (Exception error) { PatchGuard.Report(nameof(DronePilotAxesPatch), error); pilot.Release(); return true; }
        }
    }

    [HarmonyPatch]
    internal static class DronePilotActionsPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(PilotPlayerState), "PlayerControls");
        private static bool Prepare() => TargetMethod() != null && DronePilot.Supported;

        private static bool Prefix(PilotPlayerState __instance)
        {
            DronePilot pilot = DronePilot.Active;
            if (pilot == null || !pilot.Controls(__instance)) return true;
            try { pilot.ReadPlayerActions(__instance); }
            catch (Exception error) { PatchGuard.Report(nameof(DronePilotActionsPatch), error); pilot.Release(); }
            return false;
        }
    }

    [HarmonyPatch]
    internal static class DronePilotSteeringPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Missile), "Steering");
        private static bool Prepare() => TargetMethod() != null && DronePilot.Supported;

        private static bool Prefix(Missile __instance, ref Vector3 ___inputs, ref Vector3 ___localAngularVel)
        {
            try { return !DronePilot.ApplySteering(__instance, ref ___inputs, ref ___localAngularVel); }
            catch (Exception error) { PatchGuard.Report(nameof(DronePilotSteeringPatch), error); return true; }
        }
    }
}
