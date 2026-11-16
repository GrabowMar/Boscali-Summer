using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.Effects;
using RoadPathfinding;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>Cell-build-only placement probes; native geometry and exclusion maps stay untouched.</summary>
    internal sealed class WorldDetailPlacement
    {
        private const float RoadClearance = 10f, BlockerPadding = 2f;
        private const int MaximumBlockers = 256, MaximumSegments = 512, MaximumRoadTests = 8192;
        private static readonly AccessTools.FieldRef<Road, Bounds> RoadBounds = FindRoadBounds();

        private readonly struct Segment
        {
            internal readonly Vector2 Start, Delta;
            internal readonly float InverseLength;

            internal Segment(Vector2 start, Vector2 end)
            {
                Start = start;
                Delta = end - start;
                InverseLength = Delta.sqrMagnitude > .0001f ? 1f / Delta.sqrMagnitude : 0f;
            }

            internal bool Near(Vector2 point)
            {
                float along = Mathf.Clamp01(Vector2.Dot(point - Start, Delta) * InverseLength);
                return (point - Start - Delta * along).sqrMagnitude <= RoadClearance * RoadClearance;
            }
        }

        private readonly List<TerrainHeightMap.Entry> providers = new List<TerrainHeightMap.Entry>(32);
        private readonly List<TerrainHeightMap.Entry> blockers = new List<TerrainHeightMap.Entry>(128);
        private readonly List<Bounds> blocked = new List<Bounds>(128);
        private readonly List<Segment> segments = new List<Segment>(64);
        private MapSettings map;
        private RoadNetwork network;
        private List<Road> roads;
        private PhysicMaterial terrainMaterial;
        private bool ready;
        internal bool Retryable { get; private set; }

        internal bool Begin(GlobalPosition center, float size)
        {
            ready = false;
            Retryable = true;
            blocked.Clear();
            segments.Clear();
            LevelInfo level = LevelInfo.i;
            MapSettings current = level != null ? level.LoadedMapSettings : null;
            if (current != map) { Reset(); map = current; }
            TerrainHeightMap height = SceneSingleton<TerrainHeightMap>.i;
            terrainMaterial = GameAssets.i != null ? GameAssets.i.terrainMaterial : null;
            if (current == null || Datum.origin == null || terrainMaterial == null ||
                height == null || !height.IsActive || !float.IsFinite(size) || size <= 0f ||
                !Finite(center.AsVector3())) return false;
            Retryable = false; // Budget rejection is terminal for this cell; availability may recover.

            // Its CPU grid includes non-collider roads and building blockers. It exists
            // only while the native height window is active; otherwise fail closed.
            height.GetObjectsInBounds(center, size + BlockerPadding * 2f, providers, blockers);
            if (blockers.Count > MaximumBlockers) return false;
            Vector3 origin = Datum.originPosition;
            float half = size * .5f;
            foreach (TerrainHeightMap.Entry entry in blockers)
            {
                if (entry == null || entry.renderer == null || !entry.renderer.gameObject.activeInHierarchy) continue;
                Bounds bounds = entry.renderer.bounds;
                bounds.center -= origin;
                bounds.Expand(BlockerPadding * 2f);
                if (!Finite(bounds.center) || !Finite(bounds.size)) return false;
                if (Overlaps(bounds.min.x, bounds.max.x, center.x - half, center.x + half) &&
                    Overlaps(bounds.min.z, bounds.max.z, center.z - half, center.z + half)) blocked.Add(bounds);
            }

            if (network != level.roadNetwork)
            {
                network = level.roadNetwork;
                roads = network != null ? network.roads : null;
            }
            if (roads != null && (RoadBounds == null || !CacheRoads(center, half + RoadClearance))) return false;
            ready = true;
            return true;
        }

        internal bool TryGround(float x, float z, out GlobalPosition position, out Vector3 normal)
        {
            position = default;
            normal = Vector3.up;
            if (!ready || map == null || LevelInfo.i == null || LevelInfo.i.LoadedMapSettings != map ||
                Datum.origin == null || terrainMaterial == null || !float.IsFinite(x) || !float.IsFinite(z) ||
                Mathf.Abs(x) >= map.MapSize.x * .5f || Mathf.Abs(z) >= map.MapSize.y * .5f) return false;
            foreach (Bounds bounds in blocked)
                if (x >= bounds.min.x && x <= bounds.max.x && z >= bounds.min.z && z <= bounds.max.z) return false;
            Vector2 point = new Vector2(x, z);
            foreach (Segment segment in segments) if (segment.Near(point)) return false;

            Vector3 from = new GlobalPosition(x, 5000f, z).ToLocalPosition();
            if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, 6000f,
                    PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore) || hit.collider == null ||
                hit.collider.sharedMaterial != terrainMaterial || hit.normal.y < .72f ||
                hit.point.y <= Datum.LocalSeaY + 1f || !Finite(hit.point) || !Finite(hit.normal)) return false;
            position = hit.point.ToGlobalPosition();
            normal = hit.normal;
            return true;
        }

        private bool CacheRoads(GlobalPosition center, float reach)
        {
            float minX = center.x - reach, maxX = center.x + reach;
            float minZ = center.z - reach, maxZ = center.z + reach;
            int tested = 0;
            foreach (Road road in roads)
            {
                if (++tested > MaximumRoadTests) return false;
                if (road == null || road.points == null || road.points.Count < 2) continue;
                Bounds bounds = RoadBounds(road); // Road.UpdateBB stores global coordinates.
                if (!Overlaps(bounds.min.x, bounds.max.x, minX, maxX) ||
                    !Overlaps(bounds.min.z, bounds.max.z, minZ, maxZ)) continue;
                for (int i = 1; i < road.points.Count; i++)
                {
                    if (++tested > MaximumRoadTests) return false;
                    GlobalPosition a = road.points[i - 1], b = road.points[i];
                    if (!Finite(a.AsVector3()) || !Finite(b.AsVector3())) return false;
                    if (!Overlaps(Mathf.Min(a.x, b.x), Mathf.Max(a.x, b.x), minX, maxX) ||
                        !Overlaps(Mathf.Min(a.z, b.z), Mathf.Max(a.z, b.z), minZ, maxZ)) continue;
                    if (segments.Count == MaximumSegments) return false;
                    segments.Add(new Segment(new Vector2(a.x, a.z), new Vector2(b.x, b.z)));
                }
            }
            return true;
        }

        internal void Reset()
        {
            ready = false;
            map = null;
            network = null;
            roads = null;
            terrainMaterial = null;
            providers.Clear();
            blockers.Clear();
            blocked.Clear();
            segments.Clear();
        }

        private static bool Overlaps(float aMin, float aMax, float bMin, float bMax)
            => aMin <= bMax && aMax >= bMin;

        private static bool Finite(Vector3 value)
            => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static AccessTools.FieldRef<Road, Bounds> FindRoadBounds()
        {
            try { return AccessTools.FieldRefAccess<Road, Bounds>("bounds"); }
            catch { return null; }
        }
    }
}
