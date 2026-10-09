using BoscaliSummer.Modules.Vanguard.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Modules.Vanguard.Patches
{
    // HAWC-X GLOW PHASE: every enemy sighting (radar return or visual line of sight, via DetectionRequest.ProcessResult)
    // lands in TargetDetector.DetectTarget, which feeds the faction track, Radar.detectedTargets (FireControl) and
    // Turret.onDetectTarget. A glowing glider is skipped there; with no fresh sighting its track goes stale within 4 s
    // (FactionHQ.IsTargetPositionAccurate) and AI target selection drops it. Glowing is filled by the server flight,
    // so this filters the detectors the server runs (all AI); a remote client's own radar is not filtered.
    [HarmonyPatch(typeof(TargetDetector), nameof(TargetDetector.DetectTarget))]
    internal static class HawcxGlowPatch
    {
        private static bool Prefix(Unit target) => HawcxState.Glowing.Count == 0 || !HawcxState.Glowing.Contains(target);
    }
}
