using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using HarmonyLib;
using NuclearOption.Effects;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>Native, replicated demolition behind the front; native GPU forest clearing on every peer.</summary>
    internal sealed class Sa74Destruction : MonoBehaviour, ISceneService
    {
        internal const int DeathBudget = 8, IndexBudget = 512, VerificationBudget = 8192;
        private const float ForestStep = 2000f;
        private static readonly AccessTools.FieldRef<DetailRenderer, DetailRenderer.ArgBuffer> Arguments =
            AccessTools.FieldRefAccess<DetailRenderer, DetailRenderer.ArgBuffer>("argHelper");
        private static readonly AccessTools.FieldRef<DetailRenderer, TreeRenderer[]> Trees =
            AccessTools.FieldRefAccess<DetailRenderer, TreeRenderer[]>("treeRenderers");
        private static readonly AccessTools.FieldRef<TreeRenderer, GraphicsBuffer> Positions =
            AccessTools.FieldRefAccess<TreeRenderer, GraphicsBuffer>("treePositionBuffer");
        private static readonly AccessTools.FieldRef<TreeRenderer, float> ClearScale =
            AccessTools.FieldRefAccess<TreeRenderer, float>("clearTreeRadiusMultiplier");
        private static readonly FieldInfo MapSet = AccessTools.Field(typeof(MapBuilding), "buildingSet");
        private static readonly FieldInfo MapIndex = AccessTools.Field(typeof(MapBuilding), "index");
        internal static Sa74Destruction Instance;

        private sealed class Target
        {
            internal Building Building;
            internal MapBuilding MapBuilding;
            internal MapBuildingSet Set;
            internal int Index;
            internal GlobalPosition Position;
        }
        private readonly struct Candidate
        {
            internal readonly Target Target;
            internal readonly float Distance;
            internal Candidate(Target target, float distance) { Target = target; Distance = distance; }
        }
        private sealed class CandidateOrder : IComparer<Candidate>
        {
            internal static readonly CandidateOrder Instance = new CandidateOrder();
            public int Compare(Candidate a, Candidate b) => a.Distance.CompareTo(b.Distance);
        }
        private sealed class Front
        {
            internal Sa74Fallout.Site Site;
            internal readonly List<Candidate> Buildings = new List<Candidate>();
            internal int Indexed, Next;
            internal bool Sorted;
            internal float ClearedRadius;
        }
        private sealed class Forest
        {
            internal TreeRenderer Renderer;
            internal GraphicsBuffer Buffer;
            internal AsyncGPUReadbackRequest Capture, Verification;
            internal bool Capturing, Verifying, Restore;
            internal Vector3[] Before, After;
            internal int Count, Cursor, Burned, Remaining, VerificationRound;
        }

        private readonly List<Target> targets = new List<Target>();
        private readonly HashSet<int> known = new HashSet<int>();
        private readonly List<Front> fronts = new List<Front>();
        private readonly List<Forest> forests = new List<Forest>();
        private CommandBuffer clearCommands;
        private RenderTexture beforeBlastMap;
        private RenderTexture capturedBlastMap;
        private uint epoch;
        private bool searched, verifyRequested;
        private int forestCursor, frontCursor, verificationRound;
        internal int CollapsedBuildings { get; private set; }
        internal int CollapsedMapBuildings { get; private set; }
        internal int ForestBatches { get; private set; }
        internal int MaxDeathsPerFrame { get; private set; }
        internal int Failures { get; private set; }
        internal int NativeForestBurnCount { get; private set; }
        internal int NativeForestRemainingCount { get; private set; }
        internal float ForestRadius { get; private set; }
        internal bool Ready => searched && (GameManager.IsHeadless || forests.Count != 0);

        private void Awake() { Instance = this; epoch = Sa74Fallout.Epoch; }

        /// <summary>Called before the native explosion submits any tree changes. Capture once, asynchronously.</summary>
        internal static void Observe(Sa74Fallout.Site site)
        {
            if (Instance == null || site == null) return;
            Instance.ObserveSite(site);
        }

        private void ObserveSite(Sa74Fallout.Site site)
        {
            if (epoch != Sa74Fallout.Epoch) { ResetAftermath(); epoch = Sa74Fallout.Epoch; }
            for (int i = 0; i < fronts.Count; i++) if (fronts[i].Site.Id == site.Id) return;
            CaptureForest();
            if (!searched)
            {
                searched = true;
                if (GameAccess.IsServer())
                {
                    // Once per mission, never a repeated scene/physics search as the blast expands.
                    foreach (Building building in UnityEngine.Object.FindObjectsOfType<Building>()) Register(building);
                    foreach (MapBuilding building in UnityEngine.Object.FindObjectsOfType<MapBuilding>())
                        Register(building, MapSet.GetValue(building) as MapBuildingSet, (int)MapIndex.GetValue(building));
                }
            }
            fronts.Add(new Front { Site = site });
        }

        internal static void Register(Building building)
        {
            Sa74Destruction service = Instance;
            if (service == null || building == null || !GameAccess.IsServer() || !building.gameObject.scene.IsValid() ||
                !service.known.Add(building.GetInstanceID())) return;
            service.targets.Add(new Target { Building = building, Position = building.transform.position.ToGlobalPosition() });
        }

        internal static void Register(MapBuilding building, MapBuildingSet set, int index)
        {
            Sa74Destruction service = Instance;
            if (service == null || building == null || set == null || !GameAccess.IsServer() ||
                !building.gameObject.scene.IsValid() || !service.known.Add(building.GetInstanceID())) return;
            service.targets.Add(new Target { MapBuilding = building, Set = set, Index = index,
                Position = building.transform.position.ToGlobalPosition() });
        }

        private void CaptureForest()
        {
            if (GameManager.IsHeadless || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            DetailRenderer detail = SceneSingleton<DetailRenderer>.i;
            if (detail == null) return;
            TreeRenderer[] renderers = Trees(detail);
            if (renderers == null) return;
            foreach (TreeRenderer renderer in renderers)
            {
                if (renderer == null) continue;
                GraphicsBuffer buffer = Positions(renderer);
                if (buffer == null || !buffer.IsValid()) continue;
                bool captured = false;
                for (int i = 0; i < forests.Count; i++) if (forests[i].Buffer == buffer) { captured = true; break; }
                if (captured) continue;
                Forest forest = new Forest { Renderer = renderer, Buffer = buffer,
                    Count = renderer.PositionData != null ? renderer.PositionData.bytes.Length / 12 : buffer.count };
                if (SystemInfo.supportsAsyncGPUReadback)
                {
                    forest.Capture = AsyncGPUReadback.Request(buffer);
                    forest.Capturing = true;
                }
                forests.Add(forest);
            }
            BlastManager blast = SceneSingleton<BlastManager>.i;
            if (beforeBlastMap == null && blast != null && blast.Texture != null)
            {
                capturedBlastMap = blast.Texture;
                beforeBlastMap = new RenderTexture(blast.Texture.descriptor) { name = "SA-74N pre-burst vegetation mask" };
                beforeBlastMap.Create();
                Graphics.Blit(blast.Texture, beforeBlastMap);
            }
        }

        private void Update()
        {
            if (Datum.origin == null) return;
            if (epoch != Sa74Fallout.Epoch) { ResetAftermath(); epoch = Sa74Fallout.Epoch; }
            for (int i = 0; i < Sa74Fallout.Sites.Count; i++) ObserveSite(Sa74Fallout.Sites[i]);
            PollForest();
            if (fronts.Count == 0) return;
            if (forests.Count == 0) CaptureForest();

            int indexWork = IndexBudget;
            for (int i = 0; i < fronts.Count && indexWork > 0; i++) Index(fronts[i], ref indexWork);
            int deathWork = DeathBudget, checks = IndexBudget;
            for (int visited = 0; visited < fronts.Count && deathWork > 0 && checks > 0; visited++)
            {
                Front front = fronts[frontCursor++ % fronts.Count];
                Demolish(front, ref deathWork, ref checks);
            }
            MaxDeathsPerFrame = Math.Max(MaxDeathsPerFrame, DeathBudget - deathWork);
            ClearOneForestBatch();
        }

        private void Index(Front front, ref int budget)
        {
            if (!GameAccess.IsServer()) return;
            if (front.Indexed == targets.Count) return;
            float radius2 = Sa74Fallout.ShockRadius * Sa74Fallout.ShockRadius;
            while (front.Indexed < targets.Count && budget-- > 0)
            {
                Target target = targets[front.Indexed++];
                float dx = target.Position.x - front.Site.Position.x, dz = target.Position.z - front.Site.Position.z;
                float distance2 = dx * dx + dz * dz;
                if (distance2 > radius2) continue;
                Candidate candidate = new Candidate(target, Mathf.Sqrt(distance2));
                if (front.Sorted)
                {
                    int index = front.Buildings.BinarySearch(front.Next, front.Buildings.Count - front.Next, candidate, CandidateOrder.Instance);
                    front.Buildings.Insert(index < 0 ? ~index : index, candidate);
                }
                else front.Buildings.Add(candidate);
            }
            if (front.Indexed == targets.Count && !front.Sorted)
            {
                front.Buildings.Sort(CandidateOrder.Instance);
                front.Sorted = true;
            }
        }

        private void Demolish(Front front, ref int budget, ref int checks)
        {
            if (!GameAccess.IsServer() || !front.Sorted) return;
            float radius = Sa74Fallout.FrontRadius(Time.time - front.Site.Started);
            while (front.Next < front.Buildings.Count && checks-- > 0 && budget > 0)
            {
                Candidate candidate = front.Buildings[front.Next];
                if (candidate.Distance > radius) break;
                front.Next++;
                Target target = candidate.Target;
                if (target.Building != null && !target.Building.disabled)
                {
                    target.Building.DisableUnit(); // Native disabled SyncVar invokes native collapse on all peers/late joiners.
                    CollapsedBuildings++;
                    budget--;
                }
                else if (target.MapBuilding != null && target.Set != null && target.Set.IsServer &&
                    target.Index >= 0 && target.Index < target.Set.buildingStates.Count && target.Set.buildingStates[target.Index])
                {
                    target.Set.DestroyBuilding(target.Index); // Native SyncList, including dependent rooftop objects.
                    CollapsedMapBuildings++;
                    budget--;
                }
            }
        }

        private void ClearOneForestBatch()
        {
            DetailRenderer detail = SceneSingleton<DetailRenderer>.i;
            if (detail == null || forests.Count == 0) return;
            DetailRenderer.ArgBuffer arguments = Arguments(detail);
            for (int visited = 0; visited < fronts.Count; visited++)
            {
                Front front = fronts[forestCursor++ % fronts.Count];
                float radius = Sa74Fallout.FrontRadius(Time.time - front.Site.Started);
                bool complete = radius >= Sa74Fallout.ShockRadius;
                if (radius <= front.ClearedRadius || (!complete && radius - front.ClearedRadius < ForestStep && front.ClearedRadius > 0f)) continue;
                if (clearCommands == null) clearCommands = new CommandBuffer { name = "SA-74N advancing forest front" };
                clearCommands.Clear();
                int commands = 0;
                for (int i = 0; i < forests.Count; i++)
                {
                    Forest forest = forests[i];
                    if (forest.Renderer == null || !forest.Buffer.IsValid()) continue;
                    float scale = ClearScale(forest.Renderer);
                    if (scale <= 0f) continue;
                    DetailRenderer.RendererOffsets offsets = arguments.GetRenderer(forest.Renderer.ArgIndex);
                    if (offsets.Buffer == null || !offsets.Buffer.IsValid()) continue;
                    forest.Renderer.ClearTrees(clearCommands, offsets, new BlastManager.DetailBlast(front.Site.Position, radius / scale));
                    commands++;
                }
                if (commands == 0) return;
                Graphics.ExecuteCommandBuffer(clearCommands);
                ForestBatches++;
                ForestRadius = Mathf.Max(ForestRadius, radius);
                front.ClearedRadius = radius;
                if (complete) RequestVerification();
                // One GPU scan per tree species, at most once per frame, normally once per ~6 game seconds/site.
                break;
            }
        }

        private void PollForest()
        {
            int budget = VerificationBudget;
            NativeForestBurnCount = 0;
            NativeForestRemainingCount = 0;
            for (int i = 0; i < forests.Count; i++)
            {
                Forest forest = forests[i];
                if (forest.Capturing && forest.Capture.done)
                {
                    forest.Capturing = false;
                    if (!forest.Capture.hasError) forest.Before = forest.Capture.GetData<Vector3>().ToArray();
                    else Failures++;
                }
                if (forest.Restore && forest.Before != null)
                {
                    if (forest.Buffer.IsValid()) forest.Buffer.SetData(forest.Before);
                    forest.Restore = false;
                }
                if (verifyRequested && forest.VerificationRound != verificationRound && !forest.Verifying &&
                    forest.After == null && forest.Before != null && forest.Buffer.IsValid())
                {
                    forest.Verification = AsyncGPUReadback.Request(forest.Buffer);
                    forest.Verifying = true;
                    forest.VerificationRound = verificationRound;
                    forest.Cursor = forest.Burned = forest.Remaining = 0;
                }
                if (forest.Verifying && forest.Verification.done)
                {
                    forest.Verifying = false;
                    if (!forest.Verification.hasError) forest.After = forest.Verification.GetData<Vector3>().ToArray();
                    else Failures++;
                }
                if (forest.After != null)
                {
                    int count = Math.Min(forest.Count, Math.Min(forest.Before.Length, forest.After.Length));
                    while (forest.Cursor < count && budget-- > 0)
                    {
                        int at = forest.Cursor++;
                        Vector3 before = forest.Before[at], after = forest.After[at];
                        if (!Covered(before.x, before.z)) continue;
                        if (before != after) forest.Burned++;
                        else if (before.y > -9000f) forest.Remaining++;
                    }
                    if (forest.Cursor == count) forest.After = null;
                }
                NativeForestBurnCount += forest.Burned;
                NativeForestRemainingCount += forest.Remaining;
            }
            if (verifyRequested)
            {
                bool waiting = false;
                for (int i = 0; i < forests.Count; i++)
                    if (forests[i].Capturing || forests[i].Verifying || forests[i].After != null ||
                        forests[i].Before != null && forests[i].VerificationRound != verificationRound) waiting = true;
                if (!waiting) verifyRequested = false;
            }
        }

        private bool Covered(float x, float z)
        {
            for (int i = 0; i < fronts.Count; i++)
            {
                Front front = fronts[i];
                float dx = x - front.Site.Position.x, dz = z - front.Site.Position.z;
                if (dx * dx + dz * dz <= front.ClearedRadius * front.ClearedRadius) return true;
            }
            return false;
        }

        internal static void RequestVerification()
        {
            if (Instance == null) return;
            Instance.verificationRound++;
            Instance.verifyRequested = true;
        }

        internal static Dictionary<string, object> Snapshot()
        {
            Sa74Destruction service = Instance;
            int pending = 0, positions = 0;
            if (service != null)
            {
                for (int i = 0; i < service.fronts.Count; i++) pending += service.fronts[i].Buildings.Count - service.fronts[i].Next;
                for (int i = 0; i < service.forests.Count; i++) positions += service.forests[i].Count;
            }
            return new Dictionary<string, object>
            {
                ["ready"] = service != null && service.Ready,
                ["buildingTargets"] = service?.targets.Count ?? 0, ["pendingBuildings"] = pending,
                ["collapsedBuildings"] = service?.CollapsedBuildings ?? 0,
                ["collapsedMapBuildings"] = service?.CollapsedMapBuildings ?? 0,
                ["deathBudget"] = DeathBudget, ["maxDeathsPerFrame"] = service?.MaxDeathsPerFrame ?? 0,
                ["indexBudget"] = IndexBudget, ["verificationBudget"] = VerificationBudget,
                ["forestRenderers"] = service?.forests.Count ?? 0, ["forestPositions"] = positions,
                ["forestBatches"] = service?.ForestBatches ?? 0, ["forestRadius"] = service?.ForestRadius ?? 0f,
                ["nativeForestBurnCount"] = service?.NativeForestBurnCount ?? 0,
                ["nativeForestRemainingCount"] = service?.NativeForestRemainingCount ?? 0,
                ["verificationPending"] = service?.verifyRequested ?? false, ["failures"] = service?.Failures ?? 0
            };
        }

        internal static void Reset() => Instance?.ResetAftermath();

        private void ResetAftermath()
        {
            fronts.Clear();
            foreach (Forest forest in forests)
            {
                if (forest.Before != null && forest.Buffer.IsValid()) forest.Buffer.SetData(forest.Before);
                else if (forest.Capturing) forest.Restore = true;
                forest.Burned = forest.Cursor = forest.Remaining = 0;
                forest.After = null;
                forest.Verifying = false;
            }
            if (beforeBlastMap != null && capturedBlastMap != null) Graphics.Blit(beforeBlastMap, capturedBlastMap);
            DetailRenderer.DatumShifted = true; // Rebuild procedural grass from the restored native burn mask.
            CollapsedBuildings = CollapsedMapBuildings = ForestBatches = MaxDeathsPerFrame = NativeForestBurnCount = NativeForestRemainingCount = Failures = 0;
            ForestRadius = 0f;
            verifyRequested = false;
            forestCursor = frontCursor = 0;
            epoch = Sa74Fallout.Epoch;
            // Buildings deliberately keep their native destroyed state; native mission reload owns their reset.
        }

        public void ResetForScene()
        {
            ResetAftermath();
            targets.Clear();
            known.Clear();
            forests.Clear();
            searched = false;
            clearCommands?.Clear();
            if (beforeBlastMap != null) { beforeBlastMap.Release(); UnityEngine.Object.Destroy(beforeBlastMap); }
            beforeBlastMap = capturedBlastMap = null;
        }

        private void OnDestroy()
        {
            ResetForScene();
            if (Instance == this) Instance = null;
            clearCommands?.Release();
        }
    }

    [HarmonyPatch(typeof(Building), "OnStartServer")]
    internal static class Sa74BuildingRegistrationPatch
    {
        private static void Postfix(Building __instance) => Sa74Destruction.Register(__instance);
    }

    [HarmonyPatch(typeof(MapBuilding), "AssignBuildingSet")]
    internal static class Sa74MapBuildingRegistrationPatch
    {
        private static void Postfix(MapBuilding __instance, MapBuildingSet buildingSet, int index) =>
            Sa74Destruction.Register(__instance, buildingSet, index);
    }
}
