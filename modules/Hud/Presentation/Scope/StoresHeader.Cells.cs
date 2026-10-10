using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The countermeasure cell and the capacitor row of the stores header.
    internal sealed partial class StoresHeader
    {
        /// <summary>
        /// The countermeasure cell: every dispenser as a dot grid with its word and count (up to three side by side), a state band under each, and the jammer's charge on a row of its own.
        /// Without a jammer the columns sit in the middle of the cell. True while a call pulses or rounds are just fired.
        /// </summary>
        private bool PaintCm(ScopeCanvas g, float x, float y, float w, float h, bool flashOn)
        {
            g.Fill(x, y, w, h, Bg, 0.5f);
            g.Frame(x, y, w, h);
            int grids = 0, jam = -1;
            for (int i = 0; i < d.CmCount; i++)
                if (d.Cm[i].Chargeable) { if (jam < 0) jam = i; }
                else if (grids < 3) grids++;
            bool beat = false;
            if (grids > 0)
            {
                float colW = (float)Math.Floor((w - 18f - (grids - 1) * 8f) / grids), top = y + 4f + (jam >= 0 ? 0f : 14f), cx = x + 9f;
                for (int i = 0, k = 0; i < d.CmCount && k < grids; i++)
                {
                    if (d.Cm[i].Chargeable) continue;
                    beat |= PaintDispenser(g, i, cx, top, colW, flashOn);
                    cx += colW + 8f;
                    k++;
                }
            }
            if (jam >= 0) PaintJammer(g, d.Cm[jam], x, y, w);
            return beat;
        }

        /// <summary>One dispenser: bracketed when selected, boxed and pulsing while the THREAT page calls for it, its word and count, its rounds as dots and its state band.</summary>
        private bool PaintDispenser(ScopeCanvas g, int s, float x, float y, float w, bool flashOn)
        {
            CmStation c = d.Cm[s];
            bool call = StoresMath.Called(c, d.Call), selected = c.Selected, firing = s < cmUntil.Length && now < cmUntil[s];
            CmCellState state = StoresMath.CmStateOf(c, call, firing);
            CmLevel level = HudCm.Level(c);
            bool none = level == CmLevel.Empty, low = level == CmLevel.Low;
            if (call) g.Box(x - 4f, y - 1f, w + 4f, 69f, Hi, flashOn ? 1f : 0.4f, 1f, Ink, 0.1f);
            else if (selected) g.Brackets(x - 4f, y - 1f, w + 4f, 69f, 5f, Hi);

            string count = ScopeStr.Int(c.Ammo), word = c.Tag == "IR" ? "IR FLR" : c.Tag == "RDR" ? "RDR CHF" : c.Tag;
            if (ScopeCanvas.TextW(word, 12f) + ScopeCanvas.TextW(count, 12f) + 8f > w) word = c.Tag == "IR" ? "FLR" : c.Tag == "RDR" ? "CHF" : c.Tag;     // a 3-digit count drops the airframe prefix
            g.Text(word, x, y + 8f, call || selected ? Hi : Dim, 12f, ScopeAlign.Left, call);
            g.Text(count, x + w - 2f, y + 8f, none ? Red : low ? Amber : Hi, 12f, ScopeAlign.Right, true);

            StoresMath.Grid(c.Max > 0 ? c.Max : c.Ammo, w - 2f, 30f, out int cols, out int rows, out float pitch, out int perDot);
            Dots(g, x + 1f, y + 19f, cols, rows, pitch, (c.Ammo + perDot - 1) / perDot, firing ? (cmFired[s] + perDot - 1) / perDot : 0, none ? Faint : low ? Amber : Ink, (c.Mask & HudCm.RadarMask) != 0 && (c.Mask & HudCm.IrMask) == 0);

            // The band: only DISPENSE, FIRING, LOW and EMPTY light it; a plain selected dispenser is just the word SEL.
            ReadOnlySpan<char> band;
            ScopeTone bt;
            bool solid = false;
            switch (state)
            {
                case CmCellState.Dispense: band = "DISPENSE"; bt = Hi; solid = flashOn; break;
                case CmCellState.Firing: band = "FIRING"; bt = Hi; break;
                case CmCellState.Empty: band = "EMPTY"; bt = Red; solid = true; break;
                case CmCellState.Low: band = "LOW"; bt = Amber; break;
                case CmCellState.Selected: band = "SEL"; bt = Ink; break;
                default: band = "READY"; bt = Dim; break;
            }
            float by = y + 50f, bh = 16f, bw = w - 2f;
            bool lit = bt != Dim && bt != Ink;
            if (solid)
            {
                g.Fill(x + 1f, by, bw, bh, bt);
                g.Text(band, x + 1f + bw / 2f, by + bh / 2f + 1f, Bg, 12f, ScopeAlign.Center, true);
            }
            else
            {
                if (lit) g.Box(x + 1f, by, bw, bh, bt, 1f, 1f, bt, 0.14f);
                g.Text(band, x + 1f + bw / 2f, by + bh / 2f + 1f, bt, 12f, ScopeAlign.Center, lit, bt == Dim ? 0.85f : 1f);
            }
            return call || firing;
        }

        /// <summary>The dispenser's rounds as dots, row by row from the top left: solid for rounds left, the next few flashing hi (just fired), hollow for the rest. Circles for flares, squares for chaff.</summary>
        private static void Dots(ScopeCanvas g, float x, float y, int cols, int rows, float pitch, int filled, int flash, ScopeTone tone, bool square)
        {
            float r = Math.Max(1f, (float)Math.Round(pitch / 3f));
            for (int i = 0; i < cols * rows; i++)
            {
                float cx = x + (i % cols) * pitch + pitch / 2f, cy = y + (i / cols) * pitch + pitch / 2f;
                bool on = i < filled, hot = !on && i < filled + flash;
                ScopeTone t = on ? tone : hot ? Hi : Faint;
                if (square)
                {
                    if (on || hot) g.Fill(Mathf.Round(cx - r), Mathf.Round(cy - r), r * 2f, r * 2f, t, hot ? 0.9f : 1f);
                    else g.Box(Mathf.Round(cx - r), Mathf.Round(cy - r), r * 2f, r * 2f, t, 0.8f);
                }
                else if (on || hot) Disc(g, cx, cy, r, t, hot ? 0.9f : 1f);
                else g.Ring(cx, cy, r - 0.5f, t, 0.8f);
            }
        }

        /// <summary>A solid dot as pixels: a square with its corners cut (a hexagon of a ring would be a star at 4 px).</summary>
        private static void Disc(ScopeCanvas g, float cx, float cy, float r, ScopeTone tone, float alpha)
        {
            float x = Mathf.Round(cx - r), y = Mathf.Round(cy - r), d = r * 2f;
            if (r < 2f) { g.Fill(x, y, d, d, tone, alpha); return; }
            g.Fill(x + 1f, y, d - 2f, 1f, tone, alpha);
            g.Fill(x, y + 1f, d, d - 2f, tone, alpha);
            g.Fill(x + 1f, y + d - 1f, d - 2f, 1f, tone, alpha);
        }

        /// <summary>The jammer row: its charge as a segmented bar and RDY / ON / CHG (the kJ per second it draws is on the capacitor row).</summary>
        private static void PaintJammer(ScopeCanvas g, in CmStation j, float x, float y, float w)
        {
            string word = SystemsMath.JamWord(j);
            bool on = j.Active, charging = word == "CHG";
            float jy = y + 94f;
            g.Line(x + 5f, y + 80f, x + w - 5f, y + 80f, Faint);
            g.Text("JAM", x + 9f, jy, on ? Hi : Dim, 12f, ScopeAlign.Left, on);
            g.Bar(x + 40f, jy - 6f, 88f, 12f, j.Charge, charging ? Dim : Ink, 8);
            g.Text(word, x + 136f, jy, on ? Hi : charging ? Dim : Ink, 12f, ScopeAlign.Left, true);
        }

        // ------------------------------------------------------------------------------------------------ the capacitor row

        /// <summary>
        /// The capacitor row: CAP, a segmented bar, the figure in kJ, the measured rate (CHG +n/S, DRN -n/S, FULL or HOLD) with chevrons while it moves; a marker rides the fill edge while a jammer
        /// is on. A consumer's shot cost is not drawn: the game does not say one (the RG-12 LANCE keeps its own bank and does not touch this capacitor). True while the chevrons move.
        /// </summary>
        private bool PaintCap(ScopeCanvas g, float x, float y, float w, float h, float clock)
        {
            float max = d.CapMaxKj > 0f ? d.CapMaxKj : Math.Max(1f, d.CapKj), frac = Math.Min(1f, Math.Max(0f, d.CapKj / max));
            bool low = StoresMath.CapLow(d.CapKj, max);
            CapWord word = StoresMath.CapWordOf(d.CapRate, frac);
            int dir = word == CapWord.Charge ? 1 : word == CapWord.Drain ? -1 : 0;
            g.Fill(x, y, w, h, Bg, 0.5f);
            g.Box(x, y, w, h, Dim, 0.75f);     // a plain outline (the lamp cells' language): only the two big cells carry corner ticks
            float cy = y + h / 2f + 1f;
            g.Text("CAP", x + 8f, cy, Dim);

            // Right to left: the figure and its unit, the rate word (a fixed width, so the bar never jitters), the chevrons; the bar takes what is left.
            float rx = x + w - 6f;
            string fig = ScopeStr.Int((int)Math.Round(d.CapKj));
            float fw = ScopeCanvas.TextW(fig, 14f), uw = ScopeCanvas.TextW(" KJ", 12f);
            g.Text(" KJ", rx, cy, Dim, 12f, ScopeAlign.Right);
            g.Text(fig, rx - uw, cy, low ? Amber : Hi, 14f, ScopeAlign.Right, true);
            rx -= fw + uw + 10f + 80f;
            ReadOnlySpan<char> words = word == CapWord.Drain ? ScopeStr.Cat("DRN -", (int)Math.Round(-d.CapRate), "/S") : word == CapWord.Charge ? ScopeStr.Cat("CHG +", (int)Math.Round(d.CapRate), "/S") : (ReadOnlySpan<char>)(word == CapWord.Full ? "FULL" : "HOLD");
            g.Text(words, rx, cy, dir < 0 ? Hi : Dim, 12f, ScopeAlign.Left, dir < 0);
            rx -= 4f;
            if (dir != 0)
            {
                int lit = (int)(clock * 6f) % 3;
                for (int i = 0; i < 3; i++)
                {
                    float px = rx - 24f + i * 8f;
                    bool on = (dir > 0 ? i : 2 - i) == lit;
                    ScopeTone t = on ? Hi : Ink;
                    float a = on ? 1f : 0.5f, tip = dir > 0 ? px + 5f : px, back = dir > 0 ? px : px + 5f;
                    g.Line(back, cy - 5f, tip, cy, t, a, on ? 2f : 1f);
                    g.Line(tip, cy, back, cy + 5f, t, a, on ? 2f : 1f);
                }
            }
            rx -= 28f;

            // The bar in whole segments: crisp, no shimmer.
            const float bx0 = 36f, sw = 7f, gp = 1f;
            float bx = x + bx0, by = y + 6f, bh = h - 12f;
            int n = Math.Max(1, (int)((rx - bx - 3f) / (sw + gp))), lit2 = (int)Math.Round(frac * n);
            float bw = n * (sw + gp) + 3f;
            g.Box(bx, by, bw, bh, Dim, 0.85f);
            for (int i = 0; i < n; i++)
                g.Fill(bx + 2f + i * (sw + gp), by + 2f, sw, bh - 4f, i < lit2 ? (low ? Amber : Ink) : Faint, i < lit2 ? 1f : 0.7f);
            bool jamOn = false;
            for (int i = 0; i < d.CmCount; i++) jamOn |= d.Cm[i].Chargeable && d.Cm[i].Active;
            if (jamOn) g.Tri(Mathf.Round(bx + 2f + (bw - 4f) * frac), by - 1f + (int)(clock * 4f) % 2, 180f, 5f, Hi);
            return dir != 0 || jamOn;
        }
    }
}
