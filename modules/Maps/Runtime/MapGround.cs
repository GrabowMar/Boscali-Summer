using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using NuclearOption.Effects;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Maps.Runtime
{
    /// <summary>
    /// Makes a mod map's ground look like the game's own: the vanilla terrain material (its detail textures, normals
    /// and blending) fed with this map's whole-map colour and ground-cover weights, and the vanilla grass, tree and
    /// blast-scar systems copied from a shipped map and pointed at this one. The method is NOCustomMaps' (MIT,
    /// (c) 2026 BatongMaster). Everything is taken from the running game, so the map follows the game's look as it changes.
    /// </summary>
    internal static class MapGround
    {
        private static readonly string[] Covers = { "grass", "rock", "lush" };

        /// <summary>When the template is built: swap the bake's fallback material for a copy of the game's terrain material.</summary>
        public static bool Dress(Transform terrain, MapEntry entry, MapSettingsManager manager)
        {
            Material donor = DonorMaterial(manager);
            var colour = entry.Bundle.LoadAsset<Texture2D>("macro");
            if (donor == null || colour == null)
            {
                Plugin.Logger?.LogWarning("[Maps] " + entry.Manifest.displayName + " keeps its plain ground: " +
                    (donor == null ? "no vanilla terrain material was found." : "the bundle has no whole-map colour."));
                return false;
            }
            var material = new Material(donor) { name = "BoscaliMapTerrain", hideFlags = HideFlags.DontUnloadUnusedAsset };
            material.SetTexture("_macro_basecolor", colour);
            foreach (string cover in Covers)
            {
                var weight = entry.Bundle.LoadAsset<Texture2D>("splat_" + cover);
                if (weight != null) material.SetTexture("_splat_" + cover, weight);
            }
            material.SetTexture("_splat_fields", Texture2D.blackTexture); // no ploughed fields on these islands
            foreach (MeshRenderer renderer in terrain.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.sharedMaterial = material;
                renderer.renderingLayerMask |= 3; // the layers scorch marks and craters are drawn on
            }
            Plugin.Logger?.LogInfo("[Maps] Ground uses the game's " + donor.shader.name + " (" + string.Join(", ", material.GetTexturePropertyNames()) + ").");
            return true;
        }

        private static Material DonorMaterial(MapSettingsManager manager)
        {
            PhysicMaterial ground = GameAssets.i != null ? GameAssets.i.terrainMaterial : null;
            foreach (MapSettingsManager.Map map in manager.Maps)
            {
                if (map == null || map.Prefab == null) continue;
                foreach (MeshCollider collider in map.Prefab.GetComponentsInChildren<MeshCollider>(true))
                {
                    if (collider.sharedMaterial != ground || !collider.TryGetComponent(out MeshRenderer renderer) || renderer.sharedMaterial == null) continue;
                    return renderer.sharedMaterial;
                }
            }
            return null;
        }

        /// <summary>
        /// When the map has been put in the scene: copy the shipped map's grass, tree, height-bake, blast and shader-global
        /// objects onto it. They are copied inactive and rewired before they wake, because they are scene singletons that
        /// register against whatever map they point at when they start.
        /// </summary>
        public static void Attach(MapSettings instance)
        {
            MapEntry entry = MapCatalog.All.FirstOrDefault(e => instance != null && instance.name.StartsWith(e.Manifest.prefabName, StringComparison.Ordinal));
            if (entry == null || instance.GetComponentInChildren<TerrainHeightMap>(true) != null) return;
            MapSettingsManager manager = SceneSingleton<MapSettingsManager>.i;
            MapSettings donor = manager == null ? null : manager.Maps.Where(m => m != null && m.Prefab != null && m.Prefab != entry.Template)
                .Select(m => m.Prefab).FirstOrDefault(p => p.GetComponentInChildren<DetailRenderer>(true) != null);
            if (donor == null)
            {
                Plugin.Logger?.LogWarning("[Maps] No shipped map to copy grass and trees from; " + entry.Manifest.displayName + " stays bare.");
                return;
            }
            var hosts = new List<GameObject>();
            foreach (Component part in donor.GetComponentsInChildren<TerrainHeightMap>(true).Cast<Component>()
                .Concat(donor.GetComponentsInChildren<DetailRenderer>(true)).Concat(donor.GetComponentsInChildren<BlastManager>(true))
                .Concat(donor.GetComponentsInChildren<ShaderGlobalManager>(true)))
                if (!hosts.Contains(part.gameObject)) hosts.Add(part.gameObject);
            hosts.RemoveAll(h => hosts.Any(other => other != h && h.transform.IsChildOf(other.transform))); // a host inside another is copied with it

            var holder = new GameObject("BoscaliMapSystems");
            holder.SetActive(false);
            holder.transform.SetParent(instance.transform, false);
            List<GameObject> copies = hosts.Select(h => Object.Instantiate(h, holder.transform)).ToList();
            for (int i = 0; i < copies.Count; i++) copies[i].name = hosts[i].name;

            var heights = holder.GetComponentInChildren<TerrainHeightMap>(true);
            var detail = holder.GetComponentInChildren<DetailRenderer>(true);
            var blast = holder.GetComponentInChildren<BlastManager>(true);
            if (heights == null || detail == null || blast == null)
            {
                Object.Destroy(holder);
                Plugin.Logger?.LogWarning("[Maps] The shipped map's grass and tree objects were not all found; " + entry.Manifest.displayName + " stays bare.");
                return;
            }
            Set(heights, "mapSettings", instance);
            heights.AutoSearchRoot = instance.transform.Find("Terrain");
            heights.AutoFindTerrain = true;
            heights.AutoFindBlockers = false;
            heights.detailRenderer = detail;
            Set(blast, "mapSettings", instance);
            foreach (ShaderGlobalManager globals in holder.GetComponentsInChildren<ShaderGlobalManager>(true)) Set(globals, "mapSettings", instance);
            foreach (TerrainScatter scatter in holder.GetComponentsInChildren<TerrainScatter>(true)) Set(scatter, "mapSettings", instance);
            Set(detail, "blastManager", blast);
            Set(detail, "terrainHeight", heights);

            var mask = entry.Bundle.LoadAsset<Texture2D>("grass_mask");
            foreach (GrassRenderer grass in holder.GetComponentsInChildren<GrassRenderer>(true))
            {
                Set(grass, "detailRenderer", detail);
                Set(grass, "terrainHeightMap", heights);
                if (mask != null && Get(grass, "lushMaps") is Texture2D[] maps) Set(grass, "lushMaps", maps.Select(_ => mask).ToArray());
            }
            // Each tree species the shipped map draws gets a share of this map's forest; a species with no trees is
            // removed, because a tree renderer without positions throws every frame and takes the grass down with it.
            var kept = new List<TreeRenderer>();
            int planted = 0;
            TreeRenderer[] species = Get(detail, "treeRenderers") as TreeRenderer[] ?? new TreeRenderer[0];
            for (int i = 0; i < species.Length; i++)
            {
                TextAsset positions = species[i] != null ? entry.Bundle.LoadAsset<TextAsset>("forest_" + kept.Count) : null;
                int bytes = positions != null ? positions.bytes.Length : 0;
                if (bytes >= 12 && bytes % 12 == 0)
                {
                    species[i].PositionData = positions;
                    kept.Add(species[i]);
                    planted += bytes / 12;
                }
                else if (species[i] != null) Object.Destroy(species[i]);
            }
            Set(detail, "treeRenderers", kept.ToArray());

            foreach (GameObject copy in copies) copy.transform.SetParent(instance.transform, false);
            Object.Destroy(holder);
            Plugin.Logger?.LogInfo("[Maps] " + entry.Manifest.displayName + " has the game's grass and " + planted + " forest trees of " + kept.Count + " species.");
        }

        private static object Get(object target, string field) => AccessTools.Field(target.GetType(), field)?.GetValue(target);

        private static void Set(object target, string field, object value)
        {
            var info = AccessTools.Field(target.GetType(), field);
            if (info == null) throw new MissingFieldException(target.GetType().Name, field);
            info.SetValue(target, value);
        }
    }
}
