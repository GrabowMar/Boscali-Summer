using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
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
        internal const int MaximumSites = 12;
        /// <summary>Game-start air defence camps; counted apart so they never block an insertion.</summary>
        internal const int MaximumSeededSites = 64;

        public enum EncampmentType
        {
            MGNest = 0,    // 12.7mm Heavy Machine Gun Nest
            AGMNest = 1,   // AT-145 Anti-Tank Missile Nest
            AANest = 2     // IRM-S1 & 23mm Air Defense Nest
        }

        public sealed class EncampmentSite
        {
            // Stored global: sites outlive floating-origin shifts.
            private GlobalPosition center;
            public Vector3 Center { get => center.ToLocalPosition(); set => center = value.ToGlobalPosition(); }
            public Vector3 Forward;
            public FactionHQ Owner;
            public Airbase Airbase;
            public int Tier;
            public int Troops;
            public int Id;
            public EncampmentType Type;
            public bool Rappel;
            public bool Seeded;
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
        internal static void PruneFallenSites()
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

        /// <summary>
        /// Exfil (server): strikes the nearest standing site <paramref name="owner"/> holds within
        /// <paramref name="radius"/> of <paramref name="near"/> and hands back its centre and troops.
        /// </summary>
        internal static bool TryRelease(Vector3 near, float radius, FactionHQ owner, out Vector3 centre, out int troops)
        {
            centre = default;
            troops = 0;
            PruneFallenSites();
            EncampmentSite best = null;
            float bestSq = radius * radius;
            foreach (EncampmentSite site in ActiveSites)
            {
                if (site.Owner != owner) continue;
                Vector3 d = site.Center - near;
                d.y = 0f;
                if (d.sqrMagnitude <= bestSq) { bestSq = d.sqrMagnitude; best = site; }
            }
            if (best == null || !GameAccess.IsServer() || NetworkManagerNuclearOption.i?.ServerObjectManager == null) return false;
            foreach (Building slot in best.Slots)
                if (IsStanding(slot)) NetworkManagerNuclearOption.i.ServerObjectManager.Destroy(slot.Identity, true);
            ActiveSites.Remove(best);
            centre = best.Center;
            troops = Math.Max(best.Troops, TroopDeploymentMath.DefaultSquadSize);
            return true;
        }

        public static EncampmentSite FindNearbySite(Vector3 pos, float maxDistance = 150f)
        {
            float limitSq = maxDistance * maxDistance;
            for (int i = 0; i < ActiveSites.Count; i++)
            {
                EncampmentSite site = ActiveSites[i];
                if (site != null && (site.Center - pos).sqrMagnitude <= limitSq)
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
            if (ActiveSiteCount >= MaximumSites) return false;
            return CreateNewSite(dropPos, owner, airbase, troopCount);
        }

        /// <summary>
        /// Paradrop: dig in at the touchdown point, or at the first workable ground near it.
        /// The exact slot is often a roof, a slope or a treeline where no emplacement fits, so
        /// (like a rappel camp) the search walks rings outward instead of giving up on the
        /// one point the stick aimed at. Positions are local; each candidate is snapped to
        /// the ground it would stand on before it is tried.
        /// </summary>
        public static bool DeployParadropEncampment(Vector3 position, FactionHQ owner, Airbase airbase, int troopCount)
        {
            if (!GameAccess.IsServer() || owner == null) return false;
            if (DeployOrReinforce(position, owner, airbase, troopCount)) return true;
            float[] radii = { 14f, 30f, 55f, 90f };
            for (int r = 0; r < radii.Length; r++)
                for (int k = 0; k < 8; k++)
                {
                    float a = k * (Mathf.PI * 0.25f) + r * 0.35f;
                    Vector3 candidate = position + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radii[r];
                    if (!Physics.Raycast(candidate + Vector3.up * 30f, Vector3.down, out RaycastHit ground,
                            120f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore) ||
                        ground.point.y <= Datum.LocalSeaY + 1f || ground.normal.y < 0.8f) continue;
                    if (DeployOrReinforce(ground.point, owner, airbase, troopCount)) return true;
                }
            return false;
        }

        /// <summary>Camps troops established (seeded air defence excluded): the insertion cap counts these.</summary>
        internal static int ActiveSiteCount => CountSites(false);

        private static int CountSites(bool seeded)
        {
            int n = 0;
            foreach (EncampmentSite site in ActiveSites)
                if (site.Seeded == seeded) n++;
            return n;
        }

        /// <summary>
        /// Game start (server): rings an owned zone with full-strength AA camps 2.2-3.2 km out,
        /// fanned toward the nearest enemy base so the way in is covered and the runway stays clear.
        /// </summary>
        public static int SeedAirDefense(Airbase airbase, FactionHQ owner, int camps)
        {
            if (!GameAccess.IsServer() || airbase == null || owner == null || camps <= 0) return 0;
            PruneFallenSites();
            Vector3 home = airbase.transform.position;
            Vector3 front = FrontDirection(home, owner);
            int placed = 0;
            for (int camp = 0; camp < camps && CountSites(true) < MaximumSeededSites; camp++)
            {
                float fan = (camp - (camps - 1) * 0.5f) * (360f / Mathf.Max(camps, 3)) * 0.6f;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    float yaw = fan + ((attempt & 1) * 2 - 1) * attempt * 9f;
                    Vector3 candidate = home + Quaternion.Euler(0f, yaw, 0f) * front * UnityEngine.Random.Range(2200f, 3200f);
                    if (!GroundPlacement.DryGround(candidate, out Vector3 ground) || FindNearbySite(ground, 500f) != null) continue;
                    if (CreateNewSite(ground, owner, airbase, TroopDeploymentMath.DefaultSquadSize, seeded: true)) { placed++; break; }
                }
            }
            return placed;
        }

        private static Vector3 FrontDirection(Vector3 home, FactionHQ owner)
        {
            Vector3 best = Vector3.zero;
            float bestSq = float.MaxValue;
            if (FactionRegistry.airbaseLookup != null)
                foreach (Airbase other in FactionRegistry.airbaseLookup.Values)
                {
                    if (other == null || other.AttachedAirbase || other.CurrentHQ == null || other.CurrentHQ == owner) continue;
                    Vector3 d = Vector3.ProjectOnPlane(other.transform.position - home, Vector3.up);
                    if (d.sqrMagnitude > 1f && d.sqrMagnitude < bestSq) { bestSq = d.sqrMagnitude; best = d; }
                }
            if (best == Vector3.zero) best = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
            return best.normalized;
        }

        /// <summary>How many camps one stick establishes, per base-of-operations doctrine.</summary>
        internal static int RappelWanted(FactionHQ owner)
        {
            BoscaliSummer.Core.Services.ModuleServices.TryGet(
                out BoscaliSummer.Core.Contracts.IGroundForceReadiness readiness);
            return Mathf.Clamp(readiness != null ? readiness.InsertionCamps(owner) : 1, 1, MaximumSites);
        }

        public static bool DeployRappelEncampment(Vector3 position, FactionHQ owner, Airbase airbase)
        {
            if (!BoscaliSummer.Core.Game.GameAccess.IsServer() || owner == null) return false;
            // Base-of-operations doctrine decides how many camps one stick establishes.
            int wanted = RappelWanted(owner);
            PruneFallenSites();
            int placed = 0;
            for (int camp = 0; camp < wanted && ActiveSiteCount < MaximumSites; camp++)
                if (TryCreateRappelCamp(position, owner, airbase)) placed++;
            return placed > 0;
        }

        /// <summary>
        /// One camp near the LZ. Repeated insertions at the same place get distinct
        /// four-position camps; at most twelve candidate positions, matching the site ceiling.
        /// </summary>
        internal static bool TryCreateRappelCamp(Vector3 position, FactionHQ owner, Airbase airbase)
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
                if (CreateNewSite(ground.point, owner, airbase, TroopDeploymentMath.DefaultSquadSize, true)) return true;
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

        private static bool CreateNewSite(Vector3 center, FactionHQ owner, Airbase airbase, int troopCount, bool rappel = false, bool seeded = false)
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
                Seeded = seeded,
                Type = seeded ? EncampmentType.AANest : rappel ? EncampmentType.MGNest
                    : (EncampmentType)GarrisonComposition.EncampmentTypeIndex(groundCenter.x, groundCenter.z)
            };
            nextSiteId++;
            site.Tier = site.Rappel || site.Seeded ? 4 : TroopDeploymentMath.ComputeTier(site.Troops);

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
            site.Tier = site.Rappel || site.Seeded ? 4 : TroopDeploymentMath.ComputeTier(site.Troops);

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
            if (def == null || spawner == null || !GameAccess.IsServer() || site.Owner == null ||
                !GroundPlacement.TryPlace(def, position, rotation, out Vector3 placed)) return null;

            return spawner.SpawnBuilding(
                def.unitPrefab,
                placed.ToGlobalPosition(),
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
