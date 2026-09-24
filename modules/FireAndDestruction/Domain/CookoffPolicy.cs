using BoscaliSummer.Core;

namespace BoscaliSummer.Features.FireAndDestruction.Domain
{
    /// <summary>
    /// Delayed magazine pulses after a ground-vehicle wreck. Generation 0 is the death
    /// ignition; 1..MaxGeneration are follow-ups. Pure.
    /// </summary>
    internal static class CookoffPolicy
    {
        public const int MaxGeneration = 2;
        public const int MaxScheduled = 8;
        public const int MaxWreckNotices = 8;
        public const float MinDelay = 2f;
        public const float MaxDelay = 8f;

        public static uint Hash(int instanceId, int x25, int z25) =>
            Deterministic.Hash(instanceId, x25, z25, unchecked((int)0xC00FF01u));

        public static int FollowUps(uint hash)
        {
            float u = Deterministic.UnitFloat(hash);
            if (u < 0.35f) return 0;
            if (u < 0.8f) return 1;
            return 2;
        }

        public static float DelaySeconds(uint hash, int generation)
        {
            float u = Deterministic.UnitFloat(hash ^ ((uint)generation * 0x9E3779B9u));
            return MinDelay + u * (MaxDelay - MinDelay);
        }

        public static float DueAt(float born, uint hash, int generation)
        {
            float t = born;
            for (int g = 1; g <= generation; g++)
                t += DelaySeconds(hash, g);
            return t;
        }
    }
}
