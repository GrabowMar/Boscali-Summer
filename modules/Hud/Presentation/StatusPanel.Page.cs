using System;
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

        /// <summary>Every slot the layout placed (<see cref="BoardGeometry.Slot"/>) gets its page: what its feed says, or the page's own idle face while the feed has nothing.</summary>
        private void PaintPages(in ScopePalette pal, in BoardGeometry geo, bool flashOn)
        {
            scopeBeat = false;
            for (int i = 0; i < pageSurfaces.Length; i++)
            {
                BoardRect r = geo.Slot(i);
                bool on = i < selectedCount && r.Present;
                Show(pageSurfaces[i].Scope, on);
                if (!on) continue;
                ScopePageBase current = selectedPages[i];
                bool content = pageLive[(int)current.Id];
                PaintPage(pal, r, pageSurfaces[i], current, content, content && current.HasStrip, flashOn);
            }
        }

        private void PaintPage(in ScopePalette pal, in BoardRect r, PageSurface surface, ScopePageBase current, bool content, bool strip, bool flashOn)
        {
            const bool compact = false;
            float hS = r.H, w = r.W, lift = ScopeLayout.Lift(compact), body = ScopeLayout.Body(compact, strip), bodyH = body - lift;
            float wipeW = transition.Wipe < 1f ? Mathf.Round(transition.Wipe * (w + PageGrow * 2f)) : w + PageGrow * 2f;
            float wp = Whole(w * unit), hp = Whole(hS * unit), bp = Whole(bodyH * unit), grow = PageGrow * unit;
            PlaceOnGrid(surface.Scope, (r.X - PageGrow) * unit, (r.Y - PageGrow) * unit, Whole(wipeW * unit), Whole((hS + PageGrow * 2f) * unit));
            PlaceOnGrid(surface.Body, grow, grow, wp, bp);
            PlaceOnGrid(surface.Overlay, grow, grow, wp, hp);
            PlaceOnGrid(surface.Frame, grow, grow, wp, hp);

            // The page body, drawn as the painters made it (their own height, their safe area under the old header band) lifted so its first row is 20 units under the page's top.
            ScopeCanvas g = surface.BodyCanvas;
            g.Furniture = allow.Furniture;
            g.Open = true;
            g.Begin(pal, unit, bp, pixel);
            g.Raise(lift);
            try
            {
                var paint = new PagePaint(w, body, compact, ScopeLayout.SafeTop(compact), now, units);
                if (content) current.Paint(g, paint);
                else current.PaintIdle(g, paint);
            }
            catch (Exception e)
            {
                ReportOnce(current, e);
                g.Text("PAGE ERROR", 8f, 60f, Red);
            }
            g.End();
            surface.BodyVector.Commit();

            // The header shelf and the page strip, above the page.
            g = surface.OverlayCanvas;
            g.Furniture = allow.Furniture;
            g.Open = true;
            g.Begin(pal, unit, hp, pixel);
            PaintHeader(g, current, w, flashOn);
            if (strip && content)
            {
                g.Origin(0f, bodyH);
                try { current.PaintStrip(g, new PagePaint(w, ScopeLayout.Strip, false, 0f, now, units)); }
                catch (Exception e) { ReportOnce(current, e); g.Text("STRIP ERROR", 8f, 13f, Red); }
                g.Origin(0f, 0f);
            }
            g.End();
            surface.OverlayVector.Commit();

            // The four corner brackets round the body, and the wipe's edge: the new page is revealed left to right behind it.
            g = surface.FrameCanvas;
            g.Begin(pal, unit, hp, pixel);
            g.Brackets(-3f, 14f, w + 6f, hS - 11f, 10f, Dim);
            if (transition.Wipe < 1f) g.Fill(wipeW - PageGrow - 1f, -PageGrow, 1f, hS + PageGrow * 2f, Hi, 0.9f);
            g.End();
            surface.FrameVector.Commit();
        }

        /// <summary>The page's header (LAYOUT.md 3.5): the shelf with the page's title and its headline, which is INK 12 unless it is a caution or a warning (bold 14 in that tone, pulsing while it flashes).</summary>
        private void PaintHeader(ScopeCanvas g, ScopePageBase p, float w, bool flashOn)
        {
            ScopeHeadline hl = HeadlineOf(p);
            bool hot = hl.Tone == Amber || hl.Tone == Red;
            if (hot && hl.Flash) scopeBeat = true;
            g.PageShelf(0f, 0f, w, p.Title, hl.Text, hot ? hl.Tone : Ink, hot ? 14f : 12f, hot, hot && hl.Flash && flashOn ? 0.45f : 1f);
            // The pilot chooses the context. Its name remains data even when declutter removes the decorative shelf.
            if (!g.Furniture) g.Text(p.Title, 12f, 6f, Hi, 12f, ScopeAlign.Left, true);
        }

        /// <summary>
        /// SYSTEMS (<see cref="BoardGeometry.Status"/>): the SYSTEMS page's compact strip, shelf and brackets, always on the board under the lamp line, whatever the pilot's page slots. One surface; no wipe
        /// of its own. Repainted with the shell, and every frame while SYSTEMS has something pulsing (<see cref="ScopePageBase.Fast"/>).
        /// </summary>
        private void PaintStatus(in ScopePalette pal, in BoardGeometry geo, bool flashOn)
        {
            BoardRect r = geo.Status;
            ScopePageBase sys = pages[(int)ScopePage.Systems];
            float w = r.W, hS = r.H, body = ScopeLayout.Body(true, false);
            Show(statusRect, true);
            PlaceOnGrid(statusRect, (r.X - PageGrow) * unit, (r.Y - PageGrow) * unit, Whole((w + PageGrow * 2f) * unit), Whole((hS + PageGrow * 2f) * unit));
            ScopeCanvas g = statusCanvas;
            g.Furniture = allow.Furniture;
            g.Open = true;
            g.Begin(pal, unit, Whole((hS + PageGrow * 2f) * unit), pixel);
            g.Origin(PageGrow, PageGrow);
            g.Raise(ScopeLayout.Lift(true));
            try { sys.Paint(g, new PagePaint(w, body, true, ScopeLayout.SafeTop(true), now, units)); }
            catch (Exception e) { ReportOnce(sys, e); }
            g.Raise(0f);
            PaintHeader(g, sys, w, flashOn);
            g.Brackets(-3f, 14f, w + 6f, hS - 11f, 10f, Dim);
            g.End();
            statusVector.Commit();
        }
    }
}
