using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The STORES HEADER of the TAC BOARD (prototype: <c>takeover/stores.js</c>): the board owns the whole top-right corner, so the weapon box, the countermeasure box and the capacitor bar of
    /// the native panel are drawn here instead. The WEAPON cell (state tab, LINK / TRT AUTO / LOW tags, the rounds as a big figure with the full load, the class silhouette filled to the
    /// share left, the weapon's name with its station, and the STATION RAIL: one pip per hardpoint with a number under it), the COUNTERMEASURE cell (every dispenser as a dot grid with word and
    /// count, a jammer's charge) and the CAPACITOR row (only on an airframe whose power supply has a user). Fed by <see cref="StoresScope"/> (Hud's own provider over the aircraft); an element with
    /// no data is not drawn, and <see cref="WeaponDrawn"/> / <see cref="CmDrawn"/> / <see cref="CapDrawn"/> say which native object it replaced (the board shows the native one again otherwise).
    /// Painting allocates nothing in a steady frame; the class is partial: <c>StoresHeader.Cells.cs</c> holds the countermeasure cell and the capacitor row.
    /// </summary>
    internal sealed partial class StoresHeader
    {
        private StoresScope d;
        private bool live, primed;
        private float now;
        private readonly int[] cmLast = new int[HudCm.MaxStations], cmFired = new int[HudCm.MaxStations];
        private readonly float[] cmUntil = new float[HudCm.MaxStations], clip = new float[64];

        /// <summary>What the last <see cref="Paint"/> drew, and so which native object it stands in for.</summary>
        public bool WeaponDrawn, CmDrawn, CapDrawn;

        /// <summary>Something in the header blinks or moves with the beat (a DISPENSE call, the capacitor's chevrons, a shot just gone): the board repaints it at the beat rate.</summary>
        public bool Beat { get; private set; }

        public bool HasWeapon => live && d.Selected >= 0 && d.Selected < d.StationCount;

        public bool HasCm => live && d.CmCount > 0;

        public bool HasCap => live && d.HasCap;

        /// <summary>The header's height in board units (0: nothing to show).</summary>
        public float Height => live ? ScopeLayout.StoresHeight(HasWeapon || HasCm, HasCap) : 0f;

        /// <summary>The 10 Hz read: the feed, the THREAT page's countermeasure call (which dispenser pulses DISPENSE) and the rounds that just left a dispenser.</summary>
        public void Poll(float time, byte call)
        {
            now = time;
            live = ScopeFeeds.TryRead(out d);
            if (!live) { primed = false; return; }
            d.Call |= call;
            for (int i = 0; i < d.CmCount && i < cmLast.Length; i++)
            {
                int ammo = d.Cm[i].Ammo;
                if (primed && ammo < cmLast[i] && !d.Cm[i].Chargeable) { cmFired[i] = cmLast[i] - ammo; cmUntil[i] = now + 0.45f; }
                cmLast[i] = ammo;
            }
            primed = true;
        }

        public void Clear() { live = primed = false; WeaponDrawn = CmDrawn = CapDrawn = Beat = false; }

        /// <summary>Paint the header with its top edge at <paramref name="y0"/> board units: the weapon cell, the countermeasure cell and, under them, the capacitor row.</summary>
        public void Paint(ScopeCanvas g, float y0, float clock, bool flashOn)
        {
            WeaponDrawn = CmDrawn = CapDrawn = false;
            bool beat = false;
            if (!live) { Beat = false; return; }
            now = clock;
            bool weapon = HasWeapon, cm = HasCm;
            if (weapon)
            {
                beat |= PaintWeapon(g, ScopeLayout.Pad, y0, cm ? ScopeLayout.WeaponW : ScopeLayout.Inner, ScopeLayout.StoreH);
                WeaponDrawn = true;
            }
            if (cm)
            {
                beat |= PaintCm(g, ScopeLayout.Pad + ScopeLayout.WeaponW + ScopeLayout.CellGap, y0, ScopeLayout.Inner - ScopeLayout.WeaponW - ScopeLayout.CellGap, ScopeLayout.StoreH, flashOn);
                CmDrawn = true;
            }
            if (HasCap)
            {
                beat |= PaintCap(g, ScopeLayout.Pad, y0 + (weapon || cm ? ScopeLayout.StoreH + ScopeLayout.CapGap : 0f), ScopeLayout.Inner, ScopeLayout.CapH, clock);
                CapDrawn = true;
            }
            Beat = beat;
        }

        // ------------------------------------------------------------------------------------------------ the weapon cell

        /// <summary>The weapon cell; true while it has something that moves with the beat (a shot just gone).</summary>
        private bool PaintWeapon(ScopeCanvas g, float x, float y, float w, float h)
        {
            StoreStation s = d.Stations[d.Selected];
            StoreClass cls = s.Class;
            StoresMath.Pool(d, d.Selected, out int total, out int max);
            bool empty = StoresMath.Empty(total), low = StoresMath.Low(total, max), rel = s.Reloading;
            float frac = StoresMath.Fraction(total, max);
            StoreState state = StoresMath.StateOf(d);
            bool hot = state == StoreState.Firing || state == StoreState.Launch || state == StoreState.Release;
            ScopeTone tone = state == StoreState.Safe ? Dim : hot ? Hi : state == StoreState.Reload || state == StoreState.Empty ? Amber : Ink;
            bool flash = s.FiredAgeS < StoresMath.PipFlashS;

            g.Fill(x, y, w, h, Bg, 0.5f);
            g.Frame(x, y, w, h);
            if (flash) g.Box(x, y, w, h, Hi, 1f - s.FiredAgeS / StoresMath.PipFlashS, 2f);

            // The state tab: hot words (FIRING, LAUNCH, RELEASE) are a solid fill, the most urgent word is the strongest.
            ReadOnlySpan<char> word = state == StoreState.Reload && !float.IsNaN(s.ReloadLeftS) ? ScopeStr.Cat("RELOAD ", Math.Max(0f, s.ReloadLeftS), 1, " S") : (ReadOnlySpan<char>)StoresMath.Word(state);
            float tw = (float)Math.Ceiling(ScopeCanvas.TextW(word, 12f)) + 16f, th = 18f;
            g.Box(x + 4f, y + 4f, tw, th, tone, tone == Dim ? 0.8f : 1f, 1f, hot ? Hi : tone, hot ? 1f : 0.2f, chamTL: 5f);
            g.Text(word, x + 4f + tw / 2f, y + 4f + th / 2f + 1f, hot ? Bg : tone == Dim ? Dim : tone == Amber ? Amber : Hi, 12f, ScopeAlign.Center, true);

            // Tags at the right (LOW leftmost): LINK and TRT AUTO are gun features, so only a gun shows them.
            float tx = x + w - 5f;
            if (cls == StoreClass.Gun)
            {
                if (d.TurretAuto) tx = Tag(g, "TRT AUTO", tx, y, Ink, false);
                if (d.Link) tx = Tag(g, "LINK", tx, y, Ink, false);
            }
            if (low) Tag(g, "LOW", tx, y, Amber, true);

            // The rounds: the figure with the full load, so 372 reads as three quarters; and the silhouette filled to the share left.
            string figure = ScopeStr.Int(total);
            g.Text(figure, x + 8f, y + 39f, empty || low ? Amber : rel ? Dim : Hi, 28f, ScopeAlign.Left, true);
            if (max > 1) g.Text(ScopeStr.Cat("/", max, null), x + 8f + (float)Math.Round(ScopeCanvas.TextW(figure, 28f)) + 4f, y + 44f, Dim, 12f);
            StoreShapes.CellSize(cls, out float len, out float half);
            float gx = x + (float)Math.Round(w * 0.698f), gy = y + 36f;
            ScopeTone shape = rel || low ? Amber : empty || state == StoreState.Safe ? Dim : Ink;
            StoreShapes.Fill(g, cls, gx, gy, len, half, Ink, 0.06f, gx + len, clip);
            if (frac > 0f) StoreShapes.Fill(g, cls, gx, gy, len, half, low ? Amber : Ink, 0.24f, frac >= 0.999f ? gx + len : gx - len / 2f + len * frac, clip);
            StoreShapes.Draw(g, cls, gx, gy, len, half, shape);

            // The name row: the weapon left; the station and class right, or the amber reload call when ANOTHER station is reloading (a reloading pip always carries a word).
            int other = -1;
            for (int i = 0; i < d.StationCount && other < 0; i++) if (i != d.Selected && d.Stations[i].Reloading) other = i;
            Span<char> right = stackalloc char[28];
            float nameW = (float)Math.Ceiling(ScopeCanvas.TextW(s.Name, 12f));
            int rn;
            if (other >= 0)
            {
                rn = Reload(right, d.Stations[other], true);
                if (nameW + 8f + ScopeCanvas.TextW(right.Slice(0, rn), 12f) > w - 16f) rn = Reload(right, d.Stations[other], false);   // a long name keeps its room: "RLD 3 1.4 S"
            }
            else rn = cls == StoreClass.Gun ? Put(right, 0, "GUN") : Station(right, s);
            ReadOnlySpan<char> rt = right.Slice(0, rn);
            float rw = ScopeCanvas.TextW(rt, 12f);
            g.Text(rt, x + w - 8f, y + 58f, other >= 0 ? Amber : Dim, 12f, ScopeAlign.Right, other >= 0);
            g.FitText(s.Name, x + 8f, y + 58f, w - 16f - rw - 8f, empty && !rel ? Dim : Hi, 12f, 12f, ScopeAlign.Left, true);

            PaintRail(g, x, y, w, h);
            return hot || flash;
        }

        /// <summary>One tag box with its word at the right edge <paramref name="tx"/>; returns the edge for the next tag to its left.</summary>
        private static float Tag(ScopeCanvas g, string tag, float tx, float y, ScopeTone tone, bool caution)
        {
            float wd = (float)Math.Round(ScopeCanvas.TextW(tag, 12f) + 8f);
            tx -= wd;
            g.Box(tx, y + 4f, wd, 18f, tone, 0.9f, 1f, caution ? Amber : Bg, caution ? 0.14f : 0f);
            g.Text(tag, tx + wd / 2f, y + 4f + 9f + 1f, tone, 12f, ScopeAlign.Center, caution);
            return tx - 3f;
        }

        /// <summary>The station rail along the foot of the weapon cell: a pip per hardpoint (a gun's is wider), a number under each (G = gun, 1.. the others in order), the selected one bracketed.</summary>
        private void PaintRail(ScopeCanvas g, float x, float y, float w, float h)
        {
            int n = d.StationCount, guns = 0;
            for (int i = 0; i < n; i++) if (d.Stations[i].Class == StoreClass.Gun) guns++;
            const float gw = 32f, gap = 4f;
            float rw = w - 14f, pw = StoresMath.PipWidth(n, guns, rw, gw, gap), span = guns * gw + (n - guns) * pw + (n - 1) * gap;
            float px = x + 7f + (float)Math.Floor((rw - span) / 2f), ry = y + h - 38f, dy = y + h - 9f;
            string name = d.Stations[d.Selected].Name;
            for (int i = 0; i < n; i++)
            {
                StoreStation q = d.Stations[i];
                float wd = q.Class == StoreClass.Gun ? gw : pw;
                bool sel = i == d.Selected;
                Pip(g, q, px, ry, wd, 18f, sel, !sel && q.Name == name);
                g.Text(q.Class == StoreClass.Gun ? "G" : ScopeStr.Int(q.Slot), px + wd / 2f, dy, q.Reloading ? Amber : sel ? Hi : Dim, 12f, ScopeAlign.Center, sel || q.Reloading);
                px += wd + gap;
            }
        }

        /// <summary>One pip: its fill is the rounds left (amber and rising while it reloads), hollow when empty, a flash when it fired, brackets when selected.</summary>
        private static void Pip(ScopeCanvas g, in StoreStation q, float x, float y, float w, float h, bool sel, bool same)
        {
            float frac = q.Max > 0 ? Math.Min(1f, Math.Max(0f, (float)q.Ammo / q.Max)) : 0f;
            bool rel = q.Reloading, empty = q.Ammo <= 0 && !rel;
            ScopeTone tone = rel ? Amber : sel ? Hi : empty ? Faint : same ? Ink : Dim;
            g.Fill(x, y, w, h, Bg, 0.9f);
            if (rel)
            {
                int fh = (int)Math.Round((h - 2f) * (float.IsNaN(q.ReloadElapsed) ? 0f : Math.Min(1f, Math.Max(0f, q.ReloadElapsed))));
                g.Fill(x + 1f, y + h - 1f - fh, w - 2f, fh, Amber, 0.38f);
            }
            else if (!empty)
            {
                float fh = Math.Max(2f, (float)Math.Round((h - 2f) * frac));
                g.Fill(x + 1f, y + h - 1f - fh, w - 2f, fh, sel ? Hi : Ink, sel ? 0.34f : 0.28f);
            }
            if (q.FiredAgeS < StoresMath.PipFlashS) g.Fill(x + 1f, y + 1f, w - 2f, h - 2f, Hi, 0.65f * (1f - q.FiredAgeS / StoresMath.PipFlashS));
            g.Box(x, y, w, h, tone, empty ? 0.75f : 1f);
            StoreShapes.Draw(g, q.Class, x + w / 2f, y + h / 2f, q.Class == StoreClass.Gun ? 24f : Math.Min(17f, w - 4f), 6.5f, rel ? Amber : Hi, empty ? 0.55f : 1f, true, empty);
            if (sel) g.Brackets(x - 3f, y - 3f, w + 6f, h + 6f, 5f, Hi);
        }

        // ------------------------------------------------------------------------------------------------ words

        private static int Put(Span<char> buf, int n, string s)
        {
            for (int i = 0; i < s.Length && n < buf.Length; i++) buf[n++] = s[i];
            return n;
        }

        private static int Put(Span<char> buf, int n, ReadOnlySpan<char> s)
        {
            for (int i = 0; i < s.Length && n < buf.Length; i++) buf[n++] = s[i];
            return n;
        }

        /// <summary>"STA 3 AGM": the station's number and class.</summary>
        private static int Station(Span<char> buf, in StoreStation s)
        {
            int n = Put(buf, 0, ScopeStr.Cat("STA ", s.Slot, " "));
            return Put(buf, n, StoresMath.ClassWord(s.Class));
        }

        /// <summary>"RLD STA 3 1.4 S" (long) or "RLD 3 1.4 S" (short), a gun's station named GUN or G; without the seconds when the weapon gives none.</summary>
        private static int Reload(Span<char> buf, in StoreStation q, bool longForm)
        {
            int n = Put(buf, 0, "RLD ");
            n = q.Class == StoreClass.Gun ? Put(buf, n, longForm ? "GUN" : "G") : Put(buf, n, ScopeStr.Cat(longForm ? "STA " : "", q.Slot, null));
            if (!float.IsNaN(q.ReloadLeftS)) n = Put(buf, Put(buf, n, " "), ScopeStr.Cat("", Math.Max(0f, q.ReloadLeftS), 1, " S"));
            return n;
        }
    }
}
