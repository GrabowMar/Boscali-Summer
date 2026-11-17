using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // SYS cell R1 (bar v3 3.3): the capacitor. The jammer's glyph, the charge in kJ and the charge as a thin bar, the share a shot just emptied BRIGHT and fading, the jammer's marker riding the fill's edge.
    internal sealed partial class StoresHeader
    {
        /// <summary>
        /// The capacitor row on R1 of the SYS cell <paramref name="r"/>: the jammer's glyph (BRIGHT while it draws, else DIM), the charge in kJ (VALUE bold; amber when low, BRIGHT breathing while the jammer draws, else INK: Hi is a state, not a default), its unit,
        /// then the bar to the cell's end. True while the jammer breathes or a shot's spent share fades.
        /// </summary>
        private bool PaintPower(ScopeCanvas g, in BoardRect r)
        {
            float x = r.X, cy = r.Y + ScopeLayout.R1;
            bool jamming = false;
            for (int i = 0; i < d.CmCount; i++) jamming |= d.Cm[i].Chargeable && d.Cm[i].Active;
            float max = d.CapMaxKj > 0f ? d.CapMaxKj : Math.Max(1f, d.CapKj), frac = Math.Min(1f, Math.Max(0f, d.CapKj / max));
            bool low = StoresMath.CapLow(d.CapKj, max);
            float shotAge = now - shotAt;
            bool shot = shotAge >= 0f && shotAge < StoresMath.ShotWordS;

            JamGlyph(g, x + StoresLayout.CapGlyphX, cy, jamming ? Hi : Dim, 1f);
            string figure = ScopeStr.Int((int)Math.Round(d.CapKj));
            g.Text(figure, x + StoresLayout.CapNumX, cy, low ? Amber : jamming ? Hi : Ink, HudType.Value, ScopeAlign.Left, true, jamming ? 0.85f + 0.15f * ScopeCanvas.Pulse(now, 1.2f) : 1f);
            float unitX = x + StoresLayout.CapNumX + ScopeCanvas.TextW(figure, HudType.Value) + StoresLayout.UnitGap;
            g.Text("kJ", unitX, cy, Dim, HudType.Label);

            float bx = Math.Max(x + StoresLayout.CapBarX, unitX + ScopeCanvas.TextW("kJ") + 6f), bw = x + r.W - bx, by = Mathf.Round(cy) - 1f, litW = Mathf.Round(bw * frac);
            g.Bar(bx, by, bw, StoresLayout.CapBarH, frac, low ? Amber : Ink);
            float spentW = shot ? Math.Min(bw - litW, Mathf.Round(bw * Math.Max(0f, shotFrom - frac))) : 0f;
            if (spentW > 0f) g.Fill(bx + litW, by, spentW, StoresLayout.CapBarH, Hi, StoresLayout.SpentCellAlpha * (1f - shotAge / StoresMath.ShotWordS));
            if (jamming) g.Tri(bx + Math.Min(litW, bw - 3f), cy + StoresLayout.JamMarkDy + (int)(now * 4f) % 2, 0f, 3f, Hi);
            return jamming || shot;
        }
    }
}
