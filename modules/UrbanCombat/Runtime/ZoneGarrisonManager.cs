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

        private sealed class CaptureJob
        {
            public Airbase Airbase;
            public FactionHQ Owner;
            public int Key;
            public int Tier;
            public int Count;
            public int Generation;
            public string ZoneName;
            public string OwnerName;
            public int Seed;
            public List<GameObject> Candidates;
            public int Next;
            public int Tries;
            public int OccupiedSkips;
            public int RoofSkips;
            public int SpawnSkips;
            public GarrisonRecord Record;
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

        bool IZoneFortificationService.Available => Urban.GarrisonsEnabled.Value;

        internal static bool SiegeActive =>
            Urban.GarrisonsEnabled.Value && Urban.SiegeEnabled.Value;

        internal static bool ArmorActive => SiegeActive && Urban.SiegeArmorBonus.Value;

        internal static float SiegeScale => Urban.SiegeDefenseScale.Value;

        internal static int TierFor(Airbase airbase)
        {
            if (airbase == null || Instance == null || !SiegeActive) return 0;
            if (!GameAccess.IsServer())
                return NestRegistry.TierForZone(GarrisonMarkerInfo.SanitizeZone(GetAirbaseName(airbase)));
            return Instance.urbanTiers.TryGetValue(airbase.GetInstanceID(), out int tier) ? tier : 0;
        }

        internal static int IntactNestsFor(Airbase airbase)
        {
            if (airbase == null || Instance == null || !SiegeActive) return 0;
            if (!GameAccess.IsServer())
            {
                if (airbase.CurrentHQ == null) return 0;
                return NestRegistry.IntactCount(
                    GarrisonMarkerInfo.SanitizeZone(GetAirbaseName(airbase)), airbase.CurrentHQ);
            }
            if (!Instance.records.TryGetValue(airbase.GetInstanceID(), out GarrisonRecord record)) return 0;
            if (record.Owner == null || record.Owner != airbase.CurrentHQ) return 0;
            int intact = 0;
            for (int i = 0; i < record.Defenses.Count; i++)
            {
                Building defense = record.Defenses[i];
                if (defense == null || defense.disabled || defense.NetworkHQ != record.Owner) continue;
                intact++;
            }
            return intact;
        }

        internal static void NoteShellDamage(MapBuilding shell)
        {
            if (shell == null || Instance == null || !Urban.GarrisonsEnabled.Value) return;
            GameObject go = shell.gameObject;
            if (go == null || !Instance.shellStates.TryGetValue(go, out ShellState state)) return;
            float frac = UrbanRuinMath.DamageFraction(state.MaxHp, GameAccess.GetMapBuildingHitPoints(shell));
            int stage = UrbanRuinMath.DamageStage(frac);
            if (stage == state.LastStage) return;
            state.LastStage = stage;
            if (state.Defense == null) return;
            OccupiedBuildingMarking marking = state.Defense.GetComponent<OccupiedBuildingMarking>();
            if (marking != null) marking.SetShellDamage(frac);
        }

        /// <summary>
        /// Cheap candidacy probe so the damage patch checks siege strongpoint status before
        /// doing the armour maths: siegeless scenes and unoccupied shells never count.
        /// </summary>
        internal static bool MightBeStrongpointHit(MapBuilding shell)
        {
            if (shell == null || Instance == null || !SiegeActive) return false;
            GameObject go = shell.gameObject;
            return go != null && GarrisonOccupancy.IsOccupied(go);
        }

        /// <summary>
        /// Server-side strongpoint verdict for one TakeDamage call. Returns true when
        /// vanilla must run (unoccupied shells, siege off, final and overkill hits),
        /// false when the hit was counted or ignored and vanilla must be skipped.
        /// </summary>
        internal static bool ApplyStrongpointHit(MapBuilding shell, float blastTerm, float total)
        {
            if (shell == null || Instance == null || !SiegeActive) return true;
            GameObject go = shell.gameObject;
            if (go == null || !GarrisonOccupancy.IsOccupied(go)) return true;
            int id = go.GetInstanceID();
            if (!Instance.strongpoints.TryGetValue(id, out StrongpointRecord record))
            {
                if (Instance.strongpoints.Count >= 128) Instance.strongpoints.Clear();
                record = new StrongpointRecord();
                Instance.strongpoints[id] = record;
            }
            float now = Time.timeSinceLevelLoad;
            StrongpointHitPolicy.Verdict verdict = StrongpointHitPolicy.Decide(
                record.Hits, record.LastHitAt, now, blastTerm, total);
            if (verdict == StrongpointHitPolicy.Verdict.Final ||
                verdict == StrongpointHitPolicy.Verdict.Overkill)
            {
                GameAccess.SetMapBuildingHitPoints(shell, 0f);
                return true;
            }
            if (verdict == StrongpointHitPolicy.Verdict.Count)
            {
                record.Hits++;
                record.LastHitAt = now;
                GameAccess.SetMapBuildingHitPoints(
                    shell, StrongpointHitPolicy.SteppedHitPoints(record.Hits));
                TickCarrier(go);
                if (Diagnostics.VerboseLogging.Value)
                    Plugin.Logger.LogInfo($"[Strongpoint] {go.name} took counted hit {record.Hits}.");
            }
            return false;
        }

        /// <summary>
        /// Ticks the nest's dugout part so every peer reads the stage from replicated
        /// part HP. Nests without a dugout part carry no stage; clients show stage 0.
        /// </summary>
        private static void TickCarrier(GameObject shell)
        {
            if (shell == null || Instance == null) return;
            if (!Instance.shellStates.TryGetValue(shell, out ShellState state)) return;
            Building defense = state.Defense;
            if (defense == null || defense.disabled) return;
            UnitPart dugout = null;
            Transform child = defense.transform.Find("dugout");
            if (child != null) dugout = child.GetComponent<UnitPart>();
            if (dugout == null)
            {
                UnitPart[] parts = defense.GetComponentsInChildren<UnitPart>(true);
                for (int i = 0; i < parts.Length; i++)
                    if (parts[i] != null && parts[i].name.IndexOf(
                            "dugout", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        dugout = parts[i];
                        break;
                    }
            }
            if (dugout == null) return;
            dugout.TakeDamage(0f, 0f, 1f, 0f,
                StrongpointHitPolicy.CarrierTickDamage, PersistentID.None);
        }

        internal static void NoteShellDestroyed(MapBuilding shell)
        {
            if (shell == null || Instance == null || !GameAccess.IsServer()) return;
            GameObject go = shell.gameObject;
            if (go == null || !Instance.shellStates.TryGetValue(go, out ShellState state)) return;
            Instance.shellStates.Remove(go);
            Instance.shellBounds.Remove(go.GetInstanceID());
            Instance.strongpoints.Remove(go.GetInstanceID());
            if (!Instance.records.TryGetValue(state.Key, out GarrisonRecord record)) return;
            int index = record.Shells.IndexOf(go);
            if (index < 0) return;
            Building defense = index < record.Defenses.Count ? record.Defenses[index] : null;
            DestroyNetworked(defense);
            GarrisonOccupancy.Clear(go, record.Owner);
            if (index < record.Defenses.Count) record.Defenses.RemoveAt(index);
            record.Shells.RemoveAt(index);
            if (Diagnostics.VerboseLogging.Value)
                Plugin.Logger.LogInfo("[GARRISON] Nest lost with destroyed shell " + go.name + ".");
        }


        private void SeedShellState(GameObject shell, Building defense, int key)
        {
            if (shell == null) return;
            MapBuilding map = shell.GetComponent<MapBuilding>();
            if (map == null) return;
            shellStates[shell] = new ShellState
            {
                MaxHp = Mathf.Max(1f, GameAccess.GetMapBuildingHitPoints(map)),
                Defense = defense,
                Map = map,
                Key = key,
                LastStage = UrbanRuinMath.StageIntact
            };
        }

        internal static int TierAt(Vector3 position)
        {
            if (Instance == null || !ArmorActive) return 0;
            Instance.RefreshUrbanIndex();
            int best = 0;
            List<UrbanZone> index = Instance.urbanIndex;
            for (int i = 0; i < index.Count; i++)
            {
                if (index[i].Tier <= best) continue;
                Vector3 delta = index[i].Center - position;
                delta.y = 0f;
                if (delta.sqrMagnitude <= index[i].RadiusSq) best = index[i].Tier;
            }
            return best;
        }

        private void RefreshUrbanIndex()
        {
            if (Time.unscaledTime < urbanIndexAt) return;
            urbanIndexAt = Time.unscaledTime + 30f;
            urbanIndex.Clear();
            IEnumerable<Airbase> airbases = (FactionRegistry.airbaseLookup != null && FactionRegistry.airbaseLookup.Count > 0)
                ? (IEnumerable<Airbase>)FactionRegistry.airbaseLookup.Values
                : Resources.FindObjectsOfTypeAll<Airbase>();
            if (airbases == null) return;
            foreach (Airbase airbase in airbases)
            {
                if (airbase == null || !airbase.gameObject.scene.IsValid() || airbase.AttachedAirbase) continue;
                if (!urbanTiers.TryGetValue(airbase.GetInstanceID(), out int tier) || tier < 1) continue;
                float range = ((ICapturable)airbase).CaptureRange;
                if (range < 1f || urbanIndex.Count >= 64) continue;
                Vector3 center = airbase.center != null ? airbase.center.position : airbase.transform.position;
                urbanIndex.Add(new UrbanZone { Center = center, RadiusSq = range * range, Tier = tier });
            }
        }

        bool IZoneFortificationService.TryFortify(
            Airbase airbase, FactionHQ owner, NuclearOption.Networking.Player requester, int shells)
        {
            if (!GameAccess.IsServer() || airbase == null || owner == null || requester == null ||
                airbase.AttachedAirbase || airbase.CurrentHQ != owner || requester.HQ != owner)
                return false;

            if (!Urban.GarrisonsEnabled.Value || NetworkSceneSingleton<Spawner>.i == null)
                return false;
            BuildingDefinition defense = RooftopPlacement.ResolveDefinition(0, 0, 0);
            if (defense == null || defense.unitPrefab == null) return false;
            List<GameObject> candidates = FindCandidates(airbase, out _);
            if (candidates.Count == 0 && RefreshCatalogue()) candidates = FindCandidates(airbase, out _);
            // Base-of-operations doctrine raises how many shells one order secures;
            // Occupy still enforces the zone and theater ceilings.
            int placed = 0;
            int wanted = Mathf.Clamp(shells, 1, RooftopPlacement.MaxPerZone);
            for (int i = 0, tries = 0; i < Mathf.Min(candidates.Count, 128) && placed < wanted && tries < MaxRoofTries; i++)
            {
                if (!FitsNest(candidates[i])) continue;
                tries++;
                if (Occupy(candidates[i], owner, airbase, airbase.GetInstanceID(), "Assault:")) placed++;
            }
            return placed > 0;
        }

        /// <summary>SPEC OPS seizures hold at most this many buildings theatre-wide, inside the 96 ceiling.</summary>
        private const int MaxSeized = 24;

        /// <summary>
        /// A SPEC OPS seizure: the unowned civilian shells nearest the point, each in its own record
        /// (keyed by the shell), so a hostile zone's garrison never blocks it and a capture never
        /// clears it. The ordinary lifecycle check releases a seized building like any other.
        /// </summary>
        int IZoneFortificationService.TrySeize(float x, float z, float radius, FactionHQ owner, int shells)
        {
            if (!GameAccess.IsServer() || owner == null || float.IsNaN(x) || float.IsNaN(z) ||
                !Urban.GarrisonsEnabled.Value || NetworkSceneSingleton<Spawner>.i == null) return 0;
            if (shellCatalogue.Count == 0) RefreshCatalogue();
            float limit = Mathf.Clamp(radius, 100f, 3000f);
            seizeCandidates.Clear();
            for (int i = 0; i < Math.Min(shellCatalogue.Count, 4096); i++)
            {
                GameObject shell = shellCatalogue[i];
                if (shell == null || !shell.activeInHierarchy || GarrisonOccupancy.IsOccupied(shell) ||
                    IsCriticalName(shell.name)) continue;
                Building building = shell.GetComponentInParent<Building>();
                if (building != null && (building.disabled || building.NetworkHQ != null)) continue;
                Bounds bounds = GetShellBounds(shell);
                if (bounds.size.x < RooftopPlacement.MinRoofSpan || bounds.size.z < RooftopPlacement.MinRoofSpan ||
                    bounds.size.y < 3f) continue;
                Vector3 global = bounds.center.ToGlobalPosition().AsVector3();
                float distance = (global.x - x) * (global.x - x) + (global.z - z) * (global.z - z);
                if (distance <= limit * limit) seizeCandidates.Add(new KeyValuePair<float, GameObject>(distance, shell));
            }
            seizeCandidates.Sort((a, b) => a.Key.CompareTo(b.Key));
            int wanted = Mathf.Clamp(shells, 1, 4);
            int placed = 0;
            for (int i = 0; i < Math.Min(seizeCandidates.Count, 64) && placed < wanted && CountSeized() < MaxSeized; i++)
            {
                GameObject shell = seizeCandidates[i].Value;
                int key = shell.GetInstanceID();
                if (Occupy(shell, owner, null, key, "Seize:")) { seizedKeys.Add(key); placed++; }
            }
            seizeCandidates.Clear();
            if (placed > 0)
                Plugin.Logger.LogInfo($"[Urban Combat] SPEC OPS seized {placed} building(s) near {x:0} / {z:0} for {owner}.");
            return placed;
        }

        private int CountSeized()
        {
            int count = 0;
            foreach (int key in seizedKeys)
                if (records.TryGetValue(key, out GarrisonRecord record)) count += record.Defenses.Count;
            return count;
        }

        /// <summary>
        /// An air-assault occupation: its own record keyed by the shell, like a seizure, and no
        /// airbase membership, so vanilla never re-owns the nest and no zone floor counts it.
        /// </summary>
        public bool TryOccupyBuilding(GameObject shell, FactionHQ owner)
        {
            if (shell == null || !Urban.GarrisonsEnabled.Value) return false;
            return Occupy(shell, owner, null, shell.GetInstanceID(), "Assault:");
        }

        private bool Occupy(GameObject shell, FactionHQ owner, Airbase airbase, int key, string tag)
        {
            if (!GameAccess.IsServer() || shell == null || owner == null ||
                NetworkSceneSingleton<Spawner>.i == null || GarrisonOccupancy.IsOccupied(shell))
                return false;

            if (records.TryGetValue(key, out GarrisonRecord previous) && previous.Owner != owner)
                return false;
            int slot = previous?.Defenses.Count ?? 0;
            if (slot >= RooftopPlacement.MaxPerZone || CountDefenses() >= RooftopPlacement.MaxBuildings)
                return false;
            Bounds bounds = GetShellBounds(shell);
            int seed = shell.GetInstanceID() % 3;
            int tier = 0;
            if (airbase != null)
            {
                seed = GarrisonComposition.ZoneSeed(GetAirbaseName(airbase));
                urbanTiers.TryGetValue(airbase.GetInstanceID(), out tier);
            }
            BuildingDefinition defense = RooftopPlacement.ResolveDefinition(slot, seed, tier);
            if (defense == null || !RooftopPlacement.TryPlace(shell, bounds, defense,
                out Vector3 position, out Quaternion rotation, out Vector4 roofExtents)) return false;
            int generation = generations.TryGetValue(key, out int current) ? current + 1 : 1;
            generations[key] = generation;
            Building core = NetworkSceneSingleton<Spawner>.i.SpawnBuilding(
                defense.unitPrefab, position.ToGlobalPosition(), rotation, owner, airbase,
                RooftopPlacement.BuildMarkerName(
                    $"{RooftopPlacement.NamePrefix}{tag}{shell.GetInstanceID()}:{generation}:{slot}",
                    roofExtents), false, null);
            if (core == null) return false;
            NestRegistry.Add(core);
            if (previous == null)
            {
                previous = new GarrisonRecord { Owner = owner };
                records[key] = previous;
            }
            GarrisonVisual.Apply(core);
            previous.Defenses.Add(core);
            previous.Shells.Add(shell);
            SeedShellState(shell, core, key);
            if (!nestPeaks.TryGetValue(key, out int peak) || previous.Defenses.Count > peak)
                nestPeaks[key] = previous.Defenses.Count;

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
        private readonly Queue<CaptureJob> captureJobs = new Queue<CaptureJob>();
        private const int MaxCaptureJobs = 16;
        private const int RoofTriesPerFrame = 6;
        private const int JobStepsPerFrame = 32;
        private readonly List<KeyValuePair<float, GameObject>> seizeCandidates = new List<KeyValuePair<float, GameObject>>(64);
        private readonly HashSet<int> seizedKeys = new HashSet<int>();
        private readonly HashSet<int> barrenZones = new HashSet<int>();
        private readonly Dictionary<int, int> nestPeaks = new Dictionary<int, int>();
        private readonly Dictionary<int, GarrisonRecord> records = new Dictionary<int, GarrisonRecord>();
        private readonly Dictionary<int, int> generations = new Dictionary<int, int>();
        private readonly List<GameObject> shellCatalogue = new List<GameObject>(512);
        private readonly Dictionary<int, Bounds> shellBounds = new Dictionary<int, Bounds>(512);
        private readonly Dictionary<int, int> urbanTiers = new Dictionary<int, int>();
        private readonly List<UrbanZone> urbanIndex = new List<UrbanZone>(32);
        private float urbanIndexAt;

        private struct UrbanZone
        {
            public Vector3 Center;
            public float RadiusSq;
            public int Tier;
        }

        private sealed class ShellState
        {
            public float MaxHp;
            public Building Defense;
            public MapBuilding Map;
            public int Key;
            public int LastStage;
        }

        private sealed class StrongpointRecord
        {
            public int Hits;
            public float LastHitAt = -10f;
        }

        private readonly Dictionary<GameObject, ShellState> shellStates = new Dictionary<GameObject, ShellState>();
        private readonly Dictionary<int, StrongpointRecord> strongpoints = new Dictionary<int, StrongpointRecord>();
        private float nextLifecycleCheck;
        private bool missingDefinitionReported;
        private bool initialScanComplete;
        private float initialScanAt;
        private int initialScanTries;
        private float nextCatalogueAt;
        private readonly List<int> emptyKeys = new List<int>();

        /// <summary>Each TryPlace costs up to ~440 raycasts; this bounds one capture's frame.</summary>
        private const int MaxRoofTries = 48;
        private const int MaxCatalogue = 4096;

        private void Awake() => Instance = this;
        private void OnDestroy() { ResetForScene(); if (Instance == this) Instance = null; }

        public void ResetForScene()
        {
            pending.Clear();
            captureJobs.Clear();
            barrenZones.Clear();
            nestPeaks.Clear();
            shellStates.Clear();
            strongpoints.Clear();
            NestRegistry.Reset();
            try
            {
                foreach (int key in new List<int>(records.Keys)) ClearRecord(key);
            }
            finally
            {
                // A teardown fault still reaches SceneLifecycle's log, but must not leave the
                // previous scene's bookkeeping (or a skipped initial scan) behind.
                records.Clear();
                seizedKeys.Clear();
                generations.Clear();
                shellCatalogue.Clear();
                shellBounds.Clear();
                urbanTiers.Clear();
                urbanIndex.Clear();
                urbanIndexAt = 0f;
                missingDefinitionReported = false;
                initialScanComplete = false;
                initialScanAt = Time.unscaledTime + 3f;
                initialScanTries = 0;
                nextCatalogueAt = 0f;

                InfantryEncampmentBuilder.ResetForScene();
                GarrisonOccupancy.Reset();
            }
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
                RebuildShellCatalogue();
                // The map can still be loading at +3 s; retry instead of marking every zone barren.
                if (shellCatalogue.Count == 0 && ++initialScanTries < 10)
                {
                    initialScanAt = Time.unscaledTime + 3f;
                    return;
                }
                initialScanComplete = true;
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

            if (captureJobs.Count > 0)
            {
                int triesBudget = RoofTriesPerFrame;
                int stepsBudget = JobStepsPerFrame;
                while (triesBudget > 0 && stepsBudget > 0 && captureJobs.Count > 0)
                {
                    CaptureJob job = captureJobs.Peek();
                    int triesBefore = job.Tries;
                    if (!StepCaptureJob(job))
                    {
                        captureJobs.Dequeue();
                        FinalizeCaptureJob(job);
                        continue;
                    }
                    stepsBudget--;
                    triesBudget -= job.Tries - triesBefore;
                }
            }

            if (Time.unscaledTime >= nextLifecycleCheck)
            {
                nextLifecycleCheck = Time.unscaledTime + 2f;
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
            if (barrenZones.Contains(key)) return;
            if (owner == null || !Urban.GarrisonsEnabled.Value || airbase.AttachedAirbase) return;

            BuildingDefinition defense = RooftopPlacement.ResolveDefinition(0, 0, 0);
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

            List<GameObject> candidates = FindCandidates(airbase, out bool fallback);
            if (candidates.Count == 0 && capture.Attempts == 0 && RefreshCatalogue())
                candidates = FindCandidates(airbase, out fallback);
            if (candidates.Count == 0)
            {
                if (capture.Attempts < 3) { Retry(capture); return; }
                if (Diagnostics.VerboseLogging.Value)
                    Plugin.Logger.LogInfo("No eligible civilian building shells around airbase " + GetAirbaseName(airbase));
                barrenZones.Add(key);
                return;
            }

            int generation = generations.TryGetValue(key, out int old) ? old + 1 : 1;
            generations[key] = generation;
            // Only shells inside the capture circle make a town; the 2.5 km fallback only finds nest sites.
            int tier = SiegeActive && !fallback ? SiegeMath.ComputeTier(candidates.Count) : 0;
            urbanTiers[key] = tier;
            urbanIndexAt = 0f;
            uint seed = Deterministic.Hash(
                (int)Deterministic.HashString(GetAirbaseName(airbase)),
                owner.GetInstanceID(), generation);
            Shuffle(candidates, seed);
            int count = Mathf.Clamp(
                SiegeMath.EffectiveGarrisons(Urban.GarrisonsPerZone.Value, tier, RooftopPlacement.MaxPerZone),
                0, Mathf.Min(RooftopPlacement.MaxPerZone, RooftopPlacement.MaxBuildings - CountDefenses()));
            var record = new GarrisonRecord { Owner = owner };
            records[key] = record;
            string zoneName = GetAirbaseName(airbase);
            var job = new CaptureJob
            {
                Airbase = airbase,
                Owner = owner,
                Key = key,
                Tier = tier,
                Count = count,
                Generation = generation,
                ZoneName = zoneName,
                Seed = GarrisonComposition.ZoneSeed(zoneName),
                OwnerName = owner.ToString(),
                Candidates = candidates,
                Record = record
            };
            if (captureJobs.Count >= MaxCaptureJobs)
            {
                // Past the cap a zone still garrisons at once rather than being dropped.
                while (StepCaptureJob(job)) { }
                FinalizeCaptureJob(job);
                return;
            }
            captureJobs.Enqueue(job);
        }

        /// <summary>
        /// One candidate of a capture job: cheap skips return at once, a roof search costs
        /// a try. Returns false when the job is done (candidates, count or try budget spent).
        /// </summary>
        private bool StepCaptureJob(CaptureJob job)
        {
            if (job.Next >= Mathf.Min(job.Candidates.Count, 128) ||
                job.Record.Defenses.Count >= job.Count || job.Tries >= MaxRoofTries)
                return false;
            // Try the remaining catalogue candidates when a roof is too small or stepped.
            GameObject shell = job.Candidates[job.Next++];
            if (shell == null || GarrisonOccupancy.IsOccupied(shell)) { job.OccupiedSkips++; return true; }
            if (!FitsNest(shell)) { job.RoofSkips++; return true; }
            job.Tries++;
            int slot = job.Record.Defenses.Count;
            BuildingDefinition roofDefense = RooftopPlacement.ResolveDefinition(slot, job.Seed, job.Tier);
            Bounds bounds = GetShellBounds(shell);
            if (roofDefense == null || !RooftopPlacement.TryPlace(shell, bounds, roofDefense,
                out Vector3 position, out Quaternion rotation, out Vector4 roofExtents)) { job.RoofSkips++; return true; }
            Building spawned = NetworkSceneSingleton<Spawner>.i.SpawnBuilding(
                roofDefense.unitPrefab, position.ToGlobalPosition(), rotation, job.Owner, job.Airbase,
                RooftopPlacement.BuildMarkerName(
                    RooftopPlacement.NamePrefix + Sanitize(job.ZoneName) + ":" + job.Generation + ":" + slot + ":t" + job.Tier,
                    roofExtents),
                false, null);
            if (spawned == null) { job.SpawnSkips++; return true; }
            NestRegistry.Add(spawned);
            Building shellBuilding = shell.GetComponentInParent<Building>();
            if (shellBuilding != null && !shellBuilding.disabled) shellBuilding.NetworkHQ = job.Owner;
            GarrisonOccupancy.Set(shell, job.Owner);
            GarrisonVisual.Apply(spawned);
            job.Record.Defenses.Add(spawned);
            job.Record.Shells.Add(shell);
            SeedShellState(shell, spawned, job.Key);
            return true;
        }

        private void FinalizeCaptureJob(CaptureJob job)
        {
            if (!nestPeaks.TryGetValue(job.Key, out int peak) || job.Record.Defenses.Count > peak)
                nestPeaks[job.Key] = job.Record.Defenses.Count;
            int rejected = job.OccupiedSkips + job.RoofSkips + job.SpawnSkips;
            string rejectionSuffix = string.Empty;
            if (rejected > 0)
            {
                string details = string.Empty;
                if (job.RoofSkips > 0) details += job.RoofSkips + " no roof fit";
                if (job.OccupiedSkips > 0) details += (details.Length > 0 ? ", " : string.Empty) + job.OccupiedSkips + " occupied";
                if (job.SpawnSkips > 0) details += (details.Length > 0 ? ", " : string.Empty) + job.SpawnSkips + " spawn null";
                rejectionSuffix = ", " + rejected + " rejected (" + details + ")";
            }
            Plugin.Logger.LogInfo($"Occupied {job.Record.Defenses.Count} building(s) around {job.ZoneName} for {job.OwnerName} with visible MG/AT/AA rooftop nests (requested {job.Count}, urban tier {job.Tier}){rejectionSuffix}.");
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
            foreach (KeyValuePair<int, GarrisonRecord> entry in records)
            {
                GarrisonRecord record = entry.Value;
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
                    shellStates.Remove(shell);
                    if (shell != null) shellBounds.Remove(shell.GetInstanceID());
                    if (shell != null) strongpoints.Remove(shell.GetInstanceID());
                }
                if (record.Defenses.Count == 0) { emptyKeys.Add(entry.Key); continue; }
                int peak = nestPeaks.TryGetValue(entry.Key, out int stored) ? stored : record.Defenses.Count;
                float fraction = Mathf.Clamp01(record.Defenses.Count / (float)Mathf.Max(1, peak));
                for (int i = 0; i < record.Defenses.Count; i++)
                {
                    Building defense = record.Defenses[i];
                    if (defense == null) continue;
                    OccupiedBuildingMarking marking = defense.GetComponent<OccupiedBuildingMarking>();
                    if (marking == null) continue;
                    marking.SetZoneHealth(fraction);
                }
            }
            // An emptied record must not keep a zone "garrisoned" (ScheduleCapture skips it), nor a
            // dead seizure's shell owned by its first faction for the rest of the scene.
            for (int i = 0; i < emptyKeys.Count; i++)
            {
                records.Remove(emptyKeys[i]);
                nestPeaks.Remove(emptyKeys[i]);
                seizedKeys.Remove(emptyKeys[i]);
            }
            emptyKeys.Clear();
        }

        private void ClearRecord(int key)
        {
            if (!records.TryGetValue(key, out GarrisonRecord record)) return;
            // Detach first so a failed teardown can never pin the record into every later reset.
            records.Remove(key);
            CancelCaptureJob(key);
            nestPeaks.Remove(key);
            for (int i = 0; i < record.Defenses.Count; i++) DestroyNetworked(record.Defenses[i]);
            for (int i = 0; i < record.Shells.Count; i++)
            {
                GameObject shell = record.Shells[i];
                shellStates.Remove(shell);
                if (shell != null) shellBounds.Remove(shell.GetInstanceID());
                if (shell != null) strongpoints.Remove(shell.GetInstanceID());
                // Unity's == (not ?.) catches shells the scene unload already destroyed.
                if (shell == null) continue;
                Building shellBuilding = shell.GetComponentInParent<Building>();
                if (shellBuilding != null && shellBuilding.NetworkHQ == record.Owner)
                    shellBuilding.NetworkHQ = null;
                GarrisonOccupancy.Clear(shell, record.Owner);
            }
        }

        private void CancelCaptureJob(int key)
        {
            if (captureJobs.Count == 0) return;
            int count = captureJobs.Count;
            for (int i = 0; i < count; i++)
            {
                CaptureJob job = captureJobs.Dequeue();
                if (job == null || job.Key == key) continue;
                captureJobs.Enqueue(job);
            }
        }

        private static void DestroyNetworked(Building building)
        {
            if (building == null || !GameAccess.IsServer() ||
                NetworkManagerNuclearOption.i?.ServerObjectManager == null) return;
            NetworkManagerNuclearOption.i.ServerObjectManager.Destroy(building.Identity, true);
        }

        private List<GameObject> FindCandidates(Airbase airbase, out bool fallback)
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
            fallback = usedRadius > radius;

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
            for (int i = 0; i < shellCatalogue.Count; i++)
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
            for (int i = 0; i < shellCatalogue.Count; i++)
            {
                GameObject shell = shellCatalogue[i];
                if (shell == null || !shell.activeInHierarchy || GarrisonOccupancy.IsOccupied(shell) || IsCriticalName(shell.name)) continue;
                Building building = shell.GetComponentInParent<Building>();
                if (building != null && (building.disabled || building.NetworkHQ != null)) continue;
                Bounds bounds = GetShellBounds(shell);
                if (bounds.size.x < RooftopPlacement.MinRoofSpan || bounds.size.z < RooftopPlacement.MinRoofSpan ||
                    bounds.size.y < 3f) continue;
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

        /// <summary>
        /// On-demand rebuild for a barren search: at most every 30 s, so a barren zone or a
        /// repeated order never turns into per-request scene scans.
        /// </summary>
        private bool RefreshCatalogue()
        {
            if (Time.unscaledTime < nextCatalogueAt) return false;
            nextCatalogueAt = Time.unscaledTime + 30f;
            RebuildShellCatalogue();
            return true;
        }

        private bool FitsNest(GameObject shell)
        {
            Bounds bounds = GetShellBounds(shell);
            return bounds.size.x >= RooftopPlacement.MinRoofSpan && bounds.size.z >= RooftopPlacement.MinRoofSpan;
        }

        private void RebuildShellCatalogue()
        {
            shellCatalogue.Clear();
            shellBounds.Clear();
            var seen = new HashSet<int>();
            MapBuilding[] mapBuildings = Resources.FindObjectsOfTypeAll<MapBuilding>();
            for (int i = 0; i < mapBuildings.Length && shellCatalogue.Count < MaxCatalogue; i++)
            {
                MapBuilding building = mapBuildings[i];
                if (building == null || !building.gameObject.scene.IsValid()) continue;
                if (seen.Add(building.gameObject.GetInstanceID())) shellCatalogue.Add(building.gameObject);
            }
            Building[] networkBuildings = Resources.FindObjectsOfTypeAll<Building>();
            for (int i = 0; i < networkBuildings.Length && shellCatalogue.Count < MaxCatalogue; i++)
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

        private static string Sanitize(string value) => GarrisonMarkerInfo.SanitizeZone(value);

    }
}
