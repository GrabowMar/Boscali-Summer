using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Comms.Domain
{

    /// <summary>One line on the leaderboard.</summary>
    internal struct ScoreRow
    {
        public ulong Player;
        public string Name;
        public int Points;
        public int Wins;
    }

    /// <summary>
    /// The session's fun points. Nothing buys anything with them; they exist so a hunt or a
    /// duel has a table to climb. Host-owned and broadcast whole, it is small by design.
    /// </summary>
    internal sealed class CommsScoreboard
    {
        public const int MaxRows = 64;

        private readonly List<ScoreRow> rows = new List<ScoreRow>();

        public IReadOnlyList<ScoreRow> Rows => rows;

        /// <summary>Replace the table wholesale: what a peer does with the host's broadcast.</summary>
        public void Set(IList<ScoreRow> table)
        {
            rows.Clear();
            if (table != null)
                for (int i = 0; i < table.Count && rows.Count < MaxRows; i++) rows.Add(table[i]);
            Sort();
        }

        public void Clear() => rows.Clear();

        private void Sort() => rows.Sort((a, b) =>
        {
            int byPoints = b.Points.CompareTo(a.Points);
            if (byPoints != 0) return byPoints;
            int byWins = b.Wins.CompareTo(a.Wins);
            return byWins != 0 ? byWins : string.CompareOrdinal(a.Name, b.Name);
        });
    }

    /// <summary>
    /// A per-sender token bucket. Every post costs tokens and the bucket refills at a steady
    /// rate, so a burst of pings in a fight is fine and a macro spamming the map is not.
    /// </summary>
    internal sealed class TokenBucket
    {
        private struct Bucket
        {
            public float Tokens;
            public float Stamp;
        }

        private readonly Dictionary<ulong, Bucket> buckets = new Dictionary<ulong, Bucket>();
        private readonly List<ulong> stale = new List<ulong>();
        private readonly float capacity;
        private readonly float refillPerSecond;
        private readonly int maxSenders;
        private float nextPrune;

        public TokenBucket(float capacity, float refillPerSecond, int maxSenders = 128)
        {
            this.capacity = Math.Max(1f, capacity);
            this.refillPerSecond = Math.Max(0.01f, refillPerSecond);
            this.maxSenders = Math.Max(1, maxSenders);
        }

        public bool TryTake(ulong sender, float now, float cost)
        {
            Prune(now);
            if (!buckets.TryGetValue(sender, out Bucket bucket))
            {
                if (buckets.Count >= maxSenders) return false;
                bucket = new Bucket { Tokens = capacity, Stamp = now };
            }
            bucket.Tokens = Math.Min(capacity, bucket.Tokens + Math.Max(0f, now - bucket.Stamp) * refillPerSecond);
            bucket.Stamp = now;
            if (bucket.Tokens < cost)
            {
                buckets[sender] = bucket;
                return false;
            }
            bucket.Tokens -= cost;
            buckets[sender] = bucket;
            return true;
        }

        public void Clear() => buckets.Clear();

        private void Prune(float now)
        {
            if (now < nextPrune) return;
            nextPrune = now + 30f;
            stale.Clear();
            float full = capacity / refillPerSecond;
            foreach (KeyValuePair<ulong, Bucket> pair in buckets)
                if (now - pair.Value.Stamp > full) stale.Add(pair.Key);
            for (int i = 0; i < stale.Count; i++) buckets.Remove(stale[i]);
        }
    }
}
