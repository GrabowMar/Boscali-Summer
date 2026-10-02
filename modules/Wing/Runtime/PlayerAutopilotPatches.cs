using System;
using HarmonyLib;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
// Harmony calls postfixes by reflection.
#pragma warning disable IDE0051

namespace BoscaliSummer.Modules.Wing.Runtime
{
    [HarmonyPatch(typeof(PilotPlayerState))]
    internal static class PlayerAutopilotPatches
    {
        [HarmonyPatch("PlayerAxisControls")]
        [HarmonyPostfix]
        private static void AxisPostfix(PilotPlayerState __instance)
        {
            PlayerAutopilot ap = PlayerAutopilot.Instance;
            if (ap == null) return;
            try
            {
                ap.AfterAxisControls(__instance);
            }
            catch (Exception e)
            {
                ap.Fault(e);
            }
        }

        [HarmonyPatch("PlayerThrottleAxis1Controls")]
        [HarmonyPostfix]
        private static void ThrottlePostfix(PilotPlayerState __instance)
        {
            PlayerAutopilot ap = PlayerAutopilot.Instance;
            if (ap == null) return;
            try
            {
                ap.AfterThrottle(__instance);
            }
            catch (Exception e)
            {
                ap.Fault(e);
            }
        }
    }
}
