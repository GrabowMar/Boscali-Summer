using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// DOCK (prototype: <c>pages/dock.js</c>): the SKYWELL tanker hook-up from the receiver's seat. One dominant shape, a rear-view bullseye in tanker axes at a fixed
    /// 3.2 px per metre: the contact point is the centre, your probe is the dot, and the FUNNEL ring (the capture cone, 0.12 x range, 6 to 90 m) tightens around the centre as
    /// you close, so the error that was fine at 200 m is a miss at 60 m. A faint rear silhouette of the glider grows as 1 / range. Director arrows sit outside the rings. Right:
    /// range, time to contact, the gate ribbon, the closure bar against the hook-up's limit and the prompt panel (directors, HOLD BRAKE, TRANSFERRING, SERVED). In contact the
    /// bullseye becomes the transfer instrument: fuel on the outer ring, the rounds as segments, the flow drawn in from the rim. The strip carries the receiver's REAL fuel and
    /// ammunition. Feed: <see cref="DockScope"/> (Vanguard's provider over SkywellBoard and the two aircraft). The game moves the fuel and rounds in one step at the end of the
    /// transfer, so the bars hold the real value, a ghost marks what is coming and a time bar shows the transfer; they jump when it lands. Repaints every frame while live.
    /// </summary>
    internal sealed class DockPage : ScopePageBase
    {
        private const float Cx = 132f, Cy = 150f, R = DockMath.BullseyeR, K = DockMath.PxPerM, X0 = 252f, W = 180f;
        /// <summary>"Long ago" for an animation clock (a finite value: an infinity would make the ripple maths NaN).</summary>
        private const float Never = -1000f;
        private static readonly string[] Words = { "CUE", "CAPTURE", "ENGAGE", "TRANSFER", "SERVED" };
        // The glider seen from astern, half-span 1, y DOWN: the wing with its dihedral, the hexagonal fuselage, the twin fins (the left ones; the right are mirrors).
        private static readonly float[] WingL = { -1f, 0.1f, -0.2f, -0.02f, -0.2f, 0.1f, -0.96f, 0.2f };
        private static readonly float[] Fus = { 0f, -0.2f, 0.12f, -0.09f, 0.12f, 0.1f, 0f, 0.2f, -0.12f, 0.1f, -0.12f, -0.09f };
        private static readonly float[] FinL = { -0.5f, 0.05f, -0.46f, -0.32f, -0.4f, 0.05f };

        private DockScope d;
        private bool live, primed;
        private AvUnits units;
        private float now, lastSample = Never;
        private DockPhase lastPhase;
        private readonly float[] enterAt = new float[5];
        private float ammoAt = Never;
        private int lastAmmo, ammoFrom;
        private bool lastFast, lastEdge;
        private readonly float[] hx = new float[6], hy = new float[6], trail = new float[12];
        private readonly float[] circle = new float[48], shape = new float[24], clipped = new float[64], tmp = new float[64];

        private string headlineText = "";
        private int headlineKey = -1;
        private ScopeTone headlineTone = Hi;
        private bool headlineFlash;
        private int gaugeKey = int.MinValue;
        private AvUnits gaugeUnits = (AvUnits)(-1);
        private string skyText = "", skySub = "", needText = "", movedText = "";
        private int needKey = int.MinValue, movedKey = int.MinValue;

        public DockPage()
        {
            for (int i = 0; i < 24; i++)
            {
                double a = i * Math.PI / 12.0;
                circle[2 * i] = Cx + (R + 14f) * (float)Math.Cos(a);
                circle[2 * i + 1] = Cy + (R + 14f) * (float)Math.Sin(a);
            }
        }

        public override ScopePage Id => ScopePage.Dock;

        public override string Title => "DOCK";

        public override char Code => 'D';

        public override bool Fast => live;

        public override bool HasStrip => live;

        // ------------------------------------------------------------------------------------------------ poll

        private bool TooFast => d.Phase < DockPhase.Transfer && DockMath.TooFast(d.ClosureMps, d.MaxClosingMps, d.RangeM) && d.RangeM <= DockMath.RibbonM;

        private float Cone => d.ServiceZone ? d.CaptureM : DockMath.ConeM(d.RangeM);

        private float Err => DockMath.ErrM(d.RelX, d.RelY);

        private bool Near => d.RangeM <= DockMath.RibbonM;

        /// <summary>Outside the funnel (a caution only once the tanker is close: at the cue range nobody is lined up yet).</summary>
        private bool Bad => d.ServiceZone ? !d.ZoneReady && Near : Near && d.RangeM > 0f && !DockMath.Inside(Err, Cone);

        private bool Edge => Near && DockMath.NearEdge(Err, Cone, d.RangeM);

        public override bool Poll(in ScopeEnv env)
        {
            now = env.Now;
            units = env.Units;
            live = ScopeFeeds.TryRead(out d);
            if (!live) { primed = false; return false; }
            if (!primed)
            {
                for (int i = 0; i < enterAt.Length; i++) enterAt[i] = Never;
                lastPhase = d.Phase;
                lastAmmo = d.Ammo; ammoFrom = d.Ammo; ammoAt = Never;
                lastFast = lastEdge = false;
                for (int i = 0; i < 6; i++) { hx[i] = d.RelX; hy[i] = d.RelY; }
            }
            else Edges(env);
            if (d.Ammo != lastAmmo)
            {
                if (d.Ammo > lastAmmo) { ammoFrom = lastAmmo; ammoAt = now; }
                lastAmmo = d.Ammo;
            }
            primed = true;
            Words10();
            UpdateHeadline();
            return true;
        }

        /// <summary>Events once: a phase is entered, the closure goes over the limit, the probe drifts to the funnel's edge.</summary>
        private void Edges(in ScopeEnv env)
        {
            if (d.Phase != lastPhase)
            {
                enterAt[(int)d.Phase] = now;
                if (d.Phase == DockPhase.Engage && lastPhase < DockPhase.Engage) env.Notice?.Invoke("skywell", HudTone.Info, d.ServiceZone ? "SERVICE ZONE - HOLD STEADY" : "ENGAGED - HOLD BRAKE", null);
                lastPhase = d.Phase;
            }
            bool fast = TooFast;
            if (fast && !lastFast)
                env.Notice?.Invoke("skywell", HudTone.Caution, "CLOSURE " + (int)Math.Round(Closure(d.ClosureMps)) + " " + ClosureUnit().Trim() + " - AFT",
                    "MAX " + (int)Math.Round(Closure(d.MaxClosingMps)));
            lastFast = fast;
            bool edge = d.Phase < DockPhase.Engage && (Edge || Bad);
            if (edge && !lastEdge)
                env.Notice?.Invoke("skywell", HudTone.Caution, (d.ServiceZone ? "SERVICE ZONE - " : "NEAR FUNNEL EDGE - ") + (d.RelX < 0f ? "RIGHT " : "LEFT ") + Len(Math.Abs(d.RelX)) + LenUnit(), null);
            lastEdge = edge;
        }

        /// <summary>The kilograms still to come and the kilograms moved, as words ("FUEL +1840 KG", "+1840 KG  +4 RND"), rebuilt only when they change.</summary>
        private void Words10()
        {
            int need = (int)Math.Round(d.FuelNeedKg / 10f) + (units == AvUnits.Imperial ? 100000 : 0);
            if (need != needKey) { needKey = need; needText = "FUEL +" + Mass(d.FuelNeedKg); }
            int moved = ((int)Math.Round(d.MovedFuelKg / 10f) * 100 + d.MovedRounds) * 2 + (units == AvUnits.Imperial ? 1 : 0);
            if (moved != movedKey) { movedKey = moved; movedText = "+" + Mass(d.MovedFuelKg) + "  +" + d.MovedRounds + " RND"; }
        }

        private float Closure(float mps) => units == AvUnits.Imperial ? mps * 1.94384f : mps;

        private string ClosureUnit() => units == AvUnits.Imperial ? " KT" : " M/S";

        private int Len(float m) => (int)Math.Round(AvUnitTable.Altitude(m, units));

        private string LenUnit() => units == AvUnits.Imperial ? " FT" : " M";

        private void UpdateHeadline()
        {
            int key;
            string text = null;      // built only when the key changes (this runs at 10 Hz): a steady figure builds nothing
            ScopeTone tone = Hi;
            bool flash = false;
            if (TooFast) { key = 1; text = "TOO FAST"; tone = Amber; flash = true; }
            else if (d.Phase == DockPhase.Served) { key = 2; text = "SERVED"; }
            else if (d.Phase == DockPhase.Transfer) { int p = (int)Math.Round(d.Fuel * 100f); key = (d.ServiceZone ? 4000 : 3000) + p; text = d.ServiceZone ? d.JobName ?? "SERVICE" : key != headlineKey ? "FUEL " + p + "%" : null; }
            else if (d.Phase == DockPhase.Engage) { key = 4; text = d.ServiceZone ? "HOLD STEADY" : "HOLD BRAKE"; }
            else
            {
                int v = Len(d.RangeM);
                key = 10000 + v + (units == AvUnits.Imperial ? 5000 : 0);
                if (key != headlineKey) text = v + LenUnit();
                tone = Edge || Bad ? Amber : Hi;
            }
            if (key != headlineKey || (text != null && text != headlineText)) { headlineKey = key; headlineText = text ?? headlineText; }
            headlineTone = tone;
            headlineFlash = flash;
        }

        public override ScopeHeadline Headline() => live ? new ScopeHeadline { Text = headlineText, Tone = headlineTone, Flash = headlineFlash } : default;

        public override int Gauges(GaugeSpec[] into)
        {
            if (!live || !d.HasStock) return 0;
            int key = (int)(d.StockFuelKg / 10f) * 1000 + (d.ServiceZone ? d.StockRounds : (int)(d.StockMunKg / 10f));
            if (key != gaugeKey || units != gaugeUnits)
            {
                gaugeKey = key; gaugeUnits = units;
                skyText = Mass(d.StockFuelKg);
                skySub = d.ServiceZone ? "RDS " + d.StockRounds + "/6" : "MUN " + Mass(d.StockMunKg);
            }
            into[0] = new GaugeSpec { Id = "sky", Label = "SKYWELL", Kind = GaugeKind.Bar, Value = d.StockFuelMaxKg > 0f ? d.StockFuelKg / d.StockFuelMaxKg : 0f, Text = skyText, Sub = skySub, Tone = Ink };
            return 1;
        }

        private string Mass(float kg) => units == AvUnits.Imperial ? (int)(Math.Round(kg * 2.20462f / 100f) * 100f) + " LB" : (int)(Math.Round(kg / 10f) * 10f) + " KG";

        // ------------------------------------------------------------------------------------------------ paint

        public override void Paint(ScopeCanvas g, in PagePaint p)
        {
            if (!ScopeFeeds.TryRead(out d)) return;
            live = true;
            now = p.Now;
            units = p.Units;
            if (!primed) { for (int i = 0; i < 6; i++) { hx[i] = d.RelX; hy[i] = d.RelY; } }
            if (now - lastSample >= 0.12f || now < lastSample)
            {
                lastSample = now;
                for (int i = 0; i < 5; i++) { hx[i] = hx[i + 1]; hy[i] = hy[i + 1]; }
                hx[5] = d.RelX; hy[5] = d.RelY;
            }
            hx[5] = d.RelX; hy[5] = d.RelY;
            Bullseye(g, p.Now);
            RightColumn(g, p.Now);
        }

        /// <summary>A silhouette polygon (unit outline, y down) at scale <paramref name="s"/> around the bullseye, cut to its circle.</summary>
        private void Part(ScopeCanvas g, float[] unit, int n, bool mirror, float s, float alpha, float fillAlpha)
        {
            for (int i = 0; i < n; i++) { shape[2 * i] = Cx + (mirror ? -unit[2 * i] : unit[2 * i]) * s; shape[2 * i + 1] = Cy + unit[2 * i + 1] * s; }
            int m = ScreenClip.Convex(shape, n, circle, 24, clipped, tmp);
            if (m < 3) return;
            int path = g.Poly(clipped, m);
            g.FillPoly(path, Ink, fillAlpha);
            g.Path(path, Ink, alpha);
        }

        private void Silhouette(ScopeCanvas g, float s, float alpha)
        {
            Part(g, WingL, 4, false, s, alpha * 0.8f, 0.05f); Part(g, WingL, 4, true, s, alpha * 0.8f, 0.05f);
            Part(g, FinL, 3, false, s, alpha * 0.8f, 0.05f); Part(g, FinL, 3, true, s, alpha * 0.8f, 0.05f);
            Part(g, Fus, 6, false, s, alpha, 0.08f);
        }

        /// <summary>Inside the bullseye's reach (R + 5): the metres' place on the screen, pulled onto that circle when beyond it.</summary>
        private static void Clamp(float ex, float ey, out float px, out float py, out bool off)
        {
            px = Cx + ex * K; py = Cy - ey * K;
            float dx = px - Cx, dy = py - Cy, len = (float)Math.Sqrt(dx * dx + dy * dy), lim = R + 5f;
            off = len > lim;
            if (off) { px = Cx + dx * lim / len; py = Cy + dy * lim / len; }
        }

        private void Bullseye(ScopeCanvas g, float t)
        {
            int phase = (int)d.Phase;
            float r = d.RangeM, cone = Cone, err = Err;
            bool bad = Bad, edge = Edge, pre = phase < 3;
            float hs = DockMath.SilhouetteScale(r), fade = DockMath.SilhouetteFade(hs);
            if (fade > 0.02f && r > 0f) Silhouette(g, hs, 0.55f * fade);

            // Fixed metre rings (the scale), cross lines, then the shrinking funnel ring (approach only; in contact the ring instrument takes over).
            if (pre)
            {
                g.Line(Cx - R - 4f, Cy, Cx + R + 4f, Cy, Faint, 1f, 1f, 2f, 4f);
                g.Line(Cx, Cy - R - 4f, Cx, Cy + R + 4f, Faint, 1f, 1f, 2f, 4f);
                for (int k = 1; k <= 2; k++)
                {
                    float m = 10f * k;
                    g.Ring(Cx, Cy, m * K, Dim, 0.45f, 1f, Ink, 0f, 2f, 4f);
                    g.Text(ScopeStr.Int((int)Math.Round(AvUnitTable.Altitude(m, units) / (units == AvUnits.Imperial ? 10f : 1f)) * (units == AvUnits.Imperial ? 10 : 1)), Cx + m * K * 0.707f + 3f, Cy - m * K * 0.707f - 4f, Dim, 12f, ScopeAlign.Left, false, 0.85f);
                }
            }
            float rho = DockMath.ConePx(cone);
            ScopeTone ft = bad ? Amber : Ink;
            if (pre)
            {
                g.Ring(Cx, Cy, rho, ft, 0.95f, 1f, ft, 0.04f);
                for (int a = 0; a < 360; a += 15)
                {
                    ScopeCanvas.Polar(a, rho, out float ox, out float oy);
                    ScopeCanvas.Polar(a, rho - (a % 90 == 0 ? 7f : 3f), out float ix, out float iy);
                    g.Line(Cx + ox, Cy + oy, Cx + ix, Cy + iy, ft, 0.85f);
                }
            }

            // Gate ripples at the crossings.
            for (int gate = 1; gate <= 3; gate++)
            {
                float u = ScopeCanvas.Seg(t, enterAt[gate], enterAt[gate] + 0.9f);
                if (u <= 0f || u >= 1f) continue;
                g.Ring(Cx, Cy, 8f + (R - 8f) * ScopeCanvas.EaseOut(u), Hi, 1f - u);
                g.Stamp(gate == 3 ? "CONTACT" : Words[gate], Cx, Cy - (gate == 3 ? 80f : 38f), Hi, 14f, 0f, 1f - 0.7f * u);
            }

            // The contact socket (the thing you are flying at), fixed.
            if (pre)
            {
                g.Ring(Cx, Cy, 7f, Hi, 0.9f);
                g.Line(Cx - 12f, Cy, Cx - 7f, Cy, Hi); g.Line(Cx + 7f, Cy, Cx + 12f, Cy, Hi); g.Line(Cx, Cy - 12f, Cx, Cy - 7f, Hi); g.Line(Cx, Cy + 7f, Cx, Cy + 12f, Hi);
                // Your dot, its trail and the correction line to the centre.
                for (int i = 0; i < 6; i++) { Clamp(hx[i], hy[i], out float tx, out float ty, out _); trail[2 * i] = tx; trail[2 * i + 1] = ty; }
                g.Trail(trail, 6, bad ? Amber : Hi, 1f, 1f, 5);
                Clamp(d.RelX, d.RelY, out float px, out float py, out bool off);
                float len = (float)Math.Sqrt((px - Cx) * (px - Cx) + (py - Cy) * (py - Cy));
                ScopeTone dn = bad ? Amber : Hi;
                if (len > 16f)
                {
                    float ux = (Cx - px) / len, uy = (Cy - py) / len;
                    g.Dashed(px + ux * 9f, py + uy * 9f, Cx - ux * 14f, Cy - uy * 14f, dn, 0.8f, 1f, -t * 12f);
                    g.Chevron(Cx - ux * 18f, Cy - uy * 18f, (float)(Math.Atan2(ux, -uy) * 180.0 / Math.PI), 3.5f, dn, true, 0.95f);
                }
                if (off) g.Ring(px, py, 5f, Amber); else g.Ring(px, py, 4f, dn, 1f, 1f, dn, 1f);
                g.Ring(px, py, 8f + (edge ? 2f * ScopeCanvas.Pulse(t, 3f) : 0f), dn, bad || edge ? 0.9f : 0.5f);
                if (bad) g.Text("OUT", px + (px > Cx ? -12f : 12f), py + 15f, Amber, 12f, px > Cx ? ScopeAlign.Right : ScopeAlign.Left, true);
                if (r > 0f) g.Text(ScopeStr.Cat(d.ServiceZone ? "ZONE " : "FUNNEL ", Len(cone), LenUnit()), Cx, Cy + rho + 11f, ft, 12f, ScopeAlign.Center, false, 0.9f);
            }
            else TransferRing(g, t);

            // Director arrows outside the rings: they point the way to move.
            if (!pre) return;
            DockMath.Directors(d.RelX, d.RelY, out float up, out float down, out float left, out float right);
            Director(g, 0f, up, bad); Director(g, 180f, down, bad); Director(g, 270f, left, bad); Director(g, 90f, right, bad);
        }

        private void Director(ScopeCanvas g, float angle, float m, bool bad)
        {
            ScopeCanvas.Polar(angle, R + 9f, out float dx, out float dy);
            if (m < DockMath.OkM) { g.Tri(Cx + dx, Cy + dy, angle, 4f, Dim, false, 0.7f); return; }
            g.Tri(Cx + dx, Cy + dy, angle, 5f, bad ? Amber : m > DockMath.BoldM ? Hi : Ink);
        }

        /// <summary>In contact the bullseye is the transfer instrument: fuel on the outer ring, the rounds as segments, the flow drawn in from the rim.</summary>
        private void TransferRing(ScopeCanvas g, float t)
        {
            bool done = d.Phase == DockPhase.Served;
            float fr = Math.Max(0f, Math.Min(1f, d.Fuel)), rx = 62f;
            Silhouette(g, 150f, 0.3f);
            g.Arc(Cx, Cy, rx, 0f, 359.9f, Faint, 1f, 6f);
            if (!done && d.FuelTarget > fr + 0.005f) g.Arc(Cx, Cy, rx, 360f * fr, 360f * Math.Min(1f, d.FuelTarget) - 0.01f, Dim, 0.6f, 6f);
            if (fr > 0.003f) g.Arc(Cx, Cy, rx, 0f, Math.Max(0.5f, 360f * fr - 0.01f), done ? Hi : Ink, 1f, 6f);
            if (!done)
            {
                ScopeCanvas.Polar(360f * fr, rx, out float mx, out float my);
                g.Line(Cx + mx * 0.88f, Cy + my * 0.88f, Cx + mx * 1.1f, Cy + my * 1.1f, Hi, 1f, 2f);
            }
            int full = Math.Max(1, d.AmmoFull), segs = Math.Min(full, 8);
            int lit = (int)Math.Round((double)d.Ammo / full * segs), from = (int)Math.Round((double)ammoFrom / full * segs);
            bool fresh = t - ammoAt < 0.45f;
            float step = 360f / segs, gap = Math.Min(10f, step * 0.17f);
            for (int k = 0; k < segs; k++)
            {
                bool on = k < lit;
                ScopeTone tone = fresh && on && k >= from ? Hi : on ? Ink : Faint;
                g.Arc(Cx, Cy, 46f, k * step + gap * 0.5f, (k + 1) * step - gap * 0.5f, tone, 1f, 6f);
            }
            if (!done)
            {
                for (int k = 0; k < 8; k++)
                {
                    float u = (t * 1.2f + (k % 2) * 0.5f) % 1f, rr = Mathf.Lerp(R + 2f, rx + 9f, u), a = k * 45f + 22.5f;
                    ScopeCanvas.Polar(a, rr, out float ox, out float oy);
                    g.Chevron(Cx + ox, Cy + oy, a + 180f, 3.5f, Hi, true, (float)Math.Sin(u * 3.1416f) * 0.9f);
                }
            }
            if (done && fr >= 0.98f) { g.Check(Cx, Cy - 6f, 22f, Hi, 1f, 3f); g.Text("FULL", Cx, Cy + 20f, Hi, 12f, ScopeAlign.Center, true); }
            else
            {
                g.Text(ScopeStr.Pct((int)Math.Round(fr * 100f)), Cx, Cy - 6f, Hi, 22f, ScopeAlign.Center, true);
                g.Text("FUEL", Cx, Cy + 12f, Dim, 12f, ScopeAlign.Center);
                // n/m while the load is a few weapons; a long gun belt or a rocket pod would not fit the ring, so it reads as a share.
                g.FitText(d.AmmoFull <= AmmoCountMax ? ScopeStr.Cat("AMMO ", d.Ammo, "/", d.AmmoFull, null) : ScopeStr.Cat("AMMO ", (int)Math.Round(100.0 * d.Ammo / full), "%"), Cx, Cy + 28f, 84f, Ink, 12f, 12f, ScopeAlign.Center);
            }
        }

        // ------------------------------------------------------------------------------------------------ right column

        private void DirRow(ScopeCanvas g, float x, float y, float ang, string word, float m, bool bad)
        {
            ScopeTone tone = bad ? Amber : m > DockMath.BoldM ? Hi : m > DockMath.OkM ? Ink : Dim;
            if (m <= DockMath.OkM)
            {
                g.Check(x + 6f, y, 9f, Ink, 0.8f, 1.5f);
                g.Text(word, x + 20f, y, Dim);
                g.Text("OK", x + 160f, y, Dim, 12f, ScopeAlign.Right);
                return;
            }
            g.Tri(x + 6f, y, ang, 5f, tone);
            g.Text(word, x + 20f, y, tone, 12f, ScopeAlign.Left, m > DockMath.BoldM || bad);
            g.Text(ScopeStr.Cat("", Len(m), LenUnit()), x + 160f, y, tone, 12f, ScopeAlign.Right, m > DockMath.BoldM || bad);
        }

        private void RightColumn(ScopeCanvas g, float t)
        {
            int phase = (int)d.Phase;
            float r = d.RangeM, c = d.ClosureMps, err = Err, cone = Cone;
            bool fast = TooFast, bad = Bad, edge = Edge, contact = phase >= 3;
            float ttc = d.ServiceZone ? 0f : DockMath.TtcS(r, c);
            g.FitText(d.Tanker ?? "SKYWELL", X0, 42f, 100f, Hi, 12f, 12f, ScopeAlign.Left, true);
            g.Text(Words[phase], X0 + W, 42f, phase == 0 ? Ink : Hi, 12f, ScopeAlign.Right, phase > 0);
            g.Line(X0, 54f, X0 + W, 54f, Faint);

            // Range and time to contact.
            g.Text("RANGE", X0, 66f, Dim); g.Text(d.ServiceZone ? "ZONE" : "TTC", X0 + W, 66f, Dim, 12f, ScopeAlign.Right);
            float rw = g.Text(ScopeStr.Cat("", Len(r), null), X0, 86f, Hi, 22f, ScopeAlign.Left, true);
            g.Text(units == AvUnits.Imperial ? "FT" : "M", X0 + rw + 4f, 88f, Dim);
            if (d.ServiceZone) g.Text(d.ZoneReady ? "READY" : "JOIN", X0 + W, 86f, d.ZoneReady ? Hi : Dim, 16f, ScopeAlign.Right, d.ZoneReady);
            else if (ttc > 0f)
            {
                float tw = g.Text("S", X0 + W, 88f, Dim, 12f, ScopeAlign.Right);
                g.Text(ScopeStr.Int((int)Math.Round(ttc)), X0 + W - tw - 3f, 86f, Hi, 22f, ScopeAlign.Right, true);
            }
            else g.Text(contact ? "LOCKED" : "--", X0 + W, 86f, contact ? Hi : Dim, 16f, ScopeAlign.Right, contact);

            // Gate ribbon (linear 240 -> 0 m): ticks at 150, the capture and the engage radii; the passed part solid.
            float ry = 118f, mx = X0 + 2f + (W - 4f) * DockMath.RibbonAt(r);
            g.Line(X0 + 2f, ry, X0 + W - 2f, ry, Faint);
            g.Line(X0 + 2f, ry, mx, ry, Ink, 1f, 2f);
            if (d.ServiceZone) Gate(g, d.CaptureM, r, ry, "ZONE");
            else
            {
                Gate(g, DockMath.RibbonMarkM, r, ry, null);
                Gate(g, d.CaptureM, r, ry, "CAPT");
                Gate(g, d.EngageM, r, ry, "ENG");
            }
            g.Diamond(X0 + W - 3f, ry, 4f, Hi, contact);
            g.Chevron(mx, ry, 90f, 6f, Hi, true);

            // Closure against the hook-up's limit.
            g.Line(X0, 142f, X0 + W, 142f, Faint);
            g.Text("CLOSURE", X0, 154f, Dim);
            float cv = Closure(c);
            g.Text(ScopeStr.Cat("", cv, cv < 10f ? 1 : 0, ClosureUnit()), X0 + W, 154f, fast ? Amber : Hi, 12f, ScopeAlign.Right, true);
            g.Bar(X0, 164f, W, 10f, DockMath.ClosureFill(c, d.MaxClosingMps), fast ? Amber : Ink);
            g.BarTick(X0, 164f, W, 10f, DockMath.LimitTick, c >= 0.733f * d.MaxClosingMps ? Amber : Dim, ScopeStr.Cat("MAX ", (int)Math.Round(Closure(d.MaxClosingMps)), null));
            if (fast) { g.Tri(X0 + 5f, 186f, 180f, 4f, Amber); g.Text("AFT", X0 + 14f, 186f, Amber, 12f, ScopeAlign.Left, true); }

            // The prompt panel.
            float py = 196f, ph = 74f, cx = X0 + W / 2f;
            bool flash = phase == 2 && (int)(t * 3f) % 2 == 0 && t < enterAt[2] + 1.5f;
            g.Box(X0, py, W, ph, phase >= 2 ? Hi : Dim, phase >= 2 ? 0.9f : 0.8f, 1f, Bg, 0f, chamTL: 6f);
            if (phase < 2)
            {
                bool off = !DockMath.Inside(err, cone) && Near;
                DirRow(g, X0 + 8f, py + 14f, d.RelX < 0f ? 90f : 270f, d.RelX < 0f ? "RIGHT" : "LEFT", Math.Abs(d.RelX), off);
                DirRow(g, X0 + 8f, py + 33f, d.RelY < 0f ? 0f : 180f, d.RelY < 0f ? "UP" : "DN", Math.Abs(d.RelY), off);
                g.Line(X0 + 8f, py + 46f, X0 + W - 8f, py + 46f, Faint);
                g.Text(ScopeStr.Cat("ERR ", Len(err), LenUnit()), X0 + 10f, py + 60f, edge || off ? Amber : Ink, 12f, ScopeAlign.Left, edge);
                g.Text(d.ServiceZone ? d.ZoneReady ? "IN ZONE" : "JOIN ZONE" : !off ? (edge ? "NEAR EDGE" : "IN FUNNEL") : "FUNNEL OUT", X0 + W - 8f, py + 60f, edge || off ? Amber : Dim, 12f, ScopeAlign.Right, edge || off);
            }
            else if (phase == 2)
            {
                g.PlateTag(d.ServiceZone ? "HOLD STEADY" : "HOLD BRAKE", cx, py + 22f, Hi, 16f, ScopeAlign.Center, 8f, flash ? 0.55f : 1f);
                if (d.ServiceZone) g.Text(d.Docked ? "TENDER INBOUND" : "FLY FORMATION", cx, py + 46f, Dim, 12f, ScopeAlign.Center);
                else if (ttc > 0f) g.Text(ScopeStr.Cat("CONTACT IN ", Math.Max(1, (int)Math.Round(ttc)), " S"), cx, py + 46f, Dim, 12f, ScopeAlign.Center);
                else g.Text(d.Docked ? "DOCKING" : "BRAKE TO DOCK", cx, py + 46f, Dim, 12f, ScopeAlign.Center);
                if (DockMath.Inside(err, cone)) { g.Check(X0 + 18f, py + 62f, 9f, Ink, 1f, 1.5f); g.Text("ALIGNED", X0 + 30f, py + 62f, Ink); }
                else { g.Cross(X0 + 18f, py + 62f, 8f, Amber, 1f, 1.5f); g.Text("OFF LINE", X0 + 30f, py + 62f, Amber, 12f, ScopeAlign.Left, true); }
                g.Text(d.ServiceZone ? d.ZoneReady ? "IN ZONE" : "ZONE LOST" : d.Brake ? "BRK ON" : "BRK OFF", X0 + W - 8f, py + 62f, d.ServiceZone ? d.ZoneReady ? Hi : Amber : d.Brake ? Hi : Amber, 12f, ScopeAlign.Right, true);
            }
            else if (phase == 3)
            {
                g.Text("TRANSFERRING", cx, py + 14f, Hi, 12f, ScopeAlign.Center, true);
                for (int k = 0; k < 7; k++)
                {
                    float u = (t * 1.6f + k / 7f) % 1f;
                    g.Chevron(X0 + 16f + (W - 32f) * u, py + 32f, 90f, 4f, Hi, true, (float)Math.Sin(u * 3.1416f));
                }
                g.Text(needText, X0 + 10f, py + 50f, Ink);
                int rounds = Math.Max(0, d.AmmoFull - d.Ammo);
                g.Text(ScopeStr.Cat("RND +", rounds, null), X0 + W - 10f, py + 50f, Ink, 12f, ScopeAlign.Right);
                g.Text(d.ServiceZone ? "HOLD FORMATION" : "KEEP BRAKE ON", cx, py + 65f, Dim, 12f, ScopeAlign.Center);
            }
            else
            {
                g.Stamp("SERVED", cx, py + 24f, Hi, 16f);
                g.Check(cx + 54f, py + 24f, 12f, Hi, 1f, 2f);
                g.Text(d.ServiceZone ? "CLEAR THE ZONE" : "RELEASE BRAKE", cx, py + 49f, Ink, 12f, ScopeAlign.Center, true, 0.6f + 0.4f * ScopeCanvas.Pulse(t, 2f));
                if (d.HasMoved) g.FitText(movedText, cx, py + 65f, W - 12f, Dim, 12f, 12f, ScopeAlign.Center);
            }
        }

        /// <summary>A tick on the ribbon at <paramref name="m"/> metres with its figure under it (and a word above for the capture and engage gates).</summary>
        private void Gate(ScopeCanvas g, float m, float r, float ry, string word)
        {
            float gx = X0 + 2f + (W - 4f) * DockMath.RibbonAt(m);
            bool passed = r < m;
            g.Line(gx, ry - 5f, gx, ry + 5f, passed ? Ink : Dim);
            // Three-figure feet would touch each other under the close capture and engage ticks: those two hang either side of theirs.
            ScopeAlign at = word == "CAPT" && units == AvUnits.Imperial ? ScopeAlign.Right : word == "ENG" && units == AvUnits.Imperial ? ScopeAlign.Left : ScopeAlign.Center;
            g.Text(ScopeStr.Int(Len(m)), gx, ry + 15f, passed ? Ink : Dim, 12f, at, false, passed ? 0.85f : 1f);
            if (word != null) g.Text(word, gx + (word == "ENG" ? 14f : 0f), ry - 13f, passed ? Hi : Dim, 12f, ScopeAlign.Center, false, passed ? 1f : 0.8f);
        }

        // ------------------------------------------------------------------------------------------------ strip: the real transfer bars

        public override void PaintStrip(ScopeCanvas g, in PagePaint p)
        {
            if (!live) return;
            bool xfer = d.Phase == DockPhase.Transfer, done = d.Phase == DockPhase.Served;
            Half(g, 4f, "FUEL", d.Fuel, ScopeStr.Pct((int)Math.Round(d.Fuel * 100f)), 0, xfer, done, p.Now);
            g.Line(218f, 4f, 218f, 22f, Faint);
            int full = Math.Max(1, d.AmmoFull);
            Half(g, 226f, "AMMO", (float)d.Ammo / full, d.AmmoFull <= AmmoCountMax ? ScopeStr.Cat("", d.Ammo, "/", d.AmmoFull, null) : ScopeStr.Cat("", (int)Math.Round(100.0 * d.Ammo / full), "%"), full <= 8 ? full : 0, xfer, done, p.Now);
        }

        /// <summary>A load of more rounds than this reads as a percentage (the strip leaves six characters for the figure).</summary>
        private const int AmmoCountMax = 99;

        private static void Half(ScopeCanvas g, float x, string label, float frac, ReadOnlySpan<char> text, int segs, bool xfer, bool done, float t)
        {
            g.Text(label, x, 13f, Dim);
            float bx = x + 40f, bw = 112f;
            g.Bar(bx, 7f, bw, 12f, frac, done ? Hi : Ink, segs);
            if (xfer)
            {
                float u = (t * 1.5f) % 1f, sx = bx + 2f + (bw - 4f) * Math.Max(0f, Math.Min(1f, frac)) - 3f;
                g.Fill(Mathf.Round(sx), 9f, 3f, 8f, Hi, 0.9f * (1f - u));
            }
            g.FitText(text, bx + bw + 6f, 13f, 436f - (bx + bw + 6f), xfer || done ? Hi : Ink, 12f, 12f, ScopeAlign.Left, xfer || done);
        }
    }
}
