using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    internal sealed class TrenchNetwork
    {
        public const int MaxNodesPerNetwork = 32;
        public const int MaxEdgesPerNetwork = 48;

        public int Id { get; }
        public string Name { get; set; }
        public FactionHQ OwnerHq { get; set; }
        public Vector3 Center { get; private set; }
        public float Radius { get; private set; }
        public Vector3 ThreatDirection { get; set; }
        public TrenchStage Stage { get; set; }
        public float LastSimTime { get; set; }

        private readonly Dictionary<int, TrenchNode> nodes = new Dictionary<int, TrenchNode>(MaxNodesPerNetwork);
        private readonly Dictionary<int, TrenchEdge> edges = new Dictionary<int, TrenchEdge>(MaxEdgesPerNetwork);

        private int nextNodeId = 1;
        private int nextEdgeId = 1;

        public IReadOnlyCollection<TrenchNode> Nodes => nodes.Values;
        public IReadOnlyCollection<TrenchEdge> Edges => edges.Values;
        public int NodeCount => nodes.Count;
        public int EdgeCount => edges.Count;

        public TrenchNetwork(int id, string name, FactionHQ owner, Vector3 center, Vector3 threatDir)
        {
            Id = id;
            Name = name;
            OwnerHq = owner;
            Center = center;
            ThreatDirection = threatDir.sqrMagnitude > 0.001f ? threatDir.normalized : Vector3.forward;
            Stage = TrenchStage.Stage0_Scrape;
            Radius = 25f;
        }

        public TrenchNode AddNode(Vector3 position, TrenchNodeType type, TrenchStage stage = TrenchStage.Stage0_Scrape)
        {
            if (nodes.Count >= MaxNodesPerNetwork) return null;

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

            int id = nextEdgeId++;
            var edge = new TrenchEdge(id, nodeAId, nodeBId, type, stage);
            edge.PathPoints = TrenchEdge.GeneratePathPoints(nodeA.Position, nodeB.Position, ThreatDirection, type, terrainSampler);

            edges[id] = edge;
            nodeA.Connect(id, nodeBId);
            nodeB.Connect(id, nodeAId);

            RecalculateBounds();
            return edge;
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
