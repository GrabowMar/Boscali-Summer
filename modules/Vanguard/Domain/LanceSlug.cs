using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>
    /// LANCE through-shot maths. From 78 % power the slug keeps going after the first impact: each part it crosses
    /// takes damage scaled by remaining speed, each further unit a 25 % cut, and its speed drops by what the armour
    /// ate (same rule as the game's ArmorPenetrate, but clamped so heavy armour stops it instead of reversing it).
    /// </summary>
    internal static class LanceSlug
    {
        public const int MaxUnits = 6, MaxParts = 20;
        public const float UnitFalloff = 0.75f;
        /// <summary>sqrt(0.1) * 3000: below this speed the slug has no punch left.</summary>
        public const float StopSpeed = 948.68f;

        public static bool ThroughEnabled(float power01) => power01 >= LanceCapacitor.ThroughMinPower;

        /// <summary>Metres beyond the first impact that the slug can still reach.</summary>
        public static float ThroughReach(float power01)
        {
            if (power01 < LanceCapacitor.ThroughMinPower) return 14f;
            float t = (power01 - LanceCapacitor.ThroughMinPower) / (1f - LanceCapacitor.ThroughMinPower);
            return 14f + 1486f * Mathf.Pow(Mathf.Clamp01(t), 1.3f);
        }

        public static float HitPierce(float power01, float speed, float dot, int unitIndex) =>
            LanceCapacitor.Pierce(power01) * (speed / LanceCapacitor.MuzzleVelocity) * Mathf.Max(dot, 0.5f) * Mathf.Pow(UnitFalloff, unitIndex);

        public static float Residual(float speed, float hitPierce, float pierceArmor) =>
            hitPierce <= pierceArmor * 2f ? 0f : speed * (hitPierce - 2f * pierceArmor) / hitPierce;

        public static bool Continues(float speed) => speed * speed > 0.1f * LanceCapacitor.MuzzleVelocity * LanceCapacitor.MuzzleVelocity;
    }
}
