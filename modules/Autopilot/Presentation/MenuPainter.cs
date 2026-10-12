using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Autopilot.Domain;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Autopilot.Presentation
{
    /// <summary>
    /// Paints the controller's state: every plate, outline, disc, glyph and line into ONE vector surface (a single
    /// <see cref="AvQuadBuffer"/>, so every colour renders the same brightness; lit elements and plates share it), and every word through
    /// the <see cref="MenuTextPool"/> above it. All numbers are the prototype's (style.css) times the layout scales. Origin top-left, y down;
    /// the primitives flip to the canvas. Called every frame while the menu or its toast shows, so everything that builds a string, wraps text
    /// or fits the readout is memoized on the values it depends on (the focused row and its live text, the layout, the controller's mode and
    /// device); a steady frame allocates nothing, a change allocates once.
    /// </summary>
    internal sealed partial class MenuPainter
    {
        private const float OpenSec = 0.06f, DockSec = 0.07f, SwapSec = 0.06f;

        private readonly AvQuadBuffer q;
        private readonly MenuTextPool text;
        private readonly float[] leaderPts = new float[MenuLeader.MaxPoints * 2];
        private MenuController c;
        private MenuLayout l;
        private MenuPalette P, D;
        private float H, T;

        // dock animation: slide in when the dock first shows, slide the list when its content swaps
        private int dockCat = -1;
        private MenuRow dockSub;
        private bool dockWasShown;
        private float dockShownAt, swapAt = -10f;

        public MenuPainter(AvQuadBuffer buffer, MenuTextPool pool) { q = buffer; text = pool; }

        /// <summary>The ring plates and readout need the open fade; 0..1.</summary>
        public static float OpenProgress(float openedAt, float now) => Ease(Mathf.Clamp01((now - openedAt) / OpenSec));

        private static float Ease(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

        public void Paint(MenuController controller, MenuLayout layout, float now)
        {
            c = controller; l = layout; H = l.H; T = l.T;
            P = MenuPalette.Get(); D = MenuPalette.Get(true);
            q.Clear();
            text.Begin(H);
            if (c.Open)
            {
                PrepareReadout();
                Fill(0f, 0f, l.W, l.H, P.Scrim); // the whole screen a shade greyer, edge to edge: nothing to cut off
                DockAnimation(now);
                bool wheel = c.Wheel, docked = c.Cat >= 0 && !wheel; // a controller reads the list off the ring itself
                if (docked) PaintLeaderAndDock(now); else dockWasShown = false;
                PaintOrbit();
                if (wheel) PaintWheel(); else PaintRing();
                PaintHub();
                PaintAction();
                PaintHint();
                if (docked) PaintDock(now);
                PaintPointer();
            }
            if (c.Showing) PaintToast();
            text.End();
        }

        // ---------------------------------------------------------------- primitives (menu pixels in, canvas out)

        private void Fill(float x, float y, float w, float h, Rgba col) { if (w > 0f && h > 0f) AvStrokes.Fill(q, x, H - y - h, w, h, col); }

        private void Fill(MenuRect r, Rgba col) => Fill(r.L, r.T, r.W, r.H, col);

        private void Frame(MenuRect r, float w, Rgba col)
        {
            Fill(r.L, r.T, r.W, w, col); Fill(r.L, r.B - w, r.W, w, col);
            Fill(r.L, r.T + w, w, r.H - 2f * w, col); Fill(r.R - w, r.T + w, w, r.H - 2f * w, col);
        }

        private void Line(float x0, float y0, float x1, float y1, float w, Rgba col) => AvStrokes.Line(q, x0, H - y0, x1, H - y1, w, col);

        private void Disc(float cx, float cy, float r, Rgba col) => AvStrokes.Ring(q, cx, H - cy, r * 0.5f, 40, r, col);

        private void Ring(float cx, float cy, float r, float w, Rgba col, int segs = 48) => AvStrokes.Ring(q, cx, H - cy, r, segs, w, col);

        /// <summary>The 4 corner brackets of a focused row, inset 4 px.</summary>
        private void Brackets(MenuRect r, Rgba col)
        {
            float a = 4f, arm = 9f, w = 2f;
            Fill(r.L + a, r.T + a, arm, w, col); Fill(r.L + a, r.T + a, w, arm, col);
            Fill(r.R - a - arm, r.T + a, arm, w, col); Fill(r.R - a - w, r.T + a, w, arm, col);
            Fill(r.L + a, r.B - a - w, arm, w, col); Fill(r.L + a, r.B - a - arm, w, arm, col);
            Fill(r.R - a - arm, r.B - a - w, arm, w, col); Fill(r.R - a - w, r.B - a - arm, w, arm, col);
        }

        /// <summary>A glyph from the generated table (the NOA pack's shapes at the pack's stroke), scaled to <paramref name="box"/> pixels square around (cx, cy).
        /// Each segment runs half a stroke past a corner, so the pack's mitred joins close instead of showing a notch.</summary>
        private void Glyph(string id, float cx, float cy, float box, Rgba col)
        {
            if (id == null || !MenuIcons.Table.TryGetValue(id, out IconPoly[] polys)) return;
            float k = box / 24f, x0 = cx - box / 2f, y0 = cy - box / 2f, w = MenuIcons.Stroke * k;
            foreach (IconPoly p in polys)
            {
                float[] v = p.Points;
                int n = v.Length / 2;
                if (p.Filled)
                {
                    float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                    for (int i = 0; i < n; i++) { minX = Math.Min(minX, v[2 * i]); maxX = Math.Max(maxX, v[2 * i]); minY = Math.Min(minY, v[2 * i + 1]); maxY = Math.Max(maxY, v[2 * i + 1]); }
                    Fill(x0 + minX * k, y0 + minY * k, (maxX - minX) * k, (maxY - minY) * k, col);
                    continue;
                }
                int segs = p.Closed ? n : n - 1;
                for (int i = 0; i < segs; i++)
                {
                    int j = (i + 1) % n;
                    float ax = x0 + v[2 * i] * k, ay = y0 + v[2 * i + 1] * k, bx = x0 + v[2 * j] * k, by = y0 + v[2 * j + 1] * k;
                    float dx = bx - ax, dy = by - ay, len = Mathf.Sqrt(dx * dx + dy * dy);
                    if (len < 0.001f) continue;
                    float ex = dx / len * w * 0.5f, ey = dy / len * w * 0.5f;
                    bool joinA = p.Closed || i > 0, joinB = p.Closed || i < segs - 1; // an open end keeps the pack's butt cap
                    Line(joinA ? ax - ex : ax, joinA ? ay - ey : ay, joinB ? bx + ex : bx, joinB ? by + ey : by, w, col);
                }
            }
        }

        private static Color Col(Rgba c) => MenuPalette.ToColor(c);

        /// <summary>The refusal pulse: 1 right after a refused press, fading to 0 over <see cref="MenuController.DenySec"/> (the prototype's 180 ms denypulse).</summary>
        private float DenyK() => c.Denying ? Mathf.Clamp01((c.DenyUntil - c.Now) / MenuController.DenySec) : 0f;

        private static Rgba Mix(Rgba a, Rgba b, float k) => new Rgba(a.R + (b.R - a.R) * k, a.G + (b.G - a.G) * k, a.B + (b.B - a.B) * k, a.A + (b.A - a.A) * k);

        /// <summary>A greedy word wrap joined with explicit line breaks, redone only when the text, width, face or scale change.</summary>
        private sealed class Wrapped
        {
            private string text;
            private float width, scale;
            private MenuFont font;
            private bool valid;
            public string Joined = "";
            public int Lines;

            public void Set(string source, float w, IMenuMetrics metrics, MenuFont f, float t)
            {
                if (valid && width == w && scale == t && font == f && string.Equals(text, source)) return;
                var lines = MenuReadoutFit.Wrap(source, w, metrics, f, t);
                Joined = string.Join("\n", lines); Lines = lines.Count;
                text = source; width = w; scale = t; font = f; valid = true;
            }
        }

        private static readonly string[] Digits = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
        private static readonly string[] Ups = new string[40], Downs = new string[40];

        /// <summary>"up3" / "down5" chips without building the string every frame.</summary>
        private static string Chip(string[] cache, char arrow, int n)
        {
            n = Math.Max(0, n);
            if (n >= cache.Length) return arrow + n.ToString();
            return cache[n] ??= arrow + n.ToString();
        }

        // ---------------------------------------------------------------- ring

        private void PaintRing()
        {
            bool dockUp = c.Cat >= 0;
            float deny = DenyK();
            for (int i = 0; i < l.Count; i++)
            {
                MenuCategory cat = c.Model.Categories[i];
                bool sel = i == c.Cat, off = !cat.Available;
                MenuPalette p = (dockUp && !sel) || off ? D : P;
                float x = l.DiscX[i], y = l.DiscY[i], rd = l.Rd;
                // disc
                Disc(x, y, rd, sel ? new Rgba(0f, 0.051f, 0.027f) : p.PlateHi);
                if (sel) { Ring(x, y, rd + 3f, 6f, P.Glow.WithAlpha(0.10f)); Ring(x, y, rd + 6f, 6f, P.Glow.WithAlpha(0.05f)); }
                if (off) AvStrokes.DashedRing(q, x, H - y, rd, 20, 0.55f, 2f, p.Ink2);
                else Ring(x, y, rd, 2f, sel ? P.InkHi : p.Ink);
                float box = rd * 1.12f;
                Glyph(cat.Icon, x, y, box, sel ? P.InkHi : off ? p.Ink2 : p.Ink);
                // plate
                bool two = sel || cat.BadgeShow;
                MenuRect plate = l.Plate(i, two ? cat.Badge : null);
                Fill(plate, sel ? P.PlateHi : p.Plate);
                Frame(plate, 1f, sel ? P.InkHi : p.Line);
                bool left = l.Side[i] < 0;
                var align = left ? MenuTextPool.Align.Right : MenuTextPool.Align.Left;
                float padX = 8f * T;
                float lh = 15f * T * MenuFonts.LinePitch, bh = 13f * T * MenuFonts.LinePitch;
                Color labelColor = Col(sel ? P.InkHi : off ? p.Ink2 : p.Txt);
                if (two)
                {
                    float top = plate.T + (plate.H - lh - bh) / 2f;
                    text.Draw(cat.Label, MenuFont.Plate, 15f * T, new MenuRect(plate.L + padX, top, plate.R - padX, top + lh), align, labelColor);
                    Rgba badge = cat.BadgeAlert && !sel ? P.Amber : sel ? P.Ink : off ? p.InkDim : p.Ink2;
                    text.Draw(cat.Badge, MenuFont.Badge, 13f * T, new MenuRect(plate.L + padX, top + lh, plate.R - padX, top + lh + bh), align, Col(badge));
                }
                else text.Draw(cat.Label, MenuFont.Plate, 15f * T, new MenuRect(plate.L + padX, plate.T, plate.R - padX, plate.B), align, labelColor);
                // number key-cap: the live set is the rows once a dock is up, so the ring digits fade
                MenuRect kc = l.KeyCap[i];
                float opacity = dockUp ? (sel ? 0.55f : 0.15f) : 1f;
                Rgba kcInk = sel ? P.InkHi : p.Ink2, kcLine = sel ? P.InkHi : p.Line.WithAlpha(1f);
                if (!dockUp && deny > 0f) { kcInk = Mix(kcInk, P.Amber, deny); kcLine = Mix(kcLine, P.Amber, deny); } // a refused press (the ring keys are the live set)
                if (opacity >= 1f) Fill(kc, p.PlateHi); // a faded cap keeps only its outline and digit: a plate fill would be a dark square over the cockpit
                Frame(kc, 1f, P.Fade(kcLine, opacity));
                text.Draw(cat.Key, MenuFont.Badge, 13f * T, kc, MenuTextPool.Align.Center, Col(P.Fade(kcInk, opacity)));
            }
        }

        // ---------------------------------------------------------------- the controller wheel

        /// <summary>The ring as a controller sees a list: one row per slot (glyph in the disc, label over its state on the plate), BACK on the 0 slot and
        /// MORE on slot 9 of a long list. The aimed slot is the solid one; amber is a danger row, dim a refused one.</summary>
        private void PaintWheel()
        {
            List<MenuRow> items = c.Items;
            for (int i = 0; i < l.Count; i++)
            {
                int w = c.WheelSlot(i);
                float x = l.DiscX[i], y = l.DiscY[i], rd = l.Rd;
                if (w == MenuController.SlotEmpty) { AvStrokes.DashedRing(q, x, H - y, rd * 0.45f, 8, 0.5f, 1.5f, D.InkDim); continue; }
                MenuRow row = w >= 0 ? items[w] : null;
                bool aim = i == c.WheelAim, blocked = row != null && row.IsBlocked, amber = row != null && (row.Kind == MenuKind.Danger || row.NeedsHold);
                Rgba ink = blocked ? P.InkDim : amber ? P.Amber : aim ? P.InkHi : P.Ink;
                Disc(x, y, rd, aim ? new Rgba(0f, 0.051f, 0.027f) : P.PlateHi);
                if (aim) { Ring(x, y, rd + 3f, 6f, P.Glow.WithAlpha(0.10f)); Ring(x, y, rd + 6f, 6f, P.Glow.WithAlpha(0.05f)); }
                Ring(x, y, rd, 2f, ink);
                string icon = w == MenuController.SlotBack ? "back" : w == MenuController.SlotMore ? "station-next" : row.Kind == MenuKind.Danger ? (row.WarnIcon ?? row.Icon) : row.IconNow;
                Glyph(icon, x, y, rd * 1.12f, ink);
                string label = w == MenuController.SlotBack ? "BACK" : w == MenuController.SlotMore ? "MORE" : row.Label;
                string state = w == MenuController.SlotBack ? "ONE LEVEL UP" : w == MenuController.SlotMore ? "NEXT PAGE" : row.State;
                MenuRect plate = l.SlotPlate(i, label, state);
                bool solid = aim && !blocked && !amber;
                Fill(plate, solid ? P.InkHi : P.Plate);
                Frame(plate, 1f, aim ? ink : P.Line);
                if (aim && amber && c.Hold != null && c.Hold.Row == row) Fill(plate.L + 1f, plate.B - 4f, (plate.W - 2f) * c.Hold.Progress, 3f, P.Amber);
                var align = l.Side[i] < 0 ? MenuTextPool.Align.Right : MenuTextPool.Align.Left;
                float padX = 8f * T, lh = 15f * T * MenuFonts.LinePitch, bh = 13f * T * MenuFonts.LinePitch, top = plate.T + (plate.H - lh - bh) / 2f;
                Rgba stateInk = P.Ink2;
                if (row != null)
                    switch (row.ToneNow)
                    {
                        case MenuTone.Active: stateInk = P.InkHi; break;
                        case MenuTone.Caution: stateInk = P.Amber; break;
                        case MenuTone.Danger: stateInk = P.Red; break;
                    }
                text.Draw(label, MenuFont.Plate, 15f * T, new MenuRect(plate.L + padX, top, plate.R - padX, top + lh), align, Col(solid ? P.OnInk : blocked ? P.InkDim : amber ? P.Amber : P.Txt));
                text.Draw(state, MenuFont.Badge, 13f * T, new MenuRect(plate.L + padX, top + lh, plate.R - padX, top + lh + bh), align, Col(solid ? P.OnInk2 : blocked ? P.InkDim : amber ? P.Amber : stateInk));
            }
        }

        // ---------------------------------------------------------------- hub + bottom console

        /// <summary>The orbit the discs sit on, and a bold arc on the hub's rim under the open category (a thinner one under a category about to open).</summary>
        private void PaintOrbit()
        {
            Ring(l.Cx, l.Cy, l.R, 1f, P.Hair, 96);
            if (c.HoverCategory >= 0) RimArc(c.Model.Categories[c.HoverCategory].Angle, 2f, P.Ink2);
            if (c.Cat >= 0) RimArc(c.Category.Angle, 4f, P.InkHi);
        }

        private void RimArc(float angle, float width, Rgba col) =>
            AvStrokes.Arc(q, l.Cx, H - l.Cy, l.HubR + 7f * l.Rho, 90f - angle - MenuLayout.SectorDeg, 90f - angle + MenuLayout.SectorDeg, 12, width, col);

        /// <summary>The hub names where you are: the open category's glyph and label, or the aircraft at the ring.</summary>
        private void PaintHub()
        {
            Disc(l.Cx, l.Cy, l.HubR, P.Hub);
            Ring(l.Cx, l.Cy, l.HubR, 2f, c.Cat >= 0 ? P.InkHi : P.Ink);
            MenuCategory cat = c.Category;
            if (cat == null) { Glyph("aircraft", l.Cx, l.Cy, l.HubR * 2f * 0.58f, P.InkHi); return; }
            float lh = 13f * T * MenuFonts.LinePitch, box = l.HubR * 0.82f, top = l.Cy - (box + lh) / 2f;
            Glyph(cat.Icon, l.Cx, top + box / 2f, box, P.InkHi);
            text.Draw(cat.Label, MenuFont.Badge, 13f * T, MenuRect.Of(l.Cx - l.HubR, top + box, 2f * l.HubR, lh), MenuTextPool.Align.Center, Col(P.InkHi));
        }

        /// <summary>The focused action, spelled out on top of the help bar: what it is and what it does (or why it is refused).</summary>
        private void PaintAction()
        {
            if (!roFocus) return;
            float shift = c.Cat >= 0 ? l.HintShift(c.Dock) : 0f, pad = 12f * T;
            MenuRect r = MenuRect.Of(l.Action.L + shift, l.Action.T, l.Action.W, l.Action.H);
            Fill(r, P.Panel); Frame(r, 1f, P.InkHi);
            bool desc = !string.IsNullOrEmpty(roDesc);
            float lh = 17f * T * MenuFonts.LinePitch, dh = desc ? 13f * T * MenuFonts.LinePitch : 0f, top = r.T + (r.H - lh - dh) / 2f;
            text.Draw(roTitle, MenuFont.Row, 17f * T, MenuRect.Of(r.L + pad, top, r.W - 2f * pad, lh), MenuTextPool.Align.Center, Col(P.InkHi));
            if (desc) text.Draw(roDesc, MenuFont.State, 13f * T, MenuRect.Of(r.L + pad, top + lh, r.W - 2f * pad, dh), MenuTextPool.Align.Center, Col(roBlocked ? P.Amber : P.Txt));
        }

        private string roTitle, roDesc;
        private bool roFocus, roValid, roBlocked;
        private readonly Wrapped emptyWrap = new Wrapped();
        // what the console words were built from: rebuilt only when one of these changes
        private MenuRow roRow;
        private MenuCategory roCat;
        private string roLabelSrc, roDescSrc, roVerbSrc;

        private void PrepareReadout()
        {
            MenuRow row = c.FocusedRow;
            roFocus = row != null;
            if (!roFocus) return;
            roBlocked = row != MenuRow.BackRow && row.IsBlocked;
            string label = row.Label, desc = row.IsBlocked ? row.BlockedText : row.Desc;
            if (roValid && row == roRow && c.Category == roCat && ReferenceEquals(label, roLabelSrc) && ReferenceEquals(desc, roDescSrc) && ReferenceEquals(row.Verb, roVerbSrc)) return;
            MenuReadout ro = MenuHint.Readout(c).Value;
            roTitle = ro.Title; roDesc = ro.Desc;
            roRow = row; roCat = c.Category; roLabelSrc = label; roDescSrc = desc; roVerbSrc = row.Verb; roValid = true;
        }

        // ---------------------------------------------------------------- title, hint, toast, pointer

        private void PaintHint()
        {
            MenuHintParts h = Hint();
            float shift = c.Cat >= 0 ? l.HintShift(c.Dock) : 0f;
            MenuRect r = MenuRect.Of(l.Hint.L + shift, l.Hint.T, l.Hint.W, l.Hint.H);
            Fill(r, P.Panel); Frame(r, 1f, P.InkHi);
            float a = 124f * T, b = 396f * T;
            Fill(r.L + a, r.T + 1f, 1f, r.H - 2f, P.InkHi); Fill(r.L + a + b, r.T + 1f, 1f, r.H - 2f, P.InkHi);
            float pad = 8f * T;
            text.Draw(h.Mode, MenuFont.Hint, 15f * T, new MenuRect(r.L + pad, r.T, r.L + a - pad, r.B), MenuTextPool.Align.Center, Col(P.InkHi));
            text.Draw(h.Live, MenuFont.Hint, 15f * T, new MenuRect(r.L + a + pad, r.T, r.L + a + b - pad, r.B), MenuTextPool.Align.Center, Col(h.Warn ? P.Amber : P.Txt));
            text.Draw(h.Back, MenuFont.Hint, 15f * T, new MenuRect(r.L + a + b + pad, r.T, r.R - pad, r.B), MenuTextPool.Align.Center, Col(P.InkHi));
        }

        private MenuHintParts hint;
        private MenuRow hintRow;
        private string hintLabel, hintBlocked, hintVerb, hintKey, hintRefusal;
        private int hintCat = -1;
        private MenuRow hintSub;
        private MenuMode hintMode;
        private MenuDevice hintDevice;
        private bool hintValid, hintFromPointer, hintHolding, hintLocked, hintRoot, hintNeedsHold;

        /// <summary>The hint bar's words, rebuilt only when something they say changed (they concatenate and split strings).</summary>
        private MenuHintParts Hint()
        {
            MenuRow row = c.FocusedRow;
            string label = row?.Label, blocked = row != null && row != MenuRow.BackRow && row.IsBlocked ? row.BlockedText : null;
            bool holding = c.Hold != null, root = c.Cat < 0, needsHold = row != null && row.NeedsHold;
            if (!hintValid || row != hintRow || !ReferenceEquals(label, hintLabel) || !ReferenceEquals(blocked, hintBlocked) || !ReferenceEquals(row?.Verb, hintVerb) ||
                !ReferenceEquals(c.OpenKeyName, hintKey) || c.Mode != hintMode || c.Device != hintDevice || c.FocusFromPointer != hintFromPointer || holding != hintHolding ||
                c.Locked != hintLocked || root != hintRoot || needsHold != hintNeedsHold || c.Cat != hintCat || c.Sub != hintSub || !ReferenceEquals(c.Refusal, hintRefusal))
            {
                hint = MenuHint.Parts(c);
                hintRow = row; hintLabel = label; hintBlocked = blocked; hintVerb = row?.Verb; hintKey = c.OpenKeyName; hintMode = c.Mode; hintDevice = c.Device;
                hintFromPointer = c.FocusFromPointer; hintHolding = holding; hintLocked = c.Locked; hintRoot = root; hintNeedsHold = needsHold; hintCat = c.Cat; hintSub = c.Sub; hintRefusal = c.Refusal; hintValid = true;
            }
            return hint;
        }

        private void PaintToast()
        {
            MenuToast t = c.Toast;
            P = MenuPalette.Get();
            float icon = 26f * T, gap = 12f * T;
            float tw = text.Width(t.Text, MenuFont.Row) * T, cw = string.IsNullOrEmpty(t.Context) ? 0f : text.Width(t.Context, MenuFont.Badge) * T;
            float w = 12f * T + icon + gap + tw + (cw > 0f ? gap + 1f + gap + cw : 0f) + 18f * T, h = icon + 18f * T;
            MenuRect r = MenuRect.Of(l.Cx - w / 2f, l.ToastTop, w, h);
            Fill(r, P.PlateHi); Frame(r, 1f, P.InkHi);
            Glyph(t.Icon, r.L + 12f * T + icon / 2f, r.T + h / 2f, icon, P.InkHi);
            float x = r.L + 12f * T + icon + gap;
            text.Draw(t.Text, MenuFont.Row, 17f * T, MenuRect.Of(x, r.T, tw + 2f, h), MenuTextPool.Align.Left, Col(P.InkHi));
            if (cw > 0f)
            {
                x += tw + gap;
                Fill(x, r.T + 9f * T, 1f, h - 18f * T, P.Line);
                text.Draw(t.Context, MenuFont.Badge, 13f * T, MenuRect.Of(x + 1f + gap, r.T, cw + 2f, h), MenuTextPool.Align.Left, Col(P.Ink2));
            }
        }

        /// <summary>A controller picking on the ring: a small reticle where the stick points and a dashed line from the hub. The mouse has its own cursor.</summary>
        private void PaintPointer()
        {
            if (c.Device != MenuDevice.Pad || c.DockFocus) return;
            float px = c.PtrX, py = c.PtrY, dx = px - l.Cx, dy = py - l.Cy, r = (float)Math.Sqrt(dx * dx + dy * dy);
            if (r > l.HubR + 4f)
            {
                float k = (l.HubR + 3f) / r, x0 = l.Cx + dx * k, y0 = l.Cy + dy * k;
                AvStrokes.DashedLine(q, x0, H - y0, px, H - py, 3f, 4f, 3f, P.Casing);
                AvStrokes.DashedLine(q, x0, H - y0, px, H - py, 3f, 4f, 1f, P.InkHi.WithAlpha(0.6f));
            }
            float u = T;
            for (int pass = 0; pass < 2; pass++)
            {
                Rgba col = pass == 0 ? P.Casing : P.InkHi;
                float w = (pass == 0 ? 4f : 1.8f) * u;
                Line(px - 11f * u, py, px - 4f * u, py, w, col); Line(px + 4f * u, py, px + 11f * u, py, w, col);
                Line(px, py - 11f * u, px, py - 4f * u, w, col); Line(px, py + 4f * u, px, py + 11f * u, w, col);
            }
            Fill(px - 1.2f * u, py - 1.2f * u, 2.4f * u, 2.4f * u, P.InkHi);
        }

        // ---------------------------------------------------------------- leader

        private void PaintLeaderAndDock(float now)
        {
            MenuCategory cat = c.Category;
            int n = MenuLeader.Compute(l, c.Cat, cat, c.Dock, leaderPts);
            for (int i = 0; i + 1 < n; i++) Line(leaderPts[2 * i], leaderPts[2 * i + 1], leaderPts[2 * i + 2], leaderPts[2 * i + 3], 5f, P.Casing);
            for (int i = 0; i + 1 < n; i++) Line(leaderPts[2 * i], leaderPts[2 * i + 1], leaderPts[2 * i + 2], leaderPts[2 * i + 3], 2f, P.InkHi);
        }

        private void DockAnimation(float now)
        {
            bool shown = c.Cat >= 0 && !c.Wheel;
            if (shown && !dockWasShown) dockShownAt = now;
            else if (shown && (c.Cat != dockCat || c.Sub != dockSub)) swapAt = now;
            dockWasShown = shown; dockCat = shown ? c.Cat : -1; dockSub = shown ? c.Sub : null;
        }
    }
}
