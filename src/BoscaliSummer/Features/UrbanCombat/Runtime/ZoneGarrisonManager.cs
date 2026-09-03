using System;
using System.Collections.Generic;
using BoscaliSummer.Core;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using NuclearOption.Networking;
using UnityEngine;
using BoscaliSummer.Framework.Visuals;

namespace BoscaliSummer.Garrisons
{
    internal sealed class ZoneGarrisonManager : MonoBehaviour, ISceneService, IBuildingOccupancy,
        IZoneFortificationService
    {
        internal const string NamePrefix = "BoscaliSummer:Garrison:";

        private sealed class PendingCapture
        {
            public Airbase Airbase;
            public FactionHQ Owner;
            public float ExecuteAt;
            public int Attempts;
        }

        private sealed class GarrisonRecord
        {
            public Airbase Airbase;
            public FactionHQ Owner;
            public readonly List<Building> Defenses = new List<Building>();
            public readonly List<GameObject> Shells = new List<GameObject>();
            public readonly List<GameObject> SpawnedProps = new List<GameObject>();
        }

        public static ZoneGarrisonManager Instance { get; private set; }

        bool IBuildingOccupancy.IsOccupied(GameObject shell) =>
            GarrisonOccupancy.IsOccupied(shell);

        private readonly Dictionary<int, List<Building>> fortificationRecords = new Dictionary<int, List<Building>>();

        bool IZoneFortificationService.TryFortify(
            Airbase airbase, FactionHQ owner, NuclearOption.Networking.Player requester, Vector3 targetPosition)
        {
            if (!IsServer() || airbase == null || owner == null || requester == null ||
                airbase.AttachedAirbase || airbase.CurrentHQ != owner || requester.HQ != owner)
                return false;

            if (NetworkSceneSingleton<Spawner>.i == null) return false;

            int key = airbase.GetInstanceID();
            if (!fortificationRecords.TryGetValue(key, out var list))
            {
                list = new List<Building>();
                fortificationRecords[key] = list;
            }

            // Remove any disabled or destroyed buildings from our tracking list
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null || list[i].disabled) list.RemoveAt(i);
            }

            Vector3 center = targetPosition != default
                ? targetPosition
                : (airbase.center != null ? airbase.center.position : airbase.transform.position);

            // If designated target is right at the runway / airbase center, place on the defense perimeter
            if (targetPosition == default || (center - airbase.transform.position).sqrMagnitude < 400f)
            {
                float angle = (list.Count * 65f) * Mathf.Deg2Rad;
                float dist = Mathf.Max(220f, airbase.GetRadius() * 0.75f);
                center = airbase.transform.position + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * dist;
            }

            var spawned = InfantryEncampmentBuilder.DeployEncampment(airbase, owner, center, (list.Count / 5) + 1);
            if (spawned.Count == 0)
            {
                // Fallback attempt with alternative offset near airbase
                Vector3 fallbackCenter = (airbase.center != null ? airbase.center.position : airbase.transform.position)
                    + new Vector3(100f, 0f, 100f);
                spawned = InfantryEncampmentBuilder.DeployEncampment(airbase, owner, fallbackCenter, (list.Count / 5) + 1);
            }

            if (spawned.Count == 0) return false;

