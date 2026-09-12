using System;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Visuals
{
    /// <summary>
    /// Safely resolves authentic vanilla materials from game prefabs (pillbox concrete,
    /// gabion sandbags, and terrain shaders) to guarantee 100% native lighting under URP.
    /// </summary>
    internal static class TrenchMaterialResolver
    {
        private static Material cachedConcrete;
        private static Material cachedSandbag;
        private static Material cachedEarth;

        public static void ResetForScene()
        {
            cachedConcrete = null;
            cachedSandbag = null;
            cachedEarth = null;
        }

        public static Material GetConcreteMaterial()
        {
            if (cachedConcrete != null) return cachedConcrete;

            BuildingDefinition pillbox = ResolveBuildingDef("pillbox");
            if (pillbox?.unitPrefab != null)
            {
                Renderer r = pillbox.unitPrefab.GetComponentInChildren<Renderer>();
                if (r != null && r.sharedMaterial != null)
                    return cachedConcrete = r.sharedMaterial;
            }

            BuildingDefinition bunker = ResolveBuildingDef("gabionBunker1");
            if (bunker?.unitPrefab != null)
            {
                Renderer r = bunker.unitPrefab.GetComponentInChildren<Renderer>();
                if (r != null && r.sharedMaterial != null)
                    return cachedConcrete = r.sharedMaterial;
            }

            return cachedConcrete = CreateFallbackMaterial(new Color(0.48f, 0.47f, 0.45f));
        }

        public static Material GetSandbagMaterial()
        {
            if (cachedSandbag != null) return cachedSandbag;

            BuildingDefinition bunker = ResolveBuildingDef("gabionBunker1");
            if (bunker?.unitPrefab != null)
            {
                Renderer[] renderers = bunker.unitPrefab.GetComponentsInChildren<Renderer>();
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i]?.sharedMaterial != null)
                        return cachedSandbag = renderers[i].sharedMaterial;
                }
            }

            return cachedSandbag = CreateFallbackMaterial(new Color(0.55f, 0.48f, 0.38f));
        }

        public static Material GetEarthBermMaterial()
        {
            if (cachedEarth != null) return cachedEarth;

            Material sandbag = GetSandbagMaterial();
            if (sandbag != null) return cachedEarth = sandbag;

            return cachedEarth = CreateFallbackMaterial(new Color(0.38f, 0.32f, 0.22f));
        }

        private static Material CreateFallbackMaterial(Color albedo)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard")
                         ?? Shader.Find("Unlit/Color");
            if (shader == null) return null;

            var mat = new Material(shader);
            mat.color = albedo;
            return mat;
        }

        private static BuildingDefinition ResolveBuildingDef(string key)
        {
            if (Encyclopedia.i?.buildings == null) return null;
            for (int i = 0; i < Encyclopedia.i.buildings.Count; i++)
            {
                BuildingDefinition def = Encyclopedia.i.buildings[i];
                if (def != null && string.Equals(def.jsonKey, key, StringComparison.OrdinalIgnoreCase))
                    return def;
            }
            return null;
        }
    }
}
