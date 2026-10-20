using System;
using System.Collections.Generic;

using BoscaliSummer.Core.Math;
namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Where a field launch without a hangar stands, and where it joins the taxi graph.</summary>
    internal struct ServiceSpot
    {
        public int Point;
        public WingPose WingPose;
        public int StartNode;
        /// <summary>The queue spot and remaining edge bends; StartNode supplies the far end.</summary>
        public Vec3[] TaxiPrefix;
    }

    /// <summary>Where a field launch without a hangar stands (spec M3 §3.1): on service point j as the game parks it, or
    /// place l of a queue on that point's way out, <see cref="Spread"/>·l along its route to the hold-short and facing
    /// along it (they leave in route order, the parked one last, and nobody turns out across another); the first spot
    /// with no aircraft within <see cref="ClearRadius"/>, points nearer the hold-short by route first, every point before a second place, up to
    /// <see cref="MaxLanes"/>, never within <see cref="Spread"/> of the hold-short or on a runway. Fixed-wing spots
    /// must be near the field's actual taxi roads when it has roads; vertical launches do not need that access.</summary>
    internal static class ServiceSpots
    {
        public static float Spread = 30f, ClearRadius = 15f;
        public static int MaxLanes = 4;

        public static bool Pick(FieldTraffic field, Func<Vec3, bool> occupied, out ServiceSpot spot, bool taxiRequired = true)
        {
            var nodes = new List<int>();
            var edges = new List<int>();
            int[] order = ByRoute(field, nodes, edges);
            for (int lane = 0; lane < MaxLanes; lane++)
                foreach (int j in order)
                    if (TrySpot(field, j, lane, nodes, edges, taxiRequired, out spot) && !occupied(spot.WingPose.Pos)) return true;
            spot = default;
            return false;
        }

        /// <summary>Service points by taxi route to the departure hold-short, shortest first (index order breaks ties; sweep
        /// 2026-10-18, airbase_city: in index order launches stood at the far end of the field from the departure end).</summary>
        private static int[] ByRoute(FieldTraffic field, List<int> nodes, List<int> edges)
        {
            TaxiGraph g = field.Graph;
            int n = field.Field.ServicePoints.Length, goal = g.HoldShort(field.RunwayIndex, field.Reverse);
            var order = new int[n];
            var keys = new double[n];
            for (int j = 0; j < n; j++)
            {
                order[j] = j;
                double length = 1e9;
                if (goal >= 0 && TaxiRouter.Route(g, g.ServiceNode(j), goal, null, nodes, edges))
                {
                    length = 0;
                    foreach (int e in edges) length += g.EdgeLength(e);
                }
                keys[j] = length + j * 1e-3;
            }
            Array.Sort(keys, order);
            return order;
        }

        private static bool TrySpot(FieldTraffic field, int j, int lane, List<int> nodes, List<int> edges, bool taxiRequired, out ServiceSpot spot)
        {
            TaxiGraph g = field.Graph;
            int node = g.ServiceNode(j), goal = g.HoldShort(field.RunwayIndex, field.Reverse);
            spot = new ServiceSpot { Point = j, WingPose = field.Field.ServicePoints[j], StartNode = node };
            // Some strips have a stand at the opposite runway end but no taxi road there. A synthetic graph connector
            // crosses hundreds of metres of grass; use another place on a serviced taxi route instead.
            if (taxiRequired && !NearTaxiRoad(field.Field, spot.WingPose.Pos)) return false;
            if (lane == 0 && !taxiRequired) return true;
            if (node < 0 || goal < 0 || !TaxiRouter.Route(g, node, goal, null, nodes, edges)) return false;
            if (lane == 0) return true;
            float want = lane * Spread, walked = 0f;
            for (int k = 0; k < edges.Count; k++)
            {
                float length = g.EdgeLength(edges[k]);
                if (walked + length < want)
                {
                    walked += length;
                    continue;
                }
                if (k == edges.Count - 1 && walked + length - want < Spread) return false;
                Vec3[] pts = g.EdgePoints(edges[k]);
                bool forward = g.EdgeFrom(edges[k]) == nodes[k];
                float left = want - walked;
                for (int i = 1; i < pts.Length; i++)
                {
                    Vec3 a = forward ? pts[i - 1] : pts[pts.Length - i], b = forward ? pts[i] : pts[pts.Length - 1 - i];
                    float segment = (b - a).Horizontal.Length;
                    if (segment < left && i < pts.Length - 1)
                    {
                        left -= segment;
                        continue;
                    }
                    Vec3 dir = (b - a).Horizontal.Normalized;
                    Vec3 at = a + (b - a) * Math.Min(1f, segment > 1e-4f ? left / segment : 0f);
                    foreach (RunwaySample r in field.Field.Runways)
                        if (r.Contains(at, ClearRadius)) return false;
                    var prefix = new Vec3[pts.Length - i];
                    prefix[0] = at;
                    for (int q = i; q < pts.Length - 1; q++) prefix[q - i + 1] = forward ? pts[q] : pts[pts.Length - 1 - q];
                    spot = new ServiceSpot { Point = j, WingPose = new WingPose(at, dir), StartNode = nodes[k + 1], TaxiPrefix = prefix };
                    return true;
                }
            }
            return false;
        }

        private static bool NearTaxiRoad(AirbaseSample field, Vec3 pos)
        {
            bool roads = false;
            foreach (Vec3[] road in field.Roads)
            {
                if (road == null || road.Length < 2) continue;
                roads = true;
                for (int k = 1; k < road.Length; k++)
                {
                    Vec3 a = road[k - 1].Horizontal, ab = road[k].Horizontal - a;
                    float t = ab.SqrLength < 1e-6f ? 0f : Scalar.Clamp(Vec3.Dot(pos.Horizontal - a, ab) / ab.SqrLength, 0f, 1f);
                    if ((a + ab * t - pos.Horizontal).Length <= TaxiGraph.EntrySearchRadius) return true;
                }
            }
            return !roads;
        }
    }
}
