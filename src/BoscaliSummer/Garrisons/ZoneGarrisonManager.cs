using System;
using System.Collections.Generic;
using System.Linq;
using BoscaliSummer.Core;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    internal sealed class ZoneGarrisonManager : MonoBehaviour
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
        }

        public static ZoneGarrisonManager Instance { get; private set; }

        private readonly List<PendingCapture> pending = new List<PendingCapture>();
        private readonly Dictionary<int, GarrisonRecord> records = new Dictionary<int, GarrisonRecord>();
        private readonly Dictionary<int, int> generations = new Dictionary<int, int>();
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
            missingDefinitionReported = false;
            definitionInventoryReported = false;
            initialScanComplete = false;
            initialScanAt = Time.unscaledTime + 3f;
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
            int count = Mathf.Clamp(min + (int)(seed % (uint)Mathf.Max(1, max - min + 1)), 0, candidates.Count);

            var record = new GarrisonRecord { Airbase = airbase, Owner = owner };
            records[key] = record;
            for (int slot = 0; slot < count; slot++)
            {
                GameObject shell = candidates[slot];
                if (shell == null) continue;
                Building shellBuilding = shell.GetComponentInParent<Building>();
                Bounds shellBounds = CalculateBounds(shell);
                Bounds prefabBounds = CalculateBounds(defense.unitPrefab);
                Vector3 local = shellBounds.center;
                local.y = shellBounds.max.y - prefabBounds.min.y + 0.15f;
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

            Plugin.Logger.LogInfo($"Occupied {record.Defenses.Count} building(s) around {GetAirbaseName(airbase)} for {owner} using logic-only {defense.jsonKey} proxies.");
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
                    if ((shell != null && (shellBuilding == null || !shellBuilding.disabled)) && record.Defenses[i] != null)
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
            if (Encyclopedia.i == null || Encyclopedia.i.buildings == null) return null;
            string requested = Plugin.Settings.GarrisonDefinitionKey?.Trim();
            if (!string.IsNullOrEmpty(requested))
                return Encyclopedia.i.buildings.FirstOrDefault(x => x != null && x.jsonKey == requested && x.buildingType == BuildingType.DEF);

            BuildingDefinition[] defs = Encyclopedia.i.buildings
                .Where(x => x != null && x.buildingType == BuildingType.DEF && x.unitPrefab != null)
                .ToArray();
            if (!definitionInventoryReported)
            {
                definitionInventoryReported = true;
                Plugin.Logger.LogInfo("Loaded DEF building definitions: " +
                    (defs.Length == 0
                        ? "none"
                        : string.Join(", ", defs.Select(x => x.jsonKey + " (" + x.unitName + ")").ToArray())));
            }
            return defs.FirstOrDefault(x =>
                       (x.unitName?.IndexOf("bunker", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                       (x.jsonKey?.IndexOf("bunker", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                   ?? defs.OrderBy(x => Mathf.Max(1f, x.width) * Mathf.Max(1f, x.length)).FirstOrDefault();
        }

        private static List<GameObject> FindCandidates(Airbase airbase)
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

        private static void GatherCandidates(
            Airbase airbase, Vector3 center, float radius, HashSet<int> seen, List<GameObject> result)
        {
            MapBuilding[] all = Resources.FindObjectsOfTypeAll<MapBuilding>();
            for (int i = 0; i < all.Length; i++)
            {
                MapBuilding building = all[i];
                if (building != null) TryAddCandidate(building.gameObject, airbase, center, radius, seen, result);
            }

            // Some mission-authored towns use network Building instances rather than
            // procedural MapBuilding shells. Admit only unruined CIV definitions.
            Building[] networkBuildings = Resources.FindObjectsOfTypeAll<Building>();
            for (int i = 0; i < networkBuildings.Length; i++)
            {
                Building building = networkBuildings[i];
                if (building == null || building.disabled) continue;
                // Do not steal a civilian shell that is already owned by a faction;
                // NetworkHQ is also the occupancy state used by fire burnout handling.
                if (building.NetworkHQ != null) continue;
                BuildingDefinition definition = building.definition as BuildingDefinition;
                if (definition == null || definition.buildingType != BuildingType.CIV) continue;
                if (!string.IsNullOrEmpty(building.NetworkUniqueName) &&
                    building.NetworkUniqueName.StartsWith(NamePrefix, StringComparison.Ordinal)) continue;
                TryAddCandidate(building.gameObject, airbase, center, radius, seen, result);
            }
        }

        private static void TryAddCandidate(
            GameObject shell, Airbase airbase, Vector3 center, float radius,
            HashSet<int> seen, List<GameObject> result)
        {
            if (shell == null || !shell.scene.IsValid() || shell.scene != airbase.gameObject.scene) return;
            int id = shell.GetInstanceID();
            if (seen.Contains(id) || IsCriticalName(shell.name)) return;
            Bounds bounds = CalculateBounds(shell);
            Vector3 delta = bounds.center - center;
            delta.y = 0f;
            if (delta.sqrMagnitude > radius * radius) return;
            if (bounds.size.x < 6f || bounds.size.z < 6f || bounds.size.y < 3f) return;
            seen.Add(id);
            result.Add(shell);
        }

        private static bool IsCriticalName(string name)
        {
            string lower = (name ?? string.Empty).ToLowerInvariant();
            string[] blocked = { "hangar", "radar", "factory", "depot", "ammo", "tower", "runway", "fuel" };
            for (int i = 0; i < blocked.Length; i++) if (lower.Contains(blocked[i])) return true;
            return false;
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one * 4f);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
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
