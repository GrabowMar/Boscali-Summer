using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // PWR BUS (LAYOUT.md 3.2): the capacitor. The charge as a numeral in kJ, its rate word, a row of cells (the filled share is the charge) and, while an energy weapon is fitted, the cost of one shot as a tick on the row
    // and a label under it.
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

            // The cost of one shot of the fitted energy weapon: a tick across the row where it falls (BRIGHT when the charge covers it or the weapon is selected) and the word under it.
            float cost = StoresMath.CostKj(d);
            if (cost > 0f)
            {
                bool selectedRail = StoresMath.HasHero(d) && d.Stations[d.Selected].Class == StoreClass.Rail, bright = selectedRail || d.CapKj >= cost;
                float tx = x + StoresMath.CostTickX(cells, cost, max);
                g.Line(tx, y + StoresLayout.TickTop, tx, y + StoresLayout.TickBottom, bright ? Hi : Ink);
                g.Text(ScopeStr.Cat(CostWord(), " ", (int)Math.Round(cost), null), x + iw, y + StoresLayout.CostY, bright ? Hi : Ink, 12f, ScopeAlign.Right, bright);
            }

            // The jammer's marker rides the fill's edge while it draws power.
            bool jamOn = false;
            for (int i = 0; i < d.CmCount; i++) jamOn |= d.Cm[i].Chargeable && d.Cm[i].Active;
            if (jamOn) g.Tri(x + Math.Max(0f, lit * StoresMath.CellPitch - (StoresMath.CellPitch - StoresMath.CellW)), y + StoresLayout.JamMarkY + (int)(now * 4f) % 2, 0f, 3f, Hi);
            return jamOn || shot;
        }

        /// <summary>The first word of the fitted energy weapon's name (the first rail station's), made once per weapon.</summary>
        private string CostWord()
        {
            string name = null;
            for (int i = 0; i < d.StationCount && name == null; i++)
                if (d.Stations[i].Class == StoreClass.Rail) name = d.Stations[i].Name;
            if (!ReferenceEquals(costFor, name) || costWord == null)
            {
                costFor = name;
                costWord = StoresMath.CostWord(name);
            }
            return costWord;
        }
    }
}
