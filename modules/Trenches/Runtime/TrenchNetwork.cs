using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Trenches.Domain;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    internal sealed class TrenchNetwork
    {
        public const int MaxNodesPerNetwork = 64;
        public const int MaxEdgesPerNetwork = 96;

        public int Id { get; }
        public string Name { get; set; }
        public FactionHQ OwnerHq { get; set; }
        public Vector3 Center { get; private set; }
        public Vector3 SeedCenter { get; }
        public Func<Vector3, bool> PlacementValidator { get; set; }
        public float Radius { get; private set; }
        public Vector3 ThreatDirection { get; set; }
        public Vector3 LateralAxis { get; }
        public float FlankLimit { get; }
        public float DepthLimit { get; }
        public float FrontHalfSpan { get; set; }

        public int BunkerCount
        {
            get
            {
                int count = 0;
                foreach (var node in nodes.Values) if (node.Type == TrenchNodeType.BunkerBlindage) count++;
                return count;
            }
        }
        public TrenchStage Stage { get; set; }
        public float LastSimTime { get; set; }
        public bool Overrun { get; set; }
        public bool Suppressed { get; set; }
        public int DefenderCount { get; set; }
        public float NextGrowthAt { get; set; }
        public float RetireAt { get; set; }

        private readonly Dictionary<int, TrenchNode> nodes = new Dictionary<int, TrenchNode>(MaxNodesPerNetwork);
        private readonly Dictionary<int, TrenchEdge> edges = new Dictionary<int, TrenchEdge>(MaxEdgesPerNetwork);

        private int nextNodeId = 1;
        private int nextEdgeId = 1;

        public IReadOnlyCollection<TrenchNode> Nodes => nodes.Values;
        public IReadOnlyCollection<TrenchEdge> Edges => edges.Values;
        public int NodeCount => nodes.Count;
        public int EdgeCount => edges.Count;

        public TrenchNetwork(int id, string name, FactionHQ owner, Vector3 center, Vector3 threatDir, float flankLimit = 0f)
        {
            Id = id;
            Name = name;
            OwnerHq = owner;
            Center = center;
            SeedCenter = center;
            ThreatDirection = threatDir.sqrMagnitude > 0.001f ? threatDir.normalized : Vector3.forward;
            LateralAxis = Vector3.Cross(Vector3.up, ThreatDirection).normalized;
            FlankLimit = flankLimit > 0f ? flankLimit : TrenchTacticalMath.MinFlankHalfLength;
            DepthLimit = TrenchTacticalMath.RearLineDepth + 20f;
            FrontHalfSpan = TrenchTacticalMath.LineSpan(TrenchTacticalMath.SeedBayCount) * 0.5f;
            Stage = TrenchStage.Stage0_Scrape;
            Radius = 25f;
        }

        /// <summary>True when a global position lies inside this network's fortified sector corridor.</summary>
        public bool Contains(Vector3 global)
        {
            Vector3 delta = global - SeedCenter;
            float lateral = Vector3.Dot(delta, LateralAxis);
            float forward = Vector3.Dot(delta, ThreatDirection);
            return Math.Abs(lateral) <= FlankLimit + 10f && forward >= -(DepthLimit + 10f) && forward <= 20f;
        }

        public TrenchNode AddNode(Vector3 position, TrenchNodeType type, TrenchStage stage = TrenchStage.Stage0_Scrape)
        {
            if (nodes.Count >= MaxNodesPerNetwork) return null;
            if (PlacementValidator != null && !PlacementValidator(position)) return null;

            int id = nextNodeId++;
            var node = new TrenchNode(id, position, ThreatDirection, type, stage);
            nodes[id] = node;
            RecalculateBounds();
            return node;
        }

        public TrenchEdge AddEdge(int nodeAId, int nodeBId, TrenchEdgeType type, TrenchStage stage = TrenchStage.Stage0_Scrape, Func<Vector3, Vector3> terrainSampler = null)
        {
            if (edges.Count >= MaxEdgesPerNetwork) return null;
            if (!nodes.TryGetValue(nodeAId, out TrenchNode nodeA) || !nodes.TryGetValue(nodeBId, out TrenchNode nodeB))
                return null;

            // Check if edge already exists
            foreach (var existing in edges.Values)
            {
                if ((existing.NodeAId == nodeAId && existing.NodeBId == nodeBId) ||
                    (existing.NodeAId == nodeBId && existing.NodeBId == nodeAId))
                {
                    return existing;
                }
            }

            Vector3[] path = TrenchEdge.GeneratePathPoints(nodeA.Position, nodeB.Position, ThreatDirection, type, terrainSampler);
            if (!CanPlacePath(path)) return null;
            int id = nextEdgeId++;
            var edge = new TrenchEdge(id, nodeAId, nodeBId, type, stage);
            edge.PathPoints = path;

            edges[id] = edge;
            nodeA.Connect(id, nodeBId);
            nodeB.Connect(id, nodeAId);

            RecalculateBounds();
            return edge;
        }

        public bool CanPlacePath(Vector3[] path, float clearance = TrenchTacticalMath.PathClearance)
        {
            if (PlacementValidator == null) return true;
            for (int i = 1; i < path.Length; i++)
            {
                Vector3 side = Vector3.Cross(Vector3.up, path[i] - path[i - 1]).normalized * clearance;
                int steps = Math.Max(1, (int)Math.Ceiling(Vector3.Distance(path[i - 1], path[i]) / 2f));
                for (int s = 0; s <= steps; s++)
                {
                    Vector3 p = Vector3.Lerp(path[i - 1], path[i], (float)s / steps);
                    if (!PlacementValidator(p) || !PlacementValidator(p - side) || !PlacementValidator(p + side)) return false;
                }
            }
            return true;
        }

        public TrenchNode GetNode(int id)
        {
            nodes.TryGetValue(id, out TrenchNode node);
            return node;
        }

        public TrenchEdge GetEdge(int id)
        {
            edges.TryGetValue(id, out TrenchEdge edge);
            return edge;
        }

        public void RecalculateBounds()
        {
            if (nodes.Count == 0) return;

            Vector3 sum = Vector3.zero;
            foreach (var node in nodes.Values)
            {
                sum += node.Position;
            }
            Center = sum / nodes.Count;

            float maxDistSq = 0f;
            foreach (var node in nodes.Values)
            {
                float dSq = (node.Position - Center).sqrMagnitude;
                if (dSq > maxDistSq) maxDistSq = dSq;
            }
            Radius = Math.Max(25f, Mathf.Sqrt(maxDistSq) + 15f);
        }

        public void RemoveDestroyedElements()
        {
            var deadEdges = new List<int>();
            foreach (var edge in edges.Values)
            {
                if (edge.IsDestroyed) deadEdges.Add(edge.Id);
            }
            for (int i = 0; i < deadEdges.Count; i++)
            {
                int edgeId = deadEdges[i];
                if (edges.TryGetValue(edgeId, out TrenchEdge edge))
                {
                    GetNode(edge.NodeAId)?.Disconnect(edgeId, edge.NodeBId);
                    GetNode(edge.NodeBId)?.Disconnect(edgeId, edge.NodeAId);
                    edges.Remove(edgeId);
                }
            }

            var deadNodes = new List<int>();
            foreach (var node in nodes.Values)
            {
                if (node.IsDestroyed) deadNodes.Add(node.Id);
            }
            for (int i = 0; i < deadNodes.Count; i++)
            {
                int nodeId = deadNodes[i];
                if (nodes.TryGetValue(nodeId, out TrenchNode node))
                {
                    // Disconnect and remove connected edges
                    for (int j = node.ConnectedEdgeIds.Count - 1; j >= 0; j--)
                    {
                        int edgeId = node.ConnectedEdgeIds[j];
                        if (edges.TryGetValue(edgeId, out TrenchEdge edge))
                        {
                            int otherId = (edge.NodeAId == nodeId) ? edge.NodeBId : edge.NodeAId;
                            GetNode(otherId)?.Disconnect(edgeId, nodeId);
                            edges.Remove(edgeId);
                        }
                    }
                    nodes.Remove(nodeId);
                }
            }

            if (deadNodes.Count > 0 || deadEdges.Count > 0)
            {
                RecalculateBounds();
            }
        }
    }
}
