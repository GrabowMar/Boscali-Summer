using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    internal static class TrenchGrowthSimulator
    {
        public static bool Seed(TrenchNetwork net, Func<Vector3, Vector3> sample)
        {
            // A connected 72m fighting line from the first visible frame.
            TrenchNode previous = null;
            for (int i = 0; i < 5; i++)
            {
                var node = net.AddNode(Position(net, (i - 2) * 18f, i % 2 == 0 ? 0 : -5f, sample),
                    TrenchNodeType.RifleBay, TrenchStage.Stage1_Crawl);
                if (node == null) return false;
                if (previous != null && net.AddEdge(previous.Id, node.Id, TrenchEdgeType.CrawlTrench,
                    TrenchStage.Stage1_Crawl, sample) == null) return false;
                previous = node;
            }
            net.Stage = TrenchStage.Stage1_Crawl;
            return true;
        }

        public static bool AdvanceSimulation(TrenchNetwork net, Func<Vector3, Vector3> sample = null)
        {
            if (net == null || net.Overrun || net.NodeCount == 0) return false;
            if (net.Stage == TrenchStage.Stage1_Crawl)
            {
                var paths = new Dictionary<int, Vector3[]>();
                foreach (var edge in net.Edges)
                {
                    var path = TrenchEdge.GeneratePathPoints(net.GetNode(edge.NodeAId).Position,
                        net.GetNode(edge.NodeBId).Position, net.ThreatDirection, TrenchEdgeType.ZigzagFireTrench, sample);
                    if (!net.CanPlacePath(path)) return false;
                    paths.Add(edge.Id, path);
                }
                foreach (var edge in net.Edges)
                {
                    edge.Type = TrenchEdgeType.ZigzagFireTrench;
                    edge.Stage = TrenchStage.Stage2_FireTrench;
                    edge.TrenchWidth = 1.8f;
                    edge.ParapetHeight = 1.6f;
                    edge.PathPoints = paths[edge.Id];
                }
                net.Stage = TrenchStage.Stage2_FireTrench;
                foreach (var node in net.Nodes) node.Stage = net.Stage;
                return true;
            }
            if (net.Stage == TrenchStage.Stage2_FireTrench)
            {
                // Second line and lateral communications. Each addition is atomic;
                // invalid terrain never leaves an isolated node or advances the stage.
                if (!AddBranch(net, 1, -30, -25, sample) ||
                    !AddBranch(net, 3, 0, -25, sample) ||
                    !AddBranch(net, 5, 30, -25, sample)) return false;
                LinkRearLine(net, -25, sample);
                net.Stage = TrenchStage.Stage3_Hardened;
                return true;
            }
            if (net.Stage == TrenchStage.Stage3_Hardened)
            {
                int from = 3;
                Vector3 rearCenter = Position(net, 0, -25, sample);
                foreach (var node in net.Nodes) if ((node.Position - rearCenter).sqrMagnitude < 1f) from = node.Id;
                if (!AddBranch(net, from, 0, -48, sample)) return false;
                net.Stage = TrenchStage.Stage4_Integrated;
                return true;
            }
            return false; // No automatic healing or endless fortification generation.
        }

        private static Vector3 Position(TrenchNetwork net, float lateral, float forward, Func<Vector3, Vector3> sample)
        {
            Vector3 position = net.SeedCenter + Vector3.Cross(Vector3.up, net.ThreatDirection) * lateral + net.ThreatDirection * forward;
            return sample != null ? sample(position) : position;
        }

        private static bool AddBranch(TrenchNetwork net, int from, float lateral, float forward, Func<Vector3, Vector3> sample)
        {
            Vector3 position = Position(net, lateral, forward, sample);
            foreach (var existing in net.Nodes)
                if ((existing.Position - position).sqrMagnitude < 1f) return true;
            var start = net.GetNode(from);
            if (start == null) return false;
            var path = TrenchEdge.GeneratePathPoints(start.Position, position, net.ThreatDirection,
                TrenchEdgeType.CommunicationTrench, sample);
            if (!net.CanPlacePath(path)) return false;
            var node = net.AddNode(position, TrenchNodeType.TrenchJunction, TrenchStage.Stage3_Hardened);
            if (node == null) return false;
            var edge = net.AddEdge(start.Id, node.Id, TrenchEdgeType.CommunicationTrench, TrenchStage.Stage3_Hardened, sample);
            if (edge != null) return true;
            node.TakeDamage(node.MaxHealth);
            net.RemoveDestroyedElements();
            return false;
        }

        private static void LinkRearLine(TrenchNetwork net, float depth, Func<Vector3, Vector3> sample)
        {
            var rear = new List<TrenchNode>();
            foreach (var node in net.Nodes)
                if (Math.Abs(Vector3.Dot(node.Position - net.SeedCenter, net.ThreatDirection) - depth) < 1f) rear.Add(node);
            Vector3 side = Vector3.Cross(Vector3.up, net.ThreatDirection);
            rear.Sort((a, b) => Vector3.Dot(a.Position, side).CompareTo(Vector3.Dot(b.Position, side)));
            for (int i = 1; i < rear.Count; i++)
                net.AddEdge(rear[i - 1].Id, rear[i].Id, TrenchEdgeType.CommunicationTrench, TrenchStage.Stage3_Hardened, sample);
        }
    }
}
