using System;
using BoscaliSummer.Modules.Autopilot.Domain;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Autopilot.Presentation
{
    /// <summary>
    /// Paints the controller's state: every plate, outline, disc, glyph and line into ONE vector surface (a single
    /// <see cref="AvQuadBuffer"/>, so every colour renders the same brightness; lit elements and plates share it), and every word through
    /// the <see cref="MenuTextPool"/> above it. All numbers are the prototype's (style.css) times the layout scales. Origin top-left, y down;
    /// the primitives flip to the canvas. Called every frame while the menu or its toast shows; allocation-free once warm.
    /// </summary>
    internal sealed partial class MenuPainter
    {
        private const float OpenSec = 0.12f, DockSec = 0.14f, SwapSec = 0.12f;

        private readonly AvQuadBuffer q;
        private readonly MenuTextPool text;
        private readonly float[] leaderPts = new float[MenuLeader.MaxPoints * 2];
        private MenuController c;
        private MenuLayout l;
        private MenuPalette P, D;
        private float H, T;

        // dock animation: slide in when the dock first shows, slide the list when its content swaps
        private string dockKey;
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
                Vignette();
                DockAnimation(now);
                if (c.Cat >= 0) PaintLeaderAndDock(now); else dockWasShown = false;
                PaintRing();
                PaintHub();
                PaintTitle();
                PaintHint();
                if (c.Cat >= 0) PaintDock(now);
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

        /// <summary>A glyph from the generated table, scaled to <paramref name="box"/> pixels square around (cx, cy).</summary>
        private void Glyph(string id, float cx, float cy, float box, float stroke, Rgba col)
        {
            if (id == null || !MenuIcons.Table.TryGetValue(id, out IconPoly[] polys)) return;
            float k = box / 24f, x0 = cx - box / 2f, y0 = cy - box / 2f, w = stroke * k;
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
                for (int i = 0; i + 1 < n; i++) Line(x0 + v[2 * i] * k, y0 + v[2 * i + 1] * k, x0 + v[2 * i + 2] * k, y0 + v[2 * i + 3] * k, w, col);
                if (p.Closed) Line(x0 + v[2 * (n - 1)] * k, y0 + v[2 * (n - 1) + 1] * k, x0 + v[0] * k, y0 + v[1] * k, w, col);
            }
        }

        private static Color Col(Rgba c) => MenuPalette.ToColor(c);

        // ---------------------------------------------------------------- vignette

        /// <summary>One soft field behind the ring: a polar mesh whose alpha falls from .66 at the middle to 0 at the rim (never a stack of bands).
        /// It stops short of the heading tape.</summary>
        private void Vignette()
        {
            const int Segs = 32, Rings = 8;
            float rx = 520f * l.Rho, ry = 390f * l.Rho, cx = l.Cx, cy = l.Cy + 30f * l.Rho, top = l.Cy - 235f * l.Rho;
            for (int ring = 0; ring < Rings; ring++)
                for (int s = 0; s < Segs; s++)
                {
                    float a0 = s * 2f * Mathf.PI / Segs, a1 = (s + 1) * 2f * Mathf.PI / Segs;
                    float r0 = ring / (float)Rings, r1 = (ring + 1) / (float)Rings;
                    float alpha0 = 0.66f * (1f - Mathf.Clamp01((r0 - 0.55f) / 0.45f)), alpha1 = 0.66f * (1f - Mathf.Clamp01((r1 - 0.55f) / 0.45f));
                    Rgba c0 = new Rgba(0f, 0.04f, 0.02f, alpha0), c1 = new Rgba(0f, 0.04f, 0.02f, alpha1);
                    float ax = cx + rx * r0 * Mathf.Cos(a0), ay = Mathf.Max(top, cy + ry * r0 * Mathf.Sin(a0));
                    float bx = cx + rx * r0 * Mathf.Cos(a1), by = Mathf.Max(top, cy + ry * r0 * Mathf.Sin(a1));
                    float cx1 = cx + rx * r1 * Mathf.Cos(a1), cy1 = Mathf.Max(top, cy + ry * r1 * Mathf.Sin(a1));
                    float dx = cx + rx * r1 * Mathf.Cos(a0), dy = Mathf.Max(top, cy + ry * r1 * Mathf.Sin(a0));
                    q.Add(ax, H - ay, bx, H - by, cx1, H - cy1, dx, H - dy, c0, c0, c1, c1);
                }
        }

        // ---------------------------------------------------------------- ring

        private void PaintRing()
        {
            bool dockUp = c.Cat >= 0;
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
                Glyph(cat.Icon, x, y, box, 1.6f, sel ? P.InkHi : off ? p.Ink2 : p.Ink);
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
                Rgba kcInk = sel ? P.InkHi : p.Ink2;
                if (opacity >= 1f) Fill(kc, p.PlateHi); // a faded cap keeps only its outline and digit: a plate fill would be a dark square over the cockpit
                Frame(kc, 1f, P.Fade(sel ? P.InkHi : p.Line.WithAlpha(1f), opacity));
                text.Draw(cat.Key, MenuFont.Badge, 13f * T, kc, MenuTextPool.Align.Center, Col(P.Fade(kcInk, opacity)));
            }
        }

        // ---------------------------------------------------------------- hub + readout

        private void PaintHub()
        {
            Disc(l.Cx, l.Cy, l.HubR, P.Hub);
            Ring(l.Cx, l.Cy, l.HubR, 2f, P.Ink);
            Glyph("aircraft", l.Cx, l.Cy, l.HubR * 2f * 0.58f, 1.5f, P.InkHi);

            MenuRect room = l.Readout;
            float y0 = room.T;
            if (!l.ReadoutCompact)
            {
                MenuRect name = MenuRect.Of(room.L, y0, room.W, l.ReadoutNameH);
                Fill(name, P.Plate); Frame(name, 1f, P.Line);
                text.Draw(c.Model.AircraftName, MenuFont.Badge, 13f * T, name, MenuTextPool.Align.Center, Col(P.Txt));
                y0 += l.ReadoutNameH + 3f * T;
            }
            if (!roFocus) { PaintSummary(y0); return; }
            float pad = (l.ReadoutCompact ? 4f : 6f) + 1f;
            MenuRect act = MenuRect.Of(room.L, y0, room.W, Math.Min(roFit.Height, room.B - y0));
            Fill(act, P.PlateHi); Frame(act, 1f, P.InkHi);
            float y = act.T + (l.ReadoutCompact ? 3f : 4f) * T + 1f;
            float inner = act.W - 2f * pad;
            if (roFit.TitleLines == 1)
            {
                float th = roFit.TitleSize * MenuFonts.LinePitch;
                text.Draw(roTitle, MenuFont.Row, roFit.TitleSize, MenuRect.Of(act.L + pad, y, inner, th), MenuTextPool.Align.Center, Col(P.InkHi));
                y += th;
            }
            else
            {
                var lines = MenuReadoutFit.Wrap(roTitle, inner, text, MenuFont.Plate, T);
                float th = roFit.TitleSize * MenuFonts.LinePitch * lines.Count;
                text.Draw(string.Join("\n", lines), MenuFont.Plate, roFit.TitleSize, MenuRect.Of(act.L + pad, y, inner, th), MenuTextPool.Align.Center, Col(P.InkHi), top: true);
                y += th;
            }
            if (roFit.DescLines > 0)
            {
                var lines = MenuReadoutFit.Wrap(roDesc, inner, text, MenuFont.State, T);
                text.Draw(string.Join("\n", lines), MenuFont.State, 13f * T, MenuRect.Of(act.L + pad, y + T, inner, 13f * T * MenuFonts.LinePitch * Math.Max(1, lines.Count)),
                    MenuTextPool.Align.Center, Col(P.Ink2), top: true);
            }
        }

        // the hub action plate is sized before anything is drawn: the leader of a lower-ring category hangs from its bottom edge
        private string roTitle, roDesc;
        private bool roFocus;
        private ReadoutFit roFit;

        private void PrepareReadout()
        {
            MenuReadout? ro = MenuHint.Readout(c);
            roFocus = ro.HasValue;
            roTitle = roFocus ? ro.Value.Title : null; roDesc = roFocus ? ro.Value.Desc : null;
            float y0 = l.Readout.T + (l.ReadoutCompact ? 0f : l.ReadoutNameH + 3f * T);
            if (roFocus)
            {
                roFit = MenuReadoutFit.Fit(l, text, roTitle, roDesc);
                readoutBottom = y0 + Math.Min(roFit.Height, l.Readout.B - y0);
            }
            else readoutBottom = y0 + Math.Min(Math.Max(l.ReadoutActH, 13f * T * MenuFonts.LinePitch * 2f + 10f * T), l.Readout.B - y0);
        }

        private float readoutBottom;

        /// <summary>Nothing focused: FUEL 72% / GEAR UP / 2 TGT / WING 3, flowed and wrapped by measured width.</summary>
        private void PaintSummary(float y0)
        {
            MenuRect room = l.Readout;
            float pad = (l.ReadoutCompact ? 4f : 6f) + 1f;
            var cells = MenuReadoutFit.Flow(c.Model.Summary, room.W - 2f * pad, text, T, out int lines);
            float rowH = MenuReadoutFit.SummaryLine(T), gap = MenuReadoutFit.SummaryGap * T;
            float gridH = lines * rowH + Math.Max(0, lines - 1) * gap;
            float h = Math.Max(l.ReadoutActH, gridH + 12f * T);
            MenuRect act = MenuRect.Of(room.L, y0, room.W, Math.Min(h, room.B - y0));
            Fill(act, P.PlateHi); Frame(act, 1f, P.InkHi);
            float top = act.T + (act.H - gridH) / 2f;
            foreach (SummaryCell cell in cells)
            {
                float x = act.L + pad + cell.X, y = top + cell.Line * (rowH + gap), sp = text.Width(" ", MenuFont.Badge) * T;
                text.Draw(cell.Key, MenuFont.Badge, 13f * T, MenuRect.Of(x, y, cell.KeyW + 2f, rowH), MenuTextPool.Align.Left, Col(P.Ink2));
                float vx = x + cell.KeyW + sp;
                text.Draw(cell.Value, MenuFont.Badge, 13f * T, MenuRect.Of(vx, y, text.Width(cell.Value, MenuFont.Badge) * T + 2f, rowH), MenuTextPool.Align.Left, Col(cell.Caution ? P.Amber : P.Txt));
            }
        }

        // ---------------------------------------------------------------- title, hint, toast, pointer

        private void PaintTitle()
        {
            Fill(l.Title, P.Plate); Frame(l.Title, 1f, P.InkHi);
            text.Draw("SELF INTERACTION", MenuFont.Title, 15f * T, l.Title, MenuTextPool.Align.Center, Col(P.InkHi));
        }

        private void PaintHint()
        {
            MenuHintParts h = MenuHint.Parts(c);
            float shift = c.Cat >= 0 ? l.HintShift(c.Dock) : 0f;
            MenuRect r = MenuRect.Of(l.Hint.L + shift, l.Hint.T, l.Hint.W, l.Hint.H);
            Fill(r, P.Plate); Frame(r, 1f, P.InkHi);
            float a = 124f * T, b = 396f * T;
            Fill(r.L + a, r.T + 1f, 1f, r.H - 2f, P.InkHi); Fill(r.L + a + b, r.T + 1f, 1f, r.H - 2f, P.InkHi);
            float pad = 8f * T;
            text.Draw(h.Mode, MenuFont.Hint, 15f * T, new MenuRect(r.L + pad, r.T, r.L + a - pad, r.B), MenuTextPool.Align.Center, Col(P.InkHi));
            text.Draw(h.Live, MenuFont.Hint, 15f * T, new MenuRect(r.L + a + pad, r.T, r.L + a + b - pad, r.B), MenuTextPool.Align.Center, Col(h.Warn ? P.Amber : P.Txt));
            text.Draw(h.Back, MenuFont.Hint, 15f * T, new MenuRect(r.L + a + b + pad, r.T, r.R - pad, r.B), MenuTextPool.Align.Center, Col(P.InkHi));
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
            Glyph(t.Icon, r.L + 12f * T + icon / 2f, r.T + h / 2f, icon, 1.8f, P.InkHi);
            float x = r.L + 12f * T + icon + gap;
            text.Draw(t.Text, MenuFont.Row, 17f * T, MenuRect.Of(x, r.T, tw + 2f, h), MenuTextPool.Align.Left, Col(P.InkHi));
            if (cw > 0f)
            {
                x += tw + gap;
                Fill(x, r.T + 9f * T, 1f, h - 18f * T, P.Line);
                text.Draw(t.Context, MenuFont.Badge, 13f * T, MenuRect.Of(x + 1f + gap, r.T, cw + 2f, h), MenuTextPool.Align.Left, Col(P.Ink2));
            }
        }

        /// <summary>HOLD mode and pad: a small reticle where the virtual pointer is and a dashed line from the hub (inside a dock only the reticle remains).</summary>
        private void PaintPointer()
        {
            if (!(c.Mode == MenuMode.Hold || c.Device == MenuDevice.Pad)) return;
            float px = c.PtrX, py = c.PtrY, dx = px - l.Cx, dy = py - l.Cy, r = (float)Math.Sqrt(dx * dx + dy * dy);
            if (r > l.HubR + 4f && !c.DockFocus)
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
            int n = MenuLeader.Compute(l, c.Cat, cat, c.Dock, readoutBottom > 0f ? readoutBottom : l.Readout.B, leaderPts);
            for (int i = 0; i + 1 < n; i++) Line(leaderPts[2 * i], leaderPts[2 * i + 1], leaderPts[2 * i + 2], leaderPts[2 * i + 3], 5f, P.Casing);
            for (int i = 0; i + 1 < n; i++) Line(leaderPts[2 * i], leaderPts[2 * i + 1], leaderPts[2 * i + 2], leaderPts[2 * i + 3], 2f, P.InkHi);
        }

        private void DockAnimation(float now)
        {
            bool shown = c.Cat >= 0;
            string key = shown ? c.Cat + "|" + (c.Sub != null ? c.Sub.Id : "") : "";
            if (shown && !dockWasShown) dockShownAt = now;
            else if (shown && key != dockKey) swapAt = now;
            dockWasShown = shown; dockKey = key;
        }
    }
}
