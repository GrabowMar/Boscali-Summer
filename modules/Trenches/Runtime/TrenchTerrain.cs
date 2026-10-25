using BoscaliSummer.Modules.Trenches.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Trenches.Runtime
{
    /// <summary>
    /// Terrain probe for trench siting. The trace and every station offset are validated
    /// against the real terrain collider: dry, level enough to dig, nothing else changed.
    /// </summary>
    internal static class TrenchTerrain
    {
        /// <summary>Ground a position may be dug in: terrain that is dry and level enough (<see cref="TrenchTraceMath.IsBuildableGround"/>).</summary>
        public static bool TryGround(Vector3 global, out Vector3 ground)
            => TryTerrain(global, out ground, out float normalY) && TrenchTraceMath.IsBuildableGround(ground.y, normalY);

        public static bool TryGround(float x, float z, out Vector3 ground)
            => TryGround(new Vector3(x, 0f, z), out ground);

        /// <summary>
        /// The terrain surface under a point, whatever its slope: what a mesh is laid on. Planning asks
        /// <see cref="TryGround"/>; laying must not, or every ring on a slope over ~14 degrees kept a
        /// borrowed height and stood in the air (the ditch fins and hanging wire of issue 26).
        /// </summary>
        public static bool TryTerrain(Vector3 global, out Vector3 ground, out float normalY)
        {
            ground = default;
            normalY = 0f;
            Vector3 local = new GlobalPosition(global.x, 5000f, global.z).ToLocalPosition();
            if (GameAssets.i?.terrainMaterial == null ||
                !Physics.Raycast(local, Vector3.down, out RaycastHit hit, 6000f,
                    (int)PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore) ||
                hit.collider == null || hit.collider.sharedMaterial != GameAssets.i.terrainMaterial)
                return false;
            ground = hit.point.ToGlobalPosition().AsVector3();
            normalY = hit.normal.y;
            return true;
        }

        public static bool TryTerrain(float x, float z, out Vector3 ground)
            => TryTerrain(new Vector3(x, 0f, z), out ground, out _);

        /// <summary>Ground sample for mesh rings, at any slope; a missed probe keeps the ring as-is.</summary>
        public static Vector3 SnapToGround(Vector3 position)
            => TryTerrain(position, out Vector3 ground, out _) ? ground : position;
    }
}
