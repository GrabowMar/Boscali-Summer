using System;

namespace BoscaliSummer.Modules.Support.Domain.Calls
{
    /// <summary>Core §5.1: objectives gate tiers honestly; tiny maps fall back to a time unlock.</summary>
    internal static class CallFloors
    {
        public const float TimeUnlockMinutes = 8f;

        public static float Share(int held, int contested, int n)
        {
            if (n <= 0) return float.NaN;
            float share = (Math.Max(0, held) + 0.5f * Math.Max(0, contested)) / n;
            return Math.Max(0f, Math.Min(1f, share));
        }

        public static bool Unlocked(CallTier tier, int held, int n, float missionMinutes, float timeScale)
        {
            if (tier == CallTier.Light) return true;
            if (n < 3) return missionMinutes >= TimeUnlockMinutes * Scale(timeScale);
            return held >= Need(tier, n);
        }

        public static string NextUnlock(int held, int n, float missionMinutes, float timeScale)
        {
            if (n < 3)
            {
                float left = TimeUnlockMinutes * Scale(timeScale) - missionMinutes;
                return left > 0f ? (int)Math.Ceiling(left) + " MIN → HEAVY" : "";
            }
            if (held < Need(CallTier.Heavy, n)) return "HOLD A BASE → HEAVY";
            int more = Need(CallTier.Strategic, n) - held;
            return more > 0 ? "HOLD " + more + " MORE → STRATEGIC" : "";
        }

        private static int Need(CallTier tier, int n) =>
            tier == CallTier.Heavy ? Math.Min(1, n) : Math.Min(2, (int)Math.Ceiling(n / 2f));

        private static float Scale(float timeScale) =>
            float.IsNaN(timeScale) || timeScale <= 0f ? 1f : Math.Max(0.4f, Math.Min(1.5f, timeScale));
    }
}
