using UnityEngine;

namespace BoscaliSummer.Modules.CombatAdjust.Domain
{
    /// <summary>What steers a missile, as far as countermeasures care.</summary>
    internal enum SeekerKind { None, Radar, Ir, Optical, Laser, AntiRadiation, Shell }

    /// <summary>When armour smokes, ships decoy and radars go dark, decided the same way on every peer.</summary>
    internal static class Triggers
    {
        public const float CloudMaxRadius = 25f, CloudGrowSeconds = 3f, CloudHoldUntil = 25f, CloudGoneAt = 28f;

        public static bool SmokeEligible(SeekerKind kind, bool armoured, int chargesLeft) =>
            armoured && chargesLeft > 0 && (kind == SeekerKind.Laser || kind == SeekerKind.Ir || kind == SeekerKind.Optical);

        public static bool Reacts(uint missileId, uint targetId, float chance) => Seeded.Roll01(missileId, targetId, 0x5307u) < chance;

        public static float ReactDelay(uint missileId, uint targetId) => Seeded.Range(missileId, targetId, 0x5308u, 0.6f, 1.8f);

        /// <summary>Smoke grows to 25 m in 3 s, holds to 25 s, thins away by 28 s.</summary>
        public static float CloudRadius(float age)
        {
            if (age <= 0f || age >= CloudGoneAt) return 0f;
            if (age < CloudGrowSeconds) return CloudMaxRadius * age / CloudGrowSeconds;
            if (age <= CloudHoldUntil) return CloudMaxRadius;
            return CloudMaxRadius * (CloudGoneAt - age) / (CloudGoneAt - CloudHoldUntil);
        }

        public static bool SegmentHitsSphere(Vector3 a, Vector3 b, Vector3 centre, float radius)
        {
            if (radius <= 0f) return false;
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(centre - a, ab) / len2) : 0f;
            return (centre - (a + ab * t)).sqrMagnitude <= radius * radius;
        }

        public static bool DecoyEligible(SeekerKind kind) =>
            kind == SeekerKind.Radar || kind == SeekerKind.Ir || kind == SeekerKind.Optical || kind == SeekerKind.Laser;

        /// <summary>Chance one decoy salvo seduces a missile: radar seekers 40 %, IR/TV/laser 25 %.</summary>
        public static float DecoyChance(SeekerKind kind) =>
            kind == SeekerKind.Radar ? 0.4f : DecoyEligible(kind) ? 0.25f : 0f;

        public static bool EmconReacts(uint missileId, uint radarUnitId, float chance) => Seeded.Roll01(missileId, radarUnitId, 0xE3C0u) < chance;

        public static float EmconDelay(uint missileId, uint radarUnitId) => Seeded.Range(missileId, radarUnitId, 0xE3C1u, 1f, 4f);
    }
}
