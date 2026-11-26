using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The wing side's panels (LAYOUT.md 3.5; RWR, FIRES, WING since 2026-11-16): each a scope (page body, header shelf and strip, corner brackets and the wipe edge) in its slot's rectangle of ScopeLayout. A slot's
    // height animates on its own (one ScopeTransition each); the rectangles never depend on the gauge line or the ticker that hang under them. There is no glass, no header band and no closed frame: the body is drawn through the
    // page conversions (ScopeCanvas.Open), the header is a caption shelf: the shelf and its rule are the only frame.
    internal sealed partial class StatusPanel
    {
        private readonly ScopeTransition[] transitions = { new ScopeTransition(), new ScopeTransition(), new ScopeTransition() };
        /// <summary>The header words of the three slots: the panel's name, whatever page it hosts.</summary>
        private static readonly string[] SlotTitles = { "RWR", "FIRES", "WING" };
        private int wipeSoundKey = int.MinValue;
        private bool scopeBeat;

        /// <summary>The wipe's clip is the page's rectangle grown by this on every side (LAYOUT.md 10), so the brackets and the edge are inside it.</summary>
        private const float PageGrow = 8f;

        /// <summary>Every slot the layout placed (<see cref="BoardGeometry.Slot"/>) gets its panel: the hosted page's body while it has one, under the panel's header.</summary>
        private void PaintPages(in ScopePalette pal, in BoardGeometry geo, bool flashOn, int slotMask = -1)
        {
            int all = (1 << pageSurfaces.Length) - 1;
            if ((slotMask & all) == all) scopeBeat = false;      // a partial paint keeps the beat of the slots it leaves
            for (int i = 0; i < pageSurfaces.Length; i++)
            {
                if ((slotMask & (1 << i)) == 0) continue;      // a slot that did not move keeps its last paint
                BoardRect r = geo.Slot(i);
                ScopePageBase current = slotPages[i];
                bool on = current != null && r.Present;
                Show(pageSurfaces[i].Scope, on);
                if (!on) continue;
                bool content = pageLive[(int)current.Id];
                PaintPage(pal, r, pageSurfaces[i], transitions[i], current, SlotTitles[i], slotFull[i], content, content && current.HasStrip, flashOn);
                if (i == ScopeLayout.SlotWing && slotOpen[i]) wingPainted = true;
            }
        }

        /// <summary><paramref name="full"/>: the height the panel's body is laid out for (the slot's rectangle <paramref name="r"/> is shorter while it wipes, and for RWR's header line alone).</summary>
        private void PaintPage(in ScopePalette pal, in BoardRect r, PageSurface surface, ScopeTransition wipe, ScopePageBase current, string title, float full, bool content, bool strip, bool flashOn)
        {
            const bool compact = false;
            bool header = r.H <= ScopeLayout.RwrClear;      // RWR CLEAR: the shelf alone, no body and no brackets
            float hS = r.H, w = r.W, lift = ScopeLayout.Lift(compact), body = ScopeLayout.Body(full, strip), bodyH = body - lift;
            float wipeW = wipe.Wipe < 1f ? Mathf.Round(wipe.Wipe * (w + PageGrow * 2f)) : w + PageGrow * 2f;
            float wp = Whole(w * unit), hp = Whole(hS * unit), bp = Whole(bodyH * unit), grow = PageGrow * unit;
            PlaceOnGrid(surface.Scope, (r.X - PageGrow) * unit, (r.Y - PageGrow) * unit, Whole(wipeW * unit), Whole((hS + PageGrow * 2f) * unit));
            PlaceOnGrid(surface.Body, grow, grow, wp, bp);
            PlaceOnGrid(surface.Overlay, grow, grow, wp, hp);
            PlaceOnGrid(surface.Frame, grow, grow, wp, hp);

            // The page body, drawn as the painters made it (their own height, their safe area under the old header band) lifted so its first row is 20 units under the page's top.
            ScopeCanvas g = surface.BodyCanvas;
            g.Furniture = allow.Furniture;
            g.Open = true;
            g.Begin(pal, unit, bp, pixel, HudType.BoardBoost);
            g.Raise(lift);
            try
            {
                var paint = new PagePaint(w, body, compact, ScopeLayout.SafeTop(compact), now, units);
                if (header) { }
                else if (content) current.Paint(g, paint);
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
            g.Begin(pal, unit, hp, pixel, HudType.BoardBoost);
            PaintHeader(g, current, title, content ? null : current.IdleWord, w, flashOn);
            if (strip && content && !header)
            {
                g.Origin(0f, bodyH);
                try { current.PaintStrip(g, new PagePaint(w, ScopeLayout.Strip, false, 0f, now, units)); }
                catch (Exception e) { ReportOnce(current, e); g.Text("STRIP ERROR", 8f, 13f, Red); }
                g.Origin(0f, 0f);
            }
            g.End();
            surface.OverlayVector.Commit();

            // The wipe's edge: the new page is revealed left to right behind it.
            g = surface.FrameCanvas;
            g.Begin(pal, unit, hp, pixel, HudType.BoardBoost);
            if (wipe.Wipe < 1f) g.Fill(wipeW - PageGrow - 1f, -PageGrow, 1f, hS + PageGrow * 2f, Hi, 0.9f);
            g.End();
            surface.FrameVector.Commit();
        }

        /// <summary>The page's header (LAYOUT.md 3.5): the shelf with the page's title and its headline (<see cref="HeaderShelf"/>); a panel with nothing to draw says <paramref name="idle"/> there (RWR ... CLEAR).</summary>
        private void PaintHeader(ScopeCanvas g, ScopePageBase p, string title, string idle, float w, bool flashOn)
        {
            ScopeHeadline hl = HeadlineOf(p);
            if (string.IsNullOrEmpty(hl.Text)) hl.Text = idle;
            if (HeaderShelf(g, w, title, hl.Text, hl.Tone, hl.Flash, flashOn)) scopeBeat = true;
        }

        /// <summary>
        /// One header shelf for a spine panel and a ROUTE card (refit C1): the <paramref name="title"/> (12 bold DIM) and the status right-aligned to <paramref name="w"/> after a pip. The ladder: a warning (RED) is VALUE 16 bold,
        /// a caution (AMBER) LABEL 12 bold, anything else LABEL DIM with a DIM pip; a flashing hot status dips to 0.55 on the off beat (<paramref name="flashOn"/> false). Lean draws the title DIM at x 0 and no rule.
        /// True while the status flashes (the caller repaints on the beat).
        /// </summary>
        private static bool HeaderShelf(ScopeCanvas g, float w, string title, ReadOnlySpan<char> text, ScopeTone tone, bool flash, bool flashOn)
        {
            bool red = tone == Red, hot = red || tone == Amber;
            float a = hot && flash && !flashOn ? 0.55f : 1f, size = red ? HudType.Value : HudType.Label;
            g.PageShelf(0f, 0f, w, title, text, hot ? tone : Dim, size, hot, a);
            if (g.Furniture && !text.IsEmpty) g.Marker(w - ScopeCanvas.TextW(text, size) - (hot ? 12f : 10f), 6f, hot ? 5f : 4f, hot ? tone : Dim, true, a);
            if (!g.Furniture) g.Text(title, 0f, 6f, Dim, HudType.Label, ScopeAlign.Left, true);
            return hot && flash;
        }

        /// <summary>
        /// INFO (<see cref="BoardGeometry.Info"/>): the SYSTEMS page's bar cell, on a surface of its own over the whole box from its top to the last bar line's bottom (box units, grown by
        /// <see cref="PageGrow"/>). No wipe of its own. Repainted with the shell.
        /// </summary>
        private void PaintStatus(in ScopePalette pal, in BoardGeometry geo, bool flashOn)
        {
            var sys = (SystemsPage)pages[(int)ScopePage.Systems];
            float barsBottom = Math.Max(geo.Bar0.Present ? geo.Bar0.Bottom : 0f, geo.Bar1.Present ? geo.Bar1.Bottom : 0f);
            Show(statusRect, true);
            float hp = Whole((barsBottom + PageGrow * 2f) * unit);
            PlaceOnGrid(statusRect, -PageGrow * unit, -PageGrow * unit, Whole((geo.Width + PageGrow * 2f) * unit), hp);
            ScopeCanvas g = statusCanvas;
            g.Furniture = allow.Furniture;
            g.Open = true;
            g.Begin(pal, unit, hp, pixel, HudType.BoardBoost);
            g.Origin(PageGrow, PageGrow);
            g.Raise(0f);
            try { sys.PaintInfo(g, geo.Info, now, units); }
            catch (Exception e) { ReportOnce(sys, e); }
            g.End();
            statusVector.Commit();
        }

        /// <summary>AIRFRAME: compact frame and caption shelf hosting the native top-down aircraft damage schematic without overlapping SYS or ACE.</summary>
        private void PaintAirframe(in ScopePalette pal, in BoardGeometry geo, bool flashOn)
        {
            BoardRect slot = ScopeLayout.AirframeSlot(geo, boardStack);
            float hp = Whole((slot.H + PageGrow * 2f) * unit);
            Show(airframeRect, true);
            PlaceOnGrid(airframeRect, (slot.X - PageGrow) * unit, (slot.Y - PageGrow) * unit, Whole((slot.W + PageGrow * 2f) * unit), hp);

            float topPad = ScopeLayout.SafeTop(true), padX = 4f, padBot = 4f;
            PlaceOnGrid(airframeArtRect, (PageGrow + padX) * unit, (PageGrow + topPad) * unit, Whole(Mathf.Max(16f, slot.W - padX * 2f) * unit), Whole(Mathf.Max(16f, slot.H - topPad - padBot) * unit));

            DamageState dmg = airframeDamage;
            bool warn = (dmg.Flags & DamageFlags.Fire) != 0 || dmg.DetachedRatio >= HudDamage.StructureWarning || dmg.EnginesOut > 0;
            bool caution = !warn && (dmg.Flags != DamageFlags.None || dmg.DetachedRatio >= HudDamage.StructureCaution);
            airframeDamaged = warn || caution;
            if (warn) shellBeat = true;

            ScopeTone tone = warn ? Red : caution ? Amber : Dim;
            string flag = warn ? "CRIT" : caution ? "DMG" : null;
            float alpha = warn && !flashOn ? 0.5f : 1f;

            ScopeCanvas g = airframeCanvas;
            g.Furniture = allow.Furniture;
            g.Open = true;
            g.Begin(pal, unit, hp, pixel, HudType.BoardBoost);
            g.Origin(PageGrow, PageGrow);
            g.Raise(0f);
            if (g.Furniture || flag != null) g.PageShelf(0f, 0f, slot.W, "AIRFRAME", flag, tone, HudType.Label, true, alpha);
            g.Brackets(0f, topPad, slot.W, slot.H - topPad, 8f, tone, warn || caution ? 0.85f : 0.55f, BoscaliSummer.Core.Contracts.HudStroke.Hairline);
            g.End();
            airframeVector.Commit();
        }

        /// <summary>
        /// The mission rows at the top of the spine (<see cref="BoardGeometry.Mission"/>, 50 high): the MSN shelf with the timer at its right, then the mission's name and phase (no phase while it reads IN PROGRESS), then the
        /// score or the detail. Lean drops the caption and the rule; every row keeps its y.
        /// </summary>
        private void PaintMissionStatus(ScopeCanvas g, in BoardRect r)
        {
            MissionDashboardStatus m = missionStatus;
            float x = r.X, w = r.W, right = x + w, cy = r.Y + 6f, ya = r.Y + 23f, yb = r.Y + 41f;
            g.CaptionShelf(x, r.Y, w, "MSN");
            if (m.HasTimer)
            {
                int seconds = Mathf.Max(0, Mathf.CeilToInt(m.RemainingSeconds));
                float tw = g.Text(ScopeStr.Cat(seconds < 600 ? "0" : "", seconds / 60, seconds % 60 < 10 ? ":0" : ":", seconds % 60, null), right, cy, m.Ready ? Hi : Amber, HudType.Label, ScopeAlign.Right, true);
                g.Text("LEFT", right - tw - 4f, cy, Dim, HudType.Label, ScopeAlign.Right);
            }

            // Row A: the name, and the phase in its tone at the right.
            float pw = 0f;
            if (m.Phase != "IN PROGRESS" && !string.IsNullOrEmpty(m.Phase)) pw = g.FitText(m.Phase, right, ya, w * 0.5f, m.Ready ? Ink : Amber, HudType.Label, HudType.Label, ScopeAlign.Right, true);
            g.FitText(m.Name, x, ya, w - pw - (pw > 0f ? 12f : 0f), Ink, HudType.Body, HudType.Label);

            // Row B: the score, or the detail.
            if (m.HasScores)
            {
                float bx = x + g.Text("BDF", x, yb, Dim, HudType.Label) + 4f;
                bx += g.Text(ScopeStr.Int(m.Score0), bx, yb, Ink, HudType.Label, ScopeAlign.Left, true) + 4f;
                bx += g.Text(":", bx, yb, Dim, HudType.Label) + 4f;
                bx += g.Text(ScopeStr.Int(m.Score1), bx, yb, Ink, HudType.Label, ScopeAlign.Left, true) + 4f;
                g.Text("PALA", bx, yb, Dim, HudType.Label);
            }
            else g.FitText(m.Detail, x, yb, w, Ink, HudType.Label, HudType.Label);
        }
    }
}
