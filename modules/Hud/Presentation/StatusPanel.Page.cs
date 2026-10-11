using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The wing side's page (LAYOUT.md 3.5): the scope (page body, header shelf and strip, corner brackets and the wipe edge) in the PAGE rectangle of ScopeLayout. Its height animates between the compact SYSTEMS
    // strip and a full page (ScopeTransition); the rectangle never depends on the gauge line or the ticker that hang under it. There is no glass, no header band and no closed frame: the body is drawn through the
    // page conversions (ScopeCanvas.Open), the header is a caption shelf, four DIM brackets are the only frame.
    internal sealed partial class StatusPanel
    {
        private readonly ScopeTransition transition = new ScopeTransition();
        private bool scopeBeat;

        /// <summary>The wipe's clip is the page's rectangle grown by this on every side (LAYOUT.md 10), so the brackets and the edge are inside it.</summary>
        private const float PageGrow = 8f;

        private void PaintPage(in ScopePalette pal, in BoardGeometry geo, bool compact, bool strip, bool flashOn)
        {
            BoardRect r = geo.Page;
            float hS = r.H, w = r.W, lift = ScopeLayout.Lift(compact), body = ScopeLayout.Body(compact, strip), bodyH = body - lift;
            float wipeW = transition.Wipe < 1f ? Mathf.Round(transition.Wipe * (w + PageGrow * 2f)) : w + PageGrow * 2f;
            Show(scopeRect, true);
            scopeBeat = false;
            float wp = Whole(w * unit), hp = Whole(hS * unit), bp = Whole(bodyH * unit), grow = PageGrow * unit;
            PlaceOnGrid(scopeRect, (r.X - PageGrow) * unit, (r.Y - PageGrow) * unit, Whole(wipeW * unit), Whole((hS + PageGrow * 2f) * unit));
            PlaceOnGrid(pageRect, grow, grow, wp, bp);
            PlaceOnGrid(overlayRect, grow, grow, wp, hp);
            PlaceOnGrid(frameRect, grow, grow, wp, hp);

            // The page body, drawn as the painters made it (their own height, their safe area under the old header band) lifted so its first row is 20 units under the page's top.
            ScopeCanvas g = pageCanvas;
            g.Furniture = allow.Furniture;
            g.Open = true;
            g.Begin(pal, unit, bp, pixel);
            g.Raise(lift);
            try { page.Paint(g, new PagePaint(w, body, compact, ScopeLayout.SafeTop(compact), now, units)); }
            catch (Exception e)
            {
                ReportOnce(page, e);
                g.Text("PAGE ERROR", 8f, 60f, Red);
            }
            g.End();
            pageVector.Commit();

            // The header shelf and the page strip, above the page.
            g = overlayCanvas;
            g.Furniture = allow.Furniture;
            g.Open = true;
            g.Begin(pal, unit, hp, pixel);
            PaintHeader(g, w, flashOn);
            if (strip)
            {
                g.Origin(0f, bodyH);
                try { page.PaintStrip(g, new PagePaint(w, ScopeLayout.Strip, false, 0f, now, units)); }
                catch (Exception e) { ReportOnce(page, e); g.Text("STRIP ERROR", 8f, 13f, Red); }
                g.Origin(0f, 0f);
            }
            g.End();
            overlayVector.Commit();

            // The four corner brackets round the body, and the wipe's edge: the new page is revealed left to right behind it.
            g = frameCanvas;
            g.Begin(pal, unit, hp, pixel);
            g.Brackets(-3f, 14f, w + 6f, hS - 11f, 10f, Dim);
            if (transition.Wipe < 1f) g.Fill(wipeW - PageGrow - 1f, -PageGrow, 1f, hS + PageGrow * 2f, Hi, 0.9f);
            g.End();
            frameVector.Commit();
        }

        /// <summary>The page's header (LAYOUT.md 3.5): the shelf with the page's title and its headline, which is INK 12 unless it is a caution or a warning (bold 14 in that tone, pulsing while it flashes).</summary>
        private void PaintHeader(ScopeCanvas g, float w, bool flashOn)
        {
            ScopeHeadline hl = HeadlineOf(page);
            bool hot = hl.Tone == Amber || hl.Tone == Red;
            if (hot && hl.Flash) scopeBeat = true;
            g.PageShelf(0f, 0f, w, page.Title, hl.Text, hot ? hl.Tone : Ink, hot ? 14f : 12f, hot, hot && hl.Flash && flashOn ? 0.45f : 1f);
        }
    }
}
