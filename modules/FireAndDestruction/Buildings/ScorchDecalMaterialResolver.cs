using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Resolves the vanilla soot material once per scene for every pooled scorch decal this
    /// module creates: impact marks on facades and ground burn scars. Bounded, cached, and
    /// the only module-level scene search for decal materials.
    /// </summary>
    internal static class ScorchDecalMaterialResolver
    {
        private static Material resolved;
        private static bool searchedThisScene;

        // The scan in progress: every material Unity listed, how far scoring has got, the best so far.
        // Scoring is two string allocations per material, thousands of them: one call was a 20+ ms frame.
        private const int MaterialsPerStep = 200;
        private static Material[] scan;
        private static int scanAt, bestScore;
        private static Material best;

        internal static Material Resolve()
        {
            if (resolved != null) return resolved;
            if (searchedThisScene) return null;
            while (!Step(int.MaxValue)) { }   // a caller that needs the material now finishes the scan in this call, as it always did
            return resolved;
        }

        /// <summary>
        /// Advances the one-time scan by at most <paramref name="budget"/> materials (the listing itself is one
        /// indivisible Unity call, taken in a step of its own). True once the scan is over, whether or not it found a material.
        /// </summary>
        internal static bool Step(int budget = MaterialsPerStep)
        {
            if (resolved != null || searchedThisScene) return true;
            if (scan == null)
            {
                scan = Resources.FindObjectsOfTypeAll<Material>();
                scanAt = bestScore = 0;
                best = null;
                return false;
            }

            try
            {
                // Prefer a material actually built on the vanilla scorch-mark decal shader, then
                // fall back to whatever a DecalSpawner carries, scoring names so a crater or
                // shockwave decal never wins over real soot.
                int end = budget >= scan.Length - scanAt ? scan.Length : scanAt + budget;
                for (; scanAt < end; scanAt++)
                    Score(scan[scanAt], ref bestScore, ref best);
                if (scanAt < scan.Length) return false;

                FieldInfo field = AccessTools.Field(typeof(DecalSpawner), "decalMaterial");
                if (field != null)
                {
                    DecalSpawner[] spawners = Resources.FindObjectsOfTypeAll<DecalSpawner>();
                    for (int i = 0; i < spawners.Length; i++)
                        if (spawners[i] != null)
                            Score(field.GetValue(spawners[i]) as Material, ref bestScore, ref best);
                }

                resolved = best;
            }
            catch
            {
                searchedThisScene = true;   // one fault gives up for the scene (once thrown, once null), as the single-call scan did
                scan = null;
                best = null;
                throw;
            }
            searchedThisScene = true;
            scan = null;
            best = null;
            if (Plugin.Settings.Diagnostics.VerboseLogging.Value)
                Plugin.Logger.LogInfo(resolved != null
                    ? $"Scorch decal material resolved: '{resolved.name}' " +
                      $"(shader '{resolved.shader.name}', score {bestScore})."
                    : "Scorch decal material unavailable; keeping prefab default.");
            return true;
        }

        /// <summary>Allow the next scene to retry a resolution that failed early.</summary>
        internal static void ResetForScene()
        {
            scan = null;   // an interrupted scan starts over
            best = null;
            if (resolved == null) searchedThisScene = false;
        }

        private static void Score(Material candidate, ref int bestScore, ref Material best)
        {
            if (candidate == null || candidate.shader == null) return;
            string shader = candidate.shader.name.ToLowerInvariant();
            string name = candidate.name.ToLowerInvariant();
            int score = 0;
            if (shader.Contains("scorchmark")) score += 400;
            if (name.Contains("scorch")) score += 200;
            if (name.Contains("soot") || name.Contains("burn") || name.Contains("char")) score += 120;
            if (name.Contains("crater") || shader.Contains("crater")) score -= 300;
            if (shader.Contains("shockwave") || name.Contains("shockwave")) score -= 300;
            if (score <= bestScore) return;
            bestScore = score;
            best = candidate;
        }
    }
}
