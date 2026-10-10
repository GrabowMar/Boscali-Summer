using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The paint of the OBJECTIVE page: the zone plot, the right column and the objective pips (pages/objective.js drawPlot, drawTimer, drawRange, drawGates, drawPayout, drawCone, drawStrip).
    internal sealed partial class ObjectivePage
    {
        // The plot (north up, the zone's centre fixed) and the column beside it, in the prototype's numbers.
        private const float PX = 4f, PY = 32f, PW = 294f, PH = 240f, CX = PX + 170f, CY = PY + PH / 2f, PlotR = 108f, RX = 306f, W = 440f;
        private const float WinL = PX + 1f, WinT = PY + 1f, WinR = PX + PW - 1f, WinB = PY + PH - 1f;

        private readonly float[] trailPx = new float[2 * (TrailPoints + 1)], poly = new float[2 * 56], clipA = new float[2 * 72], clipB = new float[2 * 72], pennant = new float[10], cone = new float[8];

        private AvUnits units = AvUnits.Metric;

        public override void Paint(ScopeCanvas g, in PagePaint p)
        {
            if (!ScopeFeeds.TryRead(out d) || d.Id == 0) return;      // fresh numbers every frame: the page is Fast
            now = p.Now;
            units = p.Units;
            if (!d.HasMarker)
            {
                // The contact is lost: no zone to plot, no range, no gates, no air defence about it. The plate, the deadline and the payout stay.
                LostPlate(g);
                Timer(g);
                Payout(g, -34);
                return;
            }
            float ownDist = ObjectiveMath.Distance(d.OwnX, d.OwnZ, d.X, d.Z);
            bool inZone = ownDist <= d.Radius;
            rangeKm = ObjectiveMath.Range(rangeKm, ObjectiveMath.Need(d.Radius / 1000f, ownDist / 1000f));
            float sc = PlotR / rangeKm;

            Plot(g, sc, ownDist, inZone);
            Plate(g);
            Timer(g);
            Range(g, ownDist, inZone);
            int shift = Gates(g, inZone) ? 0 : -34;
            Payout(g, shift);
            Side(g, shift);
        }

        // ------------------------------------------------------------------------------------------------ clipped strokes

        private static bool Inside(float x, float y, float margin) => x >= WinL + margin && x <= WinR - margin && y >= WinT + margin && y <= WinB - margin;

        private static void Cut(ScopeCanvas g, float x0, float y0, float x1, float y1, ScopeTone tone, float alpha = 1f, float w = 1f, float dash = 0f, float gap = 0f, float offset = 0f)
        {
            if (ScreenClip.Line(ref x0, ref y0, ref x1, ref y1, WinL, WinT, WinR, WinB)) g.Line(x0, y0, x1, y1, tone, alpha, w, dash, gap, offset);
        }

        /// <summary>A circle cut to the plot; dashed with the prototype's pattern when <paramref name="dash"/> is above zero (the pattern slides with <paramref name="offset"/>).</summary>
        private static void Circle(ScopeCanvas g, float cx, float cy, float r, ScopeTone tone, float alpha, float dash = 0f, float gap = 0f, float offset = 0f)
        {
            if (r < 0.5f || cx + r < WinL || cx - r > WinR || cy + r < WinT || cy - r > WinB) return;
            if (Far(cx, cy, WinL, WinT) < r && Far(cx, cy, WinR, WinT) < r && Far(cx, cy, WinL, WinB) < r && Far(cx, cy, WinR, WinB) < r) return;      // it encloses the plot: nothing to draw
            float circ = Mathf.PI * 2f * r, period = dash + gap;
            int segs = Mathf.Clamp(Mathf.CeilToInt(circ / (dash > 0f ? 2f : 5f)), 16, 720);
            float da = Mathf.PI * 2f / segs;
            for (int i = 0; i < segs; i++)
            {
                if (dash > 0f)
                {
                    float s = (i + 0.5f) * circ / segs - offset;
                    s = (s % period + period) % period;
                    if (s >= dash) continue;
                }
                float a0 = i * da, a1 = a0 + da;
                Cut(g, cx + Mathf.Cos(a0) * r, cy + Mathf.Sin(a0) * r, cx + Mathf.Cos(a1) * r, cy + Mathf.Sin(a1) * r, tone, alpha);
            }
        }

        private static float Far(float cx, float cy, float x, float y) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));

        /// <summary>A disc cut to the plot, filled faintly (a ring the pilot is inside).</summary>
        private void Disc(ScopeCanvas g, float cx, float cy, float r, ScopeTone tone, float alpha)
        {
            if (r < 1f || cx + r < WinL || cx - r > WinR || cy + r < WinT || cy - r > WinB) return;
            const int n = 48;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                poly[2 * i] = cx + Mathf.Cos(a) * r;
                poly[2 * i + 1] = cy + Mathf.Sin(a) * r;
            }
            int m = ScreenClip.Polygon(poly, n, clipB, clipA, WinL, WinT, WinR, WinB);
            if (m < 3) return;
            g.Poly(clipB, m);
            g.FillPoly(m, tone, alpha);
        }

        // ------------------------------------------------------------------------------------------------ the plot

        private void Plot(ScopeCanvas g, float sc, float ownDist, bool inZone)
        {
            float t = now;
            labelCount = 0;
            placer.Reset();
            g.Box(PX, PY, PW, PH, Faint, 1f, 1f, Bg, 0.35f);

            // known air defence: the maximum range (dashed) and, for a radar, the reach at the pilot's height (solid, filled while he is inside)
            for (int i = 0; i < d.RingCount; i++)
            {
                AirDefenceRing ring = d.Rings[i];
                SamExposure e = ObjectiveMath.Expose(ring, d.OwnX, d.OwnZ, d.OwnAgl);
                float sx = CX + (ring.X - d.X) / 1000f * sc, sy = CY - (ring.Z - d.Z) / 1000f * sc, maxR = ring.MaxRange / 1000f * sc;
                bool hot = e.Inside, radar = ObjectiveMath.Radar(ring);
                ScopeTone tone = hot ? Amber : Dim;
                Circle(g, sx, sy, maxR, tone, hot ? 0.8f : 0.6f, 4f, 3f, -t * 4f);
                if (radar)
                {
                    float reach = Math.Max(2f, e.ReachM / 1000f * sc);
                    if (hot) Disc(g, sx, sy, reach, Amber, 0.07f);
                    Circle(g, sx, sy, reach, tone, hot ? 1f : 0.9f);
                    if (Inside(sx, sy, 6f)) g.Tri(sx, sy, 0f, 5f, hot ? Amber : Ink, false);
                    if (Inside(sx - 6f, sy + 16f, 0f) && sx + 40f < WinR) Label(ScopeStr.Distance("SAM", e.DistanceM, units, 12), sx - 6f, sy + 16f, hot ? Amber : Dim, hot, ScopeAlign.Left);
                }
                else if (Inside(sx, sy, 4f))
                {
                    g.Box(sx - 3f, sy - 3f, 6f, 6f, hot ? Amber : Ink);
                    if (sx + 60f < WinR) Label(ObjectiveMath.Kind(ring.Kind), sx + 8f, sy, hot ? Amber : Dim, hot, ScopeAlign.Left);
                }
            }

            // the zone: its ring, ticks inside it, the hold arc around it
            float zr = d.Radius / 1000f * sc, prog = ObjectiveMath.Progress(d);
            ScopeTone zt = inZone ? Hi : Ink;
            g.Ring(CX, CY, zr, zt, inZone ? 1f : 0.9f, 1f, Ink, inZone ? 0.1f : 0.05f);
            if (zr >= 12f)
            {
                if (Inside(CX, CY + zr + 12f, 0f)) Label(zoneWord(), CX, CY + zr + 12f, inZone ? Ink : Dim, false, ScopeAlign.Center);
                for (int a = 0; a < 360; a += 30)
                {
                    ScopeCanvas.Polar(a, zr, out float ox, out float oy);
                    ScopeCanvas.Polar(a, zr - 4f, out float ix, out float iy);
                    g.Line(CX + ox, CY + oy, CX + ix, CY + iy, Ink, 0.7f);
                }
            }
            if (prog > 0f) g.Arc(CX, CY, zr + 4f, 0f, Math.Max(1f, 360f * prog), Hi, 1f, 3f);
            if (inZone) g.Ring(CX, CY, zr + 4f, Faint, 0.8f, 1f);

            // the marked target at its centre: a diamond until the STRIKE is done, then a cross and a burst
            if (StrikeDone)
            {
                g.Cross(CX, CY, 8f, Dim);
                float k = now - strikeDoneAt;
                if (k >= 0f && k < 1f)
                {
                    g.Ring(CX, CY, 3f + 22f * ScopeCanvas.EaseOut(k), Hi, 1f - k);
                    if (k < 0.3f) g.Ring(CX, CY, 5f, Hi, 1f, 1f, Hi, 0.7f);
                }
            }
            else
            {
                g.Diamond(CX, CY, 5f, Ink);
                g.Ring(CX, CY, 9f + 2f * ScopeCanvas.Pulse(t, 1.5f), Ink, 0.4f, 1f, Ink, 0f, 2f, 3f);
            }

            // the pilot: trail (global points), the line to the zone, his heading, the envelope warning, himself
            float ox0 = CX + (d.OwnX - d.X) / 1000f * sc, oy0 = CY - (d.OwnZ - d.Z) / 1000f * sc;
            int n = 0;
            for (int i = 0; i < trailN; i++)
            {
                trailPx[2 * n] = CX + (trailX[i] - d.X) / 1000f * sc;
                trailPx[2 * n + 1] = CY - (trailZ[i] - d.Z) / 1000f * sc;
                n++;
            }
            trailPx[2 * n] = ox0;
            trailPx[2 * n + 1] = oy0;
            n++;
            for (int i = 0; i + 1 < n; i++)
            {
                float k = Mathf.Ceil((i + 1f) / (n - 1) * 6f) / 6f;
                Cut(g, trailPx[2 * i], trailPx[2 * i + 1], trailPx[2 * i + 2], trailPx[2 * i + 3], Ink, 0.12f + 0.88f * k);
            }
            if (!inZone) Cut(g, ox0, oy0, CX, CY, Dim, 0.55f, 1f, 2f, 4f, -t * 8f);
            ScopeCanvas.Polar(d.OwnHeading, 26f, out float hx, out float hy);
            Cut(g, ox0, oy0, ox0 + hx, oy0 + hy, Dim, 1f, 1f, 3f, 3f);
            if (inSam || inGun) g.Ring(ox0, oy0, 11f + 2f * ScopeCanvas.Pulse(t, 2f), Amber, 0.75f);
            g.Chevron(ox0, oy0, d.OwnHeading, 7f, Hi, true);
            FlushLabels(g);      // the words last, each on a plate: no ring, chevron or glyph strikes one through

            // north, the scale bar, and the plate of the contract
            g.Text("N", PX + PW - 12f, PY + 11f, Dim, 12f, ScopeAlign.Center);
            g.Arrow(PX + PW - 12f, PY + 32f, PX + PW - 12f, PY + 21f, Dim, 1f, 1f, 4f);
            float barM = ObjectiveMath.ScaleBar(sc, units == AvUnits.Imperial, 80f), barPx = barM / 1000f * sc, by = PY + PH - 12f;
            g.Line(PX + 8f, by, PX + 8f + barPx, by, Dim);
            g.Line(PX + 8f, by - 3f, PX + 8f, by + 3f, Dim);
            g.Line(PX + 8f + barPx, by - 3f, PX + 8f + barPx, by + 3f, Dim);
            g.Text(ScopeStr.Distance("", barM, units, 8), PX + 14f + barPx, by, Dim);
        }

        // ---------------------------------------------------------------- deferred labels

        private const int LabelCap = 12, LabelChars = 24;
        private readonly char[] labelChars = new char[LabelCap * LabelChars];
        private readonly int[] labelLen = new int[LabelCap];
        private readonly float[] labelX = new float[LabelCap], labelY = new float[LabelCap];
        private readonly ScopeTone[] labelTone = new ScopeTone[LabelCap];
        private readonly bool[] labelBold = new bool[LabelCap];
        private readonly ScopeAlign[] labelAlign = new ScopeAlign[LabelCap];
        private readonly LabelPlacer placer = new LabelPlacer();
        private int labelCount;

        /// <summary>Queue a plot word (copied out of the shared scratch): it is placed clear of the words before it and drawn on a plate after everything else of the plot.</summary>
        private void Label(ReadOnlySpan<char> text, float x, float y, ScopeTone tone, bool bold, ScopeAlign align)
        {
            if (labelCount >= LabelCap) return;
            int l = labelCount++, len = Math.Min(text.Length, LabelChars);
            text.Slice(0, len).CopyTo(new Span<char>(labelChars, l * LabelChars, len));
            labelLen[l] = len; labelX[l] = x; labelY[l] = y; labelTone[l] = tone; labelBold[l] = bold; labelAlign[l] = align;
        }

        private void FlushLabels(ScopeCanvas g)
        {
            for (int i = 0; i < labelCount; i++)
            {
                var text = new ReadOnlySpan<char>(labelChars, i * LabelChars, labelLen[i]);
                float w = ScopeCanvas.TextW(text, 12f), x0 = labelAlign[i] == ScopeAlign.Right ? labelX[i] - w : labelAlign[i] == ScopeAlign.Center ? labelX[i] - w / 2f : labelX[i];
                float y = Mathf.Clamp(placer.Place(x0, x0 + w, labelY[i]), WinT + 9f, WinB - 9f);
                g.Fill(Mathf.Round(x0) - 2f, Mathf.Round(y) - 8f, (float)Math.Ceiling(w) + 4f, 16f, Bg, 0.85f);
                g.Text(text, labelX[i], y, labelTone[i], 12f, labelAlign[i], labelBold[i]);
            }
        }

        /// <summary>"ZONE 1.5KM": the zone's radius, cached (it changes only with the contract or the player's units).</summary>
        private string zoneWord()
        {
            int key = (int)(d.Radius / 10f) * 2 + (units == AvUnits.Imperial ? 1 : 0) + d.Id * 100000;
            if (key != zoneKey) { zoneKey = key; zoneText = ScopeStr.Distance("ZONE R", d.Radius, units, 14).ToString(); }
            return zoneText;
        }

        /// <summary>The contract's plate over the plot's corner: its title, its target and how the contact stands, and what to do or what is wrong.</summary>
        private void Plate(ScopeCanvas g)
        {
            g.Box(PX + 2f, PY + 2f, 190f, 48f, Faint, 1f, 1f, Bg, 0.9f, chamBR: 8f);
            g.FitText(d.Title, PX + 8f, PY + 11f, 178f, Ink, 12f, 12f, ScopeAlign.Left, true);
            int sk = d.Id * 8 + (int)d.Contact;
            if (sk != subKey)
            {
                subKey = sk;
                subText = string.IsNullOrEmpty(d.Target) ? null : d.Target.ToUpperInvariant();
            }
            string contact = ObjectiveMath.Contact(d.Contact);
            if (subText == null) g.Text(contact, PX + 8f, PY + 26f, Dim);
            else
            {
                float cw = ScopeCanvas.TextW(contact);
                g.Text(contact, PX + 2f + 190f - 6f, PY + 26f, Dim, 12f, ScopeAlign.Right);
                g.FitText(subText, PX + 8f, PY + 26f, 178f - cw - 8f, Dim);
            }

            ObjectiveLine line = ObjectiveMath.Line(d);
            bool warn = ObjectiveMath.Warns(line);
            string word = ObjectiveMath.LineWord(line);
            if (word == null)
            {
                int pct = (int)Math.Round(ObjectiveMath.Progress(d) * 100f), lk = (int)line * 100000 + d.Current * 1000 + pct + d.Id * 1000000;
                if (lk != lineKey)
                {
                    lineKey = lk;
                    string title = d.Phases != null && d.Current >= 0 && d.Current < d.Phases.Length ? d.Phases[d.Current].Title : "";
                    lineText = line == ObjectiveLine.Progress ? ScopeStr.Cat(title + " ", pct, "%").ToString() : title.Length > 0 ? "NEXT " + title : "";
                }
                word = lineText;
            }
            g.FitText(word, PX + 8f, PY + 41f, 178f, warn ? Amber : Ink, 12f, 12f, ScopeAlign.Left, warn);
        }

        /// <summary>The lost contact's plate where the plot would be: the contract, CONTACT LOST and what the host says is wrong, all in caution amber.</summary>
        private void LostPlate(ScopeCanvas g)
        {
            g.Box(PX, PY, PW, PH, Amber, 0.6f, 1f, Amber, 0.05f);
            g.FitText(d.Title, PX + 12f, PY + 20f, PW - 24f, Amber, 14f, 12f, ScopeAlign.Left, true);
            int sk = d.Id * 8 + (int)d.Contact;
            if (sk != subKey)
            {
                subKey = sk;
                subText = string.IsNullOrEmpty(d.Target) ? null : d.Target.ToUpperInvariant();
            }
            if (subText != null) g.FitText(subText, PX + 12f, PY + 38f, PW - 24f, Dim);
            g.Text(ObjectiveMath.LineWord(ObjectiveLine.ContactLost), PX + 12f, PY + 70f, Amber, 18f, ScopeAlign.Left, true);
            if (!ReferenceEquals(d.Blocker, lostSource))
            {
                lostSource = d.Blocker;
                for (int i = 0; i < lostLines.Length; i++) lostLines[i] = null;
                lostCount = ObjectiveMath.Wrap(d.Blocker?.ToUpperInvariant(), 31, lostLines);
            }
            for (int i = 0; i < lostCount; i++) g.Text(lostLines[i], PX + 12f, PY + 98f + i * 16f, Amber);
        }

        // ------------------------------------------------------------------------------------------------ the column

        private void Timer(ScopeCanvas g)
        {
            if (d.Total <= 0f || d.Left <= 0f) return;
            string clock = OpsScopeWords.Clock(d.Left);
            bool wide = clock.Length > 4;        // 10:00 and up: the 12 px bold clock is wider than the 34 px inside the ring
            float r = wide ? 22f : 19f, cx = RX + (wide ? 26f : 23f), cy = 55f, frac = ScopeCanvas.Clamp01(d.Left / d.Total), lx = RX + (wide ? 56f : 52f);
            bool low = ObjectiveMath.DeadlineLow(d.Left);
            g.Ring(cx, cy, r, Faint, 1f, 4f);
            if (frac > 0.003f) g.Arc(cx, cy, r, 0f, 360f * frac - 0.01f, low ? Amber : Ink, 1f, 4f);
            g.Line(cx, cy - r - 3f, cx, cy - r + 2f, Dim);
            g.Text(clock, cx, cy + 1f, low ? Amber : Hi, 12f, ScopeAlign.Center, true);
            g.Text("DEADLINE", lx, 46f, Dim);
            int tk = (int)Math.Ceiling(d.Total);
            if (tk != totalKey) { totalKey = tk; totalText = "OF " + OpsScopeWords.Clock(d.Total); }
            g.Text(totalText, lx, 64f, Dim);
        }

        private void Range(ScopeCanvas g, float ownDist, bool inZone)
        {
            float cx = RX + 17f, cy = 111f, bearing = ObjectiveMath.Bearing(d.X - d.OwnX, d.Z - d.OwnZ), rel = ObjectiveMath.Relative(bearing, d.OwnHeading);
            g.Ring(cx, cy, 15f, Faint);
            g.Line(cx, cy - 17f, cx, cy - 13f, Dim);
            ScopeCanvas.Polar(rel, 13f, out float tx, out float ty);
            g.Line(cx, cy, cx + tx, cy + ty, Hi);
            g.Chevron(cx + tx, cy + ty, rel, 5f, Hi, true);
            g.Ring(cx, cy, 2f, Ink, 1f, 1f, Ink, 1f);
            if (inZone) g.Ring(cx, cy, 19f, Hi, 0.5f + 0.4f * ScopeCanvas.Pulse(now, 2f));
            g.Text(inZone ? "ZONE CTR" : "ZONE EDGE", RX + 38f, 94f, inZone ? Hi : Dim);
            g.Text(ScopeStr.Distance("", inZone ? ownDist : Math.Max(0f, ownDist - d.Radius), units, 8), RX + 38f, 112f, Hi, 16f, ScopeAlign.Left, true);
            int brg = (int)Math.Round(bearing) % 360;
            g.Text(ScopeStr.Cat(brg < 10 ? "BRG 00" : brg < 100 ? "BRG 0" : "BRG ", brg), RX + 38f, 129f, Dim);
        }

        private static readonly string[] GateWords = { "RNG", "ALT", "LOS", "TRK" };

        /// <summary>The four hold gates: why the hold is or is not counting. False (and nothing drawn) when the current phase is not one the gates decide.</summary>
        private bool Gates(ScopeCanvas g, bool inZone)
        {
            if (!d.HasGates) return false;
            bool losKnown = d.GateRange && d.GateAlt && d.GateTrack;
            g.Line(RX, 140f, W - 4f, 140f, Faint);
            for (int i = 0; i < 4; i++)
            {
                float x = RX + i * 33f + 16f;
                bool ok = i == 0 ? d.GateRange : i == 1 ? d.GateAlt : i == 2 ? d.GateLos : d.GateTrack;
                bool amber = i != 0 && (i != 2 || inZone);
                ScopeTone bad = amber ? Amber : Dim;
                if (i == 2 && !losKnown) { g.Text(GateWords[i], x, 148f, Dim, 12f, ScopeAlign.Center); g.Line(x - 4f, 163f, x + 4f, 163f, Dim); continue; }     // the sightline is only read once the other three hold
                g.Text(GateWords[i], x, 148f, ok ? Ink : bad, 12f, ScopeAlign.Center, !ok && amber);
                if (ok) g.Check(x, 163f, 9f, Ink, 1f, 2f); else g.Cross(x, 163f, 8f, bad, 1f, 2f);
            }
            g.Line(RX, 174f, W - 4f, 174f, Faint);
            return true;
        }

        private void Payout(ScopeCanvas g, int shift)
        {
            if (d.Money <= 0 && d.Xp <= 0) return;
            float y = 184f + shift;
            if (d.Money != moneyKey) { moneyKey = d.Money; moneyText = OpsScopeWords.Money(d.Money); }
            if (d.Xp != xpKey) { xpKey = d.Xp; xpText = "+" + d.Xp + " XP"; }
            if (!d.Multiplier.Equals(multKey)) { multKey = d.Multiplier; multText = OpsScopeWords.Multiplier(d.Multiplier); }
            g.Text("PAYOUT", RX, y, Dim);
            if (multText != null) g.Text(multText, W - 6f, y, Dim, 12f, ScopeAlign.Right);
            g.Text(moneyText, RX, 202f + shift, Hi, 16f, ScopeAlign.Left, true);
            if (d.Xp > 0) g.Text(xpText, W - 6f, 203f + shift, Ink, 12f, ScopeAlign.Right);
            g.Line(RX, 215f + shift, W - 4f, 215f + shift, Faint);
        }

        /// <summary>The bottom of the column: the nearest radar SAM seen from the side (what height masks it), or, with none known, what the host wants of the pilot next.</summary>
        private void Side(ScopeCanvas g, int shift)
        {
            int radar = ObjectiveMath.NearestRadar(d.Rings, d.RingCount, d.OwnX, d.OwnZ);
            if (radar < 0) { Next(g, shift); return; }
            AirDefenceRing ring = d.Rings[radar];
            SamExposure e = ObjectiveMath.Expose(ring, d.OwnX, d.OwnZ, d.OwnAgl);
            bool exposed = e.Inside;
            float x0 = RX + 2f, gy = 269f, h = 31f - shift, axis = Math.Max(ring.MaxRange * 1.1f, e.DistanceM * 1.05f);
            float Sx(float m) => x0 + m / axis * 124f;
            float Sy(float a) => gy - Math.Min(a, ObjectiveMath.ConeCeilingM) / ObjectiveMath.ConeCeilingM * h;
            g.Text(ObjectiveMath.Kind(ring.Kind), RX, 223f + shift, Dim);
            g.Text(exposed ? "EXPOSED" : "MASKED", W - 6f, 223f + shift, exposed ? Amber : Ink, 12f, ScopeAlign.Right, true);

            // coverage is refused below minAlt x d / range and above the ceiling: a wedge from the site
            cone[0] = Sx(0f); cone[1] = gy; cone[2] = Sx(ring.MaxRange); cone[3] = Sy(ring.MinAltitude); cone[4] = Sx(ring.MaxRange); cone[5] = Sy(ObjectiveMath.ConeCeilingM); cone[6] = Sx(0f); cone[7] = Sy(ObjectiveMath.ConeCeilingM);
            int n = g.Poly(cone, 4);
            g.FillPoly(n, exposed ? Amber : Ink, exposed ? 0.1f : 0.05f);
            g.Hatch(n, 4f, exposed ? Amber : Dim, 0.35f);
            g.Path(n, exposed ? Amber : Dim, 0.9f);
            g.Line(x0, gy, W - 6f, gy, Dim);
            g.Tri(Sx(0f), gy - 3f, 0f, 4f, Ink, false);
            float px = Sx(Math.Min(e.DistanceM, axis)), py = Sy(d.OwnAgl);
            g.Line(px, py, px, gy, Dim, 0.5f, 1f, 2f, 2f);
            g.Chevron(px, py, 90f, 5f, exposed ? Amber : Hi, true);
        }

        private void Next(ScopeCanvas g, int shift)
        {
            bool blocked = !d.HasGates && !string.IsNullOrEmpty(d.Blocker);
            string source = blocked ? d.Blocker : d.NextAction;
            if (!ReferenceEquals(source, nextSource))
            {
                nextSource = source;
                for (int i = 0; i < nextLines.Length; i++) nextLines[i] = null;
                nextCount = ObjectiveMath.Wrap(source?.ToUpperInvariant(), 17, nextLines);
            }
            if (nextCount == 0) return;
            g.Text(blocked ? "BLOCKED" : "NEXT", RX, 223f + shift, blocked ? Amber : Dim, 12f, ScopeAlign.Left, blocked);
            for (int i = 0; i < nextCount; i++) g.Text(nextLines[i], RX, 238f + shift + i * 14f, blocked ? Amber : Ink);
        }

        // ------------------------------------------------------------------------------------------------ the objective pips

        public override void PaintStrip(ScopeCanvas g, in PagePaint p)
        {
            int count = Math.Min(d.PhaseCount, 3);
            if (d.Phases == null || count <= 0) return;
            float w = (432f - 4f * (count - 1)) / count;
            ObjectiveLine line = ObjectiveMath.Line(d);
            for (int i = 0; i < count && i < d.Phases.Length; i++)
            {
                ObjectivePhase ph = d.Phases[i];
                float x = 4f + i * (w + 4f);
                bool pending = ph.Status == ObjectivePhaseStatus.Pending, done = ph.Status == ObjectivePhaseStatus.Done, current = !pending && !done;
                ScopeTone tone = pending ? Dim : done ? Ink : Hi;
                bool warn = current && ObjectiveMath.Warns(line);
                Pennant(g, x, w, tone, pending, done, current ? ScopeCanvas.Clamp01(ph.Progress) : 0f);
                if (done) g.Check(x + 15f, 13f, 9f, Ink, 1f, 2f); else g.Diamond(x + 15f, 13f, 4f, tone, current);
                g.Text(ph.Title, x + 28f, 14f, tone, 12f, ScopeAlign.Left, !pending);
                string detail = done ? "DONE" : warn ? StripWord(line) : current && ph.Progress > 0f ? ScopeStr.Pct((int)Math.Round(ph.Progress * 100f)) : current ? "NOW" : null;
                if (detail != null) g.Text(detail, x + w - 20f, 14f, warn ? Amber : Dim, 12f, ScopeAlign.Right, warn);
            }
        }

        private static string StripWord(ObjectiveLine line) =>
            line == ObjectiveLine.NoSightline ? "WAIT SIGHTLINE" : line == ObjectiveLine.TooLow ? "TOO LOW" : line == ObjectiveLine.NoTrack ? "NO TRACK" : "NO CONTACT";

        /// <summary>A pennant: the outline (a notch at the right), filled by state, and for the current phase the share done filled from the left.</summary>
        private void Pennant(ScopeCanvas g, float x, float w, ScopeTone tone, bool pending, bool done, float prog)
        {
            pennant[0] = x; pennant[1] = 3f; pennant[2] = x + w - 10f; pennant[3] = 3f; pennant[4] = x + w - 2f; pennant[5] = 13f; pennant[6] = x + w - 10f; pennant[7] = 23f; pennant[8] = x; pennant[9] = 23f;
            int n = g.Poly(pennant, 5);
            if (!pending) g.FillPoly(n, tone, done ? 0.18f : 0.08f);
            g.Path(n, tone, pending ? 0.55f : 1f);
            if (prog <= 0f) return;
            int m = ScreenClip.Half(pennant, 5, clipA, 1, x + (w - 2f) * prog);
            if (m < 3) return;
            g.Poly(clipA, m);
            g.FillPoly(m, Hi, 0.3f);
        }
    }
}
