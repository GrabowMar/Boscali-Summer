using System;
using HarmonyLib;
using NuclearOption.Jobs;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    /// <summary>
    /// Skips line-of-sight checks against this module's own infantry works. They spawn as
    /// neutral scenery (<c>Spawner.SpawnScenery</c> assigns no HQ), and
    /// <c>TargetDetector.VisualCheck</c> requests a raycast for every unit in range outside the
    /// detector's own HQ, so every detector of every faction raycast every piece on each scan.
    /// Only names under <see cref="TrenchWorks.Prefix"/> are skipped; mission and vanilla
    /// scenery keep vanilla detection.
    /// </summary>
    [HarmonyPatch(typeof(DetectorManager), nameof(DetectorManager.RequestLoSCheck))]
    internal static class TrenchWorksDetectionPatch
    {
        private static bool Prefix(Unit target) =>
            !(target is Scenery scenery && scenery.UniqueName != null &&
              scenery.UniqueName.StartsWith(TrenchWorks.Prefix, StringComparison.Ordinal));
    }
}
