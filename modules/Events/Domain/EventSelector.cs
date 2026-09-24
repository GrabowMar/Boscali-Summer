using System;
using System.Collections.Generic;
using BoscaliSummer.Core;

namespace BoscaliSummer.Features.Events.Domain
{
    /// <summary>What a player can do about the active event's cost modifier.</summary>
    internal enum EventResponseKind : byte
    {
        None = 0,

        /// <summary>Halve the penalty for the rest of the event.</summary>
        Contain = 1,

        /// <summary>Deepen the discount for the rest of the event.</summary>
        Leverage = 2,

        /// <summary>Faction treasury pays for a stronger theater-wide answer.</summary>
        Treasury = 3,

        /// <summary>A completed faction contract earns a theater-wide answer.</summary>
        Contract = 4,

        /// <summary>A qualified pilot uses a personal support channel.</summary>
        Perk = 5,
    }

    /// <summary>
    /// Pure selection, scaling and response math: which catalog entry rolls next, how long
    /// it runs, how strongly its modifier applies, and what a response costs and buys.
    /// Seeded from the mission generation and the rotation counter, so a listen host reruns
    /// the same mission texture deterministically.
    /// </summary>
    internal static class EventSelector
    {
        /// <summary>Entries this many rolls back are excluded from the next draw.</summary>
        public const int RecentWindow = 3;

        private const int Attempts = 8;

        /// <summary>Below this deviation an event is flavor only and offers no response.</summary>
        private const float ResponseFloor = 0.05f;

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

        /// <summary>
        /// Which response an active multiplier invites: contain a penalty, leverage a
        /// discount, or nothing when the event is flavor only.
        /// </summary>
        public static EventResponseKind ResponseKind(float effectiveMultiplier)
        {
            if (effectiveMultiplier > 1f + ResponseFloor) return EventResponseKind.Contain;
            if (effectiveMultiplier < 1f - ResponseFloor) return EventResponseKind.Leverage;
            return EventResponseKind.None;
        }

        /// <summary>
        /// The multiplier after a response: contain halves the deviation back toward 1,
        /// leverage deepens it by half again in the player's favour.
        /// </summary>
        public static float ApplyResponse(float effectiveMultiplier, EventResponseKind kind)
        {
            float deviation = effectiveMultiplier - 1f;
            switch (kind)
            {
                case EventResponseKind.Contain: return 1f + deviation * 0.5f;
                case EventResponseKind.Leverage: return Math.Max(0.3f, 1f + deviation * 1.5f);
                case EventResponseKind.Treasury: return Math.Max(0.3f, 1f + deviation * (deviation >= 0f ? 0.25f : 1.75f));
                case EventResponseKind.Contract: return Math.Max(0.3f, 1f + deviation * (deviation >= 0f ? 0.35f : 1.65f));
                case EventResponseKind.Perk: return Math.Max(0.3f, 1f + deviation * (deviation >= 0f ? 0.4f : 1.6f));
                default: return effectiveMultiplier;
            }
        }

        /// <summary>Faction funds, in the game's million-unit treasury scale.</summary>
        public static int TreasuryCost(float effectiveMultiplier)
        {
            float deviation = Math.Abs(effectiveMultiplier - 1f);
            return deviation < ResponseFloor ? 0 : 20 + (int)Math.Ceiling(deviation * 100f / 5f) * 5;
        }

        /// <summary>
        /// Server-derived allocation price of a response, scaled by how far the event moved
        /// the price: 200 for a light nudge, 1200 for a ×1.5 crisis, rounded to 50. Zero
        /// means the event is flavor only.
        /// </summary>
        public static int ResponseCost(float effectiveMultiplier)
        {
            float deviation = Math.Abs(effectiveMultiplier - 1f);
            if (deviation < ResponseFloor) return 0;
            return 200 + (int)Math.Round(deviation * 2000f / 50f) * 50;
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
