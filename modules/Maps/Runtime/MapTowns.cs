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
    /// The downtown of a mod map's larger towns is built from the game's own map buildings, which are whole city blocks
    /// (suburb, residential and commercial blocks, mid-rises): they are copied onto the "block" lots of the map's
    /// scenery list under one <see cref="MapBuildingSet"/>, so they take damage, collapse and stay in step between host
    /// and clients like the buildings of a shipped map. Nothing of the game's art is in the mod: the shapes are looked
    /// up in the running game. When the game has none, the scenery kit's plain hall stands on the lot instead.
    /// </summary>
    internal static class MapTowns
    {
        private sealed class Shape { public GameObject Template; public float Footprint, Height; }

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
            var shapes = new Dictionary<string, Shape>();
            foreach (MapBuilding building in donors.SelectMany(d => d.GetComponentsInChildren<MapBuilding>(true)))
            {
                string name = building.name.Split('(')[0].Trim();
                if (shapes.ContainsKey(name)) continue;
                Renderer[] parts = building.GetComponentsInChildren<Renderer>(true);
                if (parts.Length == 0) continue;
                Bounds bounds = parts[0].bounds;
                foreach (Renderer part in parts) bounds.Encapsulate(part.bounds);
                shapes[name] = new Shape { Template = building.gameObject, Footprint = Mathf.Max(bounds.size.x, bounds.size.z), Height = bounds.size.y };
            }
            MapBuildingSet donor = donors.Where(d => d.GetComponent<NetworkIdentity>() != null).OrderBy(d => d.transform.childCount).FirstOrDefault();
            if (donor == null || shapes.Count == 0) return null;

            // A lot is about 110 m square. Low blocks are suburbs and housing; the taller ones are a town's centre.
            foreach (KeyValuePair<string, Shape> shape in shapes.OrderBy(s => s.Key, StringComparer.Ordinal))
                if (shape.Value.Footprint >= 45f && shape.Value.Footprint <= 100f && shape.Value.Height <= 70f && shape.Key.IndexOf("unique", StringComparison.OrdinalIgnoreCase) < 0)
                    (shape.Value.Height <= 25f ? low : mid).Add(shape.Value);
            Plugin.Logger?.LogInfo("[Maps] City blocks from the game: " + low.Count + " low, " + mid.Count + " taller, of " + shapes.Count + " map buildings.");
            if (low.Count + mid.Count == 0) return null;

            GameObject copy = Object.Instantiate(donor.gameObject, root);
            copy.name = "Towns";
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            for (int i = copy.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(copy.transform.GetChild(i).gameObject);
            AccessTools.Field(typeof(MapBuildingSet), "powerlines")?.SetValue(copy.GetComponent<MapBuildingSet>(), false); // lots are not in line order
            set = copy.transform;
            return copy.GetComponent<NetworkIdentity>();
        }

        /// <summary>Puts one of the game's buildings on a lot; false when this kind is not a town building or the game
        /// has no shape of its size, and the scenery kit should place its own.</summary>
        public static bool Place(string kind, Vector3 position, float yaw, int pick)
        {
            if (set == null || kind != "block") return false;
            int roll = Math.Abs(pick * 7919 + Mathf.RoundToInt(position.x));
            List<Shape> shapes = mid.Count > 0 && (low.Count == 0 || roll % 5 < 2) ? mid : low;
            Shape shape = shapes[roll / 5 % shapes.Count];
            GameObject building = Object.Instantiate(shape.Template, set);
            building.transform.localPosition = position;
            building.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            building.SetActive(true);
            return true;
        }

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
