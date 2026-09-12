using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    internal enum TrenchNodeType
    {
        Foxhole = 0,        // Stage 0: 1-2 soldier fighting position
        RifleBay = 1,       // Stage 1-2: standard firing bay with firing step
        HeavyWeaponPit = 2, // Stage 2-3: circular/octagonal revetted position for MG/ATGM
        DroneShelter = 3,   // Stage 3: undercut wall cave (lisya nora)
        BunkerBlindage = 4, // Stage 3-4: timber/earth command dugout or DOT pillbox
        TrenchJunction = 5, // T-junction / communication node
        TerminalRamp = 6    // Sump or entry ramp from ground level
    }

    internal enum TrenchStage
    {
        Stage0_Scrape = 0,       // Hasty prone/kneeling fighting scrapes
        Stage1_Crawl = 1,        // Sapped crawl trench connections
        Stage2_FireTrench = 2,   // Full 1.5m deep fire trench with parapets
        Stage3_Hardened = 3,     // Revetted bays, drone shelters, bunkers, flank hooks
        Stage4_Integrated = 4    // Multi-layered defense with rear communication lines
    }

    internal sealed class TrenchNode
    {
        public int Id { get; }
        public Vector3 Position { get; set; }
        public Vector3 ThreatDirection { get; set; }
        public Quaternion Rotation { get; set; }
        public TrenchNodeType Type { get; set; }
        public TrenchStage Stage { get; set; }
        public float Health { get; set; }
        public float MaxHealth { get; set; }

        public List<int> ConnectedEdgeIds { get; } = new List<int>(6);
        public List<int> ConnectedNodeIds { get; } = new List<int>(6);

        public TrenchNode(int id, Vector3 position, Vector3 threatDirection, TrenchNodeType type, TrenchStage stage = TrenchStage.Stage0_Scrape)
        {
            Id = id;
            Position = position;
            ThreatDirection = threatDirection.sqrMagnitude > 0.001f ? threatDirection.normalized : Vector3.forward;
            Rotation = Quaternion.LookRotation(ThreatDirection, Vector3.up);
            Type = type;
            Stage = stage;
            MaxHealth = GetDefaultHealth(type);
            Health = MaxHealth;
        }

        public static float GetDefaultHealth(TrenchNodeType type)
        {
            switch (type)
            {
                case TrenchNodeType.BunkerBlindage: return 800f;
                case TrenchNodeType.HeavyWeaponPit: return 400f;
                case TrenchNodeType.DroneShelter: return 350f;
                case TrenchNodeType.RifleBay: return 200f;
                case TrenchNodeType.Foxhole: return 120f;
                default: return 150f;
            }
        }

        public void TakeDamage(float amount)
        {
            Health = Math.Max(0f, Health - amount);
        }

        public bool IsDestroyed => Health <= 0f;

        public void Connect(int edgeId, int otherNodeId)
        {
            if (!ConnectedEdgeIds.Contains(edgeId)) ConnectedEdgeIds.Add(edgeId);
            if (!ConnectedNodeIds.Contains(otherNodeId)) ConnectedNodeIds.Add(otherNodeId);
        }

        public void Disconnect(int edgeId, int otherNodeId)
        {
            ConnectedEdgeIds.Remove(edgeId);
            ConnectedNodeIds.Remove(otherNodeId);
        }
    }
}
