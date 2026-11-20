using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // CM cell (ONE BAR v3 3.2): one tile per countermeasure in the order the game steps through them, up to three. A tile, stacked and centred: the glyph large on top (the jammer's game sprite), the EMPHASIS numeral under it,
    // the short name on R3 (the rounds just dispensed in its place for 0.45 s). The selected tile has the Hi rule segment (the WPN rail's idiom); a called tile an outlined pulsing box. Empty is a red 0 over a Faint glyph. The capacitor is SYS's (StoresHeader.Power).
    internal sealed partial class StoresHeader
    {
        /// <summary>The CM cell in <paramref name="r"/>. True while a call pulses, rounds are just fired or the jammer breathes.</summary>
        private bool PaintExp(ScopeCanvas g, in BoardRect r, bool flashOn)
        {
            bool beat = false;
            int n = Math.Min(d.CmCount, StoresLayout.TileMax);
            if (n <= 0) return false;
            float tw = Mathf.Floor(r.W / n);
            for (int i = 0; i < n; i++) beat |= PaintTile(g, i, r.X + i * tw, r.Y, tw, flashOn);
            return beat;
        }

        /// <summary>One countermeasure's tile at (<paramref name="tx"/>, <paramref name="y"/>), <paramref name="tw"/> wide.</summary>
        private bool PaintTile(ScopeCanvas g, int i, float tx, float y, float tw, bool flashOn)
        {
            CmStation c = d.Cm[i];
            bool selected = c.Selected, jammer = c.Chargeable;
            bool call = StoresMath.Called(c, d.Call), firing = !jammer && i < cmUntil.Length && now < cmUntil[i];
            bool empty = !jammer && c.Ammo <= 0, hot = selected || call;
            float mid = tx + (tw - StoresLayout.TileSelInset) / 2f, cy = y + StoresLayout.TileIconY, ny = y + StoresLayout.TileNumY, gx = mid;
            if (call) g.Box(tx - StoresLayout.TileCallPad, y + StoresLayout.TileCallTop, tw - 3f * StoresLayout.TileCallPad, StoresLayout.TileCallH, Hi, flashOn ? 1f : 0.4f);      // stops 2 short of the tile: never over the neighbour's words
            if (selected) g.Line(tx, y + ScopeLayout.RuleY, tx + tw - StoresLayout.TileSelInset, y + ScopeLayout.RuleY, Hi, 1f, StoresLayout.SelRuleW);

            ScopeTone glyph = empty ? Faint : hot ? Hi : Dim;
            float gs = tw < 60f ? StoresLayout.TileGlyphTight : StoresLayout.TileGlyphScale, nw = tw - StoresLayout.TileSelInset - 2f;
            bool breathing = false;
            if (jammer)
            {
                JamGlyph(g, gx, cy, glyph, gs);      // drawn like the flare and chaff (the game's sprite sat smaller and heavier than its neighbours)
                breathing = c.Active;
                bool full = c.Charge >= 0.98f;      // the charge in percent (2026-11-19, user): BRIGHT breathing while it jams, INK when full, DIM while charging
                ScopeTone tone = breathing ? Hi : full ? Ink : Dim;
                g.FitText(ScopeStr.Cat("", (int)Math.Round(Math.Max(0f, Math.Min(1f, c.Charge)) * 100f), "%"), mid, ny, nw, tone, HudType.Emphasis, HudType.Label, ScopeAlign.Center, true, breathing ? 0.85f + 0.15f * ScopeCanvas.Pulse(now, 1.2f) : 1f);
            }
            else
            {
                if (c.Tag == "IR") FlareGlyph(g, gx, cy, glyph, gs);
                else if (c.Tag == "RDR") ChaffGlyph(g, gx, cy, glyph, gs);
                ScopeTone tone = firing ? Hi : StoresMath.CountTone(HudCm.Level(c), selected);
                g.FitText(ScopeStr.Int(c.Ammo), mid, ny, nw, tone, HudType.Emphasis, HudType.Label, ScopeAlign.Center, true);
            }

            string name = jammer ? (c.Active ? "ON" : "JAM") : c.Tag == "IR" ? "FLR" : c.Tag == "RDR" ? "CHF" : HudCm.Word(c);
            if (firing) g.Text(ScopeStr.Cat("-", cmFired[i]), mid, y + ScopeLayout.R3, Hi, HudType.Label, ScopeAlign.Center, true);
            else g.Text(name, mid, y + ScopeLayout.R3, hot || breathing ? Hi : Dim, HudType.Label, ScopeAlign.Center, hot || breathing);
            return call || firing || breathing;
        }

        // The three tile icons (2026-11-19, user: "remake icons to fit there better"): drawn on one 12 x 12 box round (cx, cy) at scale s, symmetric, the same 1 unit stroke, each filling the box so they read as one set.

        /// <summary>The jammer: an emitter with its waves going out both ways (the radar fan of the SYS lamp, doubled).</summary>
        internal static void JamGlyph(ScopeCanvas g, float cx, float cy, ScopeTone tone, float s = 1f)
        {
            g.Line(cx, cy - 1f * s, cx, cy + 6f * s, tone);      // the mast
            g.Line(cx - 2f * s, cy + 6f * s, cx + 2f * s, cy + 6f * s, tone);
            g.Ring(cx, cy - 2f * s, 1.2f * s, tone, 1f, 1f, tone, 1f);      // the emitter
            g.Arc(cx, cy - 2f * s, 3.5f * s, 55f, 125f, tone); g.Arc(cx, cy - 2f * s, 3.5f * s, 235f, 305f, tone);
            g.Arc(cx, cy - 2f * s, 6f * s, 60f, 120f, tone); g.Arc(cx, cy - 2f * s, 6f * s, 240f, 300f, tone);
        }

        /// <summary>A flare: the burning pellet low in the box, three smoke trails fanning up from it.</summary>
        internal static void FlareGlyph(ScopeCanvas g, float cx, float cy, ScopeTone tone, float s = 1f)
        {
            g.Ring(cx, cy + 3f * s, 2f * s, tone, 1f, 1f, tone, 1f);
            g.Line(cx, cy + 1f * s, cx, cy - 6f * s, tone);
            g.Line(cx - 1.2f * s, cy + 1.5f * s, cx - 3.5f * s, cy - 1f * s, tone); g.Line(cx - 3.5f * s, cy - 1f * s, cx - 5f * s, cy - 5f * s, tone);
            g.Line(cx + 1.2f * s, cy + 1.5f * s, cx + 3.5f * s, cy - 1f * s, tone); g.Line(cx + 3.5f * s, cy - 1f * s, cx + 5f * s, cy - 5f * s, tone);
        }

        /// <summary>Chaff: a round cloud of foil strips, all the same length, turned every way.</summary>
        internal static void ChaffGlyph(ScopeCanvas g, float cx, float cy, ScopeTone tone, float s = 1f)
        {
            for (int k = 0; k < ChaffStrips.Length; k += 3)
            {
                float px = cx + ChaffStrips[k] * s, py = cy + ChaffStrips[k + 1] * s;
                ScopeCanvas.Polar(ChaffStrips[k + 2], 1.4f * s, out float dx, out float dy);
                g.Line(px - dx, py - dy, px + dx, py + dy, tone);
            }
        }

        /// <summary>The chaff strips: x, y (in the 12 box) and bearing of each.</summary>
        private static readonly float[] ChaffStrips =
        {
            0f, -4.6f, 70f,   -3.8f, -2.6f, 20f,   3.8f, -2.6f, 150f,   -1.4f, -0.6f, 120f,   1.6f, 0.2f, 40f,
            -4.6f, 1.6f, 100f,   4.6f, 1.8f, 80f,   -2.2f, 4.2f, 160f,   2.4f, 4.4f, 15f,
        };
    }
}
