using System.Reflection;
using BoscaliSummer.Runtime;
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
        private struct DestructGeometry
        {
            public bool Valid;
            public int BuildingId;
            public bool TreeRow;
            public Vector3 ScarPosition;
            public GlobalPosition RuinPosition;
            public Vector2 HalfExtents;
        }

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(MapBuilding), "Destruct");
        private static bool Prepare() => TargetMethod() != null;

        private static void Prefix(MapBuilding __instance, out DestructGeometry __state)
        {
            __state = default;
            if (__instance == null) return;
            __state = Capture(__instance);
        }

        private static void Postfix(DestructGeometry __state)
        {
            if (!__state.Valid) return;
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
            RuinAftermathManager.Instance?.RegisterRuin(
                __state.RuinPosition, __state.HalfExtents, 0f,
                GameAccess.IsServer(), Time.timeSinceLevelLoad >= 10f);
        }

        private static DestructGeometry Capture(MapBuilding building)
        {
            var geometry = new DestructGeometry
            {
                Valid = true,
                BuildingId = building.GetInstanceID(),
                TreeRow = building.name.IndexOf(
                    "TreeRow", System.StringComparison.OrdinalIgnoreCase) >= 0,
                RuinPosition = building.transform.GlobalPosition(),
                HalfExtents = new Vector2(8f, 8f)
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
