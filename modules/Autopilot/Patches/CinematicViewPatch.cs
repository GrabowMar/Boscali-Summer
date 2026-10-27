using System;
using BepInEx.Configuration;
using BoscaliSummer.Core.Diagnostics;
using HarmonyLib;

namespace BoscaliSummer.Modules.Autopilot.Patches
{
    /// <summary>
    /// With <c>CinematicCamera</c> off, Switch View skips the game's cinematic (TV) camera: the orbit camera's hop to it goes where the TV camera's own
    /// Switch View would (cockpit, or stays in orbit when the aircraft has no cockpit view). Only that hop is caught; anything else entering the TV camera is untouched.
    /// </summary>
    [HarmonyPatch(typeof(CameraStateManager), nameof(CameraStateManager.SwitchState))]
    internal static class CinematicViewPatch
    {
        internal static ConfigEntry<bool> Setting;

        private static bool Prefix(CameraStateManager __instance, ref CameraBaseState state)
        {
            try
            {
                if (Setting == null || Setting.Value || state != __instance.TVState || __instance.currentState != __instance.orbitState) return true;
                if (__instance.followingUnit == null || __instance.followingUnit.cockpitViewPoint == null) return false;
                state = __instance.cockpitState;
                return true;
            }
            catch (Exception e)
            {
                PatchGuard.Report("Autopilot.CinematicView", e);
                return true;
            }
        }
    }
}
