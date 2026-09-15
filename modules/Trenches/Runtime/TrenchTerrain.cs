using System;
using BoscaliSummer.Features.Trenches.Domain;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    /// <summary>
    /// Terrain probe for trench siting. The trace and every station offset are validated
    /// against the real terrain collider: dry, level enough to dig, nothing else changed.
    /// </summary>
    internal static class TrenchTerrain
    {
        public static bool TryGround(Vector3 global, out Vector3 ground)
        {
            ground = default;
            Vector3 local = new GlobalPosition(global.x, 5000f, global.z).ToLocalPosition();
            if (GameAssets.i?.terrainMaterial == null ||
                !Physics.Raycast(local, Vector3.down, out RaycastHit hit, 6000f,
                    (int)PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore) ||
                hit.collider == null || hit.collider.sharedMaterial != GameAssets.i.terrainMaterial)
                return false;
            ground = hit.point.ToGlobalPosition().AsVector3();
            return TrenchTraceMath.IsBuildableGround(ground.y, hit.normal.y);
        }

        public static bool TryGround(float x, float z, out Vector3 ground)
            => TryGround(new Vector3(x, 0f, z), out ground);

        /// <summary>Ground sample for mesh rings; a missed probe keeps the ring as-is.</summary>
        public static Vector3 SnapToGround(Vector3 position)
            => TryGround(position, out Vector3 ground) ? ground : position;
    }
}
