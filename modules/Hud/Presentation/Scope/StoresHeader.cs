using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The stores side of the TAC BOARD (prototype: <c>takeover/stores.js</c>; LAYOUT.md 3.1 to 3.3), drawn as three separate blocks into the rectangles <see cref="ScopeLayout"/> gives them: HERO (the selected weapon: the rounds,
    /// the arm word, its name and class, its picture, the station rail), PWR BUS (the capacitor as a row of cells) and EXPENDABLES (every dispenser as a dot matrix, the jammer as a bar). Each has a caption shelf, drawn only while
    /// the board has its furniture (<see cref="ScopeCanvas.Furniture"/>). Fed by <see cref="StoresScope"/> (Hud's own provider over the aircraft); a block with no data is not there, and <see cref="WeaponDrawn"/> /
    /// <see cref="CmDrawn"/> / <see cref="CapDrawn"/> say which native box it replaced (the board shows the native one again otherwise). The weapon's and the jammer's pictures are the game's own sprites: a sprite cannot be
    /// drawn through the stroke canvas, so the header writes where each goes into <see cref="Art"/> and the board puts an image there (StatusPanel.Stores.cs); without a picture HERO draws the class glyph.
    /// Painting allocates nothing in a steady frame; the class is partial: <c>StoresHeader.Hero.cs</c>, <c>StoresHeader.Pwr.cs</c> and <c>StoresHeader.Exp.cs</c> hold the blocks.
    /// </summary>
    internal sealed partial class StoresHeader
    {
        private StoresScope d;
        private bool live, primed, capPrimed;
        private float now;
        private readonly int[] cmLast = new int[HudCm.MaxStations], cmFired = new int[HudCm.MaxStations];
        private readonly float[] cmUntil = new float[HudCm.MaxStations];
        /// <summary>The capacitor's last read, and the last shot it fell by: how much, when, and the share it fell from (the just-spent cells).</summary>
        private float capLast, capLastAt, shotKj, shotAt = float.NegativeInfinity, shotFrom;
        /// <summary>The cost word (the energy weapon's first word) of the station it was made for.</summary>
        private string costFor, costWord;
        // Set by Paint for the block painters.
        private bool stack, art;
        private int declutter;

        /// <summary>The pictures the last <see cref="Paint"/> wants drawn: the weapon's (lit share, then the spent share of a magazine) and the jammer's. An unused slot has icon 0.</summary>
        public readonly ArtSlot[] Art = new ArtSlot[ArtSlots];
        public const int ArtSlots = 3;
        private const int HeroLit = 0, HeroSpent = 1, JamArt = 2;

        /// <summary>What the last <see cref="Paint"/> drew, and so which native object it stands in for.</summary>
        public bool WeaponDrawn, CmDrawn, CapDrawn;

        /// <summary>Something in the stores blocks blinks or fades (a DISPENSE call, rounds just fired, the jammer's marker, the cells of a shot): the board repaints them at the beat rate.</summary>
        public bool Beat { get; private set; }

        public bool HasWeapon => live && StoresMath.HasHero(d);

        public bool HasCm => live && StoresMath.HasExp(d);

        public bool HasCap => live && StoresMath.HasPwr(d);

        /// <summary>PWR BUS has its cost row: an energy weapon is fitted and the game says what its shot costs.</summary>
        public bool HasCost => live && StoresMath.CostKj(d) > 0f;

        /// <summary>The 10 Hz read: the feed, the THREAT page's countermeasure call (which dispenser pulses DISPENSE), the rounds that just left a dispenser and the capacitor's last shot.</summary>
        public void Poll(float time, byte call)
        {
            now = time;
            live = ScopeFeeds.TryRead(out d);
            if (!live) { primed = capPrimed = false; return; }
            d.Call |= call;
            for (int i = 0; i < d.CmCount && i < cmLast.Length; i++)
            {
                int ammo = d.Cm[i].Ammo;
                if (primed && ammo < cmLast[i] && !d.Cm[i].Chargeable) { cmFired[i] = cmLast[i] - ammo; cmUntil[i] = now + 0.45f; }
                cmLast[i] = ammo;
            }
            primed = true;
            if (!d.HasCap) { capPrimed = false; return; }
            float max = d.CapMaxKj > 0f ? d.CapMaxKj : Math.Max(1f, d.CapKj);
            if (capPrimed && StoresMath.Shot(capLast, d.CapKj, now - capLastAt, max, out float drop)) { shotKj = drop; shotAt = now; shotFrom = capLast / max; }
            capLast = d.CapKj;
            capLastAt = now;
            capPrimed = true;
        }

        public void Clear()
        {
            live = primed = capPrimed = false;
            WeaponDrawn = CmDrawn = CapDrawn = Beat = false;
            shotAt = float.NegativeInfinity;
            for (int i = 0; i < Art.Length; i++) Art[i] = default;
        }

        /// <summary>
        /// Paint the blocks that exist into their rectangles of <paramref name="geo"/> (board units; the surface's origin is the box's). <paramref name="stackLayout"/> is the classic column, <paramref name="declutterLevel"/> the
        /// number the DCL flag shows (0 full, 1 lean, 2 minimal), <paramref name="pictures"/> that the game's additive material is at hand (else HERO draws its class glyph).
        /// </summary>
        public void Paint(ScopeCanvas g, in BoardGeometry geo, bool stackLayout, int declutterLevel, bool pictures, float clock, bool flashOn)
        {
            WeaponDrawn = CmDrawn = CapDrawn = false;
            for (int i = 0; i < Art.Length; i++) Art[i] = default;
            bool beat = false;
            if (!live) { Beat = false; return; }
            now = clock;
            stack = stackLayout;
            declutter = declutterLevel;
            art = pictures;
            if (HasWeapon && geo.Hero.Present)
            {
                PaintHero(g, geo.Hero);
                WeaponDrawn = true;
            }
            if (HasCap && geo.Pwr.Present)
            {
                beat |= PaintPwr(g, geo.Pwr);
                CapDrawn = true;
            }
            if (HasCm && geo.Exp.Present)
            {
                beat |= PaintExp(g, geo.Exp, flashOn);
                CmDrawn = true;
            }
            Beat = beat;
        }

        // ------------------------------------------------------------------------------------------------ shared

        /// <summary>
        /// A caption shelf whose flag has two tones (HERO's "HMD SYM  DCL n": the first words furniture, the level data, LAYOUT.md 3.1). The same geometry as <see cref="ScopeCanvas.CaptionShelf"/>: a hairline 6 under the block's top
        /// interrupted 4 clear by the caption (at x + 12) and by the flag (right-aligned to x + w - 12), ending in a node and a 45 degree stub. Nothing without furniture.
        /// </summary>
        private static void Shelf(ScopeCanvas g, float x, float y, float w, string caption, string flag, string level)
        {
            if (!g.Furniture) return;
            const float Inset = 12f, Clear = 4f;
            float cy = y + StoresLayout.ShelfY, end = x + w, captionRight = x + Inset + ScopeCanvas.TextW(caption), levelW = ScopeCanvas.TextW(level), flagLeft = end - Inset - levelW - ScopeCanvas.TextW(flag);
            g.Line(x, cy, x + Inset - Clear, cy, Dim);
            g.Text(caption, x + Inset, cy, Dim);
            g.Line(captionRight + Clear, cy, flagLeft - Clear, cy, Dim);
            g.Text(flag, flagLeft, cy, Dim);
            g.Text(level, end - Inset, cy, Ink, 12f, ScopeAlign.Right);
            g.Line(end - Inset + Clear, cy, end, cy, Dim);
            g.Fill(end - 2f, cy - 2f, 4f, 4f, Dim);
            g.Line(end + 2f, cy - 2f, end + 8f, cy - 8f, Dim);
        }

        private static int Put(Span<char> buf, int n, ReadOnlySpan<char> s)
        {
            for (int i = 0; i < s.Length && n < buf.Length; i++) buf[n++] = s[i];
            return n;
        }

        private static readonly string[] DclWords = { "DCL 0", "DCL 1", "DCL 2" };

        private static string DclWord(int level) => DclWords[level < 0 ? 0 : level > 2 ? 2 : level];
    }
}
