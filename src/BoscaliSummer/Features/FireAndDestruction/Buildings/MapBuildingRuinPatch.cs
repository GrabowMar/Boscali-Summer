using BoscaliSummer.Runtime;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    /// <summary>
    /// MapBuilding has no vanilla damage shader channel: the base game only decrements HP and
    /// swaps to a wreck prefab on death. This patch does one job. When a hit takes a live
    /// building straight to zero HP (a direct bomb or missile kill rather than a fire burning
    /// out), it registers a logical ruin so the mission-long aftermath smoke and collapse
    /// dust still appear. Burnout demolition and its ruin are handled separately by
    /// <see cref="ImpactFireManager"/>.
    /// </summary>
    [HarmonyPatch(typeof(MapBuilding), "TakeDamage")]
    internal static class MapBuildingRuinPatch
    {
        private struct RuinGeometry
        {
            public GlobalPosition RuinPosition;
            public Vector2 HalfExtents;
        }

        private static bool Prepare() => GameAccess.MapBuildingHitPointsAvailable;

        private static void Prefix(MapBuilding __instance, out float __state)
        {
            __state = __instance != null ? GameAccess.GetMapBuildingHitPoints(__instance) : 0f;
        }

        private static void Postfix(MapBuilding __instance, float __state)
        {
            if (__instance == null) return;
            float hp = GameAccess.GetMapBuildingHitPoints(__instance);
            if (__state <= 0f || hp > 0f || !IsServer()) return;

            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.Logger.LogInfo(
                    $"MapBuilding '{__instance.name}' destroyed outright from HP {__state:0.#}.");

            RuinGeometry ruin = CaptureRuinGeometry(__instance);
            RuinAftermathManager.Instance?.RegisterRuin(
                ruin.RuinPosition, ruin.HalfExtents, 0f, true, true);
        }

        private static RuinGeometry CaptureRuinGeometry(MapBuilding building)
        {
            var geometry = new RuinGeometry
            {
                RuinPosition = building.transform.GlobalPosition(),
                HalfExtents = new Vector2(8f, 8f)
            };
            Renderer[] renderers = building.GetComponentsInChildren<Renderer>(false);
            Bounds bounds = default(Bounds);
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
            }
            return geometry;
        }

        private static bool IsServer()
        {
            try { return NetworkManagerNuclearOption.i != null && NetworkManagerNuclearOption.i.Server.Active; }
            catch { return false; }
        }
    }
}
