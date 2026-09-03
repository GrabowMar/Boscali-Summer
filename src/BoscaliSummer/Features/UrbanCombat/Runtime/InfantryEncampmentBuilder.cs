using System;
using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Deploys authentic, visible vanilla infantry combat encampments on the ground
    /// to reinforce controlled zones. Unlike civilian rooftop proxies, these are real,
    /// visible vanilla field fortifications (sandbag bunkers, heavy machine guns,
    /// ATGMs, MANPADS/AAA) that defend the airbase perimeter.
    /// </summary>
    internal static class InfantryEncampmentBuilder
    {
        internal const string NamePrefix = "BoscaliSummer:Encampment:";

        private static readonly Dictionary<string, BuildingDefinition> CachedDefs =
            new Dictionary<string, BuildingDefinition>(StringComparer.OrdinalIgnoreCase);

        private static bool catalogInitialized;

        public static void ResetForScene()
        {
            CachedDefs.Clear();
            catalogInitialized = false;
        }

        private static void EnsureCatalog()
        {
            if (catalogInitialized && CachedDefs.Count > 0) return;
            catalogInitialized = true;
            CachedDefs.Clear();

            if (Encyclopedia.i == null || Encyclopedia.i.buildings == null) return;

            for (int i = 0; i < Encyclopedia.i.buildings.Count; i++)
            {
                BuildingDefinition def = Encyclopedia.i.buildings[i];
                if (def == null || def.unitPrefab == null) continue;
                if (def.buildingType != BuildingType.DEF) continue;

                string key = def.jsonKey ?? string.Empty;
                if (!string.IsNullOrEmpty(key) && !CachedDefs.ContainsKey(key))
                {
                    CachedDefs[key] = def;
                }
            }
        }

        private static BuildingDefinition Resolve(string preferredKey, string fallbackKeyword)
        {
            EnsureCatalog();

            if (!string.IsNullOrEmpty(preferredKey) && CachedDefs.TryGetValue(preferredKey, out BuildingDefinition def))
                return def;

            foreach (var kvp in CachedDefs)
            {
                if (kvp.Key.IndexOf(fallbackKeyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (kvp.Value.unitName != null && kvp.Value.unitName.IndexOf(fallbackKeyword, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return kvp.Value;
                }
            }

            // Return any available DEF definition if keyword match fails
            using (var enumerator = CachedDefs.Values.GetEnumerator())
            {
                if (enumerator.MoveNext()) return enumerator.Current;
            }
            return null;
        }

        public static List<Building> DeployEncampment(
            Airbase airbase, FactionHQ owner, Vector3 targetCenter, int encampmentIndex)
        {
            var spawnedBuildings = new List<Building>();
            if (airbase == null || owner == null || NetworkSceneSingleton<Spawner>.i == null)
                return spawnedBuildings;

            EnsureCatalog();
            if (CachedDefs.Count == 0) return spawnedBuildings;

            // Resolve vanilla defense assets
            BuildingDefinition bunkerDef = Resolve("gabionBunker1", "bunker");
            BuildingDefinition mgDef = Resolve("Emplacement1_MG", "MG");
            BuildingDefinition atgmDef = Resolve("Emplacement1_ATGM", "ATGM");
            BuildingDefinition aaDef = Resolve("Emplacement1_MANPADS", "MANPADS") ?? Resolve("Emplacement1_23mm", "23mm");
            BuildingDefinition pillboxDef = Resolve("pillbox", "pillbox");

            // Plan perimeter positions around targetCenter
            // Radius ~18-24m gives a realistic infantry defensive compound
            float radius = 20f;
            var layout = new (BuildingDefinition Def, float AngleDeg, float Distance)[]
            {
                (mgDef ?? bunkerDef, 0f, radius),
                (bunkerDef, 72f, radius * 1.1f),
                (atgmDef ?? bunkerDef, 144f, radius),
                (bunkerDef, 216f, radius * 1.1f),
                (aaDef ?? pillboxDef ?? bunkerDef, 288f, radius)
            };

            string airbaseName = !string.IsNullOrEmpty(airbase.NetworknetworkUniqueName)
                ? airbase.NetworknetworkUniqueName
                : airbase.name;
            string cleanName = (airbaseName ?? "Airbase").Replace(':', '_').Replace(' ', '_');

            Spawner spawner = NetworkSceneSingleton<Spawner>.i;

            for (int i = 0; i < layout.Length; i++)
            {
                BuildingDefinition def = layout[i].Def;
                if (def == null || def.unitPrefab == null) continue;

                float rad = layout[i].AngleDeg * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * layout[i].Distance;
                Vector3 candidatePoint = targetCenter + offset;

                // Raycast to find exact ground surface and normal
                Vector3 rayStart = candidatePoint + Vector3.up * 250f;
                if (!Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 500f,
                    (int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.ShipsMask))
                {
                    continue;
                }

                // Check slope - encampment should be placed on reasonable terrain (< 35 degrees)
                if (Vector3.Angle(hit.normal, Vector3.up) > 35f) continue;
                if (hit.point.y <= Datum.LocalSeaY + 1f) continue;

                Vector3 spawnPos = hit.point;

                // Face outward from encampment center
                Vector3 outwardDir = Vector3.ProjectOnPlane(spawnPos - targetCenter, hit.normal);
                if (outwardDir.sqrMagnitude < 0.01f) outwardDir = Vector3.forward;
                Quaternion rotation = Quaternion.LookRotation(outwardDir.normalized, hit.normal);

                string uniqueName = $"{NamePrefix}{cleanName}:{encampmentIndex}:{i}:{def.jsonKey}";

                Building building = spawner.SpawnBuilding(
                    def.unitPrefab,
                    spawnPos.ToGlobalPosition(),
                    rotation,
                    owner,
                    airbase,
                    uniqueName,
                    false,
                    null);

                if (building != null)
                {
                    // Ensure renderers remain enabled (unlike the old logic proxies)
                    Renderer[] renderers = building.GetComponentsInChildren<Renderer>(true);
                    for (int r = 0; r < renderers.Length; r++)
                    {
                        if (renderers[r] != null) renderers[r].enabled = true;
                    }
                    spawnedBuildings.Add(building);
                }
            }

            return spawnedBuildings;
        }
    }
}
