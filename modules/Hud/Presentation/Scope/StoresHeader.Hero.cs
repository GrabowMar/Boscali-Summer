using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // HERO (LAYOUT.md 3.1): the selected weapon. The rounds as the big numeral with the full load beside it, the arm marker and word, the tags, the weapon's name and class, its picture and the station rail.
    internal sealed partial class StoresHeader
    {
        /// <summary>The HERO block in <paramref name="r"/>. Nothing in it blinks: a shot just gone is the arm word, which the 10 Hz read changes.</summary>
        private void PaintHero(ScopeCanvas g, in BoardRect r)
        {
            StoreStation s = d.Stations[d.Selected];
            StoreClass cls = s.Class;
            StoresMath.Pool(d, d.Selected, out int total, out int max);
            bool empty = StoresMath.Empty(total), low = StoresMath.Low(total, max), rel = s.Reloading;
            float frac = StoresMath.Fraction(total, max);
            StoreState state = StoresMath.StateOf(d);
            StoresLayout.HeroPlan p = StoresLayout.Hero(stack, r.W);
            float x = r.X, y = r.Y;

            Shelf(g, x, y, p.ShelfW, "STORES MGMT", "HMD SYM  ", DclWord(declutter));

            // The rounds: the numeral (smaller only when four digits would run into the arm column) and the full load beside it.
            string figure = ScopeStr.Int(total);
            int maxDigits = max > 1 ? ScopeStr.Int(max).Length : 0;
            float size = StoresLayout.NumeralSize(figure.Length, maxDigits);
            g.Text(figure, x + StoresLayout.NumeralX, y + StoresLayout.NumeralY, StoresMath.RoundsTone(rel, empty, low), size, ScopeAlign.Left, true);
            if (max > 1)
                g.Text(ScopeStr.Cat("/", max, null), x + StoresLayout.NumeralX + ScopeCanvas.TextW(figure, size) + StoresLayout.MaxGap, y + StoresLayout.NumeralY + StoresLayout.MaxDy, Ink, StoresLayout.MaxSize);

            // The master-arm marker and word, then the tags under them: LINK and TRT AUTO are gun features.
            ScopeTone armTone = StoresMath.ArmTone(state);
            string armWord = StoresMath.Word(state);
            g.Marker(x + StoresLayout.ArmMarkerX, y + StoresLayout.ArmY, StoresLayout.ArmMarkerSize, armTone, StoresMath.ArmFilled(state));
            g.Text(armWord, x + StoresLayout.ArmWordX, y + StoresLayout.ArmY, armTone, StoresLayout.ArmSize, ScopeAlign.Left, true);
            Span<float> rowRight = stackalloc float[2];
            rowRight[0] = StoresLayout.ArmWordX + ScopeCanvas.TextW(armWord, StoresLayout.ArmSize);
            float used = 0f;
            int row = 0;
            if (cls == StoreClass.Gun)
            {
                if (d.Link) Tag(g, "LINK", x, y, p, ref used, ref row, rowRight);
                if (d.TurretAuto) Tag(g, "TRT AUTO", x, y, p, ref used, ref row, rowRight);
            }

            // The declutter flag lives in the shelf; with no shelf it takes the arm row (WIDE), or the first tag row when the arm word would come within 10 units of it, or the top right (STACK).
            if (!g.Furniture)
            {
                string flag = DclWord(declutter);
                float fy = y + p.DclY;
                if (!stack) fy = y + StoresLayout.ArmY + StoresLayout.DclRow(rowRight, p.DclX - ScopeCanvas.TextW(flag)) * StoresLayout.TagPitch;
                g.Text(flag, x + p.DclX, fy, Ink, StoresLayout.WordSize, ScopeAlign.Right);
            }

            // The name row: the weapon, its class (GUN, or "STA 3 AGM") and, amber, the reload countdown or LOW.
            Span<char> classWord = stackalloc char[12], extra = stackalloc char[10];
            int cn = cls == StoreClass.Gun ? Put(classWord, 0, "GUN") : Put(classWord, Put(classWord, 0, ScopeStr.Cat("STA ", s.Slot, " ")), StoresMath.ClassWord(cls));
            int en = rel && !float.IsNaN(s.ReloadLeftS) ? Put(extra, 0, ScopeStr.Cat("", Math.Max(0f, s.ReloadLeftS), 1, " S")) : !rel && low ? Put(extra, 0, "LOW") : 0;
            float cw = ScopeCanvas.TextW(classWord.Slice(0, cn), StoresLayout.WordSize), ew = en > 0 ? ScopeCanvas.TextW(extra.Slice(0, en), StoresLayout.WordSize) : 0f;
            float ny = y + p.NameY, nameMax = r.W - StoresLayout.NameX - StoresLayout.WordGap - cw - (en > 0 ? StoresLayout.WordGap + ew : 0f);
            float nameW = g.FitText(s.Name, x + StoresLayout.NameX, ny, nameMax, empty ? Ink : Hi, StoresLayout.NameSize, StoresLayout.WordSize, ScopeAlign.Left, true);
            float wx = x + StoresLayout.NameX + nameW + StoresLayout.WordGap;
            g.Text(classWord.Slice(0, cn), wx, ny, StoresMath.ClassTone(cls), StoresLayout.WordSize);
            if (en > 0) g.Text(extra.Slice(0, en), wx + cw + StoresLayout.WordGap, ny, Amber, StoresLayout.WordSize, ScopeAlign.Left, true);

            PaintArt(g, s, StoresMath.ArtTone(state, low), frac, StoresMath.Magazine(cls, max), p, x, y);
            PaintRail(g, x, y, p);
        }

        /// <summary>One tag of the column at the right of the arm word: INK, packed after the last or wrapped to the next row (<see cref="StoresLayout.TagAt"/>).</summary>
        private static void Tag(ScopeCanvas g, string tag, float x, float y, in StoresLayout.HeroPlan p, ref float used, ref int row, Span<float> rowRight)
        {
            float w = ScopeCanvas.TextW(tag, StoresLayout.WordSize), tx = StoresLayout.TagAt(used, w, p.TagW, out bool wrap);
            if (wrap) row++;
            g.Text(tag, x + StoresLayout.TagX + tx, y + StoresLayout.TagY + row * StoresLayout.TagPitch, Ink, StoresLayout.WordSize);
            used = tx + w;
            if (row + 1 < rowRight.Length) rowRight[row + 1] = StoresLayout.TagX + used;
        }

        /// <summary>
        /// The weapon's picture in the art box: the game's own sprite, tinted to the state, cropped to the box its ink lives in so it fills the box (the board puts the image where <see cref="Art"/> says); a magazine's spent
        /// share is the right part of it at a dim INK. Without the picture (a store the game gave none, no material to draw it with) the class glyph instead.
        /// </summary>
        private void PaintArt(ScopeCanvas g, in StoreStation s, ScopeTone tone, float frac, bool magazine, in StoresLayout.HeroPlan p, float x, float y)
        {
            float bx = x + p.ArtX, by = y + p.ArtY;
            Sprite sprite = art ? StoreIcons.Get(s.Icon) : null;
            if (sprite != null)
            {
                Rect size = sprite.rect;
                ArtPlace place = StoreArt.Fit(StoreIcons.Ink(s.Icon), size.width, size.height, bx, by, p.ArtW, p.ArtH);
                if (place.W > 0f)
                {
                    if (magazine && frac < 0.999f)
                    {
                        Art[HeroLit] = new ArtSlot(s.Icon, place.Slice(0f, frac), tone, 1f);
                        Art[HeroSpent] = new ArtSlot(s.Icon, place.Slice(frac, 1f), Ink, StoresMath.SpentArtAlpha);
                    }
                    else Art[HeroLit] = new ArtSlot(s.Icon, place, tone, 1f);
                    return;
                }
            }
            StoreShapes.Draw(g, s.Class, bx + p.ArtW / 2f, by + p.ArtH / 2f, Math.Min(StoresLayout.GlyphMaxW, p.ArtW), StoresLayout.GlyphHalf, tone);
        }

        /// <summary>
        /// The station rail: a marker per station (its glyph, then its tag: G for the gun, else the station's number), the selected one bracketed in BRIGHT, a spent one dashed but readable, a reloading one amber. A crowded
        /// rail shortens the glyphs (<see cref="StoresLayout.RailGlyph"/>) before it lets the tags touch.
        /// </summary>
        private void PaintRail(ScopeCanvas g, float x, float y, in StoresLayout.HeroPlan p)
        {
            int n = d.StationCount, maxSlot = 0;
            for (int i = 0; i < n; i++) maxSlot = Math.Max(maxSlot, d.Stations[i].Slot);
            float pitch = StoresLayout.RailPitch(n, p.RailW), glyph = StoresLayout.RailGlyph(pitch, maxSlot >= 10), cy = y + p.RailY;
            for (int i = 0; i < n; i++)
            {
                StoreStation q = d.Stations[i];
                RailMark m = StoresMath.RailMarkOf(q, i == d.Selected);
                float px = x + StoresLayout.RailX + i * pitch;
                if (glyph > 0f)
                    StoreShapes.Draw(g, q.Class, px + 2f + glyph / 2f, cy, q.Class == StoreClass.Gun ? glyph + StoresLayout.RailGunExtra : glyph, StoresLayout.RailGlyphHalf, m.Tone, m.Alpha, true, m.Dashed);
                g.Text(q.Class == StoreClass.Gun ? "G" : ScopeStr.Int(q.Slot), px + StoresLayout.RailTagX(glyph), cy, m.Tone, StoresLayout.WordSize, ScopeAlign.Left, m.Bold);
                if (m.Bracketed) g.Brackets(px - 1f, cy - StoresLayout.RailBracketHalf, pitch - 3f, 2f * StoresLayout.RailBracketHalf, StoresLayout.RailBracketArm, Hi);
            }
        }
    }
}
