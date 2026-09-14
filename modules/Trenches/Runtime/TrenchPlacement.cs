using System;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Features.Trenches.Domain;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    /// <summary>
    /// Terrain checks for frontline trench sectors. Placement works on the exact fortified
    /// corridor: owned ground, dry, without cliffs, but tolerant of gentle rolls.
    /// </summary>
    internal static class TrenchPlacement
    {
        public static bool ValidateCorridor(Vector3 center, Vector3 threatDir, float flankLimit, int faction,
            ITerritoryIngress territory, out Vector3 ground)
        {
            ground = default;
            Vector3 lateral = Vector3.Cross(Vector3.up, threatDir).normalized;
            if (!TryGround(center, out ground)) return false;

            bool firstRow = true;
            float previousRow = 0f;
            for (float forward = -TrenchTacticalMath.RearLineDepth - 14f; forward <= 14f; forward += 14f)
            {
                float rowReference = 0f;
                bool hasReference = false;
                for (float side = -flankLimit; side <= flankLimit; side += 22f)
                {
                    Vector3 p = center + lateral * side + threatDir * forward;
                    if (!territory.OwnsPosition(faction, p.x, p.z) || !TryGround(p, out Vector3 sample))
                        return false;
                    if (!hasReference) { rowReference = sample.y; hasReference = true; }
                    else if (Math.Abs(sample.y - rowReference) > 3f) return false;
                }
                // A row of the corridor follows one common ground level; the corridor itself
                // may still descend toward the rear with the frontline ridge.
                if (!firstRow && Math.Abs(rowReference - previousRow) > 8f) return false;
                previousRow = rowReference;
                firstRow = false;
            }
            return true;
        }

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
            return TrenchTacticalMath.IsBuildableGround(ground.y, hit.normal.y);
        }
    }
}
