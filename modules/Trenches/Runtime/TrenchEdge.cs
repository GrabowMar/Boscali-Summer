using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    internal enum TrenchEdgeType
    {
        CrawlTrench = 0,        // Shallow connector between hasty foxholes
        ZigzagFireTrench = 1,   // Classic zigzag/traverse fire trench with raised parapet
        CommunicationTrench = 2,// Sinuous artery leading rearward to command/logistics
        BaffleEntry = 3         // 90-degree dog-leg blast barrier to dugouts/shelters
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
            SkirtDepth = 1.0f; // 1m skirt to penetrate slopes cleanly
            MaxHealth = 300f;
            Health = MaxHealth;
        }

        public static float GetDefaultWidth(TrenchEdgeType type)
        {
            switch (type)
            {
                case TrenchEdgeType.CrawlTrench: return 1.0f;
                case TrenchEdgeType.CommunicationTrench: return 1.8f;
                case TrenchEdgeType.BaffleEntry: return 1.2f;
                default: return 1.4f; // ZigzagFireTrench
            }
        }

        public static float GetDefaultParapetHeight(TrenchEdgeType type, TrenchStage stage)
        {
            if (stage == TrenchStage.Stage0_Scrape) return 0.4f;
            if (stage == TrenchStage.Stage1_Crawl) return 0.7f;
            if (type == TrenchEdgeType.CommunicationTrench) return 1.1f;
            return 1.6f; // Standard fortified parapet
        }

        public void TakeDamage(float amount)
        {
            Health = Math.Max(0f, Health - amount);
        }

        public bool IsDestroyed => Health <= 0f;

        /// <summary>
        /// Mathematically generates a zigzag traverse or sinuous polyline between two points.
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

            if (type == TrenchEdgeType.CrawlTrench || totalDist <= 10f)
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
                // Sinuous S-curve (sine wave pattern)
                int cuts = Math.Max(3, (int)(totalDist / 6.0f));
                float curveAmplitude = Math.Min(2.5f, totalDist * 0.15f);

                for (int i = 1; i < cuts; i++)
                {
                    float t = (float)i / cuts;
                    Vector3 basePoint = Vector3.Lerp(start, end, t);
                    float wave = Mathf.Sin(t * Mathf.PI * 2f);
                    Vector3 displaced = basePoint + side * (wave * curveAmplitude);
                    points.Add(terrainSampler != null ? terrainSampler(displaced) : displaced);
                }
            }
            else if (type == TrenchEdgeType.BaffleEntry)
            {
                // 90-degree dog-leg (start -> corner -> end)
                Vector3 mid = Vector3.Lerp(start, end, 0.5f);
                Vector3 dogLeg = mid + side * 3.0f;
                points.Add(terrainSampler != null ? terrainSampler(dogLeg) : dogLeg);
            }

            points.Add(terrainSampler != null ? terrainSampler(end) : end);
            return points.ToArray();
        }
    }
}
