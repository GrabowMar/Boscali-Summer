using System;
using BoscaliSummer.Modules.Hud.Domain;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // PWR BUS (LAYOUT.md 3.2): the capacitor. The charge as a numeral in kJ, its rate word and a row of cells (the filled share is the charge).
    internal sealed partial class StoresHeader
    {
        /// <summary>The PWR BUS block in <paramref name="r"/>; true while a shot's spent cells or the jammer's marker move with the beat.</summary>
        private bool PaintPwr(ScopeCanvas g, in BoardRect r)
        {
            float x = r.X, y = r.Y, w = r.W, iw = StoresLayout.PwrInner(stack, w);
            g.CaptionShelf(x, y, StoresLayout.PwrShelf(stack, w), "PWR BUS");
            float max = d.CapMaxKj > 0f ? d.CapMaxKj : Math.Max(1f, d.CapKj), frac = Math.Min(1f, Math.Max(0f, d.CapKj / max));
            bool low = StoresMath.CapLow(d.CapKj, max);
            float shotAge = now - shotAt;
            bool shot = shotAge >= 0f && shotAge < StoresMath.ShotWordS;

            // The charge and its rate: right after a shot SHOT -n (bold, BRIGHT), else DRN -n/S (bold, BRIGHT), CHG +n/S, FULL or HOLD (INK).
            string figure = ScopeStr.Int((int)Math.Round(d.CapKj));
            g.Text(figure, x, y + StoresLayout.PwrNumeralY, low ? Amber : Hi, StoresLayout.PwrNumeralSize, ScopeAlign.Left, true);
            g.Text("KJ", x + ScopeCanvas.TextW(figure, StoresLayout.PwrNumeralSize) + StoresLayout.PwrUnitGap, y + StoresLayout.PwrUnitY, Dim);
            CapWord word = StoresMath.CapWordOf(d.CapRate, frac);
            ReadOnlySpan<char> rate = shot ? ScopeStr.Cat("SHOT -", (int)Math.Round(shotKj), null)
                : word == CapWord.Drain ? ScopeStr.Cat("DRN -", (int)Math.Round(-d.CapRate), "/S")
                : word == CapWord.Charge ? ScopeStr.Cat("CHG +", (int)Math.Round(d.CapRate), "/S")
                : (ReadOnlySpan<char>)(word == CapWord.Full ? "FULL" : "HOLD");
            bool drain = shot || word == CapWord.Drain;
            g.Text(rate, x + iw, y + StoresLayout.PwrRateY, drain ? Hi : Ink, 12f, ScopeAlign.Right, drain);

            // The cells: the lit ones INK (amber when low), the cells a shot just emptied BRIGHT fading away, the rest a 1 x 10 DIM tick.
            int cells = StoresMath.Cells(iw), lit = StoresMath.LitCells(frac, cells), spent = shot ? Math.Max(0, StoresMath.LitCells(shotFrom, cells) - lit) : 0;
            float top = y + StoresLayout.CellsTop, fade = StoresLayout.SpentCellAlpha * (1f - shotAge / StoresMath.ShotWordS);
            for (int i = 0; i < cells; i++)
            {
                float cx = x + i * StoresMath.CellPitch;
                if (i < lit) g.Fill(cx, top, StoresMath.CellW, StoresMath.CellH, low ? Amber : Ink);
                else if (i < lit + spent) g.Fill(cx, top, StoresMath.CellW, StoresMath.CellH, Hi, fade);
                else g.Fill(cx + StoresMath.UnlitTickX, top, 1f, StoresMath.CellH, Dim);
            }

            // The jammer's marker rides the fill's edge while it draws power.
            bool jamOn = false;
            for (int i = 0; i < d.CmCount; i++) jamOn |= d.Cm[i].Chargeable && d.Cm[i].Active;
            if (jamOn)
            {
                bool room = stack;
                g.Tri(x + StoresLayout.JamMarkX(Math.Max(0f, lit * StoresMath.CellPitch - (StoresMath.CellPitch - StoresMath.CellW)), room, w), y + StoresLayout.JamMark(room) + (int)(now * 4f) % 2, 0f, 3f, Hi);
            }
            return jamOn || shot;
        }
    }
}
