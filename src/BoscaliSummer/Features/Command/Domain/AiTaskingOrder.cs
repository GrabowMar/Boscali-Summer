using UnityEngine;

namespace BoscaliSummer.Features.Command.Domain
{
    internal enum AiMissionType : byte
    {
        CombatAirPatrol = 0,
        Strike = 1,
        CloseAirSupport = 2,
        SEAD = 3,
        ReturnToBase = 4,
        Transport = 5,
        Convoy = 6
    }

    internal sealed class AiTaskingOrder
    {
        public Unit Unit;
        public string Callsign;
        public AiMissionType MissionType;
        public Vector3 OriginWorld;
        public Vector3 TargetWorld;
        public Unit CurrentTarget;
        public string TargetName;
        public bool IsFriendly;
        public float EstimatedRange;
        public Color MissionColor;

        public static Color GetMissionColor(AiMissionType type)
        {
            switch (type)
            {
                case AiMissionType.CombatAirPatrol:
                    return new Color(0.2f, 0.8f, 1.0f, 0.85f); // Cyan
                case AiMissionType.Strike:
                    return new Color(1.0f, 0.45f, 0.15f, 0.85f); // Amber / Orange
                case AiMissionType.CloseAirSupport:
                    return new Color(1.0f, 0.8f, 0.1f, 0.85f); // Yellow
                case AiMissionType.SEAD:
                    return new Color(0.85f, 0.25f, 0.95f, 0.85f); // Magenta
                case AiMissionType.ReturnToBase:
                    return new Color(0.3f, 0.9f, 0.4f, 0.75f); // Soft green
                case AiMissionType.Transport:
                    return new Color(0.4f, 0.7f, 0.9f, 0.75f); // Steel blue
                case AiMissionType.Convoy:
                    return new Color(0.9f, 0.6f, 0.2f, 0.8f); // Ochre
                default:
                    return Color.white;
            }
        }
    }
}
