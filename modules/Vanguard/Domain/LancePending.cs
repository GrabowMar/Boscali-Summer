using System.Collections.Generic;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    internal enum LanceClaim : byte { Matched, Duplicate, Unmatched }

    /// <summary>
    /// Server pairing of releases with the vanilla hit claims that follow them. A volley enqueues one entry per slug (a
    /// twin-pod station fires two), a claim consumes one entry, and a claim with nothing left to consume is a ricochet
    /// duplicate (dropped) when a claim was served moments ago, otherwise an unmatched weak snap. Pure bookkeeping.
    /// </summary>
    internal sealed class LancePending
    {
        private sealed class Entry
        {
            public float Power, At, UsedAt;
            public bool Used;
        }

        /// <summary>Four twin-pod volleys fit in the 4 s a claim may lag its release.</summary>
        public const int Capacity = 8;
        /// <summary>A ricochet of the same slug claims again within about 0.1 s; the window is generous.</summary>
        public const float DuplicateWindow = 0.5f;
        /// <summary>An unused entry from an earlier volley this old is a missed shot, not the slug behind a duplicate claim.</summary>
        public const float StaleSeconds = 1.5f;

        private readonly List<Entry> entries = new List<Entry>(Capacity);

        public void Add(float power, float now)
        {
            entries.Add(new Entry { Power = power, At = now });
            if (entries.Count > Capacity) entries.RemoveAt(0);
        }

        /// <summary>
        /// A hit claim at <paramref name="now"/>. Matched consumes the newest unused entry and returns its power; Duplicate
        /// (a claim was served in the last <see cref="DuplicateWindow"/> and nothing fresh is left) must be dropped;
        /// Unmatched falls back to <see cref="LanceCapacitor.UnmatchedPower"/>.
        /// </summary>
        public LanceClaim Claim(float now, out float power)
        {
            Entry open = null, recent = null;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry entry = entries[i];
                if (now - entry.At > LanceCapacitor.PendingTtl) break;
                if (!entry.Used) open ??= entry;
                else if (recent == null && now - entry.UsedAt < DuplicateWindow) recent = entry;
            }
            power = LanceCapacitor.UnmatchedPower;
            bool stale = open != null && recent != null && open.At < recent.At - 0.01f && now - open.At >= StaleSeconds;
            if (open != null && !stale)
            {
                open.Used = true;
                open.UsedAt = now;
                power = open.Power;
                return LanceClaim.Matched;
            }
            return recent != null ? LanceClaim.Duplicate : LanceClaim.Unmatched;
        }
    }
}
