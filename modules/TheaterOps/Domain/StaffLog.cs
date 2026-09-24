using System.Collections.Generic;

namespace BoscaliSummer.Features.TheaterOps.Domain
{
    /// <summary>
    /// The staff's own voice: a bounded newest-first ring of what the director decided and
    /// why. Pure text; the host writes it when a decision lands and replicates the ring so
    /// every peer reads the same narration. Anything over the text ceiling is cut, never
    /// wrapped: a log line that does not fit its row is a line half told. A line identical
    /// to the newest is refused, so a standing defense does not fill the ring with itself.
    /// </summary>
    internal sealed class StaffLog
    {
        internal const int MaximumEntries = 8;
        internal const int MaximumTextLength = 64;

        private readonly List<string> entries = new List<string>(MaximumEntries);

        public IReadOnlyList<string> Entries => entries;
        public int Count => entries.Count;

        public bool Add(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string line = text.Length > MaximumTextLength ? text.Substring(0, MaximumTextLength) : text;
            if (entries.Count > 0 && entries[0] == line) return false;
            entries.Insert(0, line);
            while (entries.Count > MaximumEntries) entries.RemoveAt(entries.Count - 1);
            return true;
        }

        public void Clear() => entries.Clear();
    }
}
