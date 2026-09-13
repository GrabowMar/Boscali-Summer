using System;
using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Features.Trenches.Domain;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    internal sealed class TrenchPlacement
    {
        private readonly List<(Vector3 a, Vector3 b)> roads = new List<(Vector3, Vector3)>(8192);
        public void Reset() => roads.Clear();

        public void ReadRoads()
        {
            roads.Clear();
            var source = NetworkSceneSingleton<LevelInfo>.i?.roadNetwork?.roads;
            if (source == null) return;
            int checkedRoads = 0;
            foreach (var road in source)
            {
                if (++checkedRoads > 2048) break;
                if (road?.points == null || road.IsBridge()) continue;
                for (int i = 1; i < road.points.Count; i++)
                {
                    if (roads.Count == 8192) return;
                    roads.Add((road.points[i - 1].AsVector3(), road.points[i].AsVector3()));
                }
            }
        }

        // Slide along the owned side of this exact map border, never toward a distant road.
        public bool TryRoadSite(FrontlineSite site, out Vector3 position)
        {
            Vector3 center = new Vector3(site.X, 0, site.Z);
            Vector3 tangent = new Vector3(-site.ThreatZ, 0, site.ThreatX);
            position = center;
            float best = 300f * 300f;
            foreach (var road in roads)
            {
                Vector3 a = new Vector3(road.a.x, 0, road.a.z);
                Vector3 delta = new Vector3(road.b.x - a.x, 0, road.b.z - a.z);
                if (delta.sqrMagnitude < 1f) continue;
                Vector3 nearest = a + delta * Mathf.Clamp01(Vector3.Dot(center - a, delta) / delta.sqrMagnitude);
                float along = Mathf.Clamp(Vector3.Dot(nearest - center, tangent),
                    -Math.Max(0, site.HalfLength - 80f), Math.Max(0, site.HalfLength - 80f));
                Vector3 candidate = center + tangent * along;
                float distance = (candidate - nearest).sqrMagnitude;
                if (distance >= best) continue;
                // Leave room for the full 120m reserve without blocking the road.
                if (distance < 80f * 80f)
                    candidate -= new Vector3(site.ThreatX, 0, site.ThreatZ) * 100f;
                position = candidate;
                best = distance;
            }
            return best < 300f * 300f;
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

        public static bool ValidateReserve(Vector3 center, int faction, ITerritoryIngress territory, out Vector3 ground)
        {
            if (!TryGround(center, out ground)) return false;
            // Reserve the complete growth footprint, including rearward communications.
            for (int z = -60; z <= 60; z += 10)
            for (int x = -60; x <= 60; x += 10)
            {
                Vector3 p = center + new Vector3(x, 0, z);
                if (!territory.OwnsPosition(faction, p.x, p.z) || !TryGround(p, out Vector3 sample) ||
                    Math.Abs(sample.y - ground.y) > 3f) return false;
            }
            return true;
        }
    }
}
