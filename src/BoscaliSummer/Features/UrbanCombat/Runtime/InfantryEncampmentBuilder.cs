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

        public enum EncampmentType
        {
            MGNest = 0,    // 12.7mm Heavy Machine Gun Nest
            AGMNest = 1,   // AT-145 Anti-Tank Missile Nest
            AANest = 2     // IRM-S1 & 23mm Air Defense Nest
        }

        public sealed class EncampmentSite
        {
            public Vector3 Center;
            public Vector3 Forward;
            public FactionHQ Owner;
            public Airbase Airbase;
            public int Tier;
            public int Troops;
            public int Id;
            public EncampmentType Type;
            public readonly HashSet<int> SpawnedSlots = new HashSet<int>();
            public readonly List<Building> Emplacements = new List<Building>();
        }

        private static readonly List<EncampmentSite> ActiveSites = new List<EncampmentSite>();
        public static IReadOnlyList<EncampmentSite> GetActiveSites() => ActiveSites;
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

        private struct SlotSpec
        {
            public string PrefKey;
            public string Fallback;
            public string Label;
            public SlotSpec(string prefKey, string fallback, string label)
            {
                PrefKey = prefKey;
                Fallback = fallback;
                Label = label;
            }
        }

        private static SlotSpec[] GetSlotsForType(EncampmentType type)
        {
            switch (type)
            {
                case EncampmentType.AANest:
                    return new[]
                    {
                        new SlotSpec("Emplacement1_MANPADS", "MANPADS", "AA"),
                        new SlotSpec("Emplacement1_MG", "MG", "MG"),
                        new SlotSpec("Emplacement1_23mm", "23mm", "AAA"),
                        new SlotSpec("Emplacement1_MANPADS", "MANPADS", "AA2")
                    };
                case EncampmentType.AGMNest:
                    return new[]
                    {
                        new SlotSpec("Emplacement1_ATGM", "ATGM", "AGM"),
                        new SlotSpec("Emplacement1_MG", "MG", "MG"),
                        new SlotSpec("Emplacement1_ATGM", "ATGM", "AGM2"),
                        new SlotSpec("Emplacement1_MANPADS", "MANPADS", "AA")
                    };
                default: // MGNest
                    return new[]
                    {
                        new SlotSpec("Emplacement1_MG", "MG", "MG"),
                        new SlotSpec("Emplacement1_ATGM", "ATGM", "AGM"),
                        new SlotSpec("Emplacement1_MANPADS", "MANPADS", "AA"),
                        new SlotSpec("Emplacement1_23mm", "23mm", "AAA")
                    };
            }
        }

        private static bool CreateNewSite(Vector3 center, FactionHQ owner, Airbase airbase, int troopCount)
        {
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null) return false;

            Vector3 groundCenter = SnapToGround(center);
            Vector3 forward = Vector3.forward;
            if (airbase != null)
            {
                Vector3 toBase = Vector3.ProjectOnPlane(airbase.transform.position - groundCenter, Vector3.up);
                if (toBase.sqrMagnitude > 1f) forward = toBase.normalized;
            }

            var site = new EncampmentSite
            {
                Center = groundCenter,
                Forward = forward,
                Owner = owner,
                Airbase = airbase,
                Troops = Math.Max(1, troopCount),
                Id = ActiveSites.Count,
                Type = (EncampmentType)(ActiveSites.Count % 3)
            };
            site.Tier = TroopDeploymentMath.ComputeTier(site.Troops);

            SpawnEmplacements(site, spawner);
            if (site.Emplacements.Count == 0) return false;

            ActiveSites.Add(site);
            Plugin.Logger.LogInfo($"[ENCAMPMENT] Established Tier {site.Tier} {site.Type} with {site.Troops} infantry committed at ({groundCenter.x:0}, {groundCenter.z:0}).");
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

            Plugin.Logger.LogInfo($"[ENCAMPMENT] Reinforced {site.Type} outpost to Tier {site.Tier} with {site.Troops} total infantry committed.");
        }

        private static void SpawnEmplacements(EncampmentSite site, Spawner spawner)
        {
            Vector3 right = Vector3.Cross(Vector3.up, site.Forward).normalized;
            SlotSpec[] slots = GetSlotsForType(site.Type);

            for (int slot = 0; slot < slots.Length; slot++)
            {
                int requiredTier = slot + 1;
                if (site.Tier < requiredTier || site.SpawnedSlots.Contains(slot))
                    continue;

                Vector3 pos;
                Quaternion rot;
                switch (slot)
                {
                    case 0:
                        pos = site.Center;
                        rot = Quaternion.LookRotation(site.Forward, Vector3.up);
                        break;
                    case 1:
                        pos = SnapToGround(site.Center + site.Forward * 9f + right * 6f);
                        rot = Quaternion.LookRotation((pos - site.Center).normalized, Vector3.up);
                        break;
                    case 2:
                        pos = SnapToGround(site.Center + site.Forward * 9f - right * 6f);
                        rot = Quaternion.LookRotation((pos - site.Center).normalized, Vector3.up);
                        break;
                    default:
                        pos = SnapToGround(site.Center - site.Forward * 12f);
                        rot = Quaternion.LookRotation(-site.Forward, Vector3.up);
                        break;
                }

                Building b = SpawnEmplacement(site, spawner, slots[slot].PrefKey, slots[slot].Fallback, pos, rot, slots[slot].Label);
                if (b != null)
                {
                    site.SpawnedSlots.Add(slot);
                }
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
            }
            return b;
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
