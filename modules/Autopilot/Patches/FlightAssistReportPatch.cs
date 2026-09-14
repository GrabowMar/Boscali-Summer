using BoscaliSummer.Features.Autopilot.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Features.Autopilot.Patches
{
    /// <summary>The native tiltwing autopilot calls Aircraft.SetFlightAssist every fixed step, and
    /// the game reports every call as a status message. While the landing autopilot drives the
    /// local ownship, that report floods the action display with "Auto Wing Tilt Enabled" lines.
    /// Keep the state change, report only when the value actually changes.</summary>
    [HarmonyPatch(typeof(ControlsFilter), nameof(ControlsFilter.SetFlightAssist))]
    internal static class FlightAssistReportPatch
    {
        private static bool Prefix(Aircraft aircraft, bool enabled, ref Aircraft ___aircraft)
        {
            ___aircraft = aircraft;
            AutopilotLandController controller = AutopilotLandController.Instance;
            return controller == null || !controller.IsEngagedOn(aircraft) ||
                aircraft.flightAssist != enabled;
        }
    }
}
