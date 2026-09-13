using BoscaliSummer.Features.Autopilot.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Features.Autopilot.Patches
{
    /// <summary>Takes over the local player's fixed-step input pass while the landing autopilot
    /// is engaged, so the native autopilot writes the same inputs the player would.</summary>
    [HarmonyPatch(typeof(PilotPlayerState), "FixedUpdateState")]
    internal static class AutopilotLandInputPatch
    {
        private static bool Prefix(Pilot pilot) =>
            AutopilotLandController.Instance?.TakeOver(pilot) != true;
    }
}
