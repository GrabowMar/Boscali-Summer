using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;
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
        public void CaptionShelf(float x, float y, float w, string caption, string flag = null) => Shelf(x, y, w, caption, flag, Dim, 1f);

        /// <summary>A <see cref="CaptionShelf"/> with no flag in <paramref name="tone"/> at <paramref name="alpha"/> (a cue of the CUE stack, a tone brighter at its first instant and fading at its end).</summary>
        public void FadingShelf(float x, float y, float w, string caption, ScopeTone tone, float alpha) => Shelf(x, y, w, caption, null, tone, alpha);

        private void Shelf(float x, float y, float w, string caption, string flag, ScopeTone tone, float alpha)
        {
            if (!Furniture) return;
            const float Inset = 12f, Clear = 4f;
            bool hasFlag = !string.IsNullOrEmpty(flag);
            float cy = y + 6f, end = x + w, captionRight = x + Inset + TextW(caption), flagLeft = hasFlag ? end - Inset - TextW(flag) : end + Clear;
            Line(x, cy, x + Inset - Clear, cy, tone, alpha);
            ShelfWord(caption, x + Inset, cy, ScopeAlign.Left, tone, alpha);
            if (captionRight + Clear < flagLeft - Clear) Line(captionRight + Clear, cy, flagLeft - Clear, cy, tone, alpha);
            if (hasFlag)
            {
                ShelfWord(flag, end - Inset, cy, ScopeAlign.Right, tone, alpha);
                Line(end - Inset + Clear, cy, end, cy, tone, alpha);
            }
            Terminal(end, cy, tone, alpha);
        }

        /// <summary>A shelf's caption. A DIM word keeps its alpha at its readable floor (<see cref="Text"/>), which a fade must go under: it fades as INK at DIM's own alpha, as DL EVT LOG's rows do.</summary>
        private void ShelfWord(string s, float x, float cy, ScopeAlign align, ScopeTone tone, float alpha)
        {
            if (tone == Dim && alpha < 1f) Text(s, x, cy, Ink, 12f, align, false, alpha * ScopePalette.DimAlpha);
            else Text(s, x, cy, tone, 12f, align, false, alpha);
        }

        /// <summary>The end of a shelf: a 4 x 4 DIM node centred on (<paramref name="x"/>, <paramref name="cy"/>) and a 6 unit 45 degree stub up and to the right, whose tip lands 8 units on.</summary>
        private void Terminal(float x, float cy, ScopeTone tone = Dim, float alpha = 1f)
        {
            Fill(x - 2f, cy - 2f, 4f, 4f, tone, alpha);
            Line(x + 2f, cy - 2f, x + 8f, cy - 8f, tone, alpha);
        }

        /// <summary>
        /// The page's header (LAYOUT.md 3.5): a caption shelf like <see cref="CaptionShelf"/>, 8 shorter than the page (node and stub, the tip on its edge). A 4 x 4 BRIGHT square on the start of the hairline, the
        /// <paramref name="title"/> as its caption (12 bold BRIGHT at x + 12) and the <paramref name="headline"/> its right-hand caption (right-aligned to x + w - 20, in <paramref name="tone"/>, <paramref name="size"/> and
        /// <paramref name="bold"/>). Without <see cref="Furniture"/> (Lean, Minimal) there is no shelf and no title: the headline alone, right-aligned to x + w - 4.
        /// </summary>
        public void PageShelf(float x, float y, float w, ReadOnlySpan<char> title, ReadOnlySpan<char> headline, ScopeTone tone, float size, bool bold, float alpha)
        {
            float cy = y + 6f;
            if (!Furniture)
            {
                if (!headline.IsEmpty) Text(headline, x + w - 4f, cy, tone, size, ScopeAlign.Right, bold, alpha);
                return;
            }
            float end = x + w - 8f, cur = x, titleW = (float)Math.Ceiling(TextW(title)), headW = (float)Math.Ceiling(TextW(headline, size));
            if (!title.IsEmpty)
            {
                Line(cur, cy, x + 8f, cy, Dim);
                Text(title, x + 12f, cy, Hi, 12f, ScopeAlign.Left, true);
                cur = x + 12f + titleW + 4f;
            }
            if (!headline.IsEmpty)
            {
                float gap = end - 12f - headW - 4f;
                if (gap > cur) Line(cur, cy, gap, cy, Dim);
                Text(headline, end - 12f, cy, tone, size, ScopeAlign.Right, bold, alpha);
                cur = end - 8f;
            }
            if (end > cur) Line(cur, cy, end, cy, Dim);
            Marker(x, cy, 4f, Hi, true);
            Terminal(end, cy);
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

        /// <summary>
        /// A small filled square in a colour that is not one of the board's tones: the game's own colour of a name in DL EVT LOG (the one place the board shows a colour it did not choose). Its left edge is at <paramref name="x"/>,
        /// its middle at <paramref name="cy"/>, on the device pixel grid like <see cref="Fill"/>.
        /// </summary>
        public void Swatch(float x, float cy, float size, Rgba color, float alpha = 1f)
        {
            if (alpha <= 0f) return;
            float x0 = Edge(X(x)), y0 = Edge(Y(cy + size / 2f)), x1 = Math.Max(Edge(X(x + size)), x0 + pixel), y1 = Math.Max(Edge(Y(cy - size / 2f)), y0 + pixel);
            AvStrokes.Fill(q, x0, y0, x1 - x0, y1 - y0, alpha >= 1f ? color : color.WithAlpha(color.A * alpha));
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
            Fill(box.X, box.Y + 1f, 2f, box.H - 2f, tone, alpha);
            Text(s, box.X + box.W / 2f + 1f, box.Y + box.H / 2f + 0.5f, tone, size, ScopeAlign.Center, true, alpha);
            Line(box.X, box.Y + box.H, box.X + box.W, box.Y + box.H, tone, alpha * 0.55f);
            return box;
        }
    }
}
