using System;
using System.Collections.Generic;

namespace BoscaliSummer.Features.TheaterOps.Domain
{
    /// <summary>
    /// One faction's main effort: the objective identity it was named after and the world
    /// position units head for. Position is carried as raw floats so the table stays pure
    /// and testable; the patch converts it back to a GlobalPosition.
    /// </summary>
    internal readonly struct PriorityDirective
    {
        internal const int MaximumKeyLength = 96;
        internal const int MaximumLabelLength = 64;

        public string Key { get; }
        public string Label { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public PriorityDirective(string key, string label, float x, float y, float z)
        {
            Key = Bound(key, MaximumKeyLength);
            Label = Bound(label, MaximumLabelLength);
            X = x;
            Y = y;
            Z = z;
        }

        public bool IsValid =>
            !string.IsNullOrEmpty(Key) && Finite(X) && Finite(Y) && Finite(Z);

        private static string Bound(string value, int length) =>
            string.IsNullOrEmpty(value) ? value
            : value.Length <= length ? value
            : value.Substring(0, length);

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// Bounded per-faction priority store. Keyed by faction name, capped at the game's eight
    /// factions, and it rejects non-finite positions and empty identities so one bad frame
    /// cannot poison every AI query that reads it.
    /// </summary>
    internal sealed class PriorityTable
    {
        internal const int MaximumFactions = 8;

        private readonly Dictionary<string, PriorityDirective> entries =
            new Dictionary<string, PriorityDirective>(MaximumFactions, StringComparer.Ordinal);

        internal int Count => entries.Count;

        internal bool TryGet(string faction, out PriorityDirective directive)
        {
            if (string.IsNullOrEmpty(faction))
            {
                directive = default;
                return false;
            }
            return entries.TryGetValue(faction, out directive);
        }

        internal bool TrySet(string faction, PriorityDirective directive)
        {
            if (string.IsNullOrEmpty(faction) || !directive.IsValid) return false;
            if (!entries.ContainsKey(faction) && entries.Count >= MaximumFactions) return false;
            entries[faction] = directive;
            return true;
        }

        internal bool TryClear(string faction) =>
            !string.IsNullOrEmpty(faction) && entries.Remove(faction);

        internal int CopyInto(List<KeyValuePair<string, PriorityDirective>> into)
        {
            into.Clear();
            foreach (KeyValuePair<string, PriorityDirective> pair in entries) into.Add(pair);
            return into.Count;
        }

        internal void Clear() => entries.Clear();
    }
}
