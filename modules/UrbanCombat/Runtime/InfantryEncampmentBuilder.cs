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
            public bool Rappel;
            /// <summary>One emplacement per slot; a destroyed one frees its slot for reinforcement.</summary>
            public readonly Building[] Slots = new Building[4];

            public bool Standing
            {
                get
                {
                    for (int i = 0; i < Slots.Length; i++)
                        if (IsStanding(Slots[i])) return true;
                    return false;
                }
            }
        }

        private static readonly List<EncampmentSite> ActiveSites = new List<EncampmentSite>();
        private static readonly Dictionary<string, BuildingDefinition> CachedDefs =
            new Dictionary<string, BuildingDefinition>(StringComparer.OrdinalIgnoreCase);
        private static bool catalogInitialized;
        private static int nextSiteId;

        public static void ResetForScene()
        {
            ActiveSites.Clear();
            CachedDefs.Clear();
            catalogInitialized = false;
            nextSiteId = 0;
        }

        private static bool IsStanding(Building building) => building != null && !building.disabled;

        /// <summary>Drops sites whose emplacements are all gone: at most 12 sites x 4 slots.</summary>
        private static void PruneFallenSites()
        {
            for (int i = ActiveSites.Count - 1; i >= 0; i--)
                if (!ActiveSites[i].Standing) ActiveSites.RemoveAt(i);
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

        public static bool DeployOrReinforce(Vector3 dropPos, FactionHQ owner, Airbase airbase, int troopCount)
        {
            // A fallen site no longer counts toward the cap, blocks placement or absorbs a drop.
            PruneFallenSites();
            EncampmentSite existing = FindNearbySite(dropPos, 150f);
            if (existing != null && existing.Owner == owner)
            {
                ReinforceSite(existing, troopCount);
                return true;
            }
            if (ActiveSites.Count >= MaximumSites) return false;
            return CreateNewSite(dropPos, owner, airbase, troopCount);
        }

        public static bool DeployRappelEncampment(Vector3 position, FactionHQ owner, Airbase airbase)
        {
            if (!BoscaliSummer.Runtime.GameAccess.IsServer() || owner == null) return false;
            // Base-of-operations doctrine decides how many camps one stick establishes.
            BoscaliSummer.Framework.Features.ModServices.TryGet(
                out BoscaliSummer.Framework.Contracts.IGroundForceReadiness readiness);
            int wanted = Mathf.Clamp(readiness != null ? readiness.InsertionCamps(owner) : 1, 1, MaximumSites);
            PruneFallenSites();
            int placed = 0;
            for (int camp = 0; camp < wanted && ActiveSites.Count < MaximumSites; camp++)
                if (TryCreateRappelCamp(position, owner, airbase)) placed++;
            return placed > 0;
        }

        /// <summary>
        /// One camp near the LZ. Repeated insertions at the same place get distinct
        /// four-position camps; at most twelve candidate positions, matching the site ceiling.
        /// </summary>
        private static bool TryCreateRappelCamp(Vector3 position, FactionHQ owner, Airbase airbase)
        {
            for (int i = 0; i < MaximumSites; i++)
            {
                float angle = i * (2f * Mathf.PI / (MaximumSites - 1));
                Vector3 candidate = i == 0 ? position : position +
                    new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 32f;
                if (FindNearbySite(candidate, 28f) != null) continue;
                if (!Physics.Raycast(candidate + Vector3.up * 10f, Vector3.down, out RaycastHit ground,
                    60f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore) ||
                    ground.point.y <= Datum.LocalSeaY + 1f || ground.normal.y < 0.8f) continue;
                return CreateNewSite(ground.point, owner, airbase, TroopDeploymentMath.DefaultSquadSize, true);
            }
            return false;
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

        private static bool CreateNewSite(Vector3 center, FactionHQ owner, Airbase airbase, int troopCount, bool rappel = false)
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
                // Ids name the emplacements, so a pruned site's id is never handed out again.
                Id = nextSiteId,
                Rappel = rappel,
                Type = rappel ? EncampmentType.MGNest : (EncampmentType)(nextSiteId % 3)
            };
            nextSiteId++;
            site.Tier = site.Rappel ? 4 : TroopDeploymentMath.ComputeTier(site.Troops);

            SpawnEmplacements(site, spawner);
            if (!site.Standing) return false;

            ActiveSites.Add(site);
            Plugin.Logger.LogInfo($"[ENCAMPMENT] Established Tier {site.Tier} {site.Type} with {site.Troops} infantry committed at ({groundCenter.x:0}, {groundCenter.z:0}).");
            return true;
        }

        private static void ReinforceSite(EncampmentSite site, int troopCount)
        {
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null || site == null) return;

            site.Troops += Math.Max(1, troopCount);
            site.Tier = site.Rappel ? 4 : TroopDeploymentMath.ComputeTier(site.Troops);

            SpawnEmplacements(site, spawner);
            if (site.Tier >= 4)
                ReplenishFirebase(site);

            Plugin.Logger.LogInfo($"[ENCAMPMENT] Reinforced {site.Type} outpost to Tier {site.Tier} with {site.Troops} total infantry committed.");
        }

        private static void SpawnEmplacements(EncampmentSite site, Spawner spawner)
        {
            Vector3 right = Vector3.Cross(Vector3.up, site.Forward).normalized;
            SlotSpec[] slots = site.Rappel ? new[]
            {
                new SlotSpec("Emplacement1_MG", "MG", "MG"),
                new SlotSpec("Emplacement1_ATGM", "ATGM", "AT"),
                new SlotSpec("Emplacement1_MANPADS", "MANPADS", "AA"),
                new SlotSpec("Emplacement1_MG", "MG", "MG2")
            } : GetSlotsForType(site.Type);

            for (int slot = 0; slot < slots.Length; slot++)
            {
                int requiredTier = slot + 1;
                if (site.Tier < requiredTier || IsStanding(site.Slots[slot]))
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
                        // Yaw only: the flanks face out along the ground, whatever its slope.
                        rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(pos - site.Center, Vector3.up), Vector3.up);
                        break;
                    case 2:
                        pos = SnapToGround(site.Center + site.Forward * 9f - right * 6f);
                        rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(pos - site.Center, Vector3.up), Vector3.up);
                        break;
                    default:
                        pos = SnapToGround(site.Center - site.Forward * 12f);
                        rot = Quaternion.LookRotation(-site.Forward, Vector3.up);
                        break;
                }

                Building b = SpawnEmplacement(site, spawner, slots[slot].PrefKey, slots[slot].Fallback, pos, rot, slots[slot].Label);
                if (b != null)
                {
                    site.Slots[slot] = b;
                }
            }
        }

        private static Building SpawnEmplacement(
            EncampmentSite site, Spawner spawner, string preferredKey, string fallbackKeyword,
            Vector3 position, Quaternion rotation, string label)
        {
            BuildingDefinition def = Resolve(preferredKey, fallbackKeyword);
            if (def == null || def.unitPrefab == null || spawner == null) return null;

            return spawner.SpawnBuilding(
                def.unitPrefab,
                position.ToGlobalPosition(),
                rotation,
                site.Owner,
                site.Airbase,
                $"{NamePrefix}{Sanitize(site.Airbase != null ? site.Airbase.name : null)}:{site.Id}:{label}",
                false,
                null);
        }


        private static void ReplenishFirebase(EncampmentSite site)
        {
            for (int i = 0; i < site.Slots.Length; i++)
            {
                Building b = site.Slots[i];
                if (!IsStanding(b)) continue;
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
