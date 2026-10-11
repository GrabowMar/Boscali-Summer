using System;
using BoscaliSummer.Core.Contracts;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The visor board's furniture (LAYOUT.md 3 and 9): hairline DIM decoration around the data, never in its way. Four corner brackets around a rectangle are Brackets (arm 10 in DIM for a page body).
    internal sealed partial class ScopeCanvas
    {
        /// <summary>
        /// A block's caption shelf (LAYOUT.md 3.1): a DIM hairline 6 under the block's top (<paramref name="y"/>), from <paramref name="x"/> across <paramref name="w"/>, interrupted 4 units clear on both sides by the
        /// <paramref name="caption"/> (12 regular, at x + 12) and by the <paramref name="flag"/> when there is one (right-aligned to x + w - 12); it ends in a 4 x 4 node centred on (x + w, y + 6) and a 45 degree stub up and to the right.
        /// Draws nothing when <see cref="Furniture"/> is off (the Lean and Minimal levels). The words are constants (strings: nothing is built per paint).
        /// </summary>
        public void CaptionShelf(float x, float y, float w, string caption, string flag = null)
        {
            if (!Furniture) return;
            const float Inset = 12f, Clear = 4f;
            bool hasFlag = !string.IsNullOrEmpty(flag);
            float cy = y + 6f, end = x + w, captionRight = x + Inset + TextW(caption), flagLeft = hasFlag ? end - Inset - TextW(flag) : end + Clear;
            Line(x, cy, x + Inset - Clear, cy, Dim);
            Text(caption, x + Inset, cy, Dim);
            if (captionRight + Clear < flagLeft - Clear) Line(captionRight + Clear, cy, flagLeft - Clear, cy, Dim);
            if (hasFlag)
            {
                Text(flag, end - Inset, cy, Dim, 12f, ScopeAlign.Right);
                Line(end - Inset + Clear, cy, end, cy, Dim);
            }
            Fill(end - 2f, cy - 2f, 4f, 4f, Dim);
            Line(end + 2f, cy - 2f, end + 8f, cy - 8f, Dim);
        }

        /// <summary>
        /// A square marker with its left edge at <paramref name="x"/> and its middle at <paramref name="cy"/>: filled, or a 1 px outline. A lit lamp is a filled 4 in its tone, an unlit one a hollow 4 in DIM
        /// (LAYOUT.md 3.4); the arm marker and the page header's are 6, an expendable's 5.
        /// </summary>
        public void Marker(float x, float cy, float size, ScopeTone tone, bool filled, float alpha = 1f)
        {
            if (filled) Fill(x, cy - size / 2f, size, size, tone, alpha);
            else Box(x, cy - size / 2f, size, size, tone, alpha);
        }

        /// <summary>The ticker's tone tick (LAYOUT.md 3.7): a solid bar, 3 x 12 unless told otherwise, whose top left is (x, y).</summary>
        public void ToneTick(float x, float y, ScopeTone tone, float alpha = 1f, float w = 3f, float h = 12f) => Fill(x, y, w, h, tone, alpha);

        /// <summary>
        /// An open tag (LAYOUT.md 9 rule 4): <see cref="PlateTag"/> without its plate. A 2 unit tick on the left edge of the tag's box, the word in bold in <paramref name="tone"/> and a hairline at 0.55 under it. The box and the
        /// anchor are those of <see cref="PlateTag"/>; returns the box.
        /// </summary>
        public ScopeRect OpenTag(ReadOnlySpan<char> s, float x, float y, ScopeTone tone = Ink, float size = 12f, ScopeAlign align = ScopeAlign.Left, float pad = 3f, float alpha = 1f)
        {
            ScopeRect box = TagBox(s, x, y, size, align, pad);
            Fill(box.X, box.Y, 2f, box.H, tone, alpha);
            Text(s, box.X + box.W / 2f, box.Y + box.H / 2f + 0.5f, tone, size, ScopeAlign.Center, true, alpha);
            Line(box.X, box.Y + box.H, box.X + box.W, box.Y + box.H, tone, alpha * 0.55f);
            return box;
        }

        /// <summary>A constant word as an open tag (the offline check draws it through reflection, which cannot pass a span).</summary>
        public ScopeRect OpenTag(string s, float x, float y, ScopeTone tone = Ink, float size = 12f, ScopeAlign align = ScopeAlign.Left, float pad = 3f, float alpha = 1f) =>
            OpenTag(s.AsSpan(), x, y, tone, size, align, pad, alpha);
    }
}
