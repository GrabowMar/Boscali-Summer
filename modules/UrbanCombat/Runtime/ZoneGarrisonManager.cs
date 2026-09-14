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
            BuildingDefinition defense = RooftopPlacement.ResolveDefinition(0);
            if (defense == null || defense.unitPrefab == null) return false;
            int key = airbase.GetInstanceID();
            int floor = records.TryGetValue(key, out GarrisonRecord existing)
                ? existing.Defenses.Count + 1
                : 1;
            if (floor > RooftopPlacement.MaxPerZone || CountDefenses() >= RooftopPlacement.MaxBuildings)
                return false;
            List<GameObject> candidates = FindCandidates(airbase);
            if (candidates.Count == 0) { RebuildShellCatalogue(); candidates = FindCandidates(airbase); }
            for (int i = 0; i < Mathf.Min(candidates.Count, 128); i++)
                if (TryOccupyBuilding(candidates[i], owner, airbase)) return true;
            return false;
        }

        public bool TryOccupyBuilding(GameObject shell, FactionHQ owner, Airbase airbase)
        {
            if (!GameAccess.IsServer() || shell == null || owner == null ||
                NetworkSceneSingleton<Spawner>.i == null || GarrisonOccupancy.IsOccupied(shell))
                return false;

            int key = airbase != null ? airbase.GetInstanceID() : shell.scene.handle;
            if (records.TryGetValue(key, out GarrisonRecord previous) && previous.Owner != owner)
                return false;
            int slot = previous?.Defenses.Count ?? 0;
            if (slot >= RooftopPlacement.MaxPerZone || CountDefenses() >= RooftopPlacement.MaxBuildings)
                return false;
            Bounds bounds = GetShellBounds(shell);
            BuildingDefinition defense = RooftopPlacement.ResolveDefinition(slot);
            if (defense == null || !RooftopPlacement.TryPlace(shell, bounds, defense,
                out Vector3 position, out Quaternion rotation, out Vector4 roofExtents)) return false;
            int generation = generations.TryGetValue(key, out int current) ? current + 1 : 1;
            generations[key] = generation;
            Building core = NetworkSceneSingleton<Spawner>.i.SpawnBuilding(
                defense.unitPrefab, position.ToGlobalPosition(), rotation, owner, airbase,
                RooftopPlacement.BuildMarkerName(
                    $"{RooftopPlacement.NamePrefix}Assault:{shell.GetInstanceID()}:{generation}:{slot}",
                    roofExtents), false, null);
            if (core == null) return false;
            if (previous == null)
            {
                previous = new GarrisonRecord { Owner = owner };
                records[key] = previous;
            }
            GarrisonVisual.Apply(core);
            previous.Defenses.Add(core);
            previous.Shells.Add(shell);

            Building shellBuilding = shell.GetComponentInParent<Building>();
            if (shellBuilding != null && !shellBuilding.disabled) shellBuilding.NetworkHQ = owner;
            GarrisonOccupancy.Set(shell, owner);
            Plugin.Logger.LogInfo($"[Air Assault] Occupied {shell.name} with visible rooftop {defense.jsonKey}.");
            return true;
        }

        public bool TryDeployEncampment(Vector3 position, FactionHQ owner, Airbase airbase, int troopCount)
        {
            return GameAccess.IsServer() && owner != null &&
                InfantryEncampmentBuilder.DeployOrReinforce(position, owner, airbase, troopCount);
        }

        private readonly List<PendingCapture> pending = new List<PendingCapture>();
        private readonly Dictionary<int, GarrisonRecord> records = new Dictionary<int, GarrisonRecord>();
        private readonly Dictionary<int, int> generations = new Dictionary<int, int>();
        private readonly List<GameObject> shellCatalogue = new List<GameObject>(512);
        private readonly Dictionary<int, Bounds> shellBounds = new Dictionary<int, Bounds>(512);
        private float nextLifecycleCheck;
        private bool missingDefinitionReported;
        private bool initialScanComplete;
        private float initialScanAt;

        private void Awake() => Instance = this;
        private void OnDestroy() { ResetForScene(); if (Instance == this) Instance = null; }

        public void ResetForScene()
        {
            pending.Clear();
            foreach (int key in new List<int>(records.Keys)) ClearRecord(key);
            records.Clear();
            generations.Clear();
            shellCatalogue.Clear();
            shellBounds.Clear();
            missingDefinitionReported = false;
            initialScanComplete = false;
            initialScanAt = Time.unscaledTime + 3f;

            InfantryEncampmentBuilder.ResetForScene();
            GarrisonOccupancy.Reset();
        }

        public void ScheduleCapture(Airbase airbase, FactionHQ owner)
        {
            if (!GameAccess.IsServer() || airbase == null) return;
            int key = airbase.GetInstanceID();
            if (records.TryGetValue(key, out GarrisonRecord current) && current.Owner == owner) return;

            for (int i = pending.Count - 1; i >= 0; i--)
                if (pending[i].Airbase == airbase) pending.RemoveAt(i);
            if (pending.Count >= 128) return;
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

            BuildingDefinition defense = RooftopPlacement.ResolveDefinition(0);
            if (defense == null || defense.unitPrefab == null || NetworkSceneSingleton<Spawner>.i == null)
            {
                if (capture.Attempts < 4) { Retry(capture); return; }
                if (!missingDefinitionReported)
                {
                    missingDefinitionReported = true;
                    Plugin.Logger.LogWarning("Garrisons disabled for this scene: no usable vanilla MG rooftop emplacement definition was loaded.");
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
            int count = Mathf.Clamp(Urban.GarrisonsPerZone.Value,
                0, Mathf.Min(RooftopPlacement.MaxPerZone, RooftopPlacement.MaxBuildings - CountDefenses()));
            var record = new GarrisonRecord { Owner = owner };
            records[key] = record;
            // Try the remaining catalogue candidates when a roof is too small or stepped.
            for (int candidate = 0; candidate < Mathf.Min(candidates.Count, 128) && record.Defenses.Count < count; candidate++)
            {
                GameObject shell = candidates[candidate];
                if (shell == null || GarrisonOccupancy.IsOccupied(shell)) continue;
                int slot = record.Defenses.Count;
                BuildingDefinition roofDefense = RooftopPlacement.ResolveDefinition(slot);
                Bounds bounds = GetShellBounds(shell);
                if (roofDefense == null || !RooftopPlacement.TryPlace(shell, bounds, roofDefense,
                    out Vector3 position, out Quaternion rotation, out Vector4 roofExtents)) continue;
                Building spawned = NetworkSceneSingleton<Spawner>.i.SpawnBuilding(
                    roofDefense.unitPrefab, position.ToGlobalPosition(), rotation, owner, airbase,
                    RooftopPlacement.BuildMarkerName(
                        RooftopPlacement.NamePrefix + Sanitize(GetAirbaseName(airbase)) + ":" + generation + ":" + slot,
                        roofExtents),
                    false, null);
                if (spawned == null) continue;
                Building shellBuilding = shell.GetComponentInParent<Building>();
                if (shellBuilding != null && !shellBuilding.disabled) shellBuilding.NetworkHQ = owner;
                GarrisonOccupancy.Set(shell, owner);
                GarrisonVisual.Apply(spawned);
                record.Defenses.Add(spawned);
                record.Shells.Add(shell);
            }
            Plugin.Logger.LogInfo($"Occupied {record.Defenses.Count} building(s) around {GetAirbaseName(airbase)} for {owner} with visible MG/AT/AA rooftop nests (requested {count}).");
        }

        private int CountDefenses()
        {
            int count = 0;
            foreach (GarrisonRecord record in records.Values) count += record.Defenses.Count;
            return count;
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
                for (int i = record.Defenses.Count - 1; i >= 0; i--)
                {
                    GameObject shell = record.Shells[i];
                    Building shellBuilding = shell != null ? shell.GetComponentInParent<Building>() : null;
                    Building defense = record.Defenses[i];
                    if (shell != null && shell.activeInHierarchy &&
                        (shellBuilding == null || !shellBuilding.disabled) &&
                        defense != null && !defense.disabled && defense.NetworkHQ == record.Owner)
                        continue;
                    DestroyNetworked(defense);
                    if (shellBuilding != null && shellBuilding.NetworkHQ == record.Owner)
                        shellBuilding.NetworkHQ = null;
                    GarrisonOccupancy.Clear(shell, record.Owner);
                    record.Defenses.RemoveAt(i);
                    record.Shells.RemoveAt(i);
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
            if (building == null || !GameAccess.IsServer() ||
                NetworkManagerNuclearOption.i?.ServerObjectManager == null) return;
            NetworkManagerNuclearOption.i.ServerObjectManager.Destroy(building.Identity, true);
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

        internal bool IsMissionRooftopAvailable(int id)
        {
            for (int i = 0; i < Math.Min(512, shellCatalogue.Count); i++)
            {
                GameObject shell = shellCatalogue[i];
                if (shell == null || shell.GetInstanceID() != id) continue;
                Building building = shell.GetComponentInParent<Building>();
                return shell.activeInHierarchy && !GarrisonOccupancy.IsOccupied(shell) &&
                    (building == null || !building.disabled);
            }
            return false;
        }

        internal bool TryMissionRooftop(float x, float z, out int id, out float roofX, out float roofZ)
        {
            id = 0; roofX = roofZ = 0f;
            if (!GameAccess.IsServer()) return false;
            float best = 2500f * 2500f;
            for (int i = 0; i < Math.Min(512, shellCatalogue.Count); i++)
            {
                GameObject shell = shellCatalogue[i];
                if (shell == null || !shell.activeInHierarchy || GarrisonOccupancy.IsOccupied(shell) || IsCriticalName(shell.name)) continue;
                Building building = shell.GetComponentInParent<Building>();
                if (building != null && (building.disabled || building.NetworkHQ != null)) continue;
                Bounds bounds = GetShellBounds(shell);
                if (bounds.size.x < 10f || bounds.size.z < 10f || bounds.size.y < 3f) continue;
                Vector3 global = bounds.center.ToGlobalPosition().AsVector3();
                float distance = (global.x - x) * (global.x - x) + (global.z - z) * (global.z - z);
                if (distance >= best || !Physics.Raycast(bounds.center + Vector3.up * (bounds.extents.y + 20f), Vector3.down,
                    out RaycastHit hit, 60f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore) ||
                    hit.normal.y < 0.95f || hit.point.y <= Datum.LocalSeaY + 1f ||
                    !(hit.collider.transform == shell.transform || hit.collider.transform.IsChildOf(shell.transform))) continue;
                best = distance; id = shell.GetInstanceID(); roofX = global.x; roofZ = global.z;
            }
            return id != 0;
        }

        private void TryAddCandidate(
            GameObject shell, Airbase airbase, Vector3 center, float radius,
            HashSet<int> seen, List<GameObject> result)
        {
            if (shell == null || !shell.activeInHierarchy || GarrisonOccupancy.IsOccupied(shell) ||
                !shell.scene.IsValid() || shell.scene != airbase.gameObject.scene) return;
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
                // Static shells translate with Datum's floating origin. Cache the
                // centre relative to the shell so later captures probe its current roof.
                bounds.center -= shell.transform.position;
                shellBounds[id] = bounds;
            }
            bounds.center += shell.transform.position;
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
                if (renderer == null || renderer is ParticleSystemRenderer || !renderer.gameObject.activeInHierarchy) continue;
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
