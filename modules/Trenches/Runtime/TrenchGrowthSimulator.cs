using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Trenches.Domain;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    /// <summary>
    /// Grows a trench network from a 7-bay frontline seed into a deliberate belt: an
    /// extended fire trench, a support line with a dugout at field depth, a rear redoubt
    /// line, and finally forward saps ending in listening posts toward the enemy.
    /// Every stage is atomic: an invalid terrain sample leaves the graph untouched and the
    /// stage is simply retried on the next growth tick.
    /// </summary>
    internal static class TrenchGrowthSimulator
    {
        /// <summary>Human-readable reason the last advance attempt was rejected; diagnostic only.</summary>
        internal static string LastFailure { get; private set; }

        private static bool Fail(string reason)
        {
            LastFailure = reason;
            return false;
        }

        public static bool Seed(TrenchNetwork net, Func<Vector3, Vector3> sample)
        {
            int bays = TrenchTacticalMath.SeedBayCount;
            TrenchNode previous = null;
            for (int i = 0; i < bays; i++)
            {
                float forward = i % 2 == 0 ? 0f : -7f;
                var node = net.AddNode(Position(net, TrenchTacticalMath.LineOffset(i, bays), forward, sample),
                    TrenchNodeType.RifleBay, TrenchStage.Stage1_Crawl);
                if (node == null) return false;
                if (previous != null && net.AddEdge(previous.Id, node.Id, TrenchEdgeType.ZigzagFireTrench,
                    TrenchStage.Stage1_Crawl, sample) == null) return false;
                previous = node;
            }
            net.FrontHalfSpan = TrenchTacticalMath.LineSpan(bays) * 0.5f;
            net.Stage = TrenchStage.Stage1_Crawl;
            return true;
        }

        public static bool AdvanceSimulation(TrenchNetwork net, Func<Vector3, Vector3> sample = null)
        {
            LastFailure = null;
            if (net == null || net.Overrun || net.NodeCount == 0) return false;
            if (net.Stage == TrenchStage.Stage6_Saps) return false; // A finished belt never grows endless filler.
            if (TrenchTacticalMath.EvaluateNextStage((int)net.Stage, net.NodeCount, net.EdgeCount, net.BunkerCount)
                != (int)net.Stage + 1) return Fail($"stage gate blocked at {net.Stage} ({(int)net.Stage}n/{net.NodeCount}n/{net.EdgeCount}e)");

            switch (net.Stage)
            {
                case TrenchStage.Stage1_Crawl: return DeepenFireTrench(net, sample);
                case TrenchStage.Stage2_FireTrench: return ExtendLine(net, sample);
                case TrenchStage.Stage3_Hardened: return BuildSupportLine(net, sample);
                case TrenchStage.Stage4_Integrated: return BuildRedoubt(net, sample);
                case TrenchStage.Stage5_Redoubt: return PushSaps(net, sample);
                default: return false; // No further stage exists.
            }
        }

        private static bool DeepenFireTrench(TrenchNetwork net, Func<Vector3, Vector3> sample)
        {
            var paths = new Dictionary<int, Vector3[]>(net.EdgeCount);
            foreach (var edge in net.Edges)
            {
                var path = TrenchEdge.GeneratePathPoints(net.GetNode(edge.NodeAId).Position,
                    net.GetNode(edge.NodeBId).Position, net.ThreatDirection, TrenchEdgeType.ZigzagFireTrench, sample);
                if (!net.CanPlacePath(path)) return Fail("deepen path rejected");
                paths.Add(edge.Id, path);
            }
            foreach (var edge in net.Edges)
            {
                edge.Type = TrenchEdgeType.ZigzagFireTrench;
                edge.Stage = TrenchStage.Stage2_FireTrench;
                edge.TrenchWidth = TrenchEdge.GetDefaultWidth(edge.Type);
                edge.ParapetHeight = TrenchEdge.GetDefaultParapetHeight(edge.Type, edge.Stage);
                edge.PathPoints = paths[edge.Id];
            }
            Upgrade(net, -44f, 0f, TrenchNodeType.HeavyWeaponPit);
            Upgrade(net, 44f, 0f, TrenchNodeType.HeavyWeaponPit);
            net.Stage = TrenchStage.Stage2_FireTrench;
            return true;
        }

        private static bool ExtendLine(TrenchNetwork net, Func<Vector3, Vector3> sample)
        {
            float target = Math.Min(88f, net.FlankLimit);
            var created = new List<TrenchNode>(2);
            if (!ExtendFlank(net, -target, 0f, sample, created) ||
                !ExtendFlank(net, target, 0f, sample, created))
            {
                Rollback(net, created);
                return Fail("flank extension rejected");
            }
            net.FrontHalfSpan = target;
            Upgrade(net, -target, 0f, TrenchNodeType.HeavyWeaponPit);
            Upgrade(net, target, 0f, TrenchNodeType.HeavyWeaponPit);
            net.Stage = TrenchStage.Stage3_Hardened;
            return true;
        }

        private static bool BuildSupportLine(TrenchNetwork net, Func<Vector3, Vector3> sample)
        {
            float depth = -TrenchTacticalMath.SupportLineDepth;
            var createdNodes = new List<TrenchNode>(3);
            var createdEdges = new List<TrenchEdge>(5);
            TrenchNode left = AddNode(net, -66f, depth, TrenchNodeType.TrenchJunction, TrenchStage.Stage4_Integrated, sample, createdNodes);
            TrenchNode center = AddNode(net, 0f, depth, TrenchNodeType.BunkerBlindage, TrenchStage.Stage4_Integrated, sample, createdNodes);
            TrenchNode right = AddNode(net, 66f, depth, TrenchNodeType.TrenchJunction, TrenchStage.Stage4_Integrated, sample, createdNodes);
            TrenchNode frontLeft = Nearest(net, -66f, 0f, 16f);
            TrenchNode frontCenter = Nearest(net, 0f, 0f, 16f);
            TrenchNode frontRight = Nearest(net, 66f, 0f, 16f);
            if (left == null || center == null || right == null ||
                !Link(net, frontLeft, left, sample, createdEdges) ||
                !Link(net, frontCenter, center, sample, createdEdges) ||
                !Link(net, frontRight, right, sample, createdEdges) ||
                !Link(net, left, center, sample, createdEdges) ||
                !Link(net, center, right, sample, createdEdges))
            {
                Rollback(net, createdNodes, createdEdges);
                return Fail("support line rejected");
            }
            net.Stage = TrenchStage.Stage4_Integrated;
            return true;
        }

        private static bool BuildRedoubt(TrenchNetwork net, Func<Vector3, Vector3> sample)
        {
            float supportDepth = -TrenchTacticalMath.SupportLineDepth;
            float rearDepth = -TrenchTacticalMath.RearLineDepth;
            // The fire and support trenches run to the full flank limit so the next
            // sector's ends meet this one and a junction trench can join the front line.
            float frontTarget = net.FlankLimit;
            float supportTarget = net.FlankLimit;
            var createdNodes = new List<TrenchNode>(18);
            var createdEdges = new List<TrenchEdge>(6);

            if (!ExtendFlank(net, -frontTarget, 0f, sample, createdNodes) ||
                !ExtendFlank(net, frontTarget, 0f, sample, createdNodes) ||
                !ExtendFlank(net, -supportTarget, supportDepth, sample, createdNodes) ||
                !ExtendFlank(net, supportTarget, supportDepth, sample, createdNodes))
            {
                Rollback(net, createdNodes, createdEdges);
                return Fail("redoubt flanks rejected");
            }

            TrenchNode rearLeft = AddNode(net, -44f, rearDepth, TrenchNodeType.HeavyWeaponPit, TrenchStage.Stage5_Redoubt, sample, createdNodes);
            TrenchNode rearCenter = AddNode(net, 0f, rearDepth, TrenchNodeType.BunkerBlindage, TrenchStage.Stage5_Redoubt, sample, createdNodes);
            TrenchNode rearRight = AddNode(net, 44f, rearDepth, TrenchNodeType.HeavyWeaponPit, TrenchStage.Stage5_Redoubt, sample, createdNodes);
            TrenchNode supportCenter = Nearest(net, 0f, supportDepth, 16f);
            if (!Link(net, rearLeft, rearCenter, sample, createdEdges) ||
                !Link(net, rearCenter, rearRight, sample, createdEdges) ||
                !Link(net, supportCenter, rearCenter, sample, createdEdges))
            {
                Rollback(net, createdNodes, createdEdges);
                return Fail("redoubt rear line rejected");
            }

            net.FrontHalfSpan = frontTarget;
            net.Stage = TrenchStage.Stage5_Redoubt;
            return true;
        }

        /// <summary>
        /// Final stage: two saps pushed forward from the fire line into no man's land,
        /// each ending in a small listening post that watches the enemy wire.
        /// </summary>
        private static bool PushSaps(TrenchNetwork net, Func<Vector3, Vector3> sample)
        {
            var createdNodes = new List<TrenchNode>(2);
            var createdEdges = new List<TrenchEdge>(2);
            for (int side = -1; side <= 1; side += 2)
            {
                TrenchNode anchor = Nearest(net, side * TrenchTacticalMath.SapLateralOffset, 0f, 12f);
                if (anchor == null)
                {
                    Rollback(net, createdNodes, createdEdges);
                    return Fail("sap anchor missing on the fire line");
                }
                Vector3 target = anchor.Position + net.ThreatDirection * TrenchTacticalMath.SapDepth;
                Vector3 position = sample != null ? sample(target) : target;
                var path = TrenchEdge.GeneratePathPoints(anchor.Position, position, net.ThreatDirection,
                    TrenchEdgeType.Sap, sample);
                if (!net.CanPlacePath(path))
                {
                    Rollback(net, createdNodes, createdEdges);
                    return Fail($"sap path blocked at lateral {side * TrenchTacticalMath.SapLateralOffset:0}m");
                }
                foreach (var existing in net.Nodes)
                {
                    if ((existing.Position - position).sqrMagnitude < 4f)
                    {
                        Rollback(net, createdNodes, createdEdges);
                        return Fail("sap head coincides with an existing position");
                    }
                }
                var node = net.AddNode(position, TrenchNodeType.Foxhole, TrenchStage.Stage6_Saps);
                if (node == null)
                {
                    Rollback(net, createdNodes, createdEdges);
                    return Fail("sap listening post rejected");
                }
                createdNodes.Add(node);
                var edge = net.AddEdge(anchor.Id, node.Id, TrenchEdgeType.Sap, TrenchStage.Stage6_Saps, sample);
                if (edge == null)
                {
                    Rollback(net, createdNodes, createdEdges);
                    return Fail("sap trench rejected");
                }
                createdEdges.Add(edge);
            }
            net.Stage = TrenchStage.Stage6_Saps;
            return true;
        }

        /// <summary>Adds bay nodes one step at a time until the flank reaches <paramref name="lateral"/>.</summary>
        private static bool ExtendFlank(TrenchNetwork net, float lateral, float forward,
            Func<Vector3, Vector3> sample, List<TrenchNode> created)
        {
            float sign = Math.Sign(lateral);
            float current = CurrentFlankEnd(net, forward, sign);
            int guard = 0;
            while (Math.Abs(lateral) - current > 1f && ++guard <= 8)
            {
                float next = Math.Min(Math.Abs(lateral), current + TrenchTacticalMath.FrontBaySpacing) * sign;
                TrenchNode from = Nearest(net, current * sign, forward, 20f);
                if (from == null) return Fail($"extension anchor missing at {current:0}m/{forward:0}m");
                if (!Extend(net, from, next, forward, TrenchNodeType.RifleBay,
                    TrenchStage.Stage5_Redoubt, sample, created)) return Fail($"extension rejected at {next:0}m/{forward:0}m");
                current = Math.Abs(next);
            }
            return true;
        }

        /// <summary>Furthest node on one signed flank of a line at <paramref name="forward"/>.</summary>
        private static float CurrentFlankEnd(TrenchNetwork net, float forward, float sign)
        {
            float end = 0f;
            foreach (var node in net.Nodes)
            {
                float nodeForward = Vector3.Dot(node.Position - net.SeedCenter, net.ThreatDirection);
                if (Math.Abs(nodeForward - forward) > 14f) continue;
                float lateral = Vector3.Dot(node.Position - net.SeedCenter, net.LateralAxis) * sign;
                if (lateral > end) end = lateral;
            }
            return end;
        }

        private static bool Extend(TrenchNetwork net, TrenchNode from, float lateral, float forward,
            TrenchNodeType type, TrenchStage stage, Func<Vector3, Vector3> sample, List<TrenchNode> created)
        {
            Vector3 position = Position(net, lateral, forward, sample);
            if ((from.Position - position).sqrMagnitude < 4f) return true;
            var path = TrenchEdge.GeneratePathPoints(from.Position, position, net.ThreatDirection,
                TrenchEdgeType.ZigzagFireTrench, sample);
            if (!net.CanPlacePath(path)) return Fail($"extension path blocked at {lateral:0}m/{forward:0}m");
            var node = AddNode(net, lateral, forward, type, stage, sample, created);
            if (node == null) return Fail($"extension node blocked at {lateral:0}m/{forward:0}m");
            return net.AddEdge(from.Id, node.Id, TrenchEdgeType.ZigzagFireTrench, stage, sample) != null
                || Fail($"extension edge blocked at {lateral:0}m/{forward:0}m");
        }

        private static bool Link(TrenchNetwork net, TrenchNode a, TrenchNode b,
            Func<Vector3, Vector3> sample, List<TrenchEdge> created)
        {
            if (a == null || b == null) return false;
            var path = TrenchEdge.GeneratePathPoints(a.Position, b.Position, net.ThreatDirection,
                TrenchEdgeType.CommunicationTrench, sample);
            if (!net.CanPlacePath(path)) return Fail($"link path blocked {a.Position.x:0},{a.Position.z:0} -> {b.Position.x:0},{b.Position.z:0}");
            var edge = net.AddEdge(a.Id, b.Id, TrenchEdgeType.CommunicationTrench,
                TrenchStage.Stage4_Integrated, sample);
            if (edge == null) return Fail($"link edge blocked {a.Position.x:0},{a.Position.z:0} -> {b.Position.x:0},{b.Position.z:0}");
            created?.Add(edge);
            return true;
        }

        private static TrenchNode AddNode(TrenchNetwork net, float lateral, float forward, TrenchNodeType type,
            TrenchStage stage, Func<Vector3, Vector3> sample, List<TrenchNode> created)
        {
            Vector3 position = Position(net, lateral, forward, sample);
            foreach (var existing in net.Nodes)
                if ((existing.Position - position).sqrMagnitude < 4f) return null;
            var node = net.AddNode(position, type, stage);
            if (node == null) return null;
            created?.Add(node);
            return node;
        }

        private static void Upgrade(TrenchNetwork net, float lateral, float forward, TrenchNodeType type)
        {
            TrenchNode node = Nearest(net, lateral, forward, 14f);
            if (node != null && node.Type != type) node.Type = type;
        }

        private static TrenchNode Nearest(TrenchNetwork net, float lateral, float forward, float tolerance)
        {
            Vector3 target = net.SeedCenter + net.LateralAxis * lateral + net.ThreatDirection * forward;
            TrenchNode best = null;
            float bestSq = tolerance * tolerance;
            foreach (var node in net.Nodes)
            {
                float dx = node.Position.x - target.x, dz = node.Position.z - target.z;
                float sq = dx * dx + dz * dz;
                if (sq < bestSq) { bestSq = sq; best = node; }
            }
            return best;
        }

        private static void Rollback(TrenchNetwork net, List<TrenchNode> nodes, List<TrenchEdge> edges = null)
        {
            if (edges != null)
                for (int i = 0; i < edges.Count; i++) edges[i]?.TakeDamage(edges[i].MaxHealth);
            if (nodes != null)
                for (int i = 0; i < nodes.Count; i++) nodes[i].TakeDamage(nodes[i].MaxHealth);
            net.RemoveDestroyedElements();
        }

        private static Vector3 Position(TrenchNetwork net, float lateral, float forward, Func<Vector3, Vector3> sample)
        {
            Vector3 position = net.SeedCenter + net.LateralAxis * lateral + net.ThreatDirection * forward;
            return sample != null ? sample(position) : position;
        }
    }
}
