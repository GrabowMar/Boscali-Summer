using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mirage;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Maps.Runtime
{
    /// <summary>
    /// The towns of a mod map are built from the game's own map buildings, which are whole city blocks: a "suburb" lot
    /// of the map's scenery list gets one of the low blocks of houses, a "block" lot (a town's centre) one of the taller
    /// ones. They are copied under one <see cref="MapBuildingSet"/>, so they take damage, collapse and stay in step
    /// between host and clients like the buildings of a shipped map. Nothing of the game's art is in the mod: the
    /// shapes are looked up in the running game. When the game has none, the scenery kit's own shapes stand on the lots.
    /// </summary>
    internal static class MapTowns
    {
        private sealed class Shape { public GameObject Template; public bool Deep; }

        private static readonly List<Shape> low = new List<Shape>(), mid = new List<Shape>();
        private static Transform set;

        /// <summary>Makes the map's empty building set under <paramref name="root"/>; null when the game has none to copy.</summary>
        public static NetworkIdentity Begin(Transform root, MapSettingsManager manager)
        {
            set = null;
            low.Clear();
            mid.Clear();
            MapBuildingSet[] donors = manager.Maps.Where(m => m != null && m.Prefab != null && !MapGround.IsOurs(m.Prefab))
                .SelectMany(m => m.Prefab.GetComponentsInChildren<MapBuildingSet>(true)).ToArray();
            var seen = new HashSet<string>();
            int all = 0;
            foreach (MapBuilding building in donors.SelectMany(d => d.GetComponentsInChildren<MapBuilding>(true)).OrderBy(b => b.name, StringComparer.Ordinal))
            {
                string name = building.name.Split('(')[0].Trim();
                Renderer[] parts = building.GetComponentsInChildren<Renderer>(true);
                if (!seen.Add(name) || parts.Length == 0) continue;
                all++;
                Bounds bounds = parts[0].bounds;
                foreach (Renderer part in parts) bounds.Encapsulate(part.bounds);
                Vector3 size = building.transform.InverseTransformVector(bounds.size); // the block in its own frame: x across, z deep
                float across = Mathf.Abs(size.x), deep = Mathf.Abs(size.z), footprint = Mathf.Max(across, deep);
                // A lot is about 100 m along its street. Low blocks are suburbs and housing; the taller ones are a town's centre.
                if (footprint < 45f || footprint > 100f || bounds.size.y > 70f || name.IndexOf("unique", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                (bounds.size.y <= 25f ? low : mid).Add(new Shape { Template = building.gameObject, Deep = deep > across });
            }
            MapBuildingSet donor = donors.Where(d => d.GetComponent<NetworkIdentity>() != null).OrderBy(d => d.transform.childCount).FirstOrDefault();
            Plugin.Logger?.LogInfo("[Maps] City blocks from the game: " + low.Count + " low, " + mid.Count + " taller, of " + all + " map buildings.");
            if (donor == null || low.Count + mid.Count == 0) return null;

            GameObject copy = Object.Instantiate(donor.gameObject, root);
            copy.name = "Towns";
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            for (int i = copy.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(copy.transform.GetChild(i).gameObject);
            AccessTools.Field(typeof(MapBuildingSet), "powerlines")?.SetValue(copy.GetComponent<MapBuildingSet>(), false); // lots are not in line order
            set = copy.transform;
            return copy.GetComponent<NetworkIdentity>();
        }

        /// <summary>The scenery kinds that are town lots, filled here and never by the scenery kit.</summary>
        public static bool Handles(string kind) => kind == "block" || kind == "suburb";

        /// <summary>The game's own road surfaces on the map's baked roads: road_2lane on a paved road, asphalt on a town
        /// street, road_dirt on a track. A road keeps the bake's plain colours when the game has no such material.</summary>
        public static int DressRoads(Transform roads, MapSettingsManager manager) =>
            Dress(roads, manager, new Dictionary<string, string> { { "paved", "road_2lane" }, { "street", "asphalt" }, { "dirt", "road_dirt" } }, false);

        /// <summary>The game's own city-block ground on every town lot; a plot the game has no ground for is hidden.</summary>
        public static int DressPlots(Transform plots, MapSettingsManager manager) =>
            Dress(plots, manager, new Dictionary<string, string> { { "plotsuburb", "blocks_suburbs" }, { "plotblock", "blocks_city1" } }, true);

        private static int Dress(Transform roads, MapSettingsManager manager, Dictionary<string, string> looks, bool hideOthers)
        {
            if (roads == null) return 0;
            var found = new Dictionary<string, Material>();
            foreach (Renderer part in manager.Maps.Where(m => m != null && m.Prefab != null && !MapGround.IsOurs(m.Prefab)).SelectMany(m => m.Prefab.GetComponentsInChildren<MeshRenderer>(true)))
                if (part.sharedMaterial != null && looks.ContainsValue(part.sharedMaterial.name) && !found.ContainsKey(part.sharedMaterial.name))
                    found[part.sharedMaterial.name] = part.sharedMaterial;
            int dressed = 0;
            foreach (MeshRenderer road in roads.GetComponentsInChildren<MeshRenderer>(true))
                if (looks.TryGetValue(road.name.Split('_')[0], out string wanted) && found.TryGetValue(wanted, out Material material))
                {
                    road.sharedMaterial = material;
                    dressed++;
                }
                else if (hideOthers) road.enabled = false;
            return dressed;
        }

        /// <summary>Puts one of the game's blocks on a lot, its long side along the street; false when this kind is not a
        /// town lot or the game has no block for it, and the scenery kit should place its own.</summary>
        public static bool Place(string kind, Vector3 position, float yaw, int pick)
        {
            if (!Handles(kind)) return false;
            List<Shape> shapes = kind == "suburb" ? low : mid.Count > 0 ? mid : low;
            if (set == null || shapes.Count == 0) return true; // the lot stays empty: the mod has no town buildings of its own
            int index = Math.Abs(pick * 7919 + Mathf.RoundToInt(position.x)) % shapes.Count;
            if (shapes.Count > 1 && index == lastPick && (position - lastAt).sqrMagnitude < 160f * 160f)
                index = (index + 1 + Math.Abs(pick) % (shapes.Count - 1)) % shapes.Count;   // never the same block beside itself
            lastPick = index;
            lastAt = position;
            Shape shape = shapes[index];
            GameObject building = Object.Instantiate(shape.Template, set);
            building.transform.localPosition = position;
            building.transform.localRotation = Quaternion.Euler(0f, yaw + (shape.Deep ? 90f : 0f), 0f);
            building.SetActive(true);
            return true;
        }

        private static int lastPick = -1;
        private static Vector3 lastAt;

        /// <summary>How many buildings the set holds; forgets the templates.</summary>
        public static int End()
        {
            int count = set != null ? set.childCount : 0;
            set = null;
            low.Clear();
            mid.Clear();
            return count;
        }
    }
}
