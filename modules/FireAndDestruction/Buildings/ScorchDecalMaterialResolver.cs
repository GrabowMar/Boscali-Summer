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

        internal static Material Resolve()
        {
            if (resolved != null) return resolved;
            if (searchedThisScene) return null;
            searchedThisScene = true;

            // Prefer a material actually built on the vanilla scorch-mark decal shader, then
            // fall back to whatever a DecalSpawner carries, scoring names so a crater or
            // shockwave decal never wins over real soot.
            int bestScore = 0;
            Material best = null;
            Material[] materials = Resources.FindObjectsOfTypeAll<Material>();
            for (int i = 0; i < materials.Length; i++)
                Score(materials[i], ref bestScore, ref best);

            FieldInfo field = AccessTools.Field(typeof(DecalSpawner), "decalMaterial");
            if (field != null)
            {
                DecalSpawner[] spawners = Resources.FindObjectsOfTypeAll<DecalSpawner>();
                for (int i = 0; i < spawners.Length; i++)
                    if (spawners[i] != null)
                        Score(field.GetValue(spawners[i]) as Material, ref bestScore, ref best);
            }

            resolved = best;
            if (Plugin.Settings.Diagnostics.VerboseLogging.Value)
                Plugin.Logger.LogInfo(resolved != null
                    ? $"Scorch decal material resolved: '{resolved.name}' " +
                      $"(shader '{resolved.shader.name}', score {bestScore})."
                    : "Scorch decal material unavailable; keeping prefab default.");
            return resolved;
        }

        /// <summary>Allow the next scene to retry a resolution that failed early.</summary>
        internal static void ResetForScene()
        {
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
