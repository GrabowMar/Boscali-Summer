using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // CM cell (ONE BAR v3 3.2): one tile per countermeasure in the order the game steps through them, up to three. A tile: the glyph (x1.5; the jammer's game sprite) and an EMPHASIS numeral on R1, the short name on R2,
    // the rounds just dispensed on R3 for 0.45 s. The selected tile has the Hi rule segment (the WPN rail's idiom); a called tile an outlined pulsing box. Empty is a red 0 over a Faint glyph. The capacitor is SYS's (StoresHeader.Power).
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
            float cy = y + ScopeLayout.R1, gx = tx + StoresLayout.TileGlyphX;
            if (call) g.Box(tx - StoresLayout.TileCallPad, y + StoresLayout.TileCallTop, tw - 3f * StoresLayout.TileCallPad, StoresLayout.TileCallH, Hi, flashOn ? 1f : 0.4f);      // stops 2 short of the tile: never over the neighbour's words
            if (selected) g.Line(tx, y + ScopeLayout.RuleY, tx + tw - StoresLayout.TileSelInset, y + ScopeLayout.RuleY, Hi, 1f, StoresLayout.SelRuleW);

            ScopeTone glyph = empty ? Faint : hot ? Hi : Dim;
            bool tight = tw < 60f;      // three tiles (48 wide): the word may reach the tile's end, so RDY / CHG and a 3 digit count fit at LABEL instead of cutting to "RD…"
            float gs = tight ? 1f : StoresLayout.TileGlyphScale;      // x1.5 ran into the numeral at 48 wide
            float nx = tx + (tight ? StoresLayout.TileNumX - 2f : StoresLayout.TileNumX), nw = tx + tw - nx - (tight ? 4f : 8f);      // 8 clear of the next tile's glyph: at 2 the numeral touched it in game
            bool breathing = false;
            if (jammer)
            {
                float box = StoresLayout.TileJamIcon * gs / StoresLayout.TileGlyphScale;
                ArtKind kind = StoreIcons.Pick(c.Icon, out ArtBounds ink, out float width, out float height);
                ArtPlace place = kind != ArtKind.None ? StoreArt.Fit(ink, width, height, gx - box / 2f, cy - box / 2f, box, box) : default;
                if (place.W > 0f) Art[JamArt] = new ArtSlot(c.Icon, place, glyph, 1f, kind == ArtKind.Mask);
                else JamGlyph(g, gx, cy, glyph, gs);
                breathing = c.Active;
                string word = SystemsMath.JamWord(c);
                ScopeTone tone = breathing ? Hi : word == "RDY" ? Ink : Dim;
                g.FitText(word, nx, cy + (HudType.Emphasis - HudType.Value) * 0.35f * ScopeCanvas.TextBoost, nw, tone, HudType.Value, HudType.Label, ScopeAlign.Left, true, breathing ? 0.85f + 0.15f * ScopeCanvas.Pulse(now, 1.2f) : 1f);
            }
            else
            {
                if (c.Tag == "IR") FlareGlyph(g, gx, cy, glyph, gs);
                else if (c.Tag == "RDR") ChaffGlyph(g, gx, cy, glyph, gs);
                ScopeTone tone = firing ? Hi : StoresMath.CountTone(HudCm.Level(c), selected);
                g.FitText(ScopeStr.Int(c.Ammo), nx, cy, nw, tone, HudType.Emphasis, HudType.Label, ScopeAlign.Left, true);
            }

            string name = jammer ? "JAM" : c.Tag == "IR" ? "FLR" : c.Tag == "RDR" ? "CHF" : HudCm.Word(c);
            g.Text(name, nx, y + ScopeLayout.R2, hot ? Hi : Dim, HudType.Label, ScopeAlign.Left, hot);
            if (firing) g.Text(ScopeStr.Cat("-", cmFired[i]), nx, y + ScopeLayout.R3, Hi, HudType.Label, ScopeAlign.Left, true);
            return call || firing || breathing;
        }

        /// <summary>The jammer's picture when the game's is not at hand: a bolt in a ring, as the game's own is.</summary>
        internal static void JamGlyph(ScopeCanvas g, float cx, float cy, ScopeTone tone, float s = 1f)
        {
            g.Ring(cx, cy, 6f * s, tone);
            g.Line(cx + 1f * s, cy - 4f * s, cx - 3f * s, cy + 1f * s, tone);
            g.Line(cx - 3f * s, cy + 1f * s, cx + 2f * s, cy, tone);
            g.Line(cx + 2f * s, cy, cx - 1f * s, cy + 4f * s, tone);
        }

        /// <summary>A flare: a burning pellet low right, its smoke trail curling up behind it.</summary>
        internal static void FlareGlyph(ScopeCanvas g, float cx, float cy, ScopeTone tone, float s = 1f)
        {
            g.Ring(cx + 3f * s, cy + 3f * s, 2.5f * s, tone, 1f, 1f, tone, 1f);
            g.Line(cx + 1f * s, cy + 1f * s, cx - 2f * s, cy - 2f * s, tone);
            g.Line(cx - 2f * s, cy - 2f * s, cx - 3f * s, cy - 6f * s, tone, 0.7f);
            g.Line(cx + 3f * s, cy, cx + 1f * s, cy - 3f * s, tone, 0.6f);
            g.Line(cx + 1f * s, cy - 3f * s, cx + 2f * s, cy - 6f * s, tone, 0.4f);
        }

        /// <summary>Chaff: a cloud of short foil strips at odd angles.</summary>
        internal static void ChaffGlyph(ScopeCanvas g, float cx, float cy, ScopeTone tone, float s = 1f)
        {
            g.Line(cx - 6f * s, cy - 4f * s, cx - 3f * s, cy - 5f * s, tone);
            g.Line(cx, cy - 6f * s, cx + 2f * s, cy - 3f * s, tone);
            g.Line(cx + 4f * s, cy - 4f * s, cx + 6f * s, cy - 6f * s, tone);
            g.Line(cx - 5f * s, cy + 1f * s, cx - 2f * s, cy - 1f * s, tone);
            g.Line(cx + 1f * s, cy, cx + 5f * s, cy + 1f * s, tone);
            g.Line(cx - 3f * s, cy + 5f * s, cx - 1f * s, cy + 3f * s, tone);
            g.Line(cx + 2f * s, cy + 4f * s, cx + 5f * s, cy + 6f * s, tone);
        }
    }
}
