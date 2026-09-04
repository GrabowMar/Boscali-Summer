using System;
using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Places or reinforces a vanilla emplacement outpost; extra troops raise its tier.
    /// </summary>
    internal static class InfantryEncampmentBuilder
    {
        internal const string NamePrefix = "BoscaliSummer:Encampment:";
        private const int MaximumSites = 12;

        public sealed class EncampmentSite
        {
            public Vector3 Center;
            public FactionHQ Owner;
            public Airbase Airbase;
            public int Tier;
            public int Troops;
            public int Id;
            public readonly List<Building> Emplacements = new List<Building>();
        }

        private static readonly List<EncampmentSite> ActiveSites = new List<EncampmentSite>();
        private static readonly Dictionary<string, BuildingDefinition> CachedDefs =
            new Dictionary<string, BuildingDefinition>(StringComparer.OrdinalIgnoreCase);
        private static bool catalogInitialized;

        public static void ResetForScene()
        {
            ActiveSites.Clear();
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

            using (var enumerator = CachedDefs.Values.GetEnumerator())
            {
                if (enumerator.MoveNext()) return enumerator.Current;
            }
            return null;
        }

        public static EncampmentSite FindNearbySite(Vector3 pos, float maxDistance = 150f)
        {
            for (int i = 0; i < ActiveSites.Count; i++)
            {
                EncampmentSite site = ActiveSites[i];
                if (site != null && Vector3.Distance(site.Center, pos) <= maxDistance)
                    return site;
            }
            return null;
        }

        public static List<Building> DeployEncampment(Airbase airbase, FactionHQ owner, Vector3 targetCenter, int encampmentIndex)
        {
            DeployOrReinforce(targetCenter, owner, airbase, TroopDeploymentMath.DefaultSquadSize);
            EncampmentSite site = FindNearbySite(targetCenter, 150f);
            return site != null ? site.Emplacements : new List<Building>();
        }

        public static bool DeployOrReinforce(Vector3 dropPos, FactionHQ owner, Airbase airbase, int troopCount)
        {
            EncampmentSite existing = FindNearbySite(dropPos, 150f);
            if (existing != null)
            {
                ReinforceSite(existing, troopCount);
                return true;
            }
            if (ActiveSites.Count >= MaximumSites) return false;
            return CreateNewSite(dropPos, owner, airbase, troopCount);
        }

        private static bool CreateNewSite(Vector3 center, FactionHQ owner, Airbase airbase, int troopCount)
        {
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null) return false;

            var site = new EncampmentSite
            {
                Center = center,
                Owner = owner,
                Airbase = airbase,
                Troops = Math.Max(1, troopCount),
                Tier = 1,
                Id = ActiveSites.Count
            };
            site.Tier = TroopDeploymentMath.ComputeTier(site.Troops);

            SpawnEmplacements(site, spawner);
            if (site.Emplacements.Count == 0) return false;

            ActiveSites.Add(site);
            Plugin.Logger.LogInfo($"[ENCAMPMENT] Established Tier {site.Tier} defensive outpost with {site.Troops} visual infantry at ({center.x:0}, {center.z:0}).");
            return true;
        }

        private static void ReinforceSite(EncampmentSite site, int troopCount)
        {
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null || site == null) return;

            site.Troops += Math.Max(1, troopCount);
            site.Tier = TroopDeploymentMath.ComputeTier(site.Troops);

            SpawnEmplacements(site, spawner);
            if (site.Tier >= 4)
                ReplenishFirebase(site);

            Plugin.Logger.LogInfo($"[ENCAMPMENT] Reinforced outpost to Tier {site.Tier} with {site.Troops} total infantry committed.");
        }

        private static void SpawnEmplacements(EncampmentSite site, Spawner spawner)
        {
            if (site.Tier >= 1 && !HasEmplacement(site, "MG"))
                SpawnEmplacement(site, spawner, "Emplacement1_MG", "MG", site.Center, Quaternion.identity, "MG");

            if (site.Tier >= 2 && !HasEmplacement(site, "ATGM"))
            {
                Vector3 atgmPos = SnapToGround(site.Center + Vector3.forward * 10f + Vector3.right * 6f);
                SpawnEmplacement(site, spawner, "Emplacement1_ATGM", "ATGM", atgmPos, Quaternion.LookRotation(Vector3.forward), "ATGM");
            }

            if (site.Tier >= 3 && !HasEmplacement(site, "23mm") && !HasEmplacement(site, "MANPADS"))
            {
                Vector3 aaPos = SnapToGround(site.Center + Vector3.forward * 10f + Vector3.left * 6f);
                Building aa = SpawnEmplacement(site, spawner, "Emplacement1_23mm", "23mm", aaPos, Quaternion.LookRotation(Vector3.forward), "AA");
                if (aa == null)
                    SpawnEmplacement(site, spawner, "Emplacement1_MANPADS", "MANPADS", aaPos, Quaternion.LookRotation(Vector3.forward), "AA");
            }

            if (site.Tier >= 4 && !HasEmplacement(site, "radar") && !HasEmplacement(site, "MC260"))
            {
                Vector3 radarPos = SnapToGround(site.Center + Vector3.back * 12f);
                SpawnEmplacement(site, spawner, "MC260_RadarContainer", "radar", radarPos, Quaternion.identity, "Radar");
            }
        }

        private static Building SpawnEmplacement(
            EncampmentSite site, Spawner spawner, string preferredKey, string fallbackKeyword,
            Vector3 position, Quaternion rotation, string label)
        {
            BuildingDefinition def = Resolve(preferredKey, fallbackKeyword);
            if (def == null || def.unitPrefab == null || spawner == null) return null;

            Building b = spawner.SpawnBuilding(
                def.unitPrefab,
                position.ToGlobalPosition(),
                rotation,
                site.Owner,
                site.Airbase,
                $"{NamePrefix}{Sanitize(site.Airbase?.name)}:{site.Id}:{label}",
                false,
                null);

            if (b != null)
            {
                site.Emplacements.Add(b);
                MakeshiftFortificationBuilder.ApplyPresentation(b);
            }
            return b;
        }

        private static bool HasEmplacement(EncampmentSite site, string keyword)
        {
            for (int i = 0; i < site.Emplacements.Count; i++)
            {
                Building b = site.Emplacements[i];
                if (b == null || b.definition == null) continue;
                if (!(b.definition is BuildingDefinition d)) continue;
                if ((d.jsonKey?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (d.unitName?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                    return true;
            }
            return false;
        }

        private static void ReplenishFirebase(EncampmentSite site)
        {
            for (int i = 0; i < site.Emplacements.Count; i++)
            {
                Building b = site.Emplacements[i];
                if (b == null) continue;
                UnitPart part = b.GetComponentInChildren<UnitPart>();
                if (part != null)
                    part.hitPoints = Mathf.Max(part.hitPoints, 100f);
            }
            Plugin.Logger.LogInfo($"[ENCAMPMENT] Forward firebase replenished at Tier {site.Tier}.");
        }

        private static Vector3 SnapToGround(Vector3 position)
        {
            if (Physics.Raycast(position + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                return hit.point;
            return position;
        }

        private static string Sanitize(string name) => (name ?? "Base").Replace(':', '_').Replace(' ', '_');
    }
}
