using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    /// <summary>
    /// Executes the autonomous tactical evolution of trench networks from hasty scrapes
    /// into fortified, zig-zagging defense-in-depth belts based on combat time and threat pressure.
    /// </summary>
    internal static class TrenchGrowthSimulator
    {
        public static bool AdvanceSimulation(TrenchNetwork network, Func<Vector3, Vector3> terrainSampler = null)
        {
            if (network == null || network.NodeCount == 0) return false;

            // Remove destroyed elements before advancing
            network.RemoveDestroyedElements();
            if (network.NodeCount == 0) return false;

            bool changed = false;

            switch (network.Stage)
            {
                case TrenchStage.Stage0_Scrape:
                    changed = AdvanceStage0To1(network, terrainSampler);
                    break;

                case TrenchStage.Stage1_Crawl:
                    changed = AdvanceStage1To2(network, terrainSampler);
                    break;

                case TrenchStage.Stage2_FireTrench:
                    changed = AdvanceStage2To3(network, terrainSampler);
                    break;

                case TrenchStage.Stage3_Hardened:
                    changed = AdvanceStage3To4(network, terrainSampler);
                    break;

                case TrenchStage.Stage4_Integrated:
                    changed = MaintainAndRepair(network);
                    break;
            }

            return changed;
        }

        /// <summary>
        /// Stage 0 -> 1: Sapping adjacent foxholes into crawl trenches.
        /// </summary>
        private static bool AdvanceStage0To1(TrenchNetwork net, Func<Vector3, Vector3> terrainSampler)
        {
            bool edgeAdded = false;
            var nodeList = new List<TrenchNode>(net.Nodes);

            for (int i = 0; i < nodeList.Count; i++)
            {
                for (int j = i + 1; j < nodeList.Count; j++)
                {
                    TrenchNode a = nodeList[i];
                    TrenchNode b = nodeList[j];

                    float dist = Vector3.Distance(a.Position, b.Position);
                    if (dist >= 8f && dist <= 55f && !a.ConnectedNodeIds.Contains(b.Id))
                    {
                        var edge = net.AddEdge(a.Id, b.Id, TrenchEdgeType.CrawlTrench, TrenchStage.Stage1_Crawl, terrainSampler);
                        if (edge != null)
                        {
                            edgeAdded = true;
                            a.Stage = TrenchStage.Stage1_Crawl;
                            b.Stage = TrenchStage.Stage1_Crawl;
                        }
                    }
                }
            }

            if (edgeAdded || net.EdgeCount >= Math.Max(1, net.NodeCount - 1))
            {
                net.Stage = TrenchStage.Stage1_Crawl;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Stage 1 -> 2: Deepening crawlways into zig-zagging fire trenches with parapets.
        /// </summary>
        private static bool AdvanceStage1To2(TrenchNetwork net, Func<Vector3, Vector3> terrainSampler)
        {
            bool changed = false;

            foreach (var edge in net.Edges)
            {
                if (edge.Type == TrenchEdgeType.CrawlTrench)
                {
                    edge.Type = TrenchEdgeType.ZigzagFireTrench;
                    edge.Stage = TrenchStage.Stage2_FireTrench;
                    edge.TrenchWidth = TrenchEdge.GetDefaultWidth(edge.Type);
                    edge.ParapetHeight = TrenchEdge.GetDefaultParapetHeight(edge.Type, edge.Stage);

                    // Re-generate path with authentic zig-zag traverse offsets
                    TrenchNode a = net.GetNode(edge.NodeAId);
                    TrenchNode b = net.GetNode(edge.NodeBId);
                    if (a != null && b != null)
                    {
                        edge.PathPoints = TrenchEdge.GeneratePathPoints(
                            a.Position, b.Position, net.ThreatDirection, edge.Type, terrainSampler);
                    }
                    changed = true;
                }
            }

            foreach (var node in net.Nodes)
            {
                if (node.Type == TrenchNodeType.Foxhole)
                {
                    node.Type = TrenchNodeType.RifleBay;
                    node.Stage = TrenchStage.Stage2_FireTrench;
                    node.MaxHealth = TrenchNode.GetDefaultHealth(node.Type);
                    node.Health = node.MaxHealth;
                    changed = true;
                }
            }

            net.Stage = TrenchStage.Stage2_FireTrench;
            return changed;
        }

        /// <summary>
        /// Stage 2 -> 3: Hardening strongpoints, bunkers, weapon pits, and flank hooks.
        /// </summary>
        private static bool AdvanceStage2To3(TrenchNetwork net, Func<Vector3, Vector3> terrainSampler)
        {
            bool changed = false;

            // 1. Upgrade central junction nodes into Bunkers and Heavy Weapon Pits
            int bunkerCount = 0;
            int weaponPitCount = 0;

            foreach (var node in net.Nodes)
            {
                if (node.ConnectedEdgeIds.Count >= 2 && bunkerCount < 2 && node.Type == TrenchNodeType.RifleBay)
                {
                    node.Type = TrenchNodeType.BunkerBlindage;
                    node.Stage = TrenchStage.Stage3_Hardened;
                    node.MaxHealth = TrenchNode.GetDefaultHealth(node.Type);
                    node.Health = node.MaxHealth;
                    bunkerCount++;
                    changed = true;
                }
                else if (weaponPitCount < 2 && node.Type == TrenchNodeType.RifleBay)
                {
                    node.Type = TrenchNodeType.HeavyWeaponPit;
                    node.Stage = TrenchStage.Stage3_Hardened;
                    node.MaxHealth = TrenchNode.GetDefaultHealth(node.Type);
                    node.Health = node.MaxHealth;
                    weaponPitCount++;
                    changed = true;
                }
            }

            // 2. Flank Hook Rule: Protect exposed ends by curving them rearward
            var terminalNodes = new List<TrenchNode>();
            foreach (var node in net.Nodes)
            {
                if (node.ConnectedEdgeIds.Count == 1 && node.Type != TrenchNodeType.TerminalRamp)
                {
                    terminalNodes.Add(node);
                }
            }

            Vector3 rearward = -net.ThreatDirection;
            for (int i = 0; i < terminalNodes.Count; i++)
            {
                if (net.NodeCount >= TrenchNetwork.MaxNodesPerNetwork - 2) break;

                TrenchNode endNode = terminalNodes[i];
                // Project hook 16m to the rear
                Vector3 hookPos = endNode.Position + rearward * 16f;
                if (terrainSampler != null) hookPos = terrainSampler(hookPos);

                var hookNode = net.AddNode(hookPos, TrenchNodeType.TerminalRamp, TrenchStage.Stage3_Hardened);
                if (hookNode != null)
                {
                    net.AddEdge(endNode.Id, hookNode.Id, TrenchEdgeType.ZigzagFireTrench, TrenchStage.Stage3_Hardened, terrainSampler);
                    changed = true;
                }
            }

            net.Stage = TrenchStage.Stage3_Hardened;
            return changed;
        }

        /// <summary>
        /// Stage 3 -> 4: Establishing rearward communication trenches toward logistics/cover.
        /// </summary>
        private static bool AdvanceStage3To4(TrenchNetwork net, Func<Vector3, Vector3> terrainSampler)
        {
            bool changed = false;
            Vector3 rearward = -net.ThreatDirection;

            // Find up to 2 bunkers or junctions to extend communication lines from
            int commLines = 0;
            foreach (var node in net.Nodes)
            {
                if (commLines >= 2 || net.NodeCount >= TrenchNetwork.MaxNodesPerNetwork - 2) break;

                if (node.Type == TrenchNodeType.BunkerBlindage || node.ConnectedEdgeIds.Count >= 2)
                {
                    // Extend 35m to the rear
                    Vector3 commEndPos = node.Position + rearward * 35f;
                    if (terrainSampler != null) commEndPos = terrainSampler(commEndPos);

                    var commNode = net.AddNode(commEndPos, TrenchNodeType.TerminalRamp, TrenchStage.Stage4_Integrated);
                    if (commNode != null)
                    {
                        net.AddEdge(node.Id, commNode.Id, TrenchEdgeType.CommunicationTrench, TrenchStage.Stage4_Integrated, terrainSampler);
                        commLines++;
                        changed = true;
                    }
                }
            }

            net.Stage = TrenchStage.Stage4_Integrated;
            return changed;
        }

        /// <summary>
        /// Stage 4 maintenance: repairs damaged nodes and reinforces low-health sectors.
        /// </summary>
        private static bool MaintainAndRepair(TrenchNetwork net)
        {
            bool repaired = false;

            foreach (var node in net.Nodes)
            {
                if (node.Health < node.MaxHealth)
                {
                    node.Health = Math.Min(node.MaxHealth, node.Health + 25f);
                    repaired = true;
                }
            }
            foreach (var edge in net.Edges)
            {
                if (edge.Health < edge.MaxHealth)
                {
                    edge.Health = Math.Min(edge.MaxHealth, edge.Health + 40f);
                    repaired = true;
                }
            }

            return repaired;
        }

        /// <summary>
        /// Under-Attack Retrenchment: If a frontline node suffers critical damage,
        /// creates an emergency fallback chord 25m behind the breach to contain enemy breakthrough.
        /// </summary>
        public static bool TriggerEmergencyRetrenchment(TrenchNetwork net, int threatenedNodeId, Func<Vector3, Vector3> terrainSampler)
        {
            if (net == null || net.NodeCount >= TrenchNetwork.MaxNodesPerNetwork - 2) return false;

            TrenchNode threatened = net.GetNode(threatenedNodeId);
            if (threatened == null) return false;

            Vector3 rear = -net.ThreatDirection;
            Vector3 fallbackPos = threatened.Position + rear * 25f;
            if (terrainSampler != null) fallbackPos = terrainSampler(fallbackPos);

            var fallbackNode = net.AddNode(fallbackPos, TrenchNodeType.HeavyWeaponPit, TrenchStage.Stage3_Hardened);
            if (fallbackNode == null) return false;

            // Connect fallback node to neighbours of the threatened node
            for (int i = 0; i < threatened.ConnectedNodeIds.Count; i++)
            {
                int neighbourId = threatened.ConnectedNodeIds[i];
                net.AddEdge(fallbackNode.Id, neighbourId, TrenchEdgeType.ZigzagFireTrench, TrenchStage.Stage3_Hardened, terrainSampler);
            }

            return true;
        }
    }
}
