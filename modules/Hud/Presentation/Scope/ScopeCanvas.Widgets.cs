using System;
using BoscaliSummer.Core.Contracts;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>A rectangle in design units (what <see cref="ScopeCanvas.PlateTag"/> returns, so a line can start at its edge).</summary>
    internal readonly struct ScopeRect
    {
        public readonly float X, Y, W, H;
        public ScopeRect(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
    }

    // The prototype's markers and widgets on top of the primitives: chevron, diamond, brackets, tag, stamp, bar, dot grid, grid.
    internal sealed partial class ScopeCanvas
    {
        /// <summary>An arrowhead marker (a missile, a contact) with its tip <paramref name="size"/> from (x, y) toward compass <paramref name="hdg"/>.</summary>
        public void Chevron(float x, float y, float hdg, float size, ScopeTone tone = Ink, bool fill = false, float alpha = 1f, float w = 1f)
        {
            int n = Load(ScopeShapes.Chevron, 4, x, y, size, hdg);
            if (fill) FillPoly(n, tone, alpha);
            Path(n, tone, alpha, w);
        }

        /// <summary>A target or waypoint diamond.</summary>
        public void Diamond(float x, float y, float r, ScopeTone tone = Ink, bool fill = false, float alpha = 1f, float w = 1f)
        {
            int n = Load(ScopeShapes.Diamond, 4, x, y, r);
            if (fill) FillPoly(n, tone, alpha);
            Path(n, tone, alpha, w);
        }

        /// <summary>A solid triangle pointing at compass <paramref name="dirDeg"/>.</summary>
        public void Tri(float x, float y, float dirDeg, float size, ScopeTone tone = Ink, bool fill = true, float alpha = 1f, float w = 1f)
        {
            int n = Load(ScopeShapes.Triangle, 3, x, y, size, dirDeg);
            if (fill) FillPoly(n, tone, alpha);
            Path(n, tone, alpha, w);
        }

        /// <summary>The airframe outline, nose toward compass <paramref name="hdg"/>, <paramref name="halfLength"/> from centre to nose.</summary>
        public void Jet(float x, float y, float hdg, float halfLength, ScopeTone tone = Ink, bool fill = false, float fillAlpha = 1f, float alpha = 1f, float w = 1f)
        {
            int n = Load(ScopeShapes.Jet, ScopeShapes.JetPoints, x, y, halfLength, hdg);
            if (fill) FillPoly(n, tone, fillAlpha);
            Path(n, tone, alpha, w);
        }

        /// <summary>Four corner brackets (target boxes). The far arms sit one line in (LAYOUT.md 0), so the rectangle keeps its outer size <paramref name="w"/> x <paramref name="h"/>.</summary>
        public void Brackets(float x, float y, float w, float h, float len = 8f, ScopeTone tone = Ink, float alpha = 1f, float lw = 1f)
        {
            float r = x + w - lw, b = y + h - lw;
            Line(x, y, x + len, y, tone, alpha, lw); Line(x, y, x, y + len, tone, alpha, lw);
            Line(r, y, r - len, y, tone, alpha, lw); Line(r, y, r, y + len, tone, alpha, lw);
            Line(x, b, x + len, b, tone, alpha, lw); Line(x, b, x, b - len, tone, alpha, lw);
            Line(r, b, r - len, b, tone, alpha, lw); Line(r, b, r, b - len, tone, alpha, lw);
        }

        /// <summary>The scope's outline with an ink tick on each corner (the prototype's <c>HUD.frame</c>): the stores header's cells wear it too.</summary>
        public void Frame(float x, float y, float w, float h)
        {
            Box(x, y, w, h, Dim, 0.75f);
            Line(x + 0.5f, y + 0.5f, x + 7.5f, y + 0.5f, Ink); Line(x + 0.5f, y + 0.5f, x + 0.5f, y + 7.5f, Ink);
            Line(x + w - 0.5f, y + 0.5f, x + w - 7.5f, y + 0.5f, Ink); Line(x + w - 0.5f, y + 0.5f, x + w - 0.5f, y + 7.5f, Ink);
            Line(x + w - 0.5f, y + h - 0.5f, x + w - 7.5f, y + h - 0.5f, Ink); Line(x + w - 0.5f, y + h - 0.5f, x + w - 0.5f, y + h - 7.5f, Ink);
            Line(x + 0.5f, y + h - 0.5f, x + 7.5f, y + h - 0.5f, Ink); Line(x + 0.5f, y + h - 0.5f, x + 0.5f, y + h - 7.5f, Ink);
        }

        /// <summary>A drawn tick (the fonts lack one).</summary>
        public void Check(float x, float y, float size, ScopeTone tone = Ink, float alpha = 1f, float w = 1f)
        {
            Line(x - size * 0.5f, y, x - size * 0.15f, y + size * 0.4f, tone, alpha, w);
            Line(x - size * 0.15f, y + size * 0.4f, x + size * 0.55f, y - size * 0.45f, tone, alpha, w);
        }

        /// <summary>A drawn cross.</summary>
        public void Cross(float x, float y, float size, ScopeTone tone = Ink, float alpha = 1f, float w = 1f)
        {
            Line(x - size / 2f, y - size / 2f, x + size / 2f, y + size / 2f, tone, alpha, w);
            Line(x - size / 2f, y + size / 2f, x + size / 2f, y - size / 2f, tone, alpha, w);
        }

        /// <summary>A tag in <paramref name="tone"/>: an open tag (LAYOUT.md 9 rule 4), the plate is gone; (x, y) anchors the tag's vertical middle. Returns the tag's rectangle.</summary>
        public ScopeRect PlateTag(ReadOnlySpan<char> s, float x, float y, ScopeTone tone = Ink, float size = 12f, ScopeAlign align = ScopeAlign.Left, float pad = 3f, float alpha = 1f) =>
            OpenTag(s, x, y, tone, size, align, pad, alpha);

        /// <summary>The box of a tag around <paramref name="s"/>, whole units, anchored by <paramref name="align"/> at (x, y = its vertical middle).</summary>
        private static ScopeRect TagBox(ReadOnlySpan<char> s, float x, float y, float size, ScopeAlign align, float pad)
        {
            float w = Mathf.Round(TextW(s, size) + pad * 2f), h = size + 5f;
            return new ScopeRect(Mathf.Round(align == ScopeAlign.Right ? x - w : align == ScopeAlign.Center ? x - w / 2f : x), Mathf.Round(y - h / 2f), w, h);
        }

        /// <summary>A result stamp (HIT / MISS / DEFEATED), centred on (x, y): the bold word inside four corner brackets (LAYOUT.md 9 rule 4). Never turned: <paramref name="angle"/> is no longer used.</summary>
        public void Stamp(ReadOnlySpan<char> s, float x, float y, ScopeTone tone = Ink, float size = 14f, float angle = 0f, float alpha = 1f)
        {
            float w = Mathf.Round(TextW(s, size) + 14f), h = size + 8f;
            Brackets(x - w / 2f, y - h / 2f, w, h, 5f, tone, alpha);
            Text(s, x, y + 1f, tone, size, ScopeAlign.Center, true, alpha);
        }

        /// <summary>An outlined fill bar: <paramref name="frac"/> of its inside filled (or lit in <paramref name="segs"/> segments). Add marks with <see cref="BarTick"/>.</summary>
        public void Bar(float x, float y, float w, float h, float frac, ScopeTone tone = Ink, int segs = 0)
        {
            frac = Clamp01(frac);
            float iw = w - 4f, ih = h - 4f;
            Box(x, y, w, h, Dim, 0.85f);
            if (segs > 0)
            {
                float sw = (iw - (segs - 1)) / segs;
                int lit = (int)Mathf.Round(frac * segs);
                for (int i = 0; i < segs; i++)
                    Fill(Mathf.Round(x + 2f + i * (sw + 1f)), y + 2f, Math.Max(1f, Mathf.Round(sw)), ih, i < lit ? tone : Faint, i < lit ? 1f : 0.7f);
            }
            else Fill(x + 2f, y + 2f, Mathf.Round(iw * frac), ih, tone);
        }

        /// <summary>A threshold tick across a <see cref="Bar"/> at 0..1 <paramref name="at"/>, optionally labelled above or below.</summary>
        public void BarTick(float x, float y, float w, float h, float at, ScopeTone tone = Amber, ReadOnlySpan<char> label = default, bool up = false, ScopeAlign align = ScopeAlign.Center)
        {
            float tx = Mathf.Round(x + 2f + (w - 4f) * Clamp01(at));
            Line(tx, y - 3f, tx, y + h + 3f, tone);
            if (!label.IsEmpty) Text(label, tx, up ? y - 11f : y + h + 11f, tone, 12f, align);
        }

        /// <summary>
        /// A dispenser display: <paramref name="filled"/> dots solid (row-major from the top-left), the next <paramref name="flash"/> flashing hi (just fired),
        /// the rest hollow; <paramref name="cols"/> x <paramref name="rows"/> dots <paramref name="pitch"/> apart.
        /// </summary>
        public void DotGrid(float x, float y, int cols, int rows, int filled, ScopeTone tone = Ink, float pitch = 7f, float r = 2f, bool square = false, int flash = 0)
        {
            for (int i = 0; i < cols * rows; i++)
            {
                float cx = x + (i % cols) * pitch + pitch / 2f, cy = y + (i / cols) * pitch + pitch / 2f;
                bool on = i < filled, hot = !on && i < filled + flash, lit = on || hot;
                ScopeTone t = on ? tone : hot ? Hi : Faint;
                float a = hot ? 0.9f : lit ? 1f : 0.8f;
                if (square)
                {
                    if (lit) Fill(Mathf.Round(cx - r), Mathf.Round(cy - r), r * 2f, r * 2f, t, a);
                    else Box(Mathf.Round(cx - r), Mathf.Round(cy - r), r * 2f, r * 2f, t, 0.8f);
                }
                else Ring(cx, cy, lit ? r : r - 0.5f, t, a, 1f, t, lit ? 1f : 0f);
            }
        }
    }
}
