using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Game;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Clean ruins from the one endpoint every peer runs: release the fallen building's
    /// hit decals and wisp, lay a ground scar (ash for tree rows), and register the
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
            public int BuildingId;
            public bool TreeRow;
            public Vector3 ScarPosition;
            public GlobalPosition RuinPosition;
            public Vector2 HalfExtents;
            public List<RuinDebrisPool.FacadePiece> Facade;
            public MapBuilding Building;
        }

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(MapBuilding), "Destruct");
        private static bool Prepare() => TargetMethod() != null;

        private static void Prefix(MapBuilding __instance, out DestructGeometry __state)
        {
            __state = null;
            if (__instance == null) return;
            __state = Capture(__instance);
        }

        private static void Postfix(DestructGeometry __state)
        {
            if (__state == null || !__state.Valid) return;
            bool keepCards = !__state.TreeRow;
            BuildingHitLedger.Instance?.Forget(__state.BuildingId, keepCards);
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
            RuinAftermathManager manager = RuinAftermathManager.Instance;
            manager?.RegisterRuin(
                __state.RuinPosition, __state.HalfExtents, 0f,
                GameAccess.IsServer(), Time.timeSinceLevelLoad >= 10f);
            if (!keepCards) return;
            GameObject shell = manager?.AttachFacade(__state.RuinPosition, __state.Facade, __state.BuildingId);
            if (shell == null)
            {
                BuildingHitLedger.Instance?.HideBreaches(__state.BuildingId);
                return;
            }
            // Vanilla may leave the mesh up for the rest of the frame. The shell is the one that stays.
            if (__state.Building == null) return;
            Renderer[] live = __state.Building.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < live.Length; i++)
                if (live[i] != null) live[i].enabled = false;
        }

        private static DestructGeometry Capture(MapBuilding building)
        {
            bool treeRow = building.name.IndexOf(
                "TreeRow", System.StringComparison.OrdinalIgnoreCase) >= 0;
            var geometry = new DestructGeometry
            {
                Valid = true,
                BuildingId = building.GetInstanceID(),
                TreeRow = treeRow,
                RuinPosition = building.transform.GlobalPosition(),
                HalfExtents = new Vector2(8f, 8f),
                Facade = treeRow ? null : RuinDebrisPool.CaptureFacade(building),
                Building = building
            };
            Renderer[] renderers = building.GetComponentsInChildren<Renderer>(false);
            Bounds bounds = default;
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer ||
                    !renderer.gameObject.activeInHierarchy) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (found)
            {
                geometry.HalfExtents = new Vector2(
                    Mathf.Max(3f, bounds.extents.x), Mathf.Max(3f, bounds.extents.z));
                Vector3 anchor = bounds.center;
                anchor.y = bounds.min.y + 0.5f;
                geometry.RuinPosition = anchor.ToGlobalPosition();
                Vector3 scar = bounds.center;
                scar.y = bounds.min.y + 0.2f;
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
    }
}
