using HarmonyLib;
using BoscaliSummer.Modules.Vanguard.Runtime;

namespace BoscaliSummer.Modules.Vanguard.Patches
{
    /// <summary>
    /// HAWC-X glow phase: radar and visual detection both funnel through TargetDetector.DetectTarget, so skipping it for
    /// a glowing glider keeps it off every track list, turret and fire-control picture until the glow ends. Locks taken
    /// before the glow keep running - this hides it from new detection, it is not a shield.
    /// </summary>
    [HarmonyPatch(typeof(TargetDetector), nameof(TargetDetector.DetectTarget))]
    internal static class HawcxGlowPatch
    {
        private static bool Prefix(Unit target) =>
            VanguardFlight.HawcxGlowing.Count == 0 || !(target is Missile m) || !VanguardFlight.HawcxGlowing.Contains(m);
    }
}
