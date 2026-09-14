using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    internal enum TrenchEdgeType
    {
        CrawlTrench = 0,        // Shallow connector between hasty foxholes
        ZigzagFireTrench = 1,   // Classic zigzag/traverse fire trench with raised parapet
        CommunicationTrench = 2,// Traversed artery leading rearward to command/logistics
        Sap = 3                 // Narrow forward crawl trench toward the enemy wire
    }

    internal sealed class TrenchEdge
    {
        public int Id { get; }
        public int NodeAId { get; }
        public int NodeBId { get; }
        public TrenchEdgeType Type { get; set; }
        public TrenchStage Stage { get; set; }
        public float Health { get; set; }
        public float MaxHealth { get; set; }

        public float TrenchWidth { get; set; }
        public float ParapetHeight { get; set; }
        public float SkirtDepth { get; set; }

        /// <summary>
        /// Ordered 3D points forming the polyline path of this trench edge.
        /// </summary>
        public Vector3[] PathPoints { get; set; } = Array.Empty<Vector3>();

        public TrenchEdge(int id, int nodeAId, int nodeBId, TrenchEdgeType type, TrenchStage stage = TrenchStage.Stage0_Scrape)
        {
            Id = id;
            NodeAId = nodeAId;
            NodeBId = nodeBId;
            Type = type;
            Stage = stage;

            TrenchWidth = GetDefaultWidth(type);
            ParapetHeight = GetDefaultParapetHeight(type, stage);
            SkirtDepth = 1.4f; // Deep skirt so wide earthworks never float on a slope
            MaxHealth = 300f;
            Health = MaxHealth;
        }

        public static float GetDefaultWidth(TrenchEdgeType type)
        {
            switch (type)
            {
                case TrenchEdgeType.CrawlTrench: return 1.4f;
                case TrenchEdgeType.CommunicationTrench: return 2.2f;
                case TrenchEdgeType.Sap: return 1.1f;
                default: return 2.6f; // ZigzagFireTrench
            }
        }

        public static float GetDefaultParapetHeight(TrenchEdgeType type, TrenchStage stage)
        {
            if (stage == TrenchStage.Stage0_Scrape) return 0.5f;
            if (stage == TrenchStage.Stage1_Crawl) return 1.0f;
            if (type == TrenchEdgeType.CommunicationTrench) return 1.7f;
            if (type == TrenchEdgeType.Sap) return 0.8f; // Shallow, exposed forward crawl
            return 2.4f; // Standard fortified parapet
        }

        public void TakeDamage(float amount)
        {
            Health = Math.Max(0f, Health - amount);
        }

        public bool IsDestroyed => Health <= 0f;

        /// <summary>
        /// Generates the traversed ditch polyline between two points. Fire and communication
        /// trenches turn through fire bays and traverses; a sap is a tight forward crawl.
        /// Raycasting against terrain is delegated to the optional terrainSampler function.
        /// </summary>
        public static Vector3[] GeneratePathPoints(
            Vector3 start,
            Vector3 end,
            Vector3 threatDirection,
            TrenchEdgeType type,
            Func<Vector3, Vector3> terrainSampler = null)
        {
            Vector3 diff = end - start;
            float totalDist = diff.magnitude;
            if (totalDist < 1.0f)
            {
                return new[] { start, end };
            }

            Vector3 forward = diff / totalDist;
            // Lateral normal perpendicular to path direction on the horizontal plane
            Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
            if (side.sqrMagnitude < 0.001f) side = Vector3.right;

            // Ensure lateral offset turns favor the parapet facing threat
            if (Vector3.Dot(side, threatDirection) < 0f)
            {
                side = -side;
            }

            var points = new List<Vector3>();
            points.Add(terrainSampler != null ? terrainSampler(start) : start);

            if (type == TrenchEdgeType.Sap)
            {
                // Narrow sap: short zigzag bays so blast and fire cannot sweep the whole ditch.
                int bays = Math.Max(2, (int)Math.Round(totalDist / 4.5f));
                float traverseOffset = Math.Min(1.2f, totalDist * 0.12f);
                for (int i = 1; i < bays; i++)
                {
                    float t = (float)i / bays;
                    Vector3 basePoint = Vector3.Lerp(start, end, t);
                    float sign = (i % 2 == 1) ? 1.0f : -0.7f;
                    Vector3 displaced = basePoint + side * (traverseOffset * sign);
                    points.Add(terrainSampler != null ? terrainSampler(displaced) : displaced);
                }
            }
            else if (type == TrenchEdgeType.CrawlTrench || totalDist <= 10f)
            {
                // Simple subdivide for terrain conformation
                int cuts = Math.Max(1, (int)(totalDist / 5.0f));
                for (int i = 1; i < cuts; i++)
                {
                    float t = (float)i / cuts;
                    Vector3 p = Vector3.Lerp(start, end, t);
                    points.Add(terrainSampler != null ? terrainSampler(p) : p);
                }
            }
            else if (type == TrenchEdgeType.ZigzagFireTrench)
            {
                // Alternating +/- traverse zigzag every 6m - 9m
                float segmentLength = 7.5f;
                int bays = Math.Max(2, (int)Math.Round(totalDist / segmentLength));
                float traverseOffset = Math.Min(3.2f, totalDist * 0.25f);

                for (int i = 1; i < bays; i++)
                {
                    float t = (float)i / bays;
                    Vector3 basePoint = Vector3.Lerp(start, end, t);
                    // Alternating traverse displacement
                    float sign = (i % 2 == 1) ? 1.0f : -0.6f;
                    Vector3 displaced = basePoint + side * (traverseOffset * sign);
                    points.Add(terrainSampler != null ? terrainSampler(displaced) : displaced);
                }
            }
            else if (type == TrenchEdgeType.CommunicationTrench)
            {
                // Traversed artery: regular zigzag bays keep the ditch safe from enfilade
                // fire and read as a link trench, not a smooth road.
                int bays = Math.Max(3, (int)Math.Round(totalDist / 9.0f));
                float traverseOffset = Math.Min(2.2f, totalDist * 0.12f);

                for (int i = 1; i < bays; i++)
                {
                    float t = (float)i / bays;
                    Vector3 basePoint = Vector3.Lerp(start, end, t);
                    float sign = (i % 2 == 1) ? 1.0f : -0.8f;
                    Vector3 displaced = basePoint + side * (traverseOffset * sign);
                    points.Add(terrainSampler != null ? terrainSampler(displaced) : displaced);
                }
            }

            points.Add(terrainSampler != null ? terrainSampler(end) : end);
            return points.ToArray();
        }
    }
}
