using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // WPN (ONE BAR): the selected weapon in the bar's first cell. Shelf: the station rail and, for the gun, its tags; the rule under the selected station is lit. R1: rounds numeral and the arm word (IN RNG in a pulsing call box); R2: the picture, the lock pips and the TGT word; R3: name, class, reload.
    internal sealed partial class StoresHeader
    {
        /// <summary>The WPN cell in <paramref name="r"/>. Only the IN RNG call box blinks (<paramref name="flashOn"/>); a shot just gone is the arm word, which the 10 Hz read changes.</summary>
        private void PaintHero(ScopeCanvas g, in BoardRect r, bool flashOn)
        {
            StoreStation s = d.Stations[d.Selected];
            StoreClass cls = s.Class;
            StoresMath.Pool(d, d.Selected, out int total, out int max);
            bool empty = StoresMath.Empty(total), low = StoresMath.Low(total, max), rel = s.Reloading;
            float frac = StoresMath.Fraction(total, max);
            StoreState state = StoresMath.StateOf(d);
            float x = r.X, y = r.Y, right = x + r.W;

            // Shelf: the rail, then the gun's tags packed against the cell's end; a tag that would crowd the rail is dropped (TRT AUTO first).
            float railEnd = PaintRail(g, x, y, r.W - 2f);
            if (cls == StoreClass.Gun)
            {
                float lim = railEnd + StoresLayout.TagGap, tx = right;
                bool trt = d.TurretAuto;
                if (trt)
                {
                    tx -= ScopeCanvas.TextW("TRT AUTO", StoresLayout.WordSize);
                    if (tx < lim) trt = false; else g.Text("TRT AUTO", tx, y + ScopeLayout.ShelfY, Dim, StoresLayout.WordSize);
                }
                if (d.Link)
                {
                    float lx = (trt ? tx - StoresLayout.TagGap : right) - ScopeCanvas.TextW("LINK", StoresLayout.WordSize);
                    if (lx >= lim) g.Text("LINK", lx, y + ScopeLayout.ShelfY, Dim, StoresLayout.WordSize);
                }
            }

            // R1: the rounds (smaller only when four digits would run into the arm word), the full load beside it, the arm word at the cell's end.
            float r1 = y + ScopeLayout.R1;
            string figure = ScopeStr.Int(total);
            int maxDigits = max > 1 ? ScopeStr.Int(max).Length : 0;
            float size = StoresLayout.NumeralSize(figure.Length, maxDigits);
            g.Text(figure, x + StoresLayout.NumeralX, r1, StoresMath.RoundsTone(rel, empty, low), size, ScopeAlign.Left, true);
            if (max > 1)
                g.Text(ScopeStr.Cat("/", max, null), x + StoresLayout.NumeralX + ScopeCanvas.TextW(figure, size) + StoresLayout.MaxGap, r1 + StoresLayout.MaxDy, Dim, StoresLayout.MaxSize);
            // The arm word: RELOAD / EMPTY VALUE bold amber / red with no marker; SAFE a hollow DIM pip and the word DIM; IN RNG (every launch requirement met) and ARMED (and a shot just gone) a filled BRIGHT pip and the word
            // LABEL bold BRIGHT, IN RNG also in the outlined pulsing call box.
            string arm = StoresMath.Word(state);
            if (state == StoreState.Reload || state == StoreState.Empty)
                g.Text(arm, right, r1, state == StoreState.Empty ? Red : Amber, StoresLayout.ArmSize, ScopeAlign.Right, true);
            else
            {
                bool safe = state == StoreState.Safe, inRange = state == StoreState.Armed && d.InRange;
                if (inRange) arm = "IN RNG";
                ScopeTone at = safe ? Dim : Hi;
                float markerLeft = right - ScopeCanvas.TextW(arm, HudType.Label) - StoresLayout.ArmMarkerGap;
                g.Marker(markerLeft, r1, StoresLayout.ArmMarker, at, !safe);
                g.Text(arm, right, r1, at, HudType.Label, ScopeAlign.Right, !safe);
                if (inRange) g.Box(markerLeft - 4f, r1 - 10f, right - markerLeft + 6f, 20f, Hi, flashOn ? 1f : 0.4f);
            }

            // R2: the TGT word at the end, the lock pips before it (one per round: Hi filled = leaves on the next trigger at a target, INK hollow = loaded, FAINT small = spent), the picture in what is left.
            // With pips, "n TGT" only when it says more than the filled pips (more targets than cued rounds); its room stays reserved so the art never jumps on a lock.
            float r2 = y + ScopeLayout.R2, artW = r.W;
            if (StoresMath.ShowTargets(cls))
            {
                bool pips = StoresMath.ShowPips(cls, max);
                int cued = StoresMath.Cued(total, d.Targets, d.Salvo);
                if (d.Targets <= 0) g.Text("NO TGT", right, r2, Dim, HudType.Label, ScopeAlign.Right);
                else if (!pips || d.Targets > cued) g.Text(ScopeStr.Cat("", d.Targets, " TGT"), right, r2, Hi, HudType.Label, ScopeAlign.Right, true);
                artW = r.W - StoresLayout.TgtW - StoresLayout.ArtGap;
                if (pips)
                {
                    float pipRight = right - StoresLayout.TgtW - StoresLayout.PipGap;
                    for (int i = 0; i < max; i++)
                    {
                        bool loaded = i < total;
                        float ps = loaded ? StoresLayout.PipSize : StoresLayout.SpentPip;
                        g.Marker(pipRight - (max - 1 - i) * StoresLayout.PipPitch - ps / 2f, r2, ps, i < cued ? Hi : loaded ? Ink : Faint, i < cued);
                    }
                    artW = pipRight - (max - 1) * StoresLayout.PipPitch - StoresLayout.PipSize / 2f - StoresLayout.ArtGap - x;
                }
            }
            PaintArt(g, s, StoresMath.ArtTone(state, low), frac, StoresMath.Magazine(cls, max), x, y + StoresLayout.ArtY, artW);

            // R3: the weapon, its class (none for the gun, else "STA 3 AGM") and, amber, the reload countdown. Low is the amber numeral and art tone, not a word.
            Span<char> classWord = stackalloc char[12], extra = stackalloc char[10];
            int cn = cls == StoreClass.Gun ? 0 : Put(classWord, Put(classWord, 0, ScopeStr.Cat("STA ", s.Slot, " ")), StoresMath.ClassWord(cls));
            int en = rel && !float.IsNaN(s.ReloadLeftS) ? Put(extra, 0, ScopeStr.Cat("", Math.Max(0f, s.ReloadLeftS), 1, "S")) : 0;
            float cw = ScopeCanvas.TextW(classWord.Slice(0, cn), StoresLayout.WordSize), ew = en > 0 ? ScopeCanvas.TextW(extra.Slice(0, en), StoresLayout.WordSize) : 0f;
            float ny = y + ScopeLayout.R3, nameMax = r.W - StoresLayout.WordGap - cw - (en > 0 ? StoresLayout.WordGap + ew : 0f);
            g.FitText(s.Name, x, ny, nameMax, Ink, StoresLayout.NameSize, StoresLayout.WordSize, ScopeAlign.Left);
            float wx = right - ew - (en > 0 && cn > 0 ? StoresLayout.WordGap : 0f) - cw;
            if (cn > 0) g.Text(classWord.Slice(0, cn), wx, ny, Dim, StoresLayout.WordSize);
            if (en > 0) g.Text(extra.Slice(0, en), right, ny, Amber, StoresLayout.WordSize, ScopeAlign.Right, true);
        }

        /// <summary>
        /// The weapon's picture in the box (<paramref name="x"/>, <paramref name="by"/>, <paramref name="w"/> x <see cref="StoresLayout.ArtH"/>): the game's own sprite as the board's alpha mask (<see cref="StoreIcons"/>), tinted to the state, cropped to the box
        /// its ink lives in so it fills the box (the board puts the image where <see cref="Art"/> says); a magazine's spent share is the right part of it at a dim INK. Without the picture the class glyph instead.
        /// </summary>
        private void PaintArt(ScopeCanvas g, in StoreStation s, ScopeTone tone, float frac, bool magazine, float x, float by, float w)
        {
            ArtKind kind = StoreIcons.Pick(s.Icon, out ArtBounds ink, out float width, out float height);
            if (kind != ArtKind.None)
            {
                ArtPlace place = StoreArt.Fit(ink, width, height, x, by, w, StoresLayout.ArtH);
                if (place.W > 0f)
                {
                    bool mask = kind == ArtKind.Mask;
                    if (magazine && frac < 0.999f)
                    {
                        Art[HeroLit] = new ArtSlot(s.Icon, place.Slice(0f, frac), tone, 1f, mask);
                        Art[HeroSpent] = new ArtSlot(s.Icon, place.Slice(frac, 1f), Ink, StoresMath.SpentArtAlpha, mask);
                    }
                    else Art[HeroLit] = new ArtSlot(s.Icon, place, tone, 1f, mask);
                    return;
                }
            }
            StoreShapes.Draw(g, s.Class, x + w / 2f, by + StoresLayout.ArtH / 2f, Math.Min(StoresLayout.GlyphMaxW, w), StoresLayout.GlyphHalf, tone);
        }

        /// <summary>
        /// The station rail on the shelf: a marker per station (its glyph, then its tag: G for the gun, else the station's number), a spent one dashed but readable, a reloading one amber, the selected one lit with a Hi rule segment over the glareshield rule
        /// under it. A crowded rail shortens the glyphs (<see cref="StoresLayout.RailGlyph"/>) before it lets the tags touch. Returns the x where the rail ends.
        /// </summary>
        private float PaintRail(ScopeCanvas g, float x, float y, float width)
        {
            int n = d.StationCount, maxSlot = 0;
            for (int i = 0; i < n; i++) maxSlot = Math.Max(maxSlot, d.Stations[i].Slot);
            float pitch = StoresLayout.RailPitch(n, width), glyph = StoresLayout.RailGlyph(pitch, maxSlot >= 10), cy = y + ScopeLayout.ShelfY;
            for (int i = 0; i < n; i++)
            {
                StoreStation q = d.Stations[i];
                RailMark m = StoresMath.RailMarkOf(q, i == d.Selected);
                float px = x + StoresLayout.RailX + i * pitch;
                bool spent = m.Dashed && !m.Bracketed;      // an empty station is FAINT (refit 3.2): selected BRIGHT bold, loaded INK, reloading amber
                ScopeTone tone = spent ? Faint : m.Tone;
                float alpha = spent ? 1f : m.Alpha;
                if (glyph > 0f)
                    StoreShapes.Draw(g, q.Class, px + 2f + glyph / 2f, cy, q.Class == StoreClass.Gun ? glyph + StoresLayout.RailGunExtra : glyph, StoresLayout.RailGlyphHalf, tone, alpha, true, m.Dashed);
                g.Text(q.Class == StoreClass.Gun ? "G" : ScopeStr.Int(q.Slot), px + StoresLayout.RailTagX(glyph), cy, tone, StoresLayout.WordSize, ScopeAlign.Left, m.Bold, alpha);
                if (m.Bracketed) g.Line(px - 1f, y + ScopeLayout.RuleY, px + pitch - 4f, y + ScopeLayout.RuleY, Hi, 1f, StoresLayout.SelRuleW);
            }
            return x + StoresLayout.RailX + n * pitch;
        }
    }
}
