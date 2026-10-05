using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Domain.Cyber
{
    /// <summary>A usable EW truck and its current reach (already scaled by its health).</summary>
    internal readonly struct EwSource
    {
        public readonly int Index;
        public readonly float X, Z, Reach;
        public EwSource(int index, float x, float z, float reach) { Index = index; X = x; Z = z; Reach = reach; }
    }

    /// <summary><see cref="From"/> is a node id (positive) or a truck, encoded as <c>-(index + 1)</c>.</summary>
    internal readonly struct CyberEdge
    {
        public readonly int From, To;
        public CyberEdge(int from, int to) { From = from; To = to; }
        public static int Truck(int index) => -(index + 1);
    }

    /// <summary>Spec §1.2 reachability: a node is reachable from an EW truck within its reach, or from a held node within 12 km.</summary>
    internal static class CyberGraph
    {
        public const float ChainReach = 12000f;
        public const int MaxNodes = 16, MaxEdges = 64;

        public static bool InReach(float ax, float az, float bx, float bz, float reach)
        {
            if (float.IsNaN(reach) || float.IsInfinity(reach) || reach <= 0f) return false;
            double dx = (double)ax - bx, dz = (double)az - bz;
            if (double.IsNaN(dx) || double.IsNaN(dz)) return false;
            return dx * dx + dz * dz <= (double)reach * reach;
        }

        /// <summary>
        /// True when <paramref name="node"/> can be hopped to. <paramref name="viaTruck"/> is the truck index of the nearest truck in reach
        /// (-1 when only a held node reaches it); <paramref name="viaNode"/> is the nearest held node in chain reach (0 when none).
        /// </summary>
        public static bool Reachable(in CyberNode node, IReadOnlyList<EwSource> trucks, IReadOnlyList<CyberNode> held, out int viaTruck, out int viaNode)
        {
            viaTruck = -1; viaNode = 0;
            double bestTruck = double.MaxValue, bestNode = double.MaxValue;
            for (int i = 0; trucks != null && i < trucks.Count; i++)
            {
                EwSource t = trucks[i];
                if (!InReach(t.X, t.Z, node.X, node.Z, t.Reach)) continue;
                double d = Dist2(t.X, t.Z, node.X, node.Z);
                if (d < bestTruck) { bestTruck = d; viaTruck = t.Index; }
            }
            for (int i = 0; held != null && i < held.Count; i++)
            {
                CyberNode h = held[i];
                if (h.Id == node.Id || !InReach(h.X, h.Z, node.X, node.Z, ChainReach)) continue;
                double d = Dist2(h.X, h.Z, node.X, node.Z);
                if (d < bestNode) { bestNode = d; viaNode = h.Id; }
            }
            return viaTruck >= 0 || viaNode != 0;
        }

        /// <summary>Closest-to-the-front first, ties by id; every id in <paramref name="keep"/> (held nodes) survives the cap.</summary>
        public static int Select(IReadOnlyList<CyberNode> candidates, HashSet<int> keep, int cap, List<CyberNode> into)
        {
            into.Clear();
            if (candidates == null || cap <= 0) return 0;
            var all = new List<CyberNode>(candidates);
            all.Sort((a, b) =>
            {
                bool ka = keep != null && keep.Contains(a.Id), kb = keep != null && keep.Contains(b.Id);
                if (ka != kb) return ka ? -1 : 1;
                int c = a.FrontDistance.CompareTo(b.FrontDistance);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });
            for (int i = 0; i < all.Count; i++)
            {
                bool kept = keep != null && keep.Contains(all[i].Id);
                if (!kept && into.Count >= cap) continue;
                into.Add(all[i]);
            }
            return into.Count;
        }

        /// <summary>Drawable edges: truck to each node in reach, held node to each node in chain reach. Bounded by <see cref="MaxEdges"/>.</summary>
        public static int Edges(IReadOnlyList<EwSource> trucks, IReadOnlyList<CyberNode> nodes, HashSet<int> heldIds, List<CyberEdge> into)
        {
            into.Clear();
            if (nodes == null) return 0;
            for (int n = 0; n < nodes.Count; n++)
            {
                for (int t = 0; trucks != null && t < trucks.Count && into.Count < MaxEdges; t++)
                    if (InReach(trucks[t].X, trucks[t].Z, nodes[n].X, nodes[n].Z, trucks[t].Reach))
                        into.Add(new CyberEdge(CyberEdge.Truck(trucks[t].Index), nodes[n].Id));
                if (heldIds == null) continue;
                for (int h = 0; h < nodes.Count && into.Count < MaxEdges; h++)
                    if (h != n && heldIds.Contains(nodes[h].Id) && InReach(nodes[h].X, nodes[h].Z, nodes[n].X, nodes[n].Z, ChainReach))
                        into.Add(new CyberEdge(nodes[h].Id, nodes[n].Id));
            }
            return into.Count;
        }

        private static double Dist2(float ax, float az, float bx, float bz)
        {
            double dx = (double)ax - bx, dz = (double)az - bz;
            return dx * dx + dz * dz;
        }
    }
}
