using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The stores side of the TAC BOARD (prototype: <c>takeover/stores.js</c>; LAYOUT.md 3.1 to 3.3), drawn as cells of the one bar into the rectangles <see cref="ScopeLayout"/> gives them: WPN (the selected weapon: the rounds,
    /// the arm word, its name and class, its picture, the station rail, the lock pips) and CM (one tile per countermeasure in cycle order), plus SYS's capacitor row (R1 of the SYS cell). Fed by <see cref="StoresScope"/> (Hud's own provider over the aircraft); a block with no data is not there, and <see cref="WeaponDrawn"/> /
    /// <see cref="CmDrawn"/> / <see cref="CapDrawn"/> say which native box it replaced (the board shows the native one again otherwise). The stores pictures are their assigned sprites (<see cref="StoreIcons"/>: baked into an
    /// alpha mask): a sprite cannot be drawn through the stroke canvas, so the header writes where each goes into <see cref="Art"/> and the board puts an image there (StatusPanel.Stores.cs); without a picture HERO draws the class glyph.
    /// Painting allocates nothing in a steady frame; the class is partial: <c>StoresHeader.Hero.cs</c> and <c>StoresHeader.Exp.cs</c> hold the blocks.
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

        /// <summary>The pictures the last <see cref="Paint"/> wants drawn, including both shares of the weapon's magazine and any custom CM icons.</summary>
        public readonly ArtSlot[] Art = new ArtSlot[ArtSlots];
        public const int ArtSlots = CmArtStart + HudCm.MaxStations;
        private const int HeroLit = 0, HeroSpent = 1, CmArtStart = 2;

        /// <summary>What the last <see cref="Paint"/> drew, and so which native object it stands in for.</summary>
        public bool WeaponDrawn, CmDrawn, CapDrawn;

        /// <summary>Something in the stores blocks blinks or fades (a DISPENSE call, rounds just fired, the jammer's marker, the cells of a shot): the board repaints them at the beat rate.</summary>
        public bool Beat { get; private set; }

        public bool HasWeapon => live && StoresMath.HasHero(d);

        public bool HasCm => live && StoresMath.HasExp(d);

        public bool HasCap => live && StoresMath.HasPwr(d);

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
        /// Paint the blocks that exist into their rectangles: WPN, CM (dispensers) and SYS's capacitor row, the last only while the stores are shown (else the native capacitor bar stays). The legacy declutter parameter is ignored.
        /// </summary>
        public void Paint(ScopeCanvas g, in BoardGeometry geo, bool stackLayout, int declutterLevel, float clock, bool flashOn, bool storesShown)
        {
            WeaponDrawn = CmDrawn = CapDrawn = false;
            for (int i = 0; i < Art.Length; i++) Art[i] = default;
            bool beat = false;
            if (!live) { Beat = false; return; }
            now = clock;
            if (HasWeapon && geo.Wpn.Present)
            {
                PaintHero(g, geo.Wpn, flashOn);
                WeaponDrawn = true;
                beat |= d.InRange;      // the IN RNG call box pulses
            }
            if (HasCm && geo.Cm.Present)
            {
                beat |= PaintExp(g, geo.Cm, flashOn);
                CmDrawn = true;
            }
            if (HasCap && geo.Sys.Present && storesShown)
            {
                beat |= PaintPower(g, geo.Sys);
                CapDrawn = true;
            }
            Beat = beat;
        }

        // ------------------------------------------------------------------------------------------------ shared

        private static int Put(Span<char> buf, int n, ReadOnlySpan<char> s)
        {
            for (int i = 0; i < s.Length && n < buf.Length; i++) buf[n++] = s[i];
            return n;
        }

    }
}
