using BoscaliSummer.Features.QoL.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Features.QoL.Patches
{
    [HarmonyPatch(typeof(ControlsFilter), nameof(ControlsFilter.GetAim))]
    internal static class GunAimSolutionPatch
    {
        private static void Postfix(Aircraft ___aircraft, Unit target, GlobalPosition? aimPoint)
        {
            // Ignore other aircraft's HUD/AI requests rather than invalidating ownship's sample.
            if (___aircraft != null && GameManager.IsLocalAircraft(___aircraft))
                GunAimAssist.Instance?.Capture(___aircraft, target, aimPoint);
        }
    }

    [HarmonyPatch(typeof(PilotPlayerState), "PlayerAxisControls")]
    internal static class GunAimInputPatch
    {
        private static void Postfix(Pilot ___pilot, float ___pilotStrength)
        {
            if (___pilot != null) GunAimAssist.Instance?.Apply(___pilot.aircraft, ___pilotStrength);
        }
    }
}
