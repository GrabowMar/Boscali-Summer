using UnityEngine;

namespace BoscaliSummer.Runtime
{
    /// <summary>
    /// Where a mod-spawned vehicle or building may stand: dry, level terrain with room for its
    /// whole footprint. Shared by the operation rewards and the high-command assets so every
    /// server-side spawn is placed by the same rule.
    /// </summary>
    internal static class GroundPlacement
    {
        /// <summary>A definition sane enough to spawn: a prefab, allowed in this mission, and a small finite footprint.</summary>
        public static bool Usable(UnitDefinition definition) => definition != null && definition.unitPrefab != null &&
            definition.IsAllowed(MissionManager.AllowEventContent) &&
            Finite(definition.spawnOffset) && definition.spawnOffset.sqrMagnitude <= 400f &&
            Finite(definition.value) && definition.value > 0f &&
            Finite(definition.width) && definition.width > 0f && definition.width <= 20f &&
            Finite(definition.length) && definition.length > 0f && definition.length <= 25f &&
            Finite(definition.height) && definition.height > 0f && definition.height <= 20f;

        /// <summary>
        /// Settle the footprint at <paramref name="desired"/>: every corner on dry terrain within a
        /// metre of the centre's height and nothing already occupying the volume.
        /// </summary>
        public static bool TryPlace(UnitDefinition definition, Vector3 desired, Quaternion rotation, out Vector3 position)
        {
            position = default;
            if (!Usable(definition)) return false;
            Vector3 offset = rotation * definition.spawnOffset;
            desired += new Vector3(offset.x, 0f, offset.z);
            if (!DryGround(desired, out Vector3 center)) return false;
            Vector3 half = new Vector3(definition.width * 0.5f + 1f, definition.height * 0.5f, definition.length * 0.5f + 1f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = center + rotation * new Vector3((i & 1) == 0 ? -half.x : half.x, 0f, (i & 2) == 0 ? -half.z : half.z);
                if (!DryGround(corner, out Vector3 hit) || Mathf.Abs(hit.y - center.y) > 1f) return false;
            }
            Vector3 volume = center + Vector3.up * (half.y + 0.15f);
            if (Physics.CheckBox(volume, half, rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
            position = center + Vector3.up * (offset.y + 0.2f);
            return Finite(position);
        }

        /// <summary>The terrain point under <paramref name="desired"/>, if it is near-level land clear of the sea.</summary>
        public static bool DryGround(Vector3 desired, out Vector3 point)
        {
            point = default;
            if (!Finite(desired) || GameAssets.i?.terrainMaterial == null ||
                !Physics.Raycast(desired + Vector3.up * 500f, Vector3.down, out RaycastHit hit, 2000f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                hit.collider == null || hit.collider.sharedMaterial != GameAssets.i.terrainMaterial ||
                hit.normal.y < 0.96f || hit.point.y <= Datum.LocalSeaY + 1f) return false;
            point = hit.point;
            return Finite(point);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
