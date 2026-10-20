using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // EXPENDABLES (LAYOUT.md 3.3, reworked 2026-10-18): one row per countermeasure, the jammer a row like the others, in the order the game's Next Countermeasure steps through them (CountermeasureWatch
    // orders them). The selected row is the only one with the caret; its words are bold and BRIGHT and it says SEL at its end, the others are quiet. A row: caret, the count and short name (the jammer: its
    // picture and JAM), the level as a bar in one column for every row, the state word at the right end when it fits.
    internal sealed partial class StoresHeader
    {
        /// <summary>
        /// The EXPENDABLES block in <paramref name="r"/>. The words keep their place under the shelf row; without shelves (Lean, Minimal) WIDE pulls the block up and the words with it
        /// (<see cref="ScopeLayout.ExpShelfRow"/>). True while a call pulses or rounds are just fired.
        /// </summary>
        private bool PaintExp(ScopeCanvas g, in BoardRect r, bool flashOn)
        {
            float x = r.X, y = r.Y + (!stack && !g.Furniture ? -ScopeLayout.ExpShelfRow : 0f), w = r.W;
            g.CaptionShelf(r.X, r.Y, StoresLayout.ExpShelf(w), "EXPENDABLES");
            bool beat = false;
            // ponytail: three rows; an airframe with a fourth countermeasure shows the first three stops of its cycle.
            for (int i = 0; i < d.CmCount && i < StoresLayout.Rows; i++) beat |= PaintRow(g, i, x, y + StoresLayout.RowY(i), w, flashOn);
            return beat;
        }

        /// <summary>One countermeasure's row (its middle at <paramref name="cy"/>): bracketed and pulsing while the THREAT page calls for it.</summary>
        private bool PaintRow(ScopeCanvas g, int index, float x, float cy, float w, bool flashOn)
        {
            CmStation c = d.Cm[index];
            bool selected = c.Selected, jammer = c.Chargeable;
            bool call = StoresMath.Called(c, d.Call), firing = !jammer && index < cmUntil.Length && now < cmUntil[index];
            CmLevel level = HudCm.Level(c);
            if (call) g.Brackets(x - StoresLayout.CallPad, cy - StoresLayout.CallHalf, w + 2f * StoresLayout.CallPad, 2f * StoresLayout.CallHalf, StoresLayout.CallArm, Hi, flashOn ? 1f : 0.4f);
            if (selected) g.Tri(x + StoresLayout.CaretX, cy, 90f, StoresLayout.CaretSize, Hi);

            ScopeTone words = selected || call ? Hi : Dim;
            if (jammer)
            {
                ArtKind kind = StoreIcons.Pick(c.Icon, out ArtBounds ink, out float width, out float height);
                float iconX = x + StoresLayout.CountX;
                ArtPlace place = kind != ArtKind.None ? StoreArt.Fit(ink, width, height, iconX, cy - StoresLayout.JamIcon / 2f, StoresLayout.JamIcon, StoresLayout.JamIcon) : default;
                if (place.W > 0f) Art[JamArt] = new ArtSlot(c.Icon, place, words, 1f, kind == ArtKind.Mask);
                else JamGlyph(g, iconX + StoresLayout.JamIcon / 2f, cy, words);
                g.Text("JAM", x + StoresLayout.JamWordX, cy, words, StoresLayout.WordSize, ScopeAlign.Left, selected || call);
            }
            else if (c.Tag == "IR" || c.Tag == "RDR")
            {
                // Flares and chaff: our own picture where the jammer's stands (the game's chaff borrows the flare picture), the count where JAM is.
                float gx = x + StoresLayout.CountX + StoresLayout.JamIcon / 2f;
                if (c.Tag == "IR") FlareGlyph(g, gx, cy, words); else ChaffGlyph(g, gx, cy, words);
                g.Text(ScopeStr.Int(c.Ammo), x + StoresLayout.JamWordX, cy, StoresMath.CountTone(level, selected), StoresLayout.CountSize, ScopeAlign.Left, true);
            }
            else
            {
                string count = ScopeStr.Int(c.Ammo);
                g.Text(count, x + StoresLayout.CountX, cy, StoresMath.CountTone(level, selected), StoresLayout.CountSize, ScopeAlign.Left, true);
                g.Text(HudCm.Word(c), x + StoresLayout.CountX + ScopeCanvas.TextW(count, StoresLayout.CountSize) + StoresLayout.NameGap, cy, words, StoresLayout.WordSize, ScopeAlign.Left, selected || call);
            }

            float bx = x + StoresLayout.BarX, bw = StoresLayout.BarW(w);
            if (!jammer && level == CmLevel.Empty) g.Text("EMPTY", bx, cy, Red, StoresLayout.WordSize, ScopeAlign.Left, true);
            else Level(g, c, index, bx, cy, bw, selected, firing, level == CmLevel.Low);

            // The state word at the right end, when it leaves the bar clear: DISPENSE, FIRING, LOW or SEL for a dispenser; ON while the jammer jams, SEL while it is the selected one, else RDY / CHG.
            string word = jammer ? (c.Active || !selected ? SystemsMath.JamWord(c) : "SEL") : level == CmLevel.Empty ? null : StoresMath.StateWord(StoresMath.CmStateOf(c, call, firing));
            if (word != null && x + w - ScopeCanvas.TextW(word) >= bx + bw + StoresLayout.StateClear)
            {
                CmCellState state = jammer ? CmCellState.Ready : StoresMath.CmStateOf(c, call, firing);
                ScopeTone tone = state == CmCellState.Low ? Amber : jammer && !c.Active && !selected ? Ink : Hi;
                g.Text(word, x + w, cy, tone, StoresLayout.WordSize, ScopeAlign.Right, true, state == CmCellState.Dispense && !flashOn ? 0.55f : 1f);
            }
            return call || firing;
        }

        /// <summary>
        /// A row's level as a bar on a DIM track: the rounds left (the jammer's charge) INK, BRIGHT on the selected row, amber when low; the rounds just fired BRIGHT where the fill ended.
        /// </summary>
        private void Level(ScopeCanvas g, in CmStation c, int index, float x, float cy, float w, bool selected, bool firing, bool low)
        {
            float y = Mathf.Round(cy - StoresLayout.BarH / 2f), lit = Mathf.Round(w * StoresMath.BarShare(c));
            float hot = firing && c.Max > 0 ? Mathf.Min(w - lit, Mathf.Round(w * cmFired[index] / (float)c.Max)) : 0f;
            g.Fill(x, y, lit, StoresLayout.BarH, low ? Amber : selected ? Hi : Ink);
            if (hot > 0f) g.Fill(x + lit, y, hot, StoresLayout.BarH, Hi, 0.9f);
            if (lit + hot < w) g.Fill(x + lit + hot, y, w - lit - hot, StoresLayout.BarH, Dim);
        }

        /// <summary>The jammer's picture when the game's is not at hand: a bolt in a ring, as the game's own is.</summary>
        private static void JamGlyph(ScopeCanvas g, float cx, float cy, ScopeTone tone)
        {
            g.Ring(cx, cy, 6f, tone);
            g.Line(cx + 1f, cy - 4f, cx - 3f, cy + 1f, tone);
            g.Line(cx - 3f, cy + 1f, cx + 2f, cy, tone);
            g.Line(cx + 2f, cy, cx - 1f, cy + 4f, tone);
        }

        /// <summary>A flare: a burning pellet low right, its smoke trail curling up behind it.</summary>
        private static void FlareGlyph(ScopeCanvas g, float cx, float cy, ScopeTone tone)
        {
            g.Ring(cx + 3f, cy + 3f, 2.5f, tone, 1f, 1f, tone, 1f);
            g.Line(cx + 1f, cy + 1f, cx - 2f, cy - 2f, tone);
            g.Line(cx - 2f, cy - 2f, cx - 3f, cy - 6f, tone, 0.7f);
            g.Line(cx + 3f, cy, cx + 1f, cy - 3f, tone, 0.6f);
            g.Line(cx + 1f, cy - 3f, cx + 2f, cy - 6f, tone, 0.4f);
        }

        /// <summary>Chaff: a cloud of short foil strips at odd angles.</summary>
        private static void ChaffGlyph(ScopeCanvas g, float cx, float cy, ScopeTone tone)
        {
            g.Line(cx - 6f, cy - 4f, cx - 3f, cy - 5f, tone);
            g.Line(cx, cy - 6f, cx + 2f, cy - 3f, tone);
            g.Line(cx + 4f, cy - 4f, cx + 6f, cy - 6f, tone);
            g.Line(cx - 5f, cy + 1f, cx - 2f, cy - 1f, tone);
            g.Line(cx + 1f, cy, cx + 5f, cy + 1f, tone);
            g.Line(cx - 3f, cy + 5f, cx - 1f, cy + 3f, tone);
            g.Line(cx + 2f, cy + 4f, cx + 5f, cy + 6f, tone);
        }
    }
}