            list.AddRange(spawned);
            Plugin.Logger.LogInfo($"[Fortification] Deployed {spawned.Count} visible infantry encampment building(s) at {GetAirbaseName(airbase)} for {owner.name}. Total active fortifications: {list.Count}.");
            return true;
        }

        public bool TryOccupyBuilding(GameObject shell, FactionHQ owner, Airbase airbase)
        {
            if (shell == null || owner == null) return false;

            Building shellBuilding = shell.GetComponentInParent<Building>();
            Bounds bounds = GetShellBounds(shell);

            int key = airbase != null ? airbase.GetInstanceID() : 0;
            if (!records.TryGetValue(key, out GarrisonRecord record))
            {
                record = new GarrisonRecord { Airbase = airbase, Owner = owner };
                records[key] = record;
            }

            int slot = record.Shells.Count;
            int generation = generations.TryGetValue(key, out int gen) ? gen : 1;

            // 1. Resolve Rooftop Anti-Aircraft unit (alternating 23mm AAA and MANPADS SAM)
            BuildingDefinition aaDef = ResolveAADefinition(slot) ?? ResolveDefenseDefinition();
            if (aaDef != null && aaDef.unitPrefab != null && NetworkSceneSingleton<Spawner>.i != null)
            {
                Bounds prefabBounds = CalculateBounds(aaDef.unitPrefab);
                Vector3 rooftopLocal = bounds.center;
                rooftopLocal.y = bounds.max.y - prefabBounds.min.y + 0.1f;

                string uniqueAA = NamePrefix + "RooftopAA:Assault:" + generation + ":" + slot;
                Building spawnedAA = NetworkSceneSingleton<Spawner>.i.SpawnBuilding(
                    aaDef.unitPrefab,
                    rooftopLocal.ToGlobalPosition(),
                    shell.transform.rotation,
                    owner,
                    airbase,
                    uniqueAA,
                    false,
                    null);

                if (spawnedAA != null)
                {
                    GarrisonVisual.Apply(spawnedAA);
                    record.Defenses.Add(spawnedAA);
                }
            }

            // 2. Deploy makeshift ground fortifications & 3D Jersey barriers around the building foot
            var groundUnits = MakeshiftFortificationBuilder.DeployGroundFortifications(
                shell, bounds, owner, airbase, slot, generation, out var spawnedProps);
            record.Defenses.AddRange(groundUnits);
            record.SpawnedProps.AddRange(spawnedProps);

            // 3. Apply high-visibility military faction markings and rooftop beacon mast
            OccupiedBuildingMarking.Apply(shell, owner, bounds);

            // 4. Register enemy ownership
            if (shellBuilding != null && !shellBuilding.disabled)
                shellBuilding.NetworkHQ = owner;

            GarrisonOccupancy.Set(shell, owner);
            record.Shells.Add(shell);

            Plugin.Logger.LogInfo($"[Air Assault] Successfully captured and occupied {shell.name} for {owner.name} with rooftop AA and ground fortifications.");
            return true;
        }

        public bool TryDeployEncampment(Vector3 groundPos, FactionHQ owner, Airbase airbase)
        {
            if (owner == null) return false;
            int key = airbase != null ? airbase.GetInstanceID() : 0;
            if (!records.TryGetValue(key, out GarrisonRecord record))
            {
                record = new GarrisonRecord { Airbase = airbase, Owner = owner };
                records[key] = record;
            }

            int index = (record.Defenses.Count / 5) + 1;
            var spawned = InfantryEncampmentBuilder.DeployEncampment(airbase, owner, groundPos, index);
            if (spawned != null && spawned.Count > 0)
            {
                record.Defenses.AddRange(spawned);
                Plugin.Logger.LogInfo($"[Air Assault] Successfully deployed {spawned.Count} ground encampment units at {groundPos} for {owner.name}.");
                return true;
            }
            return false;
        }

        private readonly List<PendingCapture> pending = new List<PendingCapture>();
        private readonly Dictionary<int, GarrisonRecord> records = new Dictionary<int, GarrisonRecord>();
        private readonly Dictionary<int, int> generations = new Dictionary<int, int>();
        private readonly List<GameObject> shellCatalogue = new List<GameObject>(512);
        private readonly Dictionary<int, Bounds> shellBounds = new Dictionary<int, Bounds>(512);
        private BuildingDefinition cachedDefenseDefinition;
        private Bounds cachedDefenseBounds;
        private bool hasCachedDefenseBounds;
        private float nextLifecycleCheck;
        private bool missingDefinitionReported;
        private bool definitionInventoryReported;
        private bool initialScanComplete;
        private float initialScanAt;

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void ResetForScene()
        {
            pending.Clear();
            records.Clear();
            generations.Clear();
            shellCatalogue.Clear();
            shellBounds.Clear();
            cachedDefenseDefinition = null;
            hasCachedDefenseBounds = false;
            missingDefinitionReported = false;
            definitionInventoryReported = false;
            initialScanComplete = false;
            initialScanAt = Time.unscaledTime + 3f;

            foreach (var fortList in fortificationRecords.Values)
            {
                for (int i = 0; i < fortList.Count; i++)
                    DestroyNetworked(fortList[i]);
            }
            fortificationRecords.Clear();
            InfantryEncampmentBuilder.ResetForScene();
        }

        public void ScheduleCapture(Airbase airbase, FactionHQ owner)
        {
            if (!IsServer() || airbase == null) return;
            int key = airbase.GetInstanceID();
            if (records.TryGetValue(key, out GarrisonRecord current) && current.Owner == owner) return;

            for (int i = pending.Count - 1; i >= 0; i--)
                if (pending[i].Airbase == airbase) pending.RemoveAt(i);
            pending.Add(new PendingCapture
            {
                Airbase = airbase,
                Owner = owner,
                ExecuteAt = Time.unscaledTime + 0.25f + pending.Count * 0.08f,
                Attempts = 0
            });
        }

        private void Update()
        {
            if (!IsServer()) return;
            if (!initialScanComplete && Time.unscaledTime >= initialScanAt)
            {
                initialScanComplete = true;
                RebuildShellCatalogue();
                Airbase[] airbases = Resources.FindObjectsOfTypeAll<Airbase>();
                for (int i = 0; i < airbases.Length; i++)
                {
                    Airbase airbase = airbases[i];
                    if (airbase == null || !airbase.gameObject.scene.IsValid() || airbase.AttachedAirbase) continue;
                    ScheduleCapture(airbase, airbase.CurrentHQ);
                }
            }
            for (int i = 0; i < pending.Count; i++)
            {
                if (Time.unscaledTime < pending[i].ExecuteAt) continue;
                PendingCapture item = pending[i];
                pending.RemoveAt(i);
                ApplyCapture(item);
                break; // At most one zone in a frame.
            }

            if (Time.unscaledTime >= nextLifecycleCheck)
            {
                nextLifecycleCheck = Time.unscaledTime + 1f;
                CheckShellLifecycle();
            }
        }

        private void ApplyCapture(PendingCapture capture)
        {
            Airbase airbase = capture.Airbase;
            FactionHQ owner = capture.Owner;
            if (airbase == null) return;
            int key = airbase.GetInstanceID();
            ClearRecord(key);
            if (owner == null || !Plugin.Settings.GarrisonsEnabled.Value || airbase.AttachedAirbase) return;

            BuildingDefinition defense = ResolveDefenseDefinition();
            if (defense == null || defense.unitPrefab == null || NetworkSceneSingleton<Spawner>.i == null)
            {
                if (capture.Attempts < 4) { Retry(capture); return; }
                if (!missingDefinitionReported)
                {
                    missingDefinitionReported = true;
                    Plugin.Logger.LogWarning("Garrisons disabled for this scene: no usable vanilla DEF building definition was loaded. Set Garrisons/DefenseDefinitionKey after checking the capability log.");
                }
                return;
            }

            List<GameObject> candidates = FindCandidates(airbase);
            if (candidates.Count == 0)
            {
                if (capture.Attempts == 0)
                {
                    RebuildShellCatalogue();
                    candidates = FindCandidates(airbase);
                }
            }
            if (candidates.Count == 0)
            {
                if (capture.Attempts < 3) { Retry(capture); return; }
                if (Plugin.Settings.VerboseLogging.Value)
                    Plugin.Logger.LogInfo("No eligible civilian building shells around airbase " + GetAirbaseName(airbase));
                return;
            }

            int generation = generations.TryGetValue(key, out int old) ? old + 1 : 1;
            generations[key] = generation;
            uint seed = Deterministic.Hash(
                (int)Deterministic.HashString(GetAirbaseName(airbase)),
                owner.GetInstanceID(), generation);
            Shuffle(candidates, seed);
            int min = Mathf.Min(Plugin.Settings.GarrisonsMinimum, Plugin.Settings.GarrisonsMaximum);
            int max = Mathf.Max(Plugin.Settings.GarrisonsMinimum, Plugin.Settings.GarrisonsMaximum);
            int rolled = min + (int)(seed % (uint)Mathf.Max(1, max - min + 1));
            int count = Mathf.Clamp(rolled, 0, candidates.Count);

            var record = new GarrisonRecord { Airbase = airbase, Owner = owner };
            records[key] = record;
            for (int slot = 0; slot < count; slot++)
            {
                GameObject shell = candidates[slot];
                if (shell == null) continue;
                Building shellBuilding = shell.GetComponentInParent<Building>();
                Bounds shellBounds = GetShellBounds(shell);
                if (!hasCachedDefenseBounds)
                {
                    cachedDefenseBounds = CalculateBounds(defense.unitPrefab);
                    hasCachedDefenseBounds = true;
                }
                Bounds prefabBounds = cachedDefenseBounds;
                // Anchor defensive proxy logic inside the building core (slightly elevated
                // for realistic embrasure sightlines) rather than perched on the roof.
                Vector3 local = shellBounds.center + Vector3.up * Mathf.Min(shellBounds.extents.y * 0.35f, 3.5f);
                string unique = NamePrefix + Sanitize(GetAirbaseName(airbase)) + ":" + generation + ":" + slot;
                Building spawned = NetworkSceneSingleton<Spawner>.i.SpawnBuilding(
                    defense.unitPrefab,
                    local.ToGlobalPosition(),
                    shell.transform.rotation,
                    owner,
                    airbase,
                    unique,
                    false,
                    null);
                if (spawned == null) continue;
                if (shellBuilding != null && !shellBuilding.disabled)
                    shellBuilding.NetworkHQ = owner;

                // Turn the building itself into a de facto bunker / fortified stronghold
                StrongholdBuilding stronghold = shell.GetComponent<StrongholdBuilding>();
                if (stronghold == null) stronghold = shell.AddComponent<StrongholdBuilding>();
                stronghold.Initialize(
                    owner,
                    airbase,
                    spawned,
                    Plugin.Settings.UrbanCombat.StrongholdHitPoints.Value,
                    Plugin.Settings.UrbanCombat.StrongholdPierceArmor.Value,
                    Plugin.Settings.UrbanCombat.StrongholdBlastArmor.Value);

                GarrisonOccupancy.Set(shell, owner);
                GarrisonVisual.Apply(spawned);
                record.Defenses.Add(spawned);
                record.Shells.Add(shell);
            }

            Plugin.Logger.LogInfo($"Fortified {record.Defenses.Count} building stronghold(s) around {GetAirbaseName(airbase)} for {owner} ({Plugin.Settings.UrbanCombat.StrongholdHitPoints.Value:0} HP, {defense.jsonKey} armament).");
        }

        private void Retry(PendingCapture capture)
        {
            capture.Attempts++;
            capture.ExecuteAt = Time.unscaledTime + 1.5f + capture.Attempts * 1.25f;
            pending.Add(capture);
        }

        private void CheckShellLifecycle()
        {
            foreach (GarrisonRecord record in records.Values)
            {
                int count = Mathf.Min(record.Defenses.Count, record.Shells.Count);
                for (int i = 0; i < count; i++)
                {
                    GameObject shell = record.Shells[i];
                    Building shellBuilding = shell != null ? shell.GetComponentInParent<Building>() : null;
                    StrongholdBuilding stronghold = shell != null ? shell.GetComponent<StrongholdBuilding>() : null;

                    bool shellAlive = shell != null &&
                        (stronghold == null || !stronghold.IsDestroyed) &&
                        (shellBuilding == null || !shellBuilding.disabled);

                    if (shellAlive && record.Defenses[i] != null)
                        continue;

                    DestroyNetworked(record.Defenses[i]);
                    record.Defenses[i] = null;
                }
            }
        }

        private void ClearRecord(int key)
        {
            if (fortificationRecords.TryGetValue(key, out var fortList))
            {
                for (int i = 0; i < fortList.Count; i++) DestroyNetworked(fortList[i]);
                fortificationRecords.Remove(key);
            }

            if (!records.TryGetValue(key, out GarrisonRecord record)) return;
            for (int i = 0; i < record.Defenses.Count; i++) DestroyNetworked(record.Defenses[i]);
            for (int i = 0; i < record.Shells.Count; i++)
            {
                GameObject shell = record.Shells[i];
                if (shell != null)
                {
                    StrongholdBuilding stronghold = shell.GetComponent<StrongholdBuilding>();
                    if (stronghold != null) Destroy(stronghold);
                    BuildingDamageVisual visual = shell.GetComponent<BuildingDamageVisual>();
                    if (visual != null) visual.ResetForScene();
                }
                Building shellBuilding = record.Shells[i]?.GetComponentInParent<Building>();
                if (shellBuilding != null && shellBuilding.NetworkHQ == record.Owner)
                    shellBuilding.NetworkHQ = null;
                GarrisonOccupancy.Clear(record.Shells[i], record.Owner);
            }
            records.Remove(key);
        }

        private static void DestroyNetworked(Building building)
        {
            if (building == null || NetworkManagerNuclearOption.i == null) return;
            NetworkManagerNuclearOption.i.ServerObjectManager.Destroy(building.Identity, true);
        }

        private BuildingDefinition ResolveAADefinition(int slot)
        {
            if (Encyclopedia.i == null || Encyclopedia.i.buildings == null) return null;

            BuildingDefinition aaa23mm = null;
            BuildingDefinition manpads = null;
            BuildingDefinition fallback = null;

            for (int i = 0; i < Encyclopedia.i.buildings.Count; i++)
            {
                BuildingDefinition def = Encyclopedia.i.buildings[i];
                if (def == null || def.buildingType != BuildingType.DEF || def.unitPrefab == null) continue;

                if (def.jsonKey.IndexOf("23mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (def.unitName?.IndexOf("23mm", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                    aaa23mm = def;
                else if (def.jsonKey.IndexOf("MANPADS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (def.unitName?.IndexOf("MANPADS", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                    manpads = def;
                else if (fallback == null && (def.jsonKey.IndexOf("pillbox", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    def.jsonKey.IndexOf("bunker", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    def.jsonKey.IndexOf("guard", StringComparison.OrdinalIgnoreCase) >= 0))
                    fallback = def;
            }

            if (slot % 2 == 0)
                return aaa23mm ?? manpads ?? fallback;
            else
                return manpads ?? aaa23mm ?? fallback;
        }

        private BuildingDefinition ResolveDefenseDefinition()
        {
            if (cachedDefenseDefinition != null) return cachedDefenseDefinition;
            if (Encyclopedia.i == null || Encyclopedia.i.buildings == null) return null;
            string requested = Plugin.Settings.GarrisonDefinitionKey?.Trim();
            if (!string.IsNullOrEmpty(requested) && !string.Equals(requested, "auto", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < Encyclopedia.i.buildings.Count; i++)
                {
                    BuildingDefinition definition = Encyclopedia.i.buildings[i];
                    if (definition != null && definition.jsonKey == requested &&
                        definition.buildingType == BuildingType.DEF)
                        return cachedDefenseDefinition = definition;
                }
            }

            var defs = new List<BuildingDefinition>();
            for (int i = 0; i < Encyclopedia.i.buildings.Count; i++)
            {
                BuildingDefinition definition = Encyclopedia.i.buildings[i];
                if (definition != null && definition.buildingType == BuildingType.DEF &&
                    definition.unitPrefab != null) defs.Add(definition);
            }
            if (!definitionInventoryReported)
            {
                definitionInventoryReported = true;
                var labels = new string[defs.Count];
                for (int i = 0; i < defs.Count; i++)
                    labels[i] = defs[i].jsonKey + " (" + defs[i].unitName + ")";
                Plugin.Logger.LogInfo("Loaded DEF building definitions: " +
                    (defs.Count == 0 ? "none" : string.Join(", ", labels)));
            }

            // Prioritize formidable stronghold defense assets:
            // 1. Pillboxes (reinforced concrete pillboxes with heavy rapid-fire armaments)
            for (int i = 0; i < defs.Count; i++)
            {
                BuildingDefinition d = defs[i];
                if (d.jsonKey.IndexOf("pillbox", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (d.unitName?.IndexOf("pillbox", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                    return cachedDefenseDefinition = d;
            }

            // 2. Heavy emplacements (ATGM, Autocannon, MG)
            for (int i = 0; i < defs.Count; i++)
            {
                BuildingDefinition d = defs[i];
                if (d.jsonKey.IndexOf("ATGM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    d.jsonKey.IndexOf("23mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    d.jsonKey.IndexOf("MG", StringComparison.OrdinalIgnoreCase) >= 0)
                    return cachedDefenseDefinition = d;
            }

            // 3. Any bunker definition
            for (int i = 0; i < defs.Count; i++)
            {
                BuildingDefinition definition = defs[i];
                if ((definition.unitName?.IndexOf("bunker", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (definition.jsonKey?.IndexOf("bunker", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                    return cachedDefenseDefinition = definition;
            }

            // 4. Any available DEF definition
            if (defs.Count > 0)
                return cachedDefenseDefinition = defs[0];

            return null;
        }

        private List<GameObject> FindCandidates(Airbase airbase)
        {
            var result = new List<GameObject>();
            var seen = new HashSet<int>();
            float radius = Mathf.Max(airbase.GetRadius() * 1.15f, 420f);
            Vector3 center = airbase.center != null ? airbase.center.position : airbase.transform.position;
            GatherCandidates(airbase, center, radius, seen, result);

            // Rural highway zones often contain no structures inside the literal capture
            // circle. Use only the nearest surrounding settlement as a bounded fallback.
            float usedRadius = radius;
            if (result.Count == 0 && radius < 2500f)
            {
                usedRadius = 2500f;
                GatherCandidates(airbase, center, usedRadius, seen, result);
            }

            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.Logger.LogInfo($"Garrison search around {GetAirbaseName(airbase)}: {result.Count} shell(s), radius {usedRadius:0} m, center {center}.");
            return result;
        }

        private void GatherCandidates(
            Airbase airbase, Vector3 center, float radius, HashSet<int> seen, List<GameObject> result)
        {
            for (int i = 0; i < shellCatalogue.Count; i++)
                TryAddCandidate(shellCatalogue[i], airbase, center, radius, seen, result);
        }

        private void TryAddCandidate(
            GameObject shell, Airbase airbase, Vector3 center, float radius,
            HashSet<int> seen, List<GameObject> result)
        {
            if (shell == null || !shell.scene.IsValid() || shell.scene != airbase.gameObject.scene) return;
            Building networkBuilding = shell.GetComponentInParent<Building>();
            if (networkBuilding != null && (networkBuilding.disabled || networkBuilding.NetworkHQ != null)) return;
            int id = shell.GetInstanceID();
            if (seen.Contains(id) || IsCriticalName(shell.name)) return;
            Bounds bounds = GetShellBounds(shell);
            Vector3 delta = bounds.center - center;
            delta.y = 0f;
            if (delta.sqrMagnitude > radius * radius) return;
            if (bounds.size.x < 6f || bounds.size.z < 6f || bounds.size.y < 3f) return;
            seen.Add(id);
            result.Add(shell);
        }

        private void RebuildShellCatalogue()
        {
            shellCatalogue.Clear();
            shellBounds.Clear();
            var seen = new HashSet<int>();
            MapBuilding[] mapBuildings = Resources.FindObjectsOfTypeAll<MapBuilding>();
            for (int i = 0; i < mapBuildings.Length; i++)
            {
                MapBuilding building = mapBuildings[i];
                if (building == null || !building.gameObject.scene.IsValid()) continue;
                if (seen.Add(building.gameObject.GetInstanceID())) shellCatalogue.Add(building.gameObject);
            }
            Building[] networkBuildings = Resources.FindObjectsOfTypeAll<Building>();
            for (int i = 0; i < networkBuildings.Length; i++)
            {
                Building building = networkBuildings[i];
                if (building == null || !building.gameObject.scene.IsValid()) continue;
                BuildingDefinition definition = building.definition as BuildingDefinition;
                if (definition == null || definition.buildingType != BuildingType.CIV) continue;
                if (!string.IsNullOrEmpty(building.NetworkUniqueName) &&
                    building.NetworkUniqueName.StartsWith(NamePrefix, StringComparison.Ordinal)) continue;
                if (seen.Add(building.gameObject.GetInstanceID())) shellCatalogue.Add(building.gameObject);
            }
            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.Logger.LogInfo($"Cached {shellCatalogue.Count} civilian shells for garrison searches.");
        }

        private Bounds GetShellBounds(GameObject shell)
        {
            int id = shell.GetInstanceID();
            Bounds bounds;
            if (!shellBounds.TryGetValue(id, out bounds))
            {
                bounds = CalculateBounds(shell);
                shellBounds[id] = bounds;
            }
            return bounds;
        }

        private static bool IsCriticalName(string name)
        {
            string value = name ?? string.Empty;
            for (int i = 0; i < CriticalNameFragments.Length; i++)
                if (value.IndexOf(CriticalNameFragments[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private static readonly string[] CriticalNameFragments =
            { "hangar", "radar", "factory", "depot", "ammo", "tower", "runway", "fuel" };

        private static Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = new Bounds(root.transform.position, Vector3.one * 4f);
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer is ParticleSystemRenderer || !renderer.enabled) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }

        private static void Shuffle(List<GameObject> list, uint seed)
        {
            uint state = seed == 0 ? 0x9e3779b9u : seed;
            for (int i = list.Count - 1; i > 0; i--)
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                int j = (int)(state % (uint)(i + 1));
                GameObject temp = list[i]; list[i] = list[j]; list[j] = temp;
            }
        }

        private static string GetAirbaseName(Airbase airbase) =>
            !string.IsNullOrEmpty(airbase.NetworknetworkUniqueName)
                ? airbase.NetworknetworkUniqueName
                : airbase.name;

        private static string Sanitize(string value) =>
            (value ?? "Airbase").Replace(':', '_').Replace(' ', '_');

        private static bool IsServer()
        {
            try { return NetworkManagerNuclearOption.i != null && NetworkManagerNuclearOption.i.Server.Active; }
            catch { return false; }
        }
    }
}
