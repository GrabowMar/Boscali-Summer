using System;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Pure mathematical policy for stronghold armor mitigation and Damage FX surface calculations.
    /// Free of UnityEngine types so it is directly testable without an engine runner.
    /// </summary>
    internal static class StrongholdDamagePolicy
    {
        public const float DefaultMaxHp = 2500f;
        public const float DefaultPierceArmor = 25f;
        public const float DefaultBlastArmor = 50f;
        public const float DefaultCoolingRate = 0.65f;

        public static float CalculateMitigatedDamage(
            float pierceDamage,
            float blastDamage,
            float amountAffected,
            float fireDamage,
            float impactDamage,
            float pierceArmor,
            float blastArmor)
        {
            float effPierceArmor = Math.Max(0f, pierceArmor);
            float effBlastArmor = Math.Max(0f, blastArmor);
            float effAmount = Math.Max(0f, amountAffected);

            float netPierce = Math.Max(0f, pierceDamage - effPierceArmor) / Math.Max(effPierceArmor * 0.5f, 1f);
            float netBlast = (Math.Max(0f, blastDamage - effBlastArmor) * effAmount) / Math.Max(effBlastArmor * 0.5f, 1f);
            float netFire = Math.Max(0f, fireDamage - 15f) * 0.5f;
            float netImpact = Math.Max(0f, impactDamage);

            return netPierce + netBlast + netFire + netImpact;
        }

        public static float CalculateDamageFraction(float currentHp, float maxHp)
        {
            if (maxHp <= 0.001f) return 1f;
            float ratio = currentHp / maxHp;
            if (ratio <= 0f) return 1f;
            if (ratio >= 1f) return 0f;
            return 1f - ratio;
        }

        public static float CalculateThermalGlowAddition(float rawDamage, float blastYield)
        {
            float addition = (Math.Max(0f, rawDamage) * 0.005f) + (Math.Max(0f, blastYield) * 0.04f);
            return Math.Min(2.2f, Math.Max(0.35f, addition));
        }

        public static float CoolThermalGlow(float currentGlow, float coolingRate, float deltaTime)
        {
            float step = Math.Max(0f, coolingRate) * Math.Max(0f, deltaTime);
            return Math.Max(0f, currentGlow - step);
        }

        public static (float r, float g, float b) CalculateSootTint(float damageFraction)
        {
            float t = Math.Max(0f, Math.Min(1f, damageFraction)) * 0.88f;
            // Lerp from white (1.0) to soot (0.22, 0.20, 0.19)
            float r = 1f + t * (0.22f - 1f);
            float g = 1f + t * (0.20f - 1f);
            float b = 1f + t * (0.19f - 1f);
            return (r, g, b);
        }
    }
}
