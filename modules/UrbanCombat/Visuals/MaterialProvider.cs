using System;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Safely resolves authentic vanilla materials from game prefabs (pillbox concrete,
    /// gabion sandbags, and slingload hook cable) to guarantee 100% native visuals.
    /// </summary>
    internal static class MaterialProvider
    {
        private static Material cachedConcrete;
        private static Material cachedSandbag;
        private static Material cachedCargoHookRope;

        public static Material GetCargoHookRopeMaterial()
        {
            if (cachedCargoHookRope != null) return cachedCargoHookRope;

            SlingloadHook[] allHooks = Resources.FindObjectsOfTypeAll<SlingloadHook>();
            for (int i = 0; i < allHooks.Length; i++)
            {
                if (allHooks[i] != null)
                {
                    LineRenderer lr = allHooks[i].GetComponentInChildren<LineRenderer>();
                    if (lr != null && lr.sharedMaterial != null)
                        return cachedCargoHookRope = lr.sharedMaterial;
                }
            }

            Material[] allMats = Resources.FindObjectsOfTypeAll<Material>();
            for (int i = 0; i < allMats.Length; i++)
            {
                if (allMats[i] != null && (allMats[i].name.IndexOf("sling", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                           allMats[i].name.IndexOf("cable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                           allMats[i].name.IndexOf("hook", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return cachedCargoHookRope = allMats[i];
                }
            }

            return GetConcreteMaterial();
        }

        public static Material GetConcreteMaterial()
        {
            if (cachedConcrete != null) return cachedConcrete;

            BuildingDefinition pillbox = ResolveDef("pillbox");
            if (pillbox?.unitPrefab != null)
            {
                Renderer r = pillbox.unitPrefab.GetComponentInChildren<Renderer>();
                if (r != null && r.sharedMaterial != null)
                    return cachedConcrete = r.sharedMaterial;
            }

            BuildingDefinition bunker = ResolveDef("gabionBunker1");
            if (bunker?.unitPrefab != null)
            {
                Renderer r = bunker.unitPrefab.GetComponentInChildren<Renderer>();
                if (r != null && r.sharedMaterial != null)
                    return cachedConcrete = r.sharedMaterial;
            }

            return null;
        }

        public static Material GetSandbagMaterial()
        {
            if (cachedSandbag != null) return cachedSandbag;

            BuildingDefinition bunker = ResolveDef("gabionBunker1");
            if (bunker?.unitPrefab != null)
            {
                Renderer[] renderers = bunker.unitPrefab.GetComponentsInChildren<Renderer>();
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i]?.sharedMaterial != null)
                        return cachedSandbag = renderers[i].sharedMaterial;
                }
            }

            return GetConcreteMaterial();
        }

        private static BuildingDefinition ResolveDef(string key)
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
