using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>Which gauges the four slots of the gauge line show and how they pack into it (LAYOUT.md 3.6), pure.</summary>
    internal static class GaugeRow
    {
        public const int Slots = 4;
        /// <summary>A gauge is its glyph (6 radius, 21 with the clearance), its label, <see cref="WordGap"/>, its value and, while there is room, <see cref="SubGap"/> and its sub-word.</summary>
        public const float GlyphW = 21f, WordGap = 6f, SubGap = 8f;

        /// <summary>The width of one gauge from the widths of its words (each rounded up); no sub-word when <paramref name="sub"/> is 0.</summary>
        public static float Width(float label, float value, float sub) =>
            GlyphW + (float)Math.Ceiling(label) + WordGap + (float)Math.Ceiling(value) + (sub > 0f ? SubGap + (float)Math.Ceiling(sub) : 0f);

        /// <summary>
        /// Pack gauges of <paramref name="widths"/> from the left, <see cref="ScopeLayout.GaugeSpace"/> apart, none split, into lines of <see cref="ScopeLayout.WingW"/>: each gauge's x and row (0, 1, ...). Returns the rows
        /// used (0 for no gauge), however many that is.
        /// </summary>
        public static int Pack(ReadOnlySpan<float> widths, Span<float> x, Span<int> row)
        {
            int rows = widths.Length == 0 ? 0 : 1;
            float at = 0f;
            for (int i = 0; i < widths.Length; i++)
            {
                if (at > 0f && at + ScopeLayout.GaugeSpace + widths[i] > ScopeLayout.WingW) { rows++; at = 0f; }
                if (at > 0f) at += ScopeLayout.GaugeSpace;
                x[i] = at;
                row[i] = rows - 1;
                at += widths[i];
            }
            return rows;
        }

        /// <summary>How many lines <paramref name="widths"/> take (see <see cref="Pack"/>).</summary>
        public static int Lines(ReadOnlySpan<float> widths)
        {
            Span<float> x = stackalloc float[widths.Length];
            Span<int> row = stackalloc int[widths.Length];
            return Pack(widths, x, row);
        }

        /// <summary>
        /// The gauge line for <paramref name="count"/> gauges whose words are <paramref name="label"/>, <paramref name="value"/> and <paramref name="sub"/> wide: the sub-words stay while two lines hold the gauges, and if two lines
        /// still do not hold them none shows its sub-word (<paramref name="subs"/> false). Fills <paramref name="widths"/> to match and returns the lines used, 0 to 2.
        /// </summary>
        public static int Lay(float[] label, float[] value, float[] sub, int count, float[] widths, out bool subs)
        {
            int lines = 0;
            subs = true;
            for (int pass = 0; pass < 2; pass++)
            {
                subs = pass == 0;
                for (int i = 0; i < count; i++) widths[i] = Width(label[i], value[i], subs ? sub[i] : 0f);
                lines = Lines(new ReadOnlySpan<float>(widths, 0, count));
                if (lines <= 2) break;
            }
            return Math.Min(lines, 2);
        }

        /// <summary>
        /// The page's own gauges first (in its order), then the registered ones by rank (registration order among equals), skipping any
        /// whose id the page already shows, at most <see cref="Slots"/>. Returns how many slots are filled.
        /// </summary>
        public static int Merge(GaugeSpec[] page, int pageCount, GaugeSpec[] registered, int registeredCount, GaugeSpec[] into)
        {
            int n = 0;
            for (int i = 0; i < pageCount && n < Slots && n < into.Length; i++) into[n++] = page[i];
            int pageEnd = n;
            ulong taken = 0;
            while (n < Slots && n < into.Length)
            {
                int best = -1;
                for (int i = 0; i < registeredCount && i < 64; i++)
                {
                    if ((taken & (1UL << i)) != 0) continue;
                    if (best < 0 || registered[i].Rank > registered[best].Rank) best = i;
                }
                if (best < 0) break;
                taken |= 1UL << best;
                bool dup = false;
                for (int k = 0; k < pageEnd; k++) if (into[k].Id == registered[best].Id) dup = true;
                if (!dup) into[n++] = registered[best];
            }
            return n;
        }
    }
}
