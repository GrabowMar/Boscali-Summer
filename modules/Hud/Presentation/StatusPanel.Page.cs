using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The wing side's page: the scope (page body, header band and strip overlay, frame and wipe) in the PAGE rectangle of ScopeLayout. Its height animates between the compact SYSTEMS strip and a full page
    // (ScopeTransition); the rectangle never depends on the gauge line or the ticker that hang under it.
    internal sealed partial class StatusPanel
    {
        private readonly ScopeTransition transition = new ScopeTransition();
        private bool scopeBeat;

        private void PaintPage(in ScopePalette pal, in BoardGeometry geo, bool compact, bool strip, bool flashOn)
        {
            BoardRect r = geo.Page;
            float hS = r.H, body = ScopeLayout.Body(compact, strip), w = r.W;
            Show(scopeRect, true);
            scopeBeat = false;
            float wp = Whole(w * unit), hp = Whole(hS * unit), bp = Whole(body * unit);
            PlaceOnGrid(scopeRect, r.X * unit, r.Y * unit, wp, hp);
            PlaceOnGrid(pageRect, 0f, 0f, wp, bp);
            PlaceOnGrid(overlayRect, 0f, 0f, wp, hp);
            PlaceOnGrid(frameRect, 0f, 0f, wp, hp);

            // The page body: the glass, its grid, then the page.
            ScopeCanvas g = pageCanvas;
            g.Furniture = allow.Furniture;
            g.Begin(pal, unit, bp, pixel);
            g.Fill(0f, 0f, w, body, Bg);
            g.Grid(w, body);
            try { page.Paint(g, new PagePaint(w, body, compact, ScopeLayout.SafeTop(compact), now, units)); }
            catch (Exception e)
            {
                ReportOnce(page, e);
                g.Text("PAGE ERROR", 8f, 60f, Red);
            }
            g.End();
            pageVector.Commit();

            // The header band (tab, pips, headline) and the page strip, above the page.
            g = overlayCanvas;
            g.Furniture = allow.Furniture;
            g.Begin(pal, unit, hp, pixel);
            PaintHeader(g, w, compact, flashOn);
            if (strip)
            {
                g.Origin(0f, body);
                g.Fill(0f, 0f, w, ScopeLayout.Strip, Bg, 0.9f);
                g.Line(0f, 0f, w, 0f, Faint);
                try { page.PaintStrip(g, new PagePaint(w, ScopeLayout.Strip, false, 0f, now, units)); }
                catch (Exception e) { ReportOnce(page, e); g.Text("STRIP ERROR", 8f, 13f, Red); }
                g.Origin(0f, 0f);
            }
            g.End();
            overlayVector.Commit();

            // The wipe edge (the new page is revealed left to right behind it) and the frame.
            g = frameCanvas;
            g.Furniture = allow.Furniture;
            g.Begin(pal, unit, hp, pixel);
            if (transition.Wipe < 1f)
            {
                float wx = Mathf.Round(transition.Wipe * w);
                g.Fill(wx, 0f, w - wx, hS, Bg);
                g.Fill(wx - 3f, 0f, 3f, hS, Hi, 0.35f);
                g.Fill(wx, 0f, 1f, hS, Hi);
            }
            g.Frame(0f, 0f, w, hS);
            g.End();
            frameVector.Commit();
        }

        /// <summary>The scope's header band: the page's tab, a pip for every other live page (in that page's tone), and the headline figure.</summary>
        private void PaintHeader(ScopeCanvas g, float w, bool compact, bool flashOn)
        {
            float hh = compact ? ScopeLayout.HeadCompact : ScopeLayout.Head, th = hh - 6f;
            g.Fill(0f, 0f, w, hh, Bg, 0.9f);
            g.Line(0f, hh, w, hh, Faint);
            string title = page.Title;
            float tw = (float)Math.Ceiling(ScopeCanvas.TextW(title, 12f)) + 16f;
            g.Box(4f, 3f, tw, th, Ink, 1f, 1f, Ink, 0.2f, chamTL: 5f);
            g.Text(title, 4f + tw / 2f, 3f + th / 2f + 1f, Hi, 12f, ScopeAlign.Center, true);

            float x = 4f + tw + 8f;
            int pips = 0;
            Span<char> code = stackalloc char[1];
            for (int i = 0; i < pages.Length && pips < 5; i++)
            {
                if (!CuePriority.Has(selector.Others, (ScopePage)i)) continue;
                ScopeHeadline other = HeadlineOf(pages[i]);
                ScopeTone tone = other.Tone == Red ? Red : other.Tone == Amber ? Amber : Ink;
                float py = Mathf.Round((hh - 16f) / 2f);
                if (tone == Red && !string.IsNullOrEmpty(other.Text))
                {
                    // A warning on a page the pilot is not looking at says WHAT in words (FIRE, BINGO, ENG 1 OUT, 7 S), not only a letter in red.
                    float pw = Math.Min((float)Math.Ceiling(ScopeCanvas.TextW(other.Text, 12f)) + 10f, 96f);
                    bool lit = other.Flash && flashOn;
                    if (other.Flash) scopeBeat = true;
                    g.Box(x, py, pw, 16f, Red, 1f, 1f, Red, lit ? 0.5f : 0.18f);
                    g.FitText(other.Text, x + pw / 2f, py + 9f, pw - 6f, lit ? Bg : Red, 12f, 12f, ScopeAlign.Center, true);
                    x += pw + 3f;
                }
                else
                {
                    g.Box(x, py, 16f, 16f, tone, 1f, 1f, tone, 0.16f);
                    code[0] = pages[i].Code;
                    g.Text(code, x + 8f, py + 9f, tone, 12f, ScopeAlign.Center, true);
                    x += 19f;
                }
                pips++;
            }

            ScopeHeadline hl = HeadlineOf(page);
            if (string.IsNullOrEmpty(hl.Text)) return;
            float size = compact ? 14f : 16f, hw = (float)Math.Ceiling(ScopeCanvas.TextW(hl.Text, size)) + 12f, hx = w - 4f - hw;
            if (hl.Tone == Amber || hl.Tone == Red)
            {
                if (hl.Flash) scopeBeat = true;
                bool pulse = hl.Flash && flashOn;
                g.Box(hx, 3f, hw, th, hl.Tone, 1f, 1f, hl.Tone, pulse ? 0.55f : 0.2f);
                g.Text(hl.Text, hx + hw / 2f, 3f + th / 2f + 1f, pulse ? Bg : hl.Tone, size, ScopeAlign.Center, true);
            }
            else g.Text(hl.Text, w - 8f, 3f + th / 2f + 1f, hl.Tone, size, ScopeAlign.Right, true, hl.Tone == Dim ? 0.8f : 1f);
        }
    }
}
