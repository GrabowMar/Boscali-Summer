using System;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Modules.Support.Patches
{
    /// <summary>Observe native pilot fields and physical axes, before autopilot postfixes or aircraft filters.</summary>
    [HarmonyPatch(typeof(PilotPlayerState), "PlayerAxisControls")]
    internal static class ActivityInputPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Postfix(PilotPlayerState __instance, Pilot ___pilot, Rewired.Player ___player)
        {
            try
            {
                if (___player == null) return;
                // ControlInputs can already contain AP throttle from its nested throttle postfix or the previous frame.
                // Native pilot stick fields and Rewired's raw throttle/custom axes never contain those commands.
                var controls = new ActivityControls(__instance.pitchInput, __instance.rollInput, __instance.yawInput,
                    ___player.GetAxisRaw("Throttle"), ___player.GetButton("Brake") ? 1f : 0f,
                    ___player.GetAxisRaw("Custom Axis 1"));
                SupportManager.Active?.RecordAircraftInput(___pilot?.aircraft, controls);
            }
            catch (Exception e) { PatchGuard.Report("Support.ActivityInput", e); }
        }
    }
}
