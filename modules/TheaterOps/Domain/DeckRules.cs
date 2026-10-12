using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.TheaterOps.Domain
{
    internal sealed class DeckCard
    {
        internal int Id, Revision;
        internal string Key;
        internal TheaterOpKind Kind;
        internal float Score;
        internal bool Recommended;
    }

    internal readonly struct DeckOffer
    {
        internal readonly string Key; internal readonly TheaterOpKind Kind; internal readonly float Score;
        internal DeckOffer(string key, TheaterOpKind kind, float score) { Key = key; Kind = kind; Score = score; }
    }

    internal struct Veto { internal string Key; internal float X, Z, Until; }

    /// <summary>
    /// Fronts the pilot called off or the staff finished. A veto holds the key and also every front within a radius of the vetoed fix,
    /// because a sector key is the nearest airbase and flips to the neighbour at the midpoint, which is exactly where contours lie.
    /// </summary>
    internal sealed class VetoBook
    {
        internal const int Capacity = 8;
        private readonly Veto[] items = new Veto[Capacity];
        private int count;

        /// <summary>Adds a veto until the mission second <paramref name="until"/>. A key already in the book is refreshed and becomes the newest; a full book evicts its oldest.</summary>
        internal void Add(string key, float x, float z, float until)
        {
            key = key ?? "";
            for (int i = 0; i < count; i++)
                if (string.Equals(items[i].Key, key, StringComparison.Ordinal)) { RemoveAt(i); break; }
            if (count == Capacity) RemoveAt(0);
            items[count++] = new Veto { Key = key, X = x, Z = z, Until = until };
        }

        /// <summary>The same key, or a front within <paramref name="radius"/> of a live veto's fix (edge inclusive). A veto is live while now &lt; Until.</summary>
        internal bool IsVetoed(string key, float x, float z, float now, float radius)
        {
            bool positioned = !float.IsNaN(x) && !float.IsInfinity(x) && !float.IsNaN(z) && !float.IsInfinity(z) && radius > 0f;
            for (int i = 0; i < count; i++)
            {
                Veto v = items[i];
                if (!(now < v.Until)) continue;
                if (!string.IsNullOrEmpty(key) && string.Equals(v.Key, key, StringComparison.Ordinal)) return true;
                if (!positioned || float.IsNaN(v.X) || float.IsInfinity(v.X) || float.IsNaN(v.Z) || float.IsInfinity(v.Z)) continue;
                double dx = (double)v.X - x, dz = (double)v.Z - z;
                if (dx * dx + dz * dz <= (double)radius * radius) return true;
            }
            return false;
        }

        internal void Prune(float now)
        {
            int kept = 0;
            for (int i = 0; i < count; i++)
                if (now < items[i].Until) items[kept++] = items[i];
            for (int i = kept; i < count; i++) items[i] = default;
            count = kept;
        }

        internal int Count(float now)
        {
            int live = 0;
            for (int i = 0; i < count; i++)
                if (now < items[i].Until) live++;
            return live;
        }

        private void RemoveAt(int index)
        {
            for (int i = index; i < count - 1; i++) items[i] = items[i + 1];
            items[--count] = default;
        }
    }

    /// <summary>The staff's deck of up to three operations. Card identity is (Key, Kind) only; an id and a revision are minted once and survive every refresh.</summary>
    internal static class DeckRules
    {
        internal const int Slots = 3;

        /// <summary>
        /// Keeps the cards whose (Key, Kind) is still ranked (taking the ranked score), drops the rest and any card on <paramref name="excludeKey"/>,
        /// fills free slots from <paramref name="ranked"/> (best first) skipping the exclude key and every front already in the deck, then marks the staff's choice.
        /// New cards get Id = nextId++ and Revision = ++revision. True when any (Id, Revision) appeared or disappeared.
        /// </summary>
        internal static bool Maintain(List<DeckCard> deck, IReadOnlyList<DeckOffer> ranked, string excludeKey, ref int nextId, ref int revision)
        {
            if (deck == null) return false;
            bool changed = false;
            bool excluding = !string.IsNullOrEmpty(excludeKey);
            for (int i = deck.Count - 1; i >= 0; i--)
            {
                DeckCard card = deck[i];
                int at = card == null ? -1 : IndexOf(ranked, card.Key, card.Kind);
                if (at < 0 || excluding && string.Equals(card.Key, excludeKey, StringComparison.Ordinal))
                {
                    deck.RemoveAt(i);
                    changed = true;
                }
                else card.Score = ranked[at].Score;
            }
            while (deck.Count > Slots) { deck.RemoveAt(deck.Count - 1); changed = true; }
            for (int i = 0; ranked != null && i < ranked.Count && deck.Count < Slots; i++)
            {
                DeckOffer offer = ranked[i];
                if (string.IsNullOrEmpty(offer.Key) || excluding && string.Equals(offer.Key, excludeKey, StringComparison.Ordinal) || HasKey(deck, offer.Key)) continue;
                deck.Add(new DeckCard { Id = nextId++, Revision = ++revision, Key = offer.Key, Kind = offer.Kind, Score = offer.Score });
                changed = true;
            }
            int best = Recommended(deck);
            for (int i = 0; i < deck.Count; i++) deck[i].Recommended = i == best;
            return changed;
        }

        /// <summary>A DEFEND on one of our airbases: its key is a place key with the '#' prefix.</summary>
        internal static bool IsBaseDefence(TheaterOpKind kind, string key) =>
            kind == TheaterOpKind.Defend && !string.IsNullOrEmpty(key) && key[0] == PlaceNames.BasePrefix;

        /// <summary>
        /// Cards keep their places, so an own base under attack cannot wait for a free slot: removes the lowest-scoring card that is not itself a base defence
        /// (the later one on a tie) and returns its key. Null, and nothing removed, when there is no such card.
        /// </summary>
        internal static string DropWeakest(List<DeckCard> deck)
        {
            int weakest = -1;
            for (int i = 0; deck != null && i < deck.Count; i++)
                if (deck[i] != null && !IsBaseDefence(deck[i].Kind, deck[i].Key) && (weakest < 0 || deck[i].Score <= deck[weakest].Score)) weakest = i;
            if (weakest < 0) return null;
            string key = deck[weakest].Key;
            deck.RemoveAt(weakest);
            return key;
        }

        /// <summary>Index of the best Score (a NaN score ranks last; ties keep the lower index), -1 for an empty deck. Reads only.</summary>
        internal static int Recommended(IReadOnlyList<DeckCard> deck)
        {
            int best = -1;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; deck != null && i < deck.Count; i++)
            {
                float score = float.IsNaN(deck[i].Score) ? float.NegativeInfinity : deck[i].Score;
                if (best < 0 || score > bestScore) { best = i; bestScore = score; }
            }
            return best;
        }

        private static int IndexOf(IReadOnlyList<DeckOffer> ranked, string key, TheaterOpKind kind)
        {
            for (int i = 0; ranked != null && i < ranked.Count; i++)
                if (ranked[i].Kind == kind && string.Equals(ranked[i].Key, key, StringComparison.Ordinal)) return i;
            return -1;
        }

        private static bool HasKey(List<DeckCard> deck, string key)
        {
            for (int i = 0; i < deck.Count; i++)
                if (string.Equals(deck[i].Key, key, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
