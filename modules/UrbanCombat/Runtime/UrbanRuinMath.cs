using System;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Pure staging for occupied-shell damage: hits scar the rooftop nest's markings, and a
    /// ravaged shell stops counting as a siege strongpoint even before it collapses.
    /// Fractions are occupy-relative (damage sustained while garrisoned), so re-occupying a
    /// battered building starts a fresh assessment instead of guessing vanilla max HP.
    /// </summary>
    internal static class UrbanRuinMath
    {
        public const int StageIntact = 0;
        public const int StageScarred = 1;
        public const int StageRavaged = 2;

        public const float ScarredAt = 0.35f;
        public const float RavagedAt = 0.75f;

        public static int DamageStage(float damageFraction)
        {
            if (damageFraction >= RavagedAt) return StageRavaged;
            if (damageFraction >= ScarredAt) return StageScarred;
            return StageIntact;
        }

        public static float DamageFraction(float maxHp, float currentHp)
        {
            if (maxHp <= 0f) return 0f;
            return Math.Max(0f, Math.Min(1f, 1f - currentHp / maxHp));
        }

        public static bool CountsAsStrongpoint(float damageFraction) =>
            DamageStage(damageFraction) < StageRavaged;
    }
}
