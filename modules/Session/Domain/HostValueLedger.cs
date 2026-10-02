using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Session.Domain
{
    /// <summary>
    /// The client's record of which of its own settings are standing in for the host's. Values
    /// travel as their serialized text, so the ledger never needs to know a setting's type:
    /// the first host value for a key remembers the player's own value, and
    /// <see cref="Restore"/> puts every remembered value back when the session ends.
    ///
    /// <para><c>read</c> answers null for a key this client does not treat as host-owned; such
    /// a key is ignored, so a host can never reach a client's keys, sound or HUD settings.</para>
    /// </summary>
    internal sealed class HostValueLedger
    {
        private readonly Dictionary<string, string> originals = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>True while at least one setting holds a host value.</summary>
        public bool Active => originals.Count > 0;

        public int Count => originals.Count;

        /// <summary>Take one host value. Returns true when the local value changed.</summary>
        public bool Apply(string key, string hostValue, Func<string, string> read, Action<string, string> write)
        {
            if (string.IsNullOrEmpty(key) || hostValue == null) return false;
            string current = read(key);
            if (current == null) return false;
            if (!originals.ContainsKey(key)) originals[key] = current;
            if (string.Equals(current, hostValue, StringComparison.Ordinal)) return false;
            write(key, hostValue);
            return true;
        }

        /// <summary>Put back every value the player had before the session, then forget them.</summary>
        public void Restore(Action<string, string> write)
        {
            foreach (KeyValuePair<string, string> pair in originals) write(pair.Key, pair.Value);
            originals.Clear();
        }
    }

    /// <summary>
    /// Splits the host's values into messages small enough for one reliable send each. A
    /// message is a pair of parallel arrays; only the last one of a full snapshot is marked
    /// complete.
    /// </summary>
    internal static class HostValueChunks
    {
        public const int MaximumEntries = 24;
        public const int MaximumKeyLength = 96;
        public const int MaximumValueLength = 256;

        /// <summary>
        /// Entries whose key or value would not fit the wire are dropped rather than cut, so a
        /// client never applies a truncated value.
        /// </summary>
        public static List<KeyValuePair<string[], string[]>> Split(IReadOnlyList<KeyValuePair<string, string>> entries)
        {
            var chunks = new List<KeyValuePair<string[], string[]>>();
            var keys = new List<string>(MaximumEntries);
            var values = new List<string>(MaximumEntries);
            for (int i = 0; i < entries.Count; i++)
            {
                string key = entries[i].Key, value = entries[i].Value;
                if (!Fits(key, value)) continue;
                keys.Add(key);
                values.Add(value);
                if (keys.Count < MaximumEntries) continue;
                chunks.Add(new KeyValuePair<string[], string[]>(keys.ToArray(), values.ToArray()));
                keys.Clear();
                values.Clear();
            }
            if (keys.Count > 0 || chunks.Count == 0)
                chunks.Add(new KeyValuePair<string[], string[]>(keys.ToArray(), values.ToArray()));
            return chunks;
        }

        public static bool Fits(string key, string value) =>
            !string.IsNullOrEmpty(key) && key.Length <= MaximumKeyLength &&
            value != null && value.Length <= MaximumValueLength;
    }
}
