using BoscaliSummer.Modules.Vanguard.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Modules.Vanguard.Patches
{
    // MALD-J SHADOW: every radar detection (DetectionRequest.ProcessResult -> Radar.CanSeeRadarReturn -> DetectTarget,
    // which feeds the faction track and SAM turret/FireControl target lists) asks here first. Runs on whichever
    // peer runs that radar's search: the server for AI/ground radars, the owning client for a player's radar.
    [HarmonyPatch(typeof(Radar), nameof(Radar.CanSeeRadarReturn))]
    internal static class MaldjShadowPatch
    {
        // dist is the original's own parameter name (Radar.CanSeeRadarReturn(IRadarReturn, float dist, float clutterFactor)).
        private static bool Prefix(Radar __instance, IRadarReturn radarReturn, float dist, ref bool __result)
        {
            if (MaldjField.Jammers.Count == 0 || !(radarReturn is Aircraft aircraft) || !MaldjField.Hides(__instance, aircraft, dist)) return true;
            __result = false;
            return false;
        }
    }
}
