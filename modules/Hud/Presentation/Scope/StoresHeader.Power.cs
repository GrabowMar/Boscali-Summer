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

            // The charge as a row of cells (2026-11-19, user: "make capacitor bar better"; the old 3 px line read as a rule): lit cells INK (amber low), the top lit cell BRIGHT while the jammer draws, empty cells a FAINT track,
            // the cells a shot just emptied BRIGHT and fading. The cell count is fixed, so a cell is always the same share of the capacitor.
            float bx = Math.Max(x + StoresLayout.CapBarX, unitX + ScopeCanvas.TextW("kJ") + 8f), bw = x + r.W - bx, by = Mathf.Round(cy) - StoresLayout.CapBarH / 2f;
            const int cells = StoresLayout.CapCells;
            float pitch = (bw + StoresLayout.CapCellGap) / cells, cw = Mathf.Max(1f, Mathf.Floor(pitch - StoresLayout.CapCellGap));
            int lit = (int)Math.Ceiling(frac * cells - 0.001f), spentTo = shot ? Math.Min(cells, (int)Math.Ceiling(shotFrom * cells - 0.001f)) : 0;
            float fade = shot ? StoresLayout.SpentCellAlpha * (1f - shotAge / StoresMath.ShotWordS) : 0f;
            for (int i = 0; i < cells; i++)
            {
                float cx = Mathf.Round(bx + i * pitch);
                if (i < lit) g.Fill(cx, by, cw, StoresLayout.CapBarH, low ? Amber : jamming && i == lit - 1 ? Hi : Ink);
                else if (i < spentTo) g.Fill(cx, by, cw, StoresLayout.CapBarH, Hi, fade);
                else g.Fill(cx, by, cw, StoresLayout.CapBarH, Faint);
            }
            if (jamming && lit > 0) g.Tri(Mathf.Round(bx + (lit - 1) * pitch) + cw / 2f, cy + StoresLayout.JamMarkDy + (int)(now * 4f) % 2, 0f, 3f, Hi);
            return jamming || shot;
        }
    }
}
