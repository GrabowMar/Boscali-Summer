using System;
using System.Collections.Generic;
using BoscaliSummer.Core;
using BoscaliSummer.Features.UrbanCombat.Configuration;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Infrastructure.Diagnostics;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;

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
            public int MinimumCount;
        }

        private sealed class GarrisonRecord
        {
            public FactionHQ Owner;
            public readonly List<Building> Defenses = new List<Building>();
            public readonly List<GameObject> Shells = new List<GameObject>();
        }

        public static ZoneGarrisonManager Instance { get; private set; }

        private static UrbanCombatSettings Urban => Plugin.Settings.UrbanCombat;
        private static DiagnosticSettings Diagnostics => Plugin.Settings.Diagnostics;

        bool IBuildingOccupancy.IsOccupied(GameObject shell) =>
            GarrisonOccupancy.IsOccupied(shell);

        bool IZoneFortificationService.TryFortify(
            Airbase airbase, FactionHQ owner, NuclearOption.Networking.Player requester)
        {
            if (!GameAccess.IsServer() || airbase == null || owner == null || requester == null ||
                airbase.AttachedAirbase || airbase.CurrentHQ != owner || requester.HQ != owner)
                return false;

            if (!Urban.GarrisonsEnabled.Value || NetworkSceneSingleton<Spawner>.i == null)
                return false;
            BuildingDefinition defense = ResolveDefenseDefinition();
            if (defense == null || defense.unitPrefab == null) return false;
            if (FindCandidates(airbase).Count == 0)
            {
                RebuildShellCatalogue();
                if (FindCandidates(airbase).Count == 0) return false;
            }

            int key = airbase.GetInstanceID();
            int floor = records.TryGetValue(key, out GarrisonRecord existing)
                ? existing.Defenses.Count + 1
                : 1;
            ClearRecord(key);
            ScheduleCapture(airbase, owner);
            for (int i = 0; i < pending.Count; i++)
                if (pending[i].Airbase == airbase) pending[i].MinimumCount = floor;
            return true;
        }

        private readonly List<PendingCapture> pending = new List<PendingCapture>();
        private readonly Dictionary<int, GarrisonRecord> records = new Dictionary<int, GarrisonRecord>();
        private readonly Dictionary<int, int> generations = new Dictionary<int, int>();
        private readonly List<GameObject> shellCatalogue = new List<GameObject>(512);
        private readonly Dictionary<int, Bounds> shellBounds = new Dictionary<int, Bounds>(512);
        private BuildingDefinition cachedDefenseDefinition;
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
            missingDefinitionReported = false;
            definitionInventoryReported = false;
            initialScanComplete = false;
            initialScanAt = Time.unscaledTime + 3f;

            GarrisonOccupancy.Reset();
        }

        public void ScheduleCapture(Airbase airbase, FactionHQ owner)
        {
            if (!GameAccess.IsServer() || airbase == null) return;
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
            if (!GameAccess.IsServer()) return;
            if (!initialScanComplete && Time.unscaledTime >= initialScanAt)
            {
                initialScanComplete = true;
                RebuildShellCatalogue();
                IEnumerable<Airbase> airbases = (FactionRegistry.airbaseLookup != null && FactionRegistry.airbaseLookup.Count > 0)
                    ? (IEnumerable<Airbase>)FactionRegistry.airbaseLookup.Values
                    : Resources.FindObjectsOfTypeAll<Airbase>();
                if (airbases != null)
                {
                    foreach (Airbase airbase in airbases)
                    {
                        if (airbase == null || !airbase.gameObject.scene.IsValid() || airbase.AttachedAirbase) continue;
                        ScheduleCapture(airbase, airbase.CurrentHQ);
                    }
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
            if (owner == null || !Urban.GarrisonsEnabled.Value || airbase.AttachedAirbase) return;

            BuildingDefinition defense = ResolveDefenseDefinition();
            if (defense == null || defense.unitPrefab == null || NetworkSceneSingleton<Spawner>.i == null)
            {
                if (capture.Attempts < 4) { Retry(capture); return; }
                if (!missingDefinitionReported)
                {
                    missingDefinitionReported = true;
                    Plugin.Logger.LogWarning("Garrisons disabled for this scene: no usable vanilla DEF building definition was loaded.");
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
                if (Diagnostics.VerboseLogging.Value)
                    Plugin.Logger.LogInfo("No eligible civilian building shells around airbase " + GetAirbaseName(airbase));
                return;
            }

            int generation = generations.TryGetValue(key, out int old) ? old + 1 : 1;
            generations[key] = generation;
            uint seed = Deterministic.Hash(
                (int)Deterministic.HashString(GetAirbaseName(airbase)),
                owner.GetInstanceID(), generation);
            Shuffle(candidates, seed);
            int count = Mathf.Clamp(
                Mathf.Max(Urban.GarrisonsPerZone.Value, capture.MinimumCount), 0, candidates.Count);

            var record = new GarrisonRecord { Owner = owner };
            records[key] = record;
            for (int slot = 0; slot < count; slot++)
            {
                GameObject shell = candidates[slot];
                if (shell == null) continue;
                Building shellBuilding = shell.GetComponentInParent<Building>();
                Bounds shellBounds = GetShellBounds(shell);
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

                GarrisonOccupancy.Set(shell, owner);
                GarrisonVisual.Apply(spawned);
                record.Defenses.Add(spawned);
                record.Shells.Add(shell);
            }

            Plugin.Logger.LogInfo($"Occupied {record.Defenses.Count} building(s) around {GetAirbaseName(airbase)} for {owner} using hidden {defense.jsonKey} defense proxies.");
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
                    bool shellAlive = shell != null &&
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
            if (!records.TryGetValue(key, out GarrisonRecord record)) return;
            for (int i = 0; i < record.Defenses.Count; i++) DestroyNetworked(record.Defenses[i]);
            for (int i = 0; i < record.Shells.Count; i++)
            {
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

        private BuildingDefinition ResolveDefenseDefinition()
        {
            if (cachedDefenseDefinition != null) return cachedDefenseDefinition;
            if (Encyclopedia.i == null || Encyclopedia.i.buildings == null) return null;
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

            BuildingDefinition smallest = null;
            float smallestArea = float.MaxValue;
            for (int i = 0; i < defs.Count; i++)
            {
                BuildingDefinition definition = defs[i];
                if ((definition.unitName?.IndexOf("bunker", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (definition.jsonKey?.IndexOf("bunker", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                    return cachedDefenseDefinition = definition;
                float area = Mathf.Max(1f, definition.width) * Mathf.Max(1f, definition.length);
                if (area < smallestArea) { smallestArea = area; smallest = definition; }
            }
            return cachedDefenseDefinition = smallest;
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

            if (Diagnostics.VerboseLogging.Value)
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
            if (Diagnostics.VerboseLogging.Value)
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
    }
}
