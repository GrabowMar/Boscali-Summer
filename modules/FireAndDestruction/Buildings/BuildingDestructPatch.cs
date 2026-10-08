using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BoscaliSummer.Core.Game;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Visual aftermath at the native destruction endpoint: release the fallen building's
    /// impact residue, lay a ground scar (ash for tree rows), and register the
    /// persistent ruin. Covers every death route — server blast kills, fire burnout and
    /// client gunfire deaths relayed through the building-states sync — including the
    /// late-join replay. The prefix captures the footprint before vanilla spawns its
    /// debris prefab as a child of the dying building.
    /// </summary>
    [HarmonyPatch]
    internal static class BuildingDestructPatch
    {
        private sealed class DestructGeometry
        {
            public bool Valid;
            public MapBuilding Building;
            public GameObject Wreck;
            public DestructGeometry Previous;
            public int BuildingId;
            public bool TreeRow;
            public Vector3 ScarPosition;
            public GlobalPosition RuinPosition;
            public Vector2 HalfExtents;
            public List<RuinDebrisPool.FacadePiece> DestroyedFacade;
        }

        [System.ThreadStatic] private static DestructGeometry capturing;
        private static readonly FieldInfo DestroyedPrefab = AccessTools.Field(typeof(MapBuilding), "destroyedPrefab");

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(MapBuilding), "Destruct");
        private static bool Prepare() => TargetMethod() != null;

        private static void Prefix(MapBuilding __instance, out DestructGeometry __state)
        {
            __state = null;
            if (__instance == null) return;
            // The carver probes street level; prepare it first so the capture below can use it.
            if (__instance.name.IndexOf("TreeRow", System.StringComparison.OrdinalIgnoreCase) < 0)
                BuildingCarver.Instance?.PrepareCollapse(__instance);
            __state = Capture(__instance);
            __state.Previous = capturing; capturing = __state;
        }

        private static void Postfix(DestructGeometry __state)
        {
            if (capturing == __state) capturing = __state?.Previous;
            if (__state == null || !__state.Valid) return;
            BuildingHitLedger.Instance?.Forget(__state.BuildingId);
            if (__state.TreeRow)
            {
                BuildingHitLedger.Instance?.StampTreeRowAsh(
                    __state.RuinPosition, __state.HalfExtents.x * 2f, __state.HalfExtents.y * 2f);
            }
            else
            {
                BuildingHitLedger.Instance?.StampGroundScar(
                    __state.ScarPosition, __state.HalfExtents.x * 2f, __state.HalfExtents.y * 2f);
            }
            // The 8 m dedup inside RegisterRuin absorbs the fire-burnout demolitions that
            // already registered, and the message echo of this same death on remotes.
            // Late-join replay runs Destruct with a fresh level clock, so ancient ruins
            // get their scar and smoulder but no new collapse burst.
            bool custom = !__state.TreeRow && BuildingCarver.Instance != null &&
                BuildingCarver.Instance.Collapse(__state.BuildingId, __state.Wreck, Time.timeSinceLevelLoad >= 10f);
            RuinAftermathManager manager = RuinAftermathManager.Instance;
            manager?.RegisterRuin(
                __state.RuinPosition, __state.HalfExtents, 0f,
                GameAccess.IsServer(), Time.timeSinceLevelLoad >= 10f, !custom);
            // Native colliders and obstacle remain untouched; unknown types retain native visuals.
            // Replay skips that prefab; only then add a renderer-only copy, without new collision.
            if (!custom && !__state.TreeRow && Time.timeSinceLevelLoad < 10f && __state.DestroyedFacade != null)
                manager?.AttachFacade(__state.RuinPosition, __state.DestroyedFacade, __state.BuildingId, true);
        }

        private static System.Exception Finalizer(System.Exception __exception, DestructGeometry __state)
        { if (capturing == __state) capturing = __state?.Previous; return __exception; }
        private static void Remember(GameObject wreck, MapBuilding building)
        { if (capturing != null && capturing.Building == building) capturing.Wreck = wreck; }
        [HarmonyPatch]
        internal static class NativeRubbleCapture
        {
            private static MethodBase TargetMethod() => AccessTools.Method(typeof(MapBuilding), "SpawnDestroyedPrefab");
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                foreach (CodeInstruction instruction in instructions)
                {
                    yield return instruction;
                    if (!(instruction.operand is MethodInfo method) || method.DeclaringType != typeof(Object) ||
                        method.Name != "Instantiate" || !method.IsGenericMethod || method.GetGenericArguments()[0] != typeof(GameObject)) continue;
                    yield return new CodeInstruction(OpCodes.Dup);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BuildingDestructPatch), nameof(Remember)));
                }
            }
        }

        private static DestructGeometry Capture(MapBuilding building)
        {
            bool treeRow = building.name.IndexOf(
                "TreeRow", System.StringComparison.OrdinalIgnoreCase) >= 0;
            var geometry = new DestructGeometry
            {
                Valid = true, Building = building,
                BuildingId = building.GetInstanceID(),
                TreeRow = treeRow,
                RuinPosition = building.transform.GlobalPosition(),
                HalfExtents = new Vector2(8f, 8f),
                DestroyedFacade = treeRow || Time.timeSinceLevelLoad >= 10f ? null : CaptureDestroyedPrefab(building)
            };
            Renderer[] renderers = building.GetComponentsInChildren<Renderer>(false);
            Bounds bounds = default;
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer is ParticleSystemRenderer ||
                    !renderer.gameObject.activeInHierarchy) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (found)
            {
                geometry.HalfExtents = new Vector2(
                    Mathf.Max(3f, bounds.extents.x), Mathf.Max(3f, bounds.extents.z));
                Vector3 anchor = bounds.center;
                // Native building meshes extend below their placement pivot as buried foundations,
                // sometimes far below; prefer the carver's probed street level when it has one.
                float ground = BuildingCarver.Instance != null && BuildingCarver.Instance.TryStreetLevel(building, out float street)
                    ? street : Mathf.Max(building.transform.position.y, bounds.min.y);
                anchor.y = ground + 0.5f;
                geometry.RuinPosition = anchor.ToGlobalPosition();
                Vector3 scar = bounds.center;
                scar.y = ground + 0.2f;
                geometry.ScarPosition = scar;
            }
            else
            {
                Vector3 fallback = building.transform.position;
                fallback.y += 0.2f;
                geometry.ScarPosition = fallback;
            }
            return geometry;
        }

        private static List<RuinDebrisPool.FacadePiece> CaptureDestroyedPrefab(MapBuilding building)
        {
            // Vanilla skips spawning rubble during early scene/late-join replay. Transform the
            // prefab's static meshes without instantiating its colliders, particles or scripts.
            GameObject prefab = DestroyedPrefab?.GetValue(building) as GameObject;
            if (prefab == null) return null;
            List<RuinDebrisPool.FacadePiece> pieces = RuinDebrisPool.CaptureFacade(prefab.transform);
            if (pieces.Count == 0) return null;
            Transform root = prefab.transform;
            Matrix4x4 toWorld = building.transform.localToWorldMatrix *
                Matrix4x4.TRS(root.localPosition, root.localRotation, root.localScale) * root.worldToLocalMatrix;
            for (int i = 0; i < pieces.Count; i++)
            {
                RuinDebrisPool.FacadePiece piece = pieces[i];
                Matrix4x4 pose = toWorld * Matrix4x4.TRS(piece.Position, piece.Rotation, piece.Scale);
                piece.Position = pose.GetColumn(3);
                piece.Rotation = pose.rotation;
                piece.Scale = pose.lossyScale;
                pieces[i] = piece;
            }
            return pieces;
        }
    }
}
