using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Autopilot.Domain;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Autopilot.Presentation
{
    internal sealed partial class MenuPainter
    {
        private float dockA = 1f, listA = 1f, dockX, listX;
        private string backFor, backTo;

        private static MenuRect Shift(MenuRect r, float dx) => new MenuRect(r.L + dx, r.T, r.R + dx, r.B);

        private Rgba A(Rgba col, float a) => col.WithAlpha(col.A * a);

        private Color TC(Rgba col, float a) => Col(A(col, a));

        private void PaintDock(float now)
        {
            DockLayout d = c.Dock;
            MenuCategory cat = c.Category;
            int side = d.Side;
            float slide = Ease(Mathf.Clamp01((now - dockShownAt) / DockSec)), swap = Ease(Mathf.Clamp01((now - swapAt) / SwapSec));
            dockA = slide; dockX = -side * 24f * (1f - slide);
            listA = slide * swap; listX = dockX + side * 14f * (1f - swap);
            MenuRect R = Shift(d.Rect, dockX);
            Fill(R, A(P.Panel, dockA)); Frame(R, 1f, A(P.Ink, dockA));
            PaintHeader(d, R);
            if (d.Empty) PaintEmpty(d, cat);
            else
            {
                foreach (DockGroup g in d.Groups) PaintGroup(g);
                if (d.RuleY > 0f) Fill(d.Rect.L + 1f + 10f * T + listX, d.RuleY, d.Rect.W - 2f - 20f * T, 1f, A(P.Line, listA));
                List<MenuRow> items = c.Items;
                foreach (DockRow dr in d.Rows)
                {
                    MenuRow row = dr.Index == -2 ? MenuRow.BackRow : items[dr.Index];
                    PaintRow(dr, row, cat);
                }
                if (d.Overflow)
                {
                    Fill(Shift(d.RailTrack, dockX), A(P.Hair, dockA));
                    Fill(Shift(d.RailThumb, dockX), A(P.Ink, dockA));
                }
            }
            if (d.HasFooter) PaintFooter(d);
        }

        private void PaintHeader(DockLayout d, MenuRect R)
        {
            float lg = l.Lg, pad = 14f * T;
            Fill(R.L + 1f, R.T + 1f + MenuLayout.HeaderH * lg - 1f, R.W - 2f, 1f, A(P.Line, dockA));
            string title = MenuHint.Title(c), crumb = MenuHint.Crumb(c), ctx = MenuHint.Context(c);
            float th = 20f * T * 1.1f, ty = R.T + 1f + 8f * lg;
            float left = R.L + 1f + pad, avail = R.W - 2f - 2f * pad;
            float ctxW = d.Overflow ? (d.ChipDown.R - d.ChipUp.L) : (ctx.Length > 0 ? text.Width(ctx, MenuFont.Badge) * T : 0f);
            float titleW = text.Width(title, MenuFont.Header) * T, crumbW = crumb.Length > 0 ? text.Width(crumb, MenuFont.Header) * T : 0f;
            bool showCrumb = crumb.Length > 0, showCtx = d.Overflow || ctx.Length > 0;
            if (crumbW + titleW + ctxW + 10f * T > avail)
            {
                if (d.Overflow) showCrumb = false; else showCtx = false;
                if (showCrumb && crumbW + titleW > avail) showCrumb = false;
            }
            float x = left;
            if (showCrumb) { text.Draw(crumb, MenuFont.Header, 20f * T, new MenuRect(x, ty, x + crumbW + 2f, ty + th), MenuTextPool.Align.Left, TC(P.Ink2, dockA)); x += crumbW; }
            text.Draw(title, MenuFont.Header, 20f * T, new MenuRect(x, ty, x + titleW + 2f, ty + th), MenuTextPool.Align.Left, TC(P.InkHi, dockA));
            if (d.Overflow)
            {
                bool canUp = d.Offset > 0, canDn = d.Offset + d.Cap < d.Count;
                MenuRect up = Shift(d.ChipUp, dockX), dn = Shift(d.ChipDown, dockX);
                text.Draw(Chip(Ups, '▲', d.Offset), MenuFont.Badge, 13f * T, up, MenuTextPool.Align.Center, TC(canUp ? P.Ink : P.Hair, dockA));
                text.Draw(Chip(Downs, '▼', d.Count - d.Offset - d.Cap), MenuFont.Badge, 13f * T, dn, MenuTextPool.Align.Center, TC(canDn ? P.Ink : P.Hair, dockA));
            }
            else if (showCtx)
                text.Draw(ctx, MenuFont.Badge, 13f * T, new MenuRect(R.R - 1f - pad - ctxW - 2f, ty, R.R - 1f - pad, ty + th), MenuTextPool.Align.Right, TC(P.Ink2, dockA));
        }

        private void PaintGroup(DockGroup g)
        {
            MenuRect r = Shift(g.Rect, listX);
            text.Draw(g.Text, MenuFont.Group, 13f * T, new MenuRect(r.L + 10f * T, r.T, r.R, r.B - 3f * l.Lg), MenuTextPool.Align.Left, TC(P.Ink2, listA), top: false);
            Fill(r.L, r.B - 1f, r.W, 1f, A(P.Hair, listA));
        }

        private void PaintEmpty(DockLayout d, MenuCategory cat)
        {
            float x = d.Rect.L + dockX + 1f + 14f * T, w = d.Rect.W - 2f - 28f * T;
            float y = d.Rect.T + 1f + MenuLayout.HeaderH * l.Lg + 6f * l.Lg + 18f * T;
            text.Draw(cat.EmptyTitle, MenuFont.Row, 17f * T, MenuRect.Of(x, y, w, 17f * T * MenuFonts.LinePitch), MenuTextPool.Align.Left, TC(P.InkDim, dockA));
            emptyWrap.Set(cat.EmptyBody, w, text, MenuFont.State, T);
            text.Draw(emptyWrap.Joined, MenuFont.State, 13f * T, MenuRect.Of(x, y + 17f * T * MenuFonts.LinePitch + 6f * T, w, 13f * T * MenuFonts.LinePitch * Math.Max(1, emptyWrap.Lines)),
                MenuTextPool.Align.Left, TC(P.Ink2, dockA), top: true);
        }

        private void PaintFooter(DockLayout d)
        {
            MenuRect f = Shift(d.Footer, dockX);
            Fill(f.L, f.T, f.W, 1f, A(P.Line, dockA));
            float pad = 14f * T;
            string left = MenuHint.FooterLeft(c), right = MenuHint.FooterRight(c);
            if (left.Length > 0) text.Draw(left, MenuFont.Badge, 13f * T, new MenuRect(f.L + pad, f.T, f.R - pad, f.B), MenuTextPool.Align.Left, TC(P.Ink2, dockA));
            if (right.Length > 0) text.Draw(right, MenuFont.Badge, 13f * T, new MenuRect(f.L + pad, f.T, f.R - pad, f.B), MenuTextPool.Align.Right, TC(P.Txt, dockA));
        }

        // ---------------------------------------------------------------- one row

        private void PaintRow(DockRow dr, MenuRow row, MenuCategory cat)
        {
            MenuRect r = Shift(dr.Rect, listX);
            bool back = dr.Index == -2;
            bool focus = c.Open && dr.Index == c.Focus && c.Focus != -1;
            bool blocked = !back && row.IsBlocked;
            bool amber = !back && (row.Kind == MenuKind.Danger || row.NeedsHold);
            bool flash = c.FlashId == row.Id && !back;
            bool iconless = !back && (cat.Iconless || (c.Sub != null && c.Sub.Iconless));
            if (back) iconless = cat.Iconless || (c.Sub != null && c.Sub.Iconless);
            float t = T, a = listA;

            // colours of this row
            Rgba label = P.Txt, state = P.Ink2, icon = P.Ink, num = P.Ink2, right = P.Ink;
            if (!back)
            {
                switch (row.ToneNow)
                {
                    case MenuTone.Active: state = P.InkHi; break;
                    case MenuTone.Caution: state = P.Amber; break;
                    case MenuTone.Danger: state = P.Red; break;
                    case MenuTone.Dim: state = P.InkDim; label = P.InkDim; icon = P.InkDim; right = P.InkDim; break;
                }
                if (amber) { label = P.Amber; state = P.Amber; icon = P.Amber; right = P.Amber; }
                if (blocked) { label = P.InkDim; state = P.InkDim; icon = P.InkDim; right = P.InkDim; }
            }
            Rgba bracket = P.OnInk;
            if (flash) { Fill(r, A(P.Flash, a)); label = icon = num = right = P.OnInk; state = P.OnInk; }
            else if (focus)
            {
                if (blocked) { Fill(r, A(P.Ink.WithAlpha(0.14f), a)); AvStrokes.DashedLine(q, r.L, H - r.T, r.R, H - r.T, 4f, 3f, 1f, A(P.InkDim, a)); AvStrokes.DashedLine(q, r.L, H - r.B, r.R, H - r.B, 4f, 3f, 1f, A(P.InkDim, a)); bracket = P.InkDim; }
                else if (amber) { Fill(r, A(P.Amber.WithAlpha(0.14f), a)); Frame(r, 1f, A(P.Amber, a)); bracket = P.Amber; num = P.Amber; }
                else { Fill(r, A(P.InkHi, a)); label = icon = num = right = P.OnInk; state = P.OnInk2; }
                // HOLD flick: the brackets close on the row once release will run it; every other way of focusing shows them at once
                if (c.Mode != MenuMode.Hold || !c.FocusFromPointer || c.Armed || blocked || amber) Brackets(r, A(bracket, a));
            }
            else if (!back && dr.Index == c.Predicted)
            {
                // the row last run here: a chevron in the margin, where the keys land first
                float my = r.T + r.H / 2f, mh = 5f * T;
                Line(r.L + 1f, my - mh, r.L + 1f + 4f * T, my, 1.5f * T, A(P.InkHi, a));
                Line(r.L + 1f + 4f * T, my, r.L + 1f, my + mh, 1.5f * T, A(P.InkHi, a));
            }
            if (!back && row.Kind == MenuKind.Picker && row.Selected) Fill(r.L, r.T, 3f, r.H, A(focus && !amber ? P.OnInk : P.InkHi, a));

            // number gutter
            float kcX = r.L + 5f * t, kcH = 26f * t, kcY = r.T + (r.H - kcH) / 2f;
            int digit = dr.Index - c.Dock.Offset + 1;
            string number = back ? "0" : dr.Index >= 0 ? (digit < Digits.Length ? Digits[digit] : digit.ToString()) : "";
            float deny = DenyK(); // a refused press: the row gutters are the live set while a dock is up
            Fill(kcX + 22f * t - 1f, kcY, 1f, kcH, A(deny > 0f ? Mix(P.Hair, P.Amber, deny) : P.Hair, a));
            text.Draw(number, MenuFont.Badge, 13f * t, MenuRect.Of(kcX, kcY, 22f * t - 1f, kcH), MenuTextPool.Align.Center, TC(deny > 0f ? Mix(num, P.Amber, deny) : num, a));

            // icon
            float x = kcX + 22f * t + 9f * t;
            if (!iconless)
            {
                string id = back ? "back" : row.Kind == MenuKind.Danger ? (row.WarnIcon ?? row.Icon) : row.IconNow;
                Glyph(id, x + 18f * t, r.T + r.H / 2f, 32f * t, A(icon, a));
                x += 36f * t + 9f * t;
            }

            // right edge
            float rightW = back ? BackBadgeW() : l.RowRightWidth(row);
            float rx = r.R - 10f * t - rightW;
            float textW = Math.Max(0f, rx - (rightW > 0f ? 9f * t : 0f) - x);
            if (back) PaintBackBadge(rx, r, a, right);
            else PaintRight(row, rx, r, a, right, focus, blocked);

            // text
            float lh = 17f * t * MenuFonts.LinePitch, sh = 13f * t * MenuFonts.LinePitch, top = r.T + (r.H - (lh + sh + t)) / 2f;
            string labelText = back ? "BACK" : row.Label;
            text.Draw(labelText, back ? MenuFont.Plate : MenuFont.Row, (back ? 15f : 17f) * t, MenuRect.Of(x, top, textW, lh), MenuTextPool.Align.Left, TC(label, a));
            if (!back && focus && row.NeedsHold && !blocked)
            {
                float hw = text.Width(row.HoldLabel, MenuFont.State) * t + 2f;
                text.Draw(row.HoldLabel, MenuFont.State, 13f * t, MenuRect.Of(x, top + lh + t, Math.Min(hw, textW), sh), MenuTextPool.Align.Left, TC(P.Amber, a));
                float bx = x + hw + 10f * t, bw = Math.Max(30f * t, x + textW - bx), bh = 9f * t, by = top + lh + t + (sh - bh) / 2f;
                MenuRect bar = MenuRect.Of(bx, by, bw, bh);
                Frame(bar, 1f, A(P.Amber, a));
                float p = c.Hold != null && c.Hold.Row == row ? c.Hold.Progress : 0f;
                Fill(bar.L + 2f, bar.T + 2f, (bar.W - 4f) * p, bar.H - 4f, A(P.Amber, a));
            }
            else
            {
                if (back && !ReferenceEquals(backFor, cat.Label)) { backFor = cat.Label; backTo = "TO " + cat.Label; }
                string stateText = back ? backTo : row.State;
                text.Draw(stateText, MenuFont.State, 13f * t, MenuRect.Of(x, top + lh + t, textW, sh), MenuTextPool.Align.Left, TC(state, a));
            }
        }

        /// <summary>The prototype's .dv: at least 30 px, wider when the device glyph (BKSP) needs it, 5 px padding each side.</summary>
        private float BackBadgeW() => Math.Max(30f * T, text.Width(MenuHint.BackGlyph(c.Device), MenuFont.Badge) * T + 10f * T);

        private void PaintBackBadge(float rx, MenuRect r, float a, Rgba col)
        {
            MenuRect box = MenuRect.Of(rx, r.T + (r.H - 22f * T) / 2f, BackBadgeW(), 22f * T);
            Frame(box, 1f, A(col, a));
            text.Draw(MenuHint.BackGlyph(c.Device), MenuFont.Badge, 13f * T, box, MenuTextPool.Align.Center, TC(col, a));
        }

        private void PaintRight(MenuRow row, float rx, MenuRect r, float a, Rgba col, bool focus, bool blocked)
        {
            float t = T, cy = r.T + r.H / 2f, x = rx;
            if (row.Ping != null) { Glyph(row.Ping, x + 9f * t, cy, 18f * t, A(col, a)); x += 18f * t + 8f * t; }
            switch (row.Kind)
            {
                case MenuKind.Toggle when row.ArmedPip:
                    Pip(x, cy, 14f * t, row.IsOn, blocked, A(row.IsOn && !focus ? P.Amber : col, a), false);
                    break;
                case MenuKind.Toggle when row.Checkbox:
                {
                    MenuRect box = MenuRect.Of(x, cy - 9f * t, 18f * t, 18f * t);
                    Frame(box, 2f, A(col, a));
                    if (row.IsOn) Fill(box.L + 4f * t, box.T + 4f * t, box.W - 8f * t, box.H - 8f * t, A(col, a));
                    break;
                }
                case MenuKind.Picker:
                    Pip(x, cy, 14f * t, row.Selected, row.Hatched, A(col, a), true);
                    break;
                case MenuKind.Submenu:
                    text.Draw("›", MenuFont.Header, 24f * t, MenuRect.Of(x, cy - 15f * t, 14f * t, 30f * t), MenuTextPool.Align.Center, TC(focus ? col : P.InkHi, a));
                    break;
            }
        }

        /// <summary>Square (or chamfered) 2 px pip: solid = selected / armed, hollow = ready, hatched = empty or cooling down.</summary>
        private void Pip(float x, float cy, float size, bool solid, bool hatched, Rgba col, bool chamfer)
        {
            float y = cy - size / 2f, c = chamfer ? size * 0.3f : 0f;
            if (solid) Fill(x + 2f, y + 2f, size - 4f, size - 4f, col);
            Line(x, y, x + size - c, y, 2f, col); Line(x + size - c, y, x + size, y + c, 2f, col);
            Line(x + size, y + c, x + size, y + size, 2f, col); Line(x + size, y + size, x, y + size, 2f, col); Line(x, y + size, x, y, 2f, col);
            if (hatched && !solid)
                for (int i = 1; i <= 3; i++)
                {
                    float o = size * i / 4f;
                    Line(x + 1f, y + o, x + o, y + 1f, 1.5f, col);
                }
        }
    }
}
