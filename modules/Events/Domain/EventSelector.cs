using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Events.Domain
{
    /// <summary>
    /// Pure selection and scaling math: which catalog entry rolls next, how long
    /// it runs, and how strongly its modifier applies.
    /// Seeded from the mission generation and the rotation counter, so a listen host reruns
    /// the same mission texture deterministically.
    /// </summary>
    internal static class EventSelector
    {
        /// <summary>Entries this many rolls back are excluded from the next draw.</summary>
        public const int RecentWindow = 3;

        private const int Attempts = 8;

        /// <summary>
        /// The next catalog index, never one of <paramref name="recent"/>. The exclusion
        /// window is best-effort: a catalog no larger than the window falls back to the
        /// first valid draw instead of failing.
        /// </summary>
        public static int SelectIndex(uint seed, int count, IReadOnlyList<int> recent)
        {
            if (count <= 1) return 0;
            int fallback = Draw(seed, 0, count);
            for (int attempt = 1; attempt <= Attempts; attempt++)
            {
                int index = Draw(seed, attempt, count);
                if (!Contains(recent, index)) return index;
            }
            return fallback;
        }

        /// <summary>One duration inside the entry's window, inclusive of both ends.</summary>
        public static int RollDuration(uint seed, int minSeconds, int maxSeconds)
        {
            if (maxSeconds <= minSeconds) return Math.Max(0, minSeconds);
            int span = maxSeconds - minSeconds + 1;
            float unit = Deterministic.UnitFloat(Deterministic.Hash(unchecked((int)seed), 0x4455, 0x5555));
            return minSeconds + Math.Min(span - 1, (int)(unit * span));
        }

        /// <summary>
        /// The catalog multiplier after the server's global strength scalar: 0 disables
        /// every modifier, 1 is the authored balance, 2 doubles each deviation.
        /// </summary>
        public static float EffectiveSupportMultiplier(float catalogMultiplier, float strength)
        {
            float scale = strength < 0f ? 0f : strength;
            return 1f + (catalogMultiplier - 1f) * scale;
        }

        /// <summary>The badge line for a multiplier, e.g. "+25% SUPPORT COST".</summary>
        public static string EffectSummary(float effectiveMultiplier)
        {
            int percent = (int)Math.Round((effectiveMultiplier - 1f) * 100f);
            return percent == 0 ? "NO EFFECT"
                : (percent > 0 ? "+" : "") + percent + "% SUPPORT COST";
        }

        private static int Draw(uint seed, int attempt, int count) =>
            (int)(Deterministic.Hash(unchecked((int)seed), attempt * 7919 + 0x4556, 0x4556) % (uint)count);

        private static bool Contains(IReadOnlyList<int> recent, int index)
        {
            if (recent == null) return false;
            for (int i = 0; i < recent.Count; i++)
                if (recent[i] == index) return true;
            return false;
        }
    }
}
