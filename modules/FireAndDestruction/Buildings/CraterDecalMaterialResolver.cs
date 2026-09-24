using UnityEngine;

namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Resolves the vanilla crater material once per scene for breach decals and ruin
    /// ground scars. Separate from the soot resolver because that one scores crater
    /// negatively; falls back to the scorch material when no crater shader exists.
    /// </summary>
    internal static class CraterDecalMaterialResolver
    {
        private static Material resolved;
        private static bool searchedThisScene;

        internal static Material Resolve()
        {
            if (resolved != null) return resolved;
            if (searchedThisScene) return ScorchDecalMaterialResolver.Resolve();
            searchedThisScene = true;

            Material best = null;
            Material[] materials = Resources.FindObjectsOfTypeAll<Material>();
            for (int i = 0; i < materials.Length; i++)
            {
                Material candidate = materials[i];
                if (candidate == null || candidate.shader == null) continue;
                string shader = candidate.shader.name.ToLowerInvariant();
                if (!shader.Contains("crater")) continue;
                string name = candidate.name.ToLowerInvariant();
                if (name.Contains("shockwave")) continue;
                best = candidate;
                break;
            }

            resolved = best;
            if (Plugin.Settings.Diagnostics.VerboseLogging.Value)
                Plugin.Logger.LogInfo(resolved != null
                    ? $"Crater decal material resolved: '{resolved.name}' " +
                      $"(shader '{resolved.shader.name}')."
                    : "Crater decal material unavailable; breach marks fall back to scorch.");
            return resolved ?? ScorchDecalMaterialResolver.Resolve();
        }

        /// <summary>Allow the next scene to retry a resolution that failed early.</summary>
        internal static void ResetForScene()
        {
            if (resolved == null) searchedThisScene = false;
        }
    }
}
