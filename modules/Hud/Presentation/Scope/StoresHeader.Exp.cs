using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // EXPENDABLES (LAYOUT.md 3.3): the dispensers. Per dispenser a SEL marker, the bold count, its short name and its rounds as a dot matrix (EMPTY, in red, takes the matrix's place at none), a state word at the right
    // end when it fits; under them the jammer: its picture, its charge as a thin bar and its state word.
    internal sealed partial class StoresHeader
    {
        /// <summary>
        /// The EXPENDABLES block in <paramref name="r"/>. The words keep their place under the shelf row; without shelves (Lean, Minimal) WIDE pulls the block up and the words with it
        /// (<see cref="ScopeLayout.ExpShelfRow"/>). True while a call pulses or rounds are just fired.
        /// </summary>
        private bool PaintExp(ScopeCanvas g, in BoardRect r, bool flashOn)
        {
            float x = r.X, y = r.Y + (!stack && !g.Furniture ? -ScopeLayout.ExpShelfRow : 0f), w = r.W;
            g.CaptionShelf(r.X, r.Y, w - StoresLayout.ShelfShort, "EXPENDABLES");
            bool beat = false;
            int rows = 0, jam = -1;
            for (int i = 0; i < d.CmCount; i++)
            {
                if (d.Cm[i].Chargeable) { if (jam < 0) jam = i; }
                // ponytail: the block has two rows (flares, chaff); an airframe with a third dispenser shows the first two.
                else if (rows < StoresLayout.Rows) beat |= PaintDispenser(g, i, x, y + (rows++ == 0 ? StoresLayout.FlrY : StoresLayout.ChfY), w, flashOn);
            }
            if (jam >= 0) PaintJammer(g, d.Cm[jam], x, y + StoresLayout.JamY, w);
            return beat;
        }

        /// <summary>One dispenser's row (its middle at <paramref name="cy"/>): bracketed and pulsing while the THREAT page calls for it.</summary>
        private bool PaintDispenser(ScopeCanvas g, int index, float x, float cy, float w, bool flashOn)
        {
            CmStation c = d.Cm[index];
            bool call = StoresMath.Called(c, d.Call), selected = c.Selected, firing = index < cmUntil.Length && now < cmUntil[index];
            CmCellState state = StoresMath.CmStateOf(c, call, firing);
            CmLevel level = HudCm.Level(c);
            bool none = level == CmLevel.Empty;
            if (call) g.Brackets(x - StoresLayout.CallPad, cy - StoresLayout.CallHalf, w + 2f * StoresLayout.CallPad, 2f * StoresLayout.CallHalf, StoresLayout.CallArm, Hi, flashOn ? 1f : 0.4f);

            g.Marker(x, cy, StoresLayout.MarkerSize, selected ? Hi : Dim, selected);
            string count = ScopeStr.Int(c.Ammo);
            g.Text(count, x + StoresLayout.CountX, cy, StoresMath.CountTone(level), StoresLayout.CountSize, ScopeAlign.Left, true);
            g.Text(HudCm.Word(c), x + StoresLayout.CountX + ScopeCanvas.TextW(count, StoresLayout.CountSize) + StoresLayout.NameGap, cy, call || selected ? Hi : Dim, StoresLayout.WordSize, ScopeAlign.Left, call);

            float matrixRight = x + StoresLayout.MatrixX;
            if (none) g.Text("EMPTY", x + StoresLayout.MatrixX, cy, Red, StoresLayout.WordSize, ScopeAlign.Left, true);
            else matrixRight += Matrix(g, c, index, x + StoresLayout.MatrixX, cy, w - StoresLayout.MatrixX, firing, level == CmLevel.Low);

            // The state word at the right end: only when it leaves the matrix 8 units clear.
            string word = StoresMath.StateWord(state);
            if (word != null && !none && x + w - ScopeCanvas.TextW(word) >= matrixRight + StoresLayout.StateClear)
                g.Text(word, x + w, cy, state == CmCellState.Low ? Amber : Hi, StoresLayout.WordSize, ScopeAlign.Right, true, state == CmCellState.Dispense && !flashOn ? 0.55f : 1f);
            return call || firing;
        }

        /// <summary>
        /// The dispenser's rounds as dots, 2 x 2 at a 4 unit pitch in up to three rows, filled column by column from the left: the rounds left INK (amber when low), the ones just fired BRIGHT, the spent ones a single DIM pixel.
        /// Returns the matrix's width.
        /// </summary>
        private float Matrix(ScopeCanvas g, in CmStation c, int index, float x, float cy, float room, bool firing, bool low)
        {
            StoresMath.Matrix(c.Max > 0 ? c.Max : c.Ammo, room, out int rows, out int cols, out int perDot);
            int dots = (Math.Max(1, c.Max > 0 ? c.Max : c.Ammo) + perDot - 1) / perDot, remain = (c.Ammo + perDot - 1) / perDot, hot = firing ? (cmFired[index] + perDot - 1) / perDot : 0;
            ScopeTone tone = low ? Amber : Ink;
            for (int col = 0; col < cols; col++)
                for (int row = 0; row < rows; row++)
                {
                    int i = col * rows + row;
                    if (i >= dots) break;
                    float dx = x + col * StoresMath.MatrixPitch + 1f, dy = Mathf.Round(cy + (row - (rows - 1) / 2f) * StoresMath.MatrixPitch - 1f);
                    if (i < remain) g.Fill(dx, dy, 2f, 2f, tone);
                    else if (i < remain + hot) g.Fill(dx, dy, 2f, 2f, Hi, 0.9f);
                    else g.Fill(dx, dy, 1f, 1f, Dim);
                }
            return cols * StoresMath.MatrixPitch;
        }

        /// <summary>
        /// The jammer row (its middle at <paramref name="cy"/>): the game's own picture of it (else a small drawn bolt in a ring), JAM, its charge as a thin bar (the INK fill is the charge on a DIM track) and its state word
        /// (RDY and CHG INK, ON BRIGHT; the kJ per second it draws is on PWR BUS).
        /// </summary>
        private void PaintJammer(ScopeCanvas g, in CmStation j, float x, float cy, float w)
        {
            string word = SystemsMath.JamWord(j);
            bool on = j.Active;
            ScopeTone tone = on ? Hi : Dim;
            Sprite sprite = art ? StoreIcons.Get(j.Icon) : null;
            ArtPlace place = default;
            if (sprite != null)
            {
                Rect size = sprite.rect;
                place = StoreArt.Fit(StoreIcons.Ink(j.Icon), size.width, size.height, x, cy - StoresLayout.JamIcon / 2f, StoresLayout.JamIcon, StoresLayout.JamIcon);
            }
            if (place.W > 0f) Art[JamArt] = new ArtSlot(j.Icon, place, tone, 1f);
            else JamGlyph(g, x + StoresLayout.JamIcon / 2f, cy, tone);
            g.Text("JAM", x + StoresLayout.JamWordX, cy, tone, StoresLayout.WordSize, ScopeAlign.Left, on);
            float bx = x + StoresLayout.JamBarX, by = cy - StoresLayout.JamBarH / 2f, fill = Mathf.Round(StoresLayout.JamBarW * Mathf.Clamp01(j.Charge));
            g.Fill(bx, by, fill, StoresLayout.JamBarH, Ink);
            g.Fill(bx + fill, by, StoresLayout.JamBarW - fill, StoresLayout.JamBarH, Dim);
            g.Text(word, x + w, cy, on ? Hi : Ink, StoresLayout.WordSize, ScopeAlign.Right, true);
        }

        /// <summary>The jammer's picture when the game's is not at hand: a bolt in a ring, as the game's own is.</summary>
        private static void JamGlyph(ScopeCanvas g, float cx, float cy, ScopeTone tone)
        {
            g.Ring(cx, cy, 6f, tone);
            g.Line(cx + 1f, cy - 4f, cx - 3f, cy + 1f, tone);
            g.Line(cx - 3f, cy + 1f, cx + 2f, cy, tone);
            g.Line(cx + 2f, cy, cx - 1f, cy + 4f, tone);
        }
    }
}
