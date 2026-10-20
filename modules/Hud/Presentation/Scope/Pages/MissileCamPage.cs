using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// MISSILE CAM (prototype: <c>pages/missilecam.js</c>): what the seeker of the missile the camera rides knows. There is no second camera, so the view is a schematic: the
    /// horizon and a ground grid tilted with the missile, a tunnel of chevron gates from the boresight reticle to the target and a box on the target that grows as the range
    /// falls (dashed on the data-link, snapping solid at the lock, dashed amber on the last known place while the seeker is blind or searching). Around it: the phase chain
    /// (BOOST, MID, ACTIVE, TERM), the range-to-go tape (the target at the top, hatched = still to fly, ACT and TERM marks), the seeker block (the target against the cone) and,
    /// under the view, the side profile of the flight since launch. When the ride ends the view turns to static for a moment with the result of the shot.
    /// Fed by <see cref="MissileCamScope"/>; the away, pitbull and result notices are the SHOT page's.
    /// </summary>
    internal sealed class MissileCamPage : ScopePageBase
    {
        private const float FX = 4f, FY = 32f, FW = 432f, FH = 206f, CX = FX + FW / 2f, CY = FY + FH / 2f + 4f;        // the view and its boresight
        private const float PX = 4f, PY = 244f, PW = 432f, PH = 52f;                                                    // the profile strip
        private const float TX = 392f, TW = 10f, TapeTop = 54f, TapeBottom = 204f, TapeLeft = 338f;                     // the range tape and its block's left edge
        private const float FlashS = 0.35f;
        private static readonly string[] Chain = { "BOOST", "MID", "ACTIVE", "TERM" };
        private const int Holes = 4;

        private MissileCamScope d;
        private bool live, primed, caution;
        private AvUnits units;
        private float now, lockAt = float.NegativeInfinity;
        private CamMode lastMode;
        private readonly float[] quad = new float[8], rangeTick = new float[1];
        /// <summary>
        /// Where words sit on the view (x0, y0, x1, y1 each; the chain's chips, the header's words, the seeker block, the tape block): the ground picture, the gates and the static are cut round them, because the plates that used to
        /// hide a line behind a word are gone (LAYOUT.md 9). The chips' and the words' are measured by <see cref="Header"/> every paint, the others are fixed. <see cref="cut"/> is the scratch of <see cref="Cut"/>.
        /// </summary>
        private readonly float[] hole = new float[Holes * 4] { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 6f, 154f, 182f, 236f, TapeLeft, 36f, FX + FW, 236f }, cut = new float[8];
        private int ttiKey = int.MinValue, rangeKey = int.MinValue, nextKey = int.MinValue, burnKey = int.MinValue;
        private string modeWord = "", ttiText = "", ttiSub = "", rangeText = "", nextLabel = "", burnSub;

        public override ScopePage Id => ScopePage.MissileCam;

        public override string Title => "MISSILE CAM";

        public override char Code => 'M';

        public override string IdleWord => "NO MISSILE";

        public override string IdleHint => "FIRE ONE TO FOLLOW ITS SEEKER";

        public override bool Fast => live;

        // ------------------------------------------------------------------------------------------------ poll

        public override bool Poll(in ScopeEnv env)
        {
            now = env.Now;
            units = env.Units;
            if (!ScopeFeeds.TryRead(out d)) { live = primed = false; lockAt = float.NegativeInfinity; return false; }
            live = true;
            if (primed && d.Mode == CamMode.Locked && lastMode != CamMode.Locked) lockAt = now;      // the box snaps onto a fresh lock
            lastMode = d.Mode;
            primed = true;
            ReadWords();
            return true;
        }

        /// <summary>The words of the headline and the gauges, rebuilt only when their figure changes.</summary>
        private void ReadWords()
        {
            bool had = d.HasTarget || d.HasLast;
            caution = !d.Lost && MissileCamMath.Caution(d.Mode, had);
            modeWord = d.Lost ? "LOST" : MissileCamMath.ModeWord(d.Mode, had);
            int tti = Seconds(d.TtiS);
            if (tti != ttiKey)
            {
                ttiKey = tti;
                ttiText = tti < 0 ? "--" : ScopeStr.Cat("", tti, " S").ToString();
                ttiSub = tti < 0 ? "TTI --" : ScopeStr.Cat("TTI ", tti, "S").ToString();
            }
            int range = float.IsNaN(d.RangeM) ? -1 : (int)(d.RangeM / 100f) * 2 + (units == AvUnits.Imperial ? 1 : 0);
            if (range != rangeKey) { rangeKey = range; rangeText = range < 0 ? "--" : ScopeStr.Cat("", d.RangeM / UnitM, 1, units == AvUnits.Imperial ? "NM" : "KM").ToString(); }
        }

        /// <summary>The player's distance unit in metres (a km or a nautical mile).</summary>
        private float UnitM => units == AvUnits.Imperial ? 1852f : 1000f;

        /// <summary>Whole seconds to go, rounded up; -1 when the game gives no time.</summary>
        private static int Seconds(float s) => float.IsNaN(s) || float.IsInfinity(s) ? -1 : (int)Math.Ceiling(Math.Max(0f, s));

        private static ScopeTone ResultTone(ShotState result) => result == ShotState.Hit ? Hi : result == ShotState.Defeated ? Amber : Dim;

        public override ScopeHeadline Headline()
        {
            if (!live) return default;
            if (d.Lost) return new ScopeHeadline { Text = MissileCamMath.ResultWord(d.Result), Tone = ResultTone(d.Result) };
            if (caution) return new ScopeHeadline { Text = modeWord, Tone = Amber, Flash = true };
            return new ScopeHeadline { Text = ttiKey < 0 ? modeWord : ttiText, Tone = Hi };
        }

        /// <summary>The seeker (how well it holds the target), the range to go against the tape, the missile the view steps to next and the motor.</summary>
        public override int Gauges(GaugeSpec[] into)
        {
            if (!live) return 0;
            int n = 0;
            bool missile = MissileCamMath.ChainIndex(d.Phase) >= 0, term = missile && d.TapeTopM > ShotMath.TerminalRangeM;
            float strength = d.Lost ? 0f : MissileCamMath.Strength(d.Mode, MissileCamMath.OffBoresight(d.ErrXDeg, d.ErrYDeg), d.ConeDeg);
            into[n++] = new GaugeSpec { Id = "seek", Label = "SEEKER", Kind = GaugeKind.Arc, Value = strength, Text = modeWord, Sub = d.Seeker, Tone = caution ? Amber : Ink, Flash = caution };
            rangeTick[0] = term ? ShotMath.TerminalRangeM / d.TapeTopM : 0f;
            into[n++] = new GaugeSpec
            {
                Id = "rng", Label = "RANGE", Kind = GaugeKind.Bar, Value = MissileCamMath.TapeFrac(d.RangeM, d.TapeTopM), Text = rangeText,
                Sub = d.Lost ? MissileCamMath.ResultWord(d.Result) : ttiSub, Tone = Ink, Ticks = rangeTick, TickCount = term ? 1 : 0, TickTone = Dim,
            };
            if (d.Next != null && d.Count > 1)
            {
                int next = (d.Index + 1) % d.Count + 1;
                if (next != nextKey) { nextKey = next; nextLabel = "MSL " + ScopeStr.Int(next); }
                into[n++] = new GaugeSpec { Id = "next", Label = nextLabel, Kind = GaugeKind.Ring, Value = 1f, Inner = ScopeStr.Int(next), Text = d.Next, Sub = "NEXT", Tone = Ink };
            }
            if (missile)
            {
                bool burning = d.BurnLeftS > 0f && d.BurnTotalS > 0f;
                int left = burning ? (int)Math.Ceiling(d.BurnLeftS) : d.BurnTotalS > 0f ? -1 : -2;
                if (left != burnKey) { burnKey = left; burnSub = burning ? ScopeStr.Cat("", left, "S").ToString() : left == -1 ? "BURNT" : null; }
                into[n++] = new GaugeSpec { Id = "motor", Label = "MOTOR", Kind = GaugeKind.Bar, Value = burning ? d.BurnLeftS / d.BurnTotalS : 0f, Text = burning ? "BURN" : "COAST", Sub = burnSub, Tone = burning ? Ink : Dim };
            }
            return n;
        }

        // ------------------------------------------------------------------------------------------------ paint

        public override void Paint(ScopeCanvas g, in PagePaint p)
        {
            if (!ScopeFeeds.TryRead(out d)) return;      // the gates and the horizon move every frame: the page is Fast
            live = true;
            now = p.Now;
            units = p.Units;
            bool had = d.HasTarget || d.HasLast, warn = MissileCamMath.Caution(d.Mode, had), gone = d.Lost && d.LostS >= FlashS;
            Header(g);      // first: it measures the words the picture is cut round
            if (gone) Static(g);
            else View(g);
            g.Box(FX, FY, FW, FH, Dim, 0.9f);
            g.Brackets(FX + 1f, FY + 1f, FW - 2f, FH - 2f, 8f, Ink);
            if (!d.Lost)
            {
                // the target's place in the view: its angles off the boresight, kept with its box inside the frame and left of the tape
                float half = MissileCamMath.BoxHalf(d.RangeM);
                float tx = Mathf.Clamp(CX + d.ErrXDeg * MissileCamMath.PxPerDeg, FX + half + 2f, TapeLeft - half - 2f);
                float ty = Mathf.Clamp(CY + d.ErrYDeg * MissileCamMath.PxPerDeg, FY + half + 2f, FY + FH - half - 2f);
                if (had) Tunnel(g, tx, ty, warn);
                Reticle(g);
                if (had) TargetBox(g, tx, ty, half, warn);
                Seeker(g, had, warn);
            }
            Tape(g);
            Profile(g);
            if (d.Lost && !gone) g.Fill(FX, FY, FW, FH, Hi, 1f - d.LostS / FlashS);      // the flash of the impact
        }

        /// <summary>The horizon where the missile's pitch and roll put it and a ground grid running toward it (it scrolls with the closure), cut at the frame.</summary>
        private void View(ScopeCanvas g)
        {
            g.Fill(FX, FY, FW, FH, Bg, 0.5f);
            float a = -d.RollDeg * Mathf.Deg2Rad, ca = Mathf.Cos(a), sa = Mathf.Sin(a), yh = Mathf.Clamp(d.PitchDeg * MissileCamMath.PxPerDeg, -300f, 300f);
            float ph = float.IsNaN(d.ClosureMps) ? 0f : (now * 0.55f * d.ClosureMps / 1000f % 1f + 1f) % 1f;
            for (int i = 0; i < 11; i++)
            {
                float f = (i + ph) / 11f, y = yh + 330f * f * f;
                Ground(g, -500f, y, 500f, y, ca, sa, Faint, 0.25f + 0.5f * f);
            }
            for (int j = -12; j <= 12; j++) Ground(g, j * 11f, yh + 40f, j * 90f, yh + 330f, ca, sa, Faint, 0.4f);      // the fan starts clear of its vanishing point: no knot of lines there
            Ground(g, -500f, yh, 500f, yh, ca, sa, Dim, 0.7f);
        }

        /// <summary>A line of the ground picture: given about the boresight in the missile's own frame, turned by its roll and cut at the view's frame.</summary>
        private void Ground(ScopeCanvas g, float x0, float y0, float x1, float y1, float ca, float sa, ScopeTone tone, float alpha)
        {
            float ax = CX + x0 * ca - y0 * sa, ay = CY + x0 * sa + y0 * ca, bx = CX + x1 * ca - y1 * sa, by = CY + x1 * sa + y1 * ca;
            if (MissileCamMath.ClipLine(ref ax, ref ay, ref bx, ref by, FX + 1f, FY + 1f, FX + FW - 1f, FY + FH - 1f)) Cut(g, ax, ay, bx, by, tone, alpha, 1f, 0);
        }

        /// <summary>A line of the picture, cut round the words (<see cref="hole"/>, from the hole <paramref name="from"/> on): a piece that a hole splits goes on to the holes after it.</summary>
        private void Cut(ScopeCanvas g, float x0, float y0, float x1, float y1, ScopeTone tone, float alpha, float w, int from)
        {
            for (int k = from; k < Holes; k++)
            {
                int n = MissileCamMath.ClipOut(x0, y0, x1, y1, hole[k * 4], hole[k * 4 + 1], hole[k * 4 + 2], hole[k * 4 + 3], cut);
                if (n < 0) continue;
                float a0 = cut[0], a1 = cut[1], a2 = cut[2], a3 = cut[3], b0 = cut[4], b1 = cut[5], b2 = cut[6], b3 = cut[7];      // the scratch is shared with the recursion
                if (n > 0) Cut(g, a0, a1, a2, a3, tone, alpha, w, k + 1);
                if (n > 1) Cut(g, b0, b1, b2, b3, tone, alpha, w, k + 1);
                return;
            }
            g.Line(x0, y0, x1, y1, tone, alpha, w);
        }

        /// <summary>No hole touches the rectangle.</summary>
        private bool Clear(float x, float y, float w, float h)
        {
            for (int k = 0; k < Holes; k++)
                if (x < hole[k * 4 + 2] && x + w > hole[k * 4] && y < hole[k * 4 + 3] && y + h > hole[k * 4 + 1]) return false;
            return true;
        }

        /// <summary>The path tunnel: eight chevron gates from under the reticle to the target, running toward it (faster in the terminal phase).</summary>
        private void Tunnel(ScopeCanvas g, float tx, float ty, bool warn)
        {
            float ph = 1f - now * (d.Phase == ShotPhase.Term ? 1.7f : 1f) * 0.8f % 1f, c0 = CY + 6f;
            float qx = CX + (tx - CX) * 0.08f, qy = c0 + (ty - c0) * 0.85f;
            for (int k = 0; k < 8; k++)
            {
                float s = ScopeCanvas.Clamp01(0.1f + 0.9f * Mathf.Pow((k + ph) / 8f, 1.5f));
                Curve(s, qx, qy, tx, ty, out float x, out float y);
                Curve(s + 0.03f, qx, qy, tx, ty, out float nx, out float ny);
                float ang = Mathf.Atan2(nx - x, -(ny - y)) * 0.5f, ca = Mathf.Cos(ang), sa = Mathf.Sin(ang), a = 7f + 58f * Mathf.Pow(1f - s, 1.3f);
                float alpha = Mathf.Clamp(1.05f - s * 0.7f, 0.2f, 0.95f) * ScopeCanvas.Clamp01(s * 6f) * (warn ? 0.75f : 1f), w = s < 0.45f ? 2f : 1f;
                ScopeTone tone = s < 0.35f ? Hi : Ink;
                // an open gate (-a, .55a) - (0, -.45a) - (a, .55a), turned half way toward the path
                float px = x + a * 0.45f * sa, py = y - a * 0.45f * ca;
                Cut(g, x - a * ca - a * 0.55f * sa, y - a * sa + a * 0.55f * ca, px, py, tone, alpha, w, 0);
                Cut(g, px, py, x + a * ca - a * 0.55f * sa, y + a * sa + a * 0.55f * ca, tone, alpha, w, 0);
            }
        }

        /// <summary>A point of the tunnel's curve: from under the reticle (s = 0, dropped toward the viewer) to the target (s = 1).</summary>
        private static void Curve(float s, float qx, float qy, float tx, float ty, out float x, out float y)
        {
            float q = 1f - s;
            x = q * q * CX + 2f * q * s * qx + s * s * tx;
            y = q * q * (CY + 6f) + 2f * q * s * qy + s * s * ty + 44f * Mathf.Pow(Math.Max(0f, q), 1.7f);
        }

        private static void Reticle(ScopeCanvas g)
        {
            g.Ring(CX, CY, 3f, Hi, 0.9f);
            g.Line(CX - 12f, CY, CX - 4f, CY, Hi, 0.8f); g.Line(CX + 4f, CY, CX + 12f, CY, Hi, 0.8f);
            g.Line(CX, CY - 12f, CX, CY - 4f, Hi, 0.8f); g.Line(CX, CY + 4f, CX, CY + 12f, Hi, 0.8f);
        }

        /// <summary>The target box: corner brackets (dashed until the seeker locks, amber on the last known place), the target's name above and the seeker's word beside it (LAST KNOWN once the target itself is gone): the converging gates run below the box.</summary>
        private void TargetBox(ScopeCanvas g, float tx, float ty, float half, bool warn)
        {
            bool locked = d.Mode == CamMode.Locked;
            ScopeTone tone = warn ? Amber : locked ? Hi : Ink;
            float u = (now - lockAt) / 0.5f, c = locked && u >= 0f && u < 1f ? 1f + 1.4f * (1f - ScopeCanvas.EaseOut(u)) : 1f, h2 = half * c;      // the lock-on snap: wide to snug
            float x = Mathf.Round(tx - h2), y = Mathf.Round(ty - h2), side = Mathf.Round(h2 * 2f), len = Math.Max(6f, Mathf.Round(h2 * 0.35f));
            float alpha = warn && ScopeCanvas.Blink(now, 3f, 0.7f) ? 0.45f : 1f, lw = locked ? 2f : 1f, dash = locked ? 0f : 3f;
            for (int i = 0; i < 4; i++)
            {
                float ex = i % 2 == 0 ? x : x + side, ey = i < 2 ? y : y + side;
                g.Line(ex, ey, ex + (i % 2 == 0 ? len : -len), ey, tone, alpha, lw, dash, dash);
                g.Line(ex, ey, ex, ey + (i < 2 ? len : -len), tone, alpha, lw, dash, dash);
            }
            if (c > 1.01f) g.Ring(tx, ty, h2 * 1.25f, Hi, (1f - u) * 0.7f);
            if (warn) g.Text("?", tx, ty + 1f, Amber, 16f, ScopeAlign.Center, true);
            // the name above, kept whole between the frame and the tape; the word beside the box, on its right while it fits clear of the tape, else on its left (LAYOUT.md 12.7)
            string word = d.HasTarget ? MissileCamMath.ModeWord(d.Mode, true) : "LAST KNOWN";
            float nameHalf = ScopeCanvas.TextW(d.Target) / 2f + 5f;
            if (!string.IsNullOrEmpty(d.Target))
                g.PlateTag(d.Target, Mathf.Clamp(tx, FX + 2f + nameHalf, TapeLeft - 2f - nameHalf), Mathf.Clamp(ty - h2 - 12f, FY + 22f, FY + FH - 40f), warn ? Amber : Ink, 12f, ScopeAlign.Center);
            bool right = tx + h2 + 8f + (float)Math.Ceiling(ScopeCanvas.TextW(word)) <= FX + FW - 100f;
            g.Text(word, right ? tx + h2 + 8f : tx - h2 - 8f, Mathf.Clamp(ty, FY + 30f, FY + FH - 30f), warn ? Amber : locked ? Hi : Dim, 12f, right ? ScopeAlign.Left : ScopeAlign.Right, d.Mode != CamMode.DataLink);
        }

        /// <summary>The seeker block: the target against the seeker's cone (50 degrees at the rim), the mode, the angle off the boresight and the seeker's class.</summary>
        private void Seeker(ScopeCanvas g, bool had, bool warn)
        {
            const float gx = 46f, gy = 196f, r = 34f, k = r / 50f;
            bool locked = d.Mode == CamMode.Locked, dim = warn && ScopeCanvas.Blink(now, 3f, 0.7f);
            ScopeTone tone = warn ? Amber : locked ? Hi : Dim;
            g.Fill(8f, 156f, 172f, 78f, Bg, 0.55f);
            g.Ring(gx, gy, r, Faint);
            g.Line(gx - r, gy, gx + r, gy, Faint); g.Line(gx, gy - r, gx, gy + r, Faint);
            if (d.ConeDeg > 0f) g.Ring(gx, gy, Math.Min(r, d.ConeDeg * k), tone, 0.95f, 1f, Ink, locked ? 0.1f : 0f, locked ? 0f : 3f, 3f);
            g.Ring(gx, gy, 2f, Dim, 1f, 1f, Dim, 1f);
            float off = MissileCamMath.OffBoresight(d.ErrXDeg, d.ErrYDeg);
            if (had)
            {
                float m = Math.Min(off * k, r - 3f) / Math.Max(off, 0.001f), x = gx + d.ErrXDeg * m, y = gy + d.ErrYDeg * m;
                g.Line(gx, gy, x, y, tone, 0.5f);
                g.Diamond(x, y, 4f, tone, locked, dim ? 0.4f : 1f);
            }
            g.Text("SEEKER", 86f, 168f, Dim);
            g.Text(MissileCamMath.ModeWord(d.Mode, had), 86f, 186f, warn ? Amber : d.Mode == CamMode.DataLink ? Ink : Hi, 16f, ScopeAlign.Left, true, dim ? 0.55f : 1f);
            if (d.Mode == CamMode.DataLink) g.Text("DATA-LINK", 86f, 204f, Dim);
            else if (had && d.ConeDeg > 0f) g.Text(ScopeStr.Cat("OFF ", (int)Math.Round(off), "° / ", (int)Math.Round(d.ConeDeg), "°"), 86f, 204f, warn ? Amber : Dim);
            else if (had) g.Text(ScopeStr.Cat("OFF ", (int)Math.Round(off), "°"), 86f, 204f, warn ? Amber : Dim);
            g.Text(d.Seeker, 86f, 220f, Dim);
        }

        /// <summary>The phase chain (a bomb has none) over the missile's place in the follow list, its name, its target and its heading.</summary>
        private void Header(ScopeCanvas g)
        {
            int cur = MissileCamMath.ChainIndex(d.Phase);
            if (cur >= 0 && d.Lost) cur = 4;      // the ride is over: every phase is behind it
            float x = 8f;
            hole[0] = hole[1] = hole[2] = hole[3] = 0f;
            for (int i = 0; i < 4 && cur >= 0; i++)
            {
                bool on = i == cur;
                float w = Mathf.Round(ScopeCanvas.TextW(Chain[i]) + 10f);
                ScopeTone tone = on ? Hi : i < cur ? Dim : Faint;
                g.Fill(x, 36f, w, 17f, Bg, 0.7f);
                g.Box(x, 36f, w, 17f, tone, on ? 1f : 0.9f, 1f, Ink, on ? 0.28f : 0f);
                g.Text(Chain[i], x + w / 2f, 45f, tone, 12f, ScopeAlign.Center, on);
                x += w + 3f;
                hole[0] = 6f; hole[1] = 34f; hole[2] = x - 1f; hole[3] = 55f;
            }
            float w1 = g.Text(ScopeStr.Cat("MSL ", d.Index + 1, "/", d.Count, null), 8f, 66f, Hi, 12f, ScopeAlign.Left, true) + 16f;
            w1 += g.FitText(d.Full, 8f + w1, 66f, 322f - w1, Hi, 12f, 12f, ScopeAlign.Left, true);
            float w2 = string.IsNullOrEmpty(d.Target) ? 0f : g.Text(ScopeStr.Cat("TGT ", d.Target), 8f, 82f, Dim) + 16f;
            int hdg = ((int)Math.Round(d.HeadingDeg) % 360 + 360) % 360;
            w2 += g.Text(ScopeStr.Cat("HDG ", hdg < 10 ? "00" : hdg < 100 ? "0" : null, hdg, null), 8f + w2, 82f, Dim);
            hole[4] = 4f; hole[5] = 56f; hole[6] = 5f + Mathf.Round(Math.Max(w1, w2)) + 7f; hole[7] = 92f;      // the horizon and the static do not run through these words
        }

        /// <summary>The range-to-go tape: the target at the top, the missile's marker rising toward it; hatched = still to fly; ACT (the seeker goes active) and TERM (the last 2.5 km) marks.</summary>
        private void Tape(ScopeCanvas g)
        {
            bool imperial = units == AvUnits.Imperial;
            float unitM = UnitM, top = Math.Max(d.TapeTopM, 1f), span = TapeBottom - TapeTop, ym = TapeTop + MissileCamMath.TapeFrac(d.RangeM, d.TapeTopM) * span;
            g.Fill(TapeLeft, 36f, FX + FW - TapeLeft, 200f, Bg, 0.55f);
            g.Text(imperial ? "RNG NM" : "RNG KM", TX + TW / 2f, 44f, Dim, 12f, ScopeAlign.Center);
            g.Box(TX, TapeTop, TW, span, Dim, 0.9f);
            g.Fill(TX + 2f, Mathf.Round(ym), TW - 4f, Mathf.Round(TapeBottom - ym - 1f), Faint, 0.8f);
            if (ym - TapeTop > 3f)
            {
                quad[0] = TX + 2f; quad[1] = TapeTop + 2f; quad[2] = TX + TW - 2f; quad[3] = TapeTop + 2f; quad[4] = TX + TW - 2f; quad[5] = ym; quad[6] = TX + 2f; quad[7] = ym;
                int n = g.Poly(quad, 4);
                g.FillPoly(n, Ink, 0.22f);
                g.Hatch(n, 4f, Ink, 0.7f);
            }
            float step = MissileCamMath.TapeStep(top, unitM);
            for (int k = 0; k * step <= top; k++)
            {
                float y = Mathf.Round(TapeTop + k * step / top * span);
                g.Line(TX - (k % 5 == 0 ? 7f : 3f), y, TX, y, Dim, 0.9f);
            }
            float act = float.NaN;
            if (d.ActiveAtM <= top) Mark(g, "ACT", act = Mathf.Round(TapeTop + d.ActiveAtM / top * span), d.ActivePassed);
            if (MissileCamMath.ChainIndex(d.Phase) >= 0 && top > ShotMath.TerminalRangeM)
            {
                float y = Mathf.Round(TapeTop + ShotMath.TerminalRangeM / top * span);
                if (!(Math.Abs(y - act) < 13f)) Mark(g, "TERM", y, d.Phase == ShotPhase.Term);       // two marks that close: ACT keeps its word
            }
            g.Diamond(TX + TW / 2f, TapeTop - 1f, 5f, Hi);
            g.Tri(TX - 3f, ym, 90f, 5f, Hi);
            g.PlateTag(ScopeStr.Cat("", d.RangeM / unitM, 1, null), TX - 11f, Mathf.Clamp(ym, TapeTop + 4f, TapeBottom - 4f), Hi, 12f, ScopeAlign.Right);
            if (float.IsNaN(d.ClosureMps)) g.Text("CLS --", 432f, 214f, Dim, 12f, ScopeAlign.Right);
            else g.Text(ScopeStr.Cat("CLS ", (int)Math.Round(AvUnitTable.Speed(d.ClosureMps, units)), ScopeStr.SpeedUnit(units)), 432f, 214f, Dim, 12f, ScopeAlign.Right);
            g.Text("TTI", 346f, 229f, Dim);
            int tti = Seconds(d.TtiS);
            g.Text(tti < 0 ? "--" : ScopeStr.Cat("", tti, " S"), 432f, 228f, Hi, 16f, ScopeAlign.Right, true);
        }

        private static void Mark(ScopeCanvas g, string word, float y, bool passed)
        {
            g.Line(TX - 2f, y, TX + TW + 4f, y, passed ? Hi : Ink, passed ? 1f : 0.8f);
            g.Text(word, TX + TW + 7f, y, passed ? Hi : Dim, 12f, ScopeAlign.Left, passed);
        }

        /// <summary>The side profile: ground distance from the launch across, altitude up (the strip is as tall as the highest of the path, the target and 1000 m); the flown path solid, the way to the target dashed.</summary>
        private void Profile(ScopeCanvas g)
        {
            const float xL = 20f, xT = PX + PW - 30f, gy = PY + PH - 5f;
            bool imperial = units == AvUnits.Imperial;
            g.Box(PX, PY, PW, PH, Faint, 0.9f, 1f, Bg, 0.6f);
            quad[0] = PX + 3f; quad[1] = gy; quad[2] = PX + PW - 3f; quad[3] = gy; quad[4] = PX + PW - 3f; quad[5] = gy + 3f; quad[6] = PX + 3f; quad[7] = gy + 3f;
            int n = g.Poly(quad, 4);
            g.FillPoly(n, Faint, 0.5f);
            g.Hatch(n, 3f, Dim, 0.6f);
            g.Path(n, Dim, 0.8f);
            int at = MissileCamMath.Apex(d.PathA, d.PathCount, out float apex);
            float far = Math.Max(Math.Max(d.NowD, d.HasTargetPlace ? d.TargetD : 0f), 1f), high = Math.Max(Math.Max(apex, d.NowA), Math.Max(d.HasTargetPlace ? d.TargetA : 0f, 1000f));
            float kx = (xT - xL) / far, ky = 30f / high, mx = xL + d.NowD * kx, my = gy - Math.Max(0f, d.NowA) * ky, lx = mx, ly = my, hdg = 90f;
            for (int i = d.PathCount - 1; i >= 0; i--)      // newest first, so the marker's heading is the last stretch of the path that has any length
            {
                float x = xL + d.PathD[i] * kx, y = gy - Math.Max(0f, d.PathA[i]) * ky;
                g.Line(x, y, lx, ly, Ink, 0.95f, 2f);
                if (hdg == 90f && (mx - x) * (mx - x) + (my - y) * (my - y) >= 9f) hdg = Mathf.Atan2(mx - x, y - my) * Mathf.Rad2Deg;
                lx = x; ly = y;
            }
            float ex = xL + d.TargetD * kx, ey = gy - Math.Max(0f, d.TargetA) * ky;
            if (d.HasTargetPlace && !d.Lost) g.Line(mx, my, ex, ey, Dim, 0.95f, 1f, 4f, 3f, -now * 8f);
            if (at >= 0)
            {
                float ax = xL + d.PathD[at] * kx, ay = gy - apex * ky;
                g.Line(ax, ay - 3f, ax, ay + 3f, Dim);
                g.Text(ScopeStr.Cat("APX ", apex / (imperial ? 304.8f : 1000f), 1, imperial ? "KFT" : "KM"), Mathf.Clamp(ax, PX + 110f, PX + PW - 110f), PY + 9f, Dim, 12f, ScopeAlign.Center);
            }
            g.Diamond(lx, ly, 3.5f, Dim);
            g.Text("LAUNCH", PX + 6f, PY + 9f, Dim);
            if (d.HasTargetPlace)
            {
                g.Diamond(ex, ey, 4f, d.Lost ? Dim : Hi);
                g.FitText(d.Target, PX + PW - 6f, PY + 9f, 96f, Dim, 12f, 12f, ScopeAlign.Right);
            }
            if (d.Lost) g.Cross(mx, my, 10f, Hi, 1f, 2f);
            else
            {
                g.Chevron(mx, my, hdg, 6f, Hi, true);
                g.Ring(mx, my, 9f + 2f * ScopeCanvas.Pulse(now, 2f), Hi, 0.4f);
            }
        }

        /// <summary>The ride is over: static where the view was, the missile and how the shot ended.</summary>
        private void Static(ScopeCanvas g)
        {
            g.Fill(FX, FY, FW, FH, Bg, 0.9f);
            int frame = (int)(now * 24f);
            for (float y = FY; y < FY + FH; y += 4f)
                for (float x = FX; x < FX + FW; x += 4f)
                {
                    float v = ScopeCanvas.Hash((int)(x * 7f + y * 13f) + frame * 1013);
                    if (v > 0.55f && Clear(x, y, 4f, Math.Min(4f, FY + FH - y))) g.Fill(x, y, 4f, Math.Min(4f, FY + FH - y), Ink, (v - 0.55f) * 0.6f);
                }
            float bar = FY + now * 90f % FH;
            g.Fill(FX, bar, FW, Math.Min(14f, FY + FH - bar), Ink, 0.12f);
            g.Text("FEED LOST", CX, CY - 30f, Hi, 16f, ScopeAlign.Center, true);
            g.Text(ScopeStr.Cat(d.Full, " - MSL ", d.Index + 1, null), CX, CY - 10f, Ink, 12f, ScopeAlign.Center);
            string word = MissileCamMath.ResultWord(d.Result);
            g.Stamp(string.IsNullOrEmpty(d.Target) ? word : ScopeStr.Cat(word, "  ", d.Target), CX, CY + 26f, ResultTone(d.Result), 16f);
        }
    }
}
