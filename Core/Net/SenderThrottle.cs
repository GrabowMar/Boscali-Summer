using System.Collections.Generic;

namespace BoscaliSummer.Core.Net
{
    /// <summary>Per-sender next-allowed-time table with a hard size cap and periodic pruning of stale senders.</summary>
    internal sealed class SenderThrottle
    {
        private const float PruneInterval = 10f, StaleAfter = 30f;
        private readonly Dictionary<ulong, float> next;
        private readonly List<ulong> stale = new List<ulong>(16);
        private readonly int capacity;
        private float nextPrune;

        internal SenderThrottle(int capacity = 64) { this.capacity = capacity; next = new Dictionary<ulong, float>(16); }

        internal int Count => next.Count;

        internal void Clear() => next.Clear();

        /// <summary>
        /// Stamps the sender and returns true unless it is inside its window (when <paramref name="enforce"/>)
        /// or is new while the table is full.
        /// </summary>
        internal bool Allow(ulong id, float now, float interval, bool enforce = true)
        {
            if (next.TryGetValue(id, out float until) ? enforce && now < until : next.Count >= capacity) return false;
            next[id] = now + interval;
            return true;
        }

        /// <summary>Gated to once per ten seconds; drops senders idle for thirty, reporting each one removed.</summary>
        internal void Prune(float now, System.Action<ulong> removed = null)
        {
            if (now < nextPrune) return;
            nextPrune = now + PruneInterval;
            stale.Clear();
            foreach (KeyValuePair<ulong, float> pair in next)
                if (now - pair.Value > StaleAfter) stale.Add(pair.Key);
            for (int i = 0; i < stale.Count; i++)
            {
                next.Remove(stale[i]);
                removed?.Invoke(stale[i]);
            }
        }
    }

    internal static class NetText
    {
        /// <summary>Null-safe cap for wire strings: "" for null/empty, otherwise at most <paramref name="max"/> chars.</summary>
        internal static string Clip(string value, int max) =>
            string.IsNullOrEmpty(value) ? "" : value.Length > max ? value.Substring(0, max) : value;
    }
}
