using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The paint of the STRIKE page: the stacked tracks, the SAT window, the CYBER chain and the perk strip (pages/strike.js drawTrack, drawSat, drawCyber, drawStrip).
    internal sealed partial class StrikePage
    {
        private const float X0 = 128f, X1 = 358f, W = 440f, HalfSat = 108f, HalfCyber = -112f;

        private static readonly float[] PanelRight = { 5f, -3f, 12f, -3f, 12f, 3f, 5f, 3f };
        private static readonly float[] PanelLeft = { -12f, -3f, -5f, -3f, -5f, 3f, -12f, 3f };

        private readonly float[] pts = new float[100], wedge = new float[6], ellipse = new float[48];

        public override void Paint(ScopeCanvas g, in PagePaint p)
        {
            if (!ScopeFeeds.TryRead(out d)) return;      // fresh numbers every frame: the page is Fast
            now = p.Now;
            int n = Math.Min(d.TrackCount, StrikeMath.MaxTracks);
            bool sat = d.SatOpen, cyber = d.CyberLive, lower = sat || cyber;
            float room = lower ? StrikeMath.BesideLower : p.H - StrikeMath.Top - 2f, pitch = StrikeMath.Pitch(n, room), top = StrikeMath.Start(n, pitch, room, lower);
            int soon = StrikeMath.Soonest(d.Tracks, n);
            for (int i = 0; i < n; i++) Track(g, d.Tracks[i], top + i * pitch, pitch, i == soon, p.Units);
            if (!lower) return;

            float lowY = StrikeMath.LowerTop(n, p.H);
            if (n > 0) g.Line(1f, lowY - 1f, W - 1f, lowY - 1f, Faint);
            if (sat && cyber) g.Line(220f, lowY + 3f, 220f, p.H - 3f, Faint);
            float oy = lowY - StrikeMath.LowerRef;       // the blocks keep the prototype's coordinates, moved to where they sit
            if (sat) { g.Origin(cyber ? 0f : HalfSat, oy); Sat(g); }
            if (cyber) { g.Origin(sat ? 0f : HalfCyber, oy); Cyber(g); }
            g.Origin(0f, 0f);
        }

        // ------------------------------------------------------------------------------------------------ tracks

        private static float Lerp(float a, float b, float u) => a + (b - a) * u;

        /// <summary>A point of the track's curve at <paramref name="u"/> 0..1 (the prototype's <c>P</c>): the rod's quadratic from the satellite, the missiles' arc, the cruise's low route.</summary>
        private static void Pos(in StrikeTrack m, float y0, float th, float u, out float x, out float y)
        {
            float gy = y0 + th - 7f, kv = StrikeMath.Depth(th);
            if (m.Kind == StrikeKind.Rod)
            {
                float v = 1f - u, ay = y0 + 9f, cy = y0 + 4f, by = gy - 5f;
                x = v * v * X0 + 2f * v * u * X1 + u * u * X1;
                y = v * v * ay + 2f * v * u * cy + u * u * by;
                return;
            }
            if (m.Kind != StrikeKind.Cruise)
            {
                x = Lerp(X0 + 4f, X1, u);
                y = Lerp(gy - 4f, gy - 5f, u) - 29f * kv * 4f * u * (1f - u);
                return;
            }
            float w0 = gy - 17f * kv, w1 = gy - 19f * kv, w2 = gy - 12f * kv, w3 = gy - 5f;
            x = Lerp(X0 + 8f, X1, u);
            float yy = x <= 208f ? Lerp(w0, w1, (x - (X0 + 8f)) / (208f - (X0 + 8f))) : x <= 272f ? Lerp(w1, w2, (x - 208f) / 64f) : Lerp(w2, w3, (x - 272f) / (X1 - 272f));
            y = yy + 1.6f * Mathf.Sin(x * 0.13f) * Math.Min(1f, u * 8f);
        }

        private int Samples(in StrikeTrack m, float y0, float th, int n, float u0, float u1)
        {
            for (int k = 0; k <= n; k++)
            {
                Pos(m, y0, th, Lerp(u0, u1, (float)k / n), out float x, out float y);
                pts[2 * k] = x;
                pts[2 * k + 1] = y;
            }
            return n + 1;
        }

        private static float Heading(float ax, float ay, float bx, float by) => Mathf.Atan2(bx - ax, -(by - ay)) * Mathf.Rad2Deg;

        private static void Burst(ScopeCanvas g, float x, float y, float k, ScopeTone tone)
        {
            float u = ScopeCanvas.Seg(k, 0f, 0.9f);
            if (u >= 1f) return;
            g.Ring(x, y, 3f + 15f * ScopeCanvas.EaseOut(u), tone, 1f - u);
            if (u < 0.35f) g.Ring(x, y, 6f * (1f - u / 0.35f) + 2f, tone, 1f, 1f, tone, 0.7f);
        }

        /// <summary>The satellite: a diamond body with a solar panel either side, turned by <paramref name="ang"/>.</summary>
        private static void SatGlyph(ScopeCanvas g, float x, float y, ScopeTone tone, float alpha, float ang)
        {
            int n = g.Load(ScopeShapes.Diamond, 4, x, y, 3.4f, ang);
            g.FillPoly(n, tone, alpha);
            g.Path(n, tone, alpha);
            g.Load(PanelRight, 4, x, y, 1f, ang);
            g.Path(4, tone, alpha);
            g.Load(PanelLeft, 4, x, y, 1f, ang);
            g.Path(4, tone, alpha);
            float a = ang * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            for (int sg = -1; sg <= 1; sg += 2) g.Line(x + sg * 3f * c, y + sg * 3f * s, x + sg * 5f * c, y + sg * 5f * s, tone, alpha);
        }

        /// <summary>A rail launcher: a base box with three tubes slanting up.</summary>
        private static void Launcher(ScopeCanvas g, float x, float y, ScopeTone tone)
        {
            g.Box(x - 7f, y - 3f, 14f, 4f, tone);
            for (int k = 0; k < 3; k++) g.Line(x - 3f + k * 4f, y - 3f, x + 2f + k * 4f, y - 11f, tone);
        }

        private void Track(ScopeCanvas g, in StrikeTrack m, float y0, float th, bool soon, AvUnits units)
        {
            StrikePhase ph = StrikeMath.Phase(m);
            bool idle = ph == StrikePhase.Done, flying = m.Eta > 0f, rod = m.Kind == StrikeKind.Rod, cruise = m.Kind == StrikeKind.Cruise, arc = !rod && !cruise;
            ScopeTone tone = idle ? Dim : soon ? Hi : Ink;
            float t = now, gy = y0 + th - 7f, tx = X1, ty = gy - 5f, dy = (th - 46f) / 2f, kv = StrikeMath.Depth(th), since = -m.Eta;
            if (soon)
            {
                g.Fill(1f, y0, W - 2f, th - 1f, Ink, 0.07f);
                g.Fill(2f, y0 + 5f, 2f, th - 11f, Hi);
            }
            g.Line(1f, y0 + th - 1f, W - 1f, y0 + th - 1f, Faint);

            // left column: the weapon, where it is going, how far from us
            g.FitText(OpsScopeWords.Name(m.Kind), 9f, y0 + 10f + dy, 114f, idle ? Dim : Ink, 12f, 12f, ScopeAlign.Left, true);
            if (m.Target != null) g.FitText(m.Target, 9f, y0 + 24f + dy, 114f, Dim);
            if (StrikeMath.Close(m)) g.Text(ScopeStr.Distance("CLOSE", m.RangeM, units, 14), 9f, y0 + 38f + dy, Amber, 12f, ScopeAlign.Left, true);
            else
            {
                float w = g.Text(ScopeStr.Distance("", m.RangeM, units, 12), 9f, y0 + 38f + dy, Dim);
                int brg = (int)Math.Round(m.BearingDeg) % 360;
                g.Text(ScopeStr.Cat(brg < 10 ? "BRG 00" : brg < 100 ? "BRG 0" : "BRG ", brg), 9f + w + 8f, y0 + 38f + dy, Dim);
            }

            // ground ruler and the flight's whole curve (dashed while it is in the air)
            for (int k = 0; k <= 4; k++)
            {
                float rx = Mathf.Round(Lerp(X0, X1, k / 4f));
                g.Line(rx, gy, rx, gy + 3f, Faint);
            }
            int np = Samples(m, y0, th, 44, 0f, 1f);
            g.Poly(pts, np);
            if (flying) g.Path(np, Dim, 0.95f, 1f, false, 3f, 3f);
            else g.Path(np, Faint, 0.9f, 1f, false);

            // the platform
            float along = StrikeMath.Along(m), elapsed = m.Lead - m.Eta;
            if (rod)
            {
                SatGlyph(g, X0, y0 + 9f, tone, 1f, 0f);
                if (ph == StrikePhase.Warn)
                {
                    g.Ring(X0, y0 + 9f, 9f, Faint, 1f, 2f);
                    g.Arc(X0, y0 + 9f, 9f, 0f, Math.Max(1f, 360f * StrikeMath.Dwell(m)), Hi, 1f, 2f);
                    g.Ring(X0, y0 + 9f, 13f, Hi, 0.35f + 0.35f * ScopeCanvas.Pulse(t, 2f));
                }
            }
            else if (arc)
            {
                Launcher(g, X0, gy - 3f, tone);
                if (elapsed >= 0f && elapsed < 0.6f) g.Ring(X0, gy - 8f, 3f + 12f * ScopeCanvas.EaseOut(elapsed / 0.6f), Hi, 1f - elapsed / 0.6f);
            }
            else g.Jet(X0 + 2f, gy - 17f * kv, 90f, 7f, tone);

            // the payload: waypoints for the cruise, the rounds for a missile, the head and its trail for the rod and the cruise
            float headX = 0f;
            bool head = false;
            if (cruise)
                for (int k = 0; k < m.Legs; k++)
                {
                    float wu = (k + 1f) / (m.Legs + 1f);
                    Pos(m, y0, th, wu, out float wx, out float wy);
                    bool pass = along >= wu;
                    g.Diamond(wx, wy, 3f, pass ? Ink : Dim, pass, pass ? 0.8f : 1f);
                }
            if (arc)
            {
                int rounds = Math.Max(1, m.Rounds);
                for (int k = 0; k < rounds; k++)
                {
                    float uk = ScopeCanvas.Clamp01((elapsed - k * Math.Max(0f, m.RoundGap)) / Math.Max(0.1f, m.Lead));
                    if (uk <= 0f) continue;
                    Pos(m, y0, th, uk, out float px, out float py);
                    if (uk < 1f)
                    {
                        np = Samples(m, y0, th, 6, Math.Max(0f, uk - 0.1f), uk);
                        g.Trail(pts, np, tone, 1f, 1f, 4);
                        ScopeTone dot = soon ? Hi : Ink;
                        g.Ring(px, py, 1.8f, dot, 1f, 1f, dot, 1f);
                    }
                    else Burst(g, tx + (k % 2 == 1 ? 4f : -4f), ty + (k % 3 - 1) * 3f, since - k * Math.Max(0f, m.RoundGap), Hi);
                }
                if (flying)
                {
                    np = Samples(m, y0, th, 30, 0f, along);
                    g.Poly(pts, np);
                    g.Path(np, tone, 0.85f, 1f, false);
                }
            }
            else if (flying)
            {
                float pu = StrikeMath.Curve(m), hdg = 0f;
                Pos(m, y0, th, pu, out float px, out float py);
                if (pu > 0f)
                {
                    Pos(m, y0, th, Math.Max(0f, pu - 0.02f), out float ax, out float ay);
                    Pos(m, y0, th, Math.Min(1f, pu + 0.02f), out float bx, out float by);
                    hdg = Heading(ax, ay, bx, by);
                    np = Samples(m, y0, th, 24, 0f, pu);
                    g.Poly(pts, np);
                    g.Path(np, tone, 0.9f, 1f, false);
                }
                if (!rod || ph == StrikePhase.Fall)
                {
                    np = Samples(m, y0, th, 8, Math.Max(0f, pu - 0.18f), pu);
                    g.Trail(pts, np, Hi, 1f, 2f, 5);
                    g.Chevron(px, py, hdg, 6.5f, soon ? Hi : Ink, true);
                }
                head = true;
                headX = px;
            }

            // the ground track: travelled in the track's tone, the rest faint
            float gx;
            if (arc)
            {
                Pos(m, y0, th, along, out float ax, out _);
                gx = ph == StrikePhase.Flight ? ax : X1;
            }
            else gx = head ? headX : flying ? X0 : X1;
            gx = Math.Max(X0, Math.Min(X1, gx));
            g.Line(X0, gy, gx, gy, tone, 1f, 2f);
            g.Line(gx, gy, X1, gy, Faint);

            // the target: a diamond until every round has landed, then a cross; the danger ring, the closing ring, the burst
            float last = StrikeMath.SinceLast(m);
            if (!flying && last >= 0f) g.Cross(tx, ty, 8f, idle ? Dim : Hi);
            else g.Diamond(tx, ty, 5f, soon ? Hi : idle ? Dim : Ink);
            if (StrikeMath.Close(m)) g.Ring(tx, ty, 11f, Amber, 0.6f + 0.4f * ScopeCanvas.Pulse(t, 2f), 1f, Ink, 0f, 2f, 2f);
            if (soon && m.Eta > 0f && m.Eta <= StrikeMath.ConvergeSeconds) g.Ring(tx, ty, 6f + 14f * (m.Eta / StrikeMath.ConvergeSeconds), Hi, 0.9f);
            if (!arc && !flying) Burst(g, tx, ty, since, Hi);
            if (last >= 0f && last < StrikeMath.Hold + 1f && !(cruise && since < 0.9f))
                g.Stamp(StrikeMath.Kinetic(m.Kind) ? "IMPACT" : "BURST", 292f, y0 + 22f + dy, Hi, 12f, 0f, last < 0.25f ? last / 0.25f : 1f);

            // right column: the countdown and the phase word
            if (ph == StrikePhase.Impact && m.Rounds > 1 && StrikeMath.Splashes(m) < m.Rounds)
                g.Text(ScopeStr.Cat("", StrikeMath.Splashes(m), "/", m.Rounds, null), 435f, y0 + 19f + dy, Hi, 20f, ScopeAlign.Right, true);
            else if (ph == StrikePhase.Impact || ph == StrikePhase.Done) g.Check(417f, y0 + 19f + dy, 11f, ph == StrikePhase.Done ? Dim : Hi, 1f, 2f);
            else
            {
                int secs = (int)Math.Ceiling(ph == StrikePhase.Effect ? Math.Max(0f, m.Linger - since) : m.Eta);
                g.Text(ScopeStr.Int(secs), 424f, y0 + 19f + dy, soon ? Hi : Ink, 20f, ScopeAlign.Right, true);
                g.Text("S", 435f, y0 + 22f + dy, Dim, 12f, ScopeAlign.Right);
            }
            g.Text(OpsScopeWords.Phase(m, ph), 435f, y0 + 37f + dy, ph == StrikePhase.Warn || ph == StrikePhase.Fall || ph == StrikePhase.Flight ? Ink : Dim, 12f, ScopeAlign.Right, soon);
        }

        // ------------------------------------------------------------------------------------------------ SAT window

        private void Ellipse(ScopeCanvas g, float cx, float cy, float rx, float ry, ScopeTone tone, float fillAlpha, bool dashed)
        {
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI * 2f / 24f;
                ellipse[2 * i] = cx + Mathf.Cos(a) * rx;
                ellipse[2 * i + 1] = cy + Mathf.Sin(a) * ry;
            }
            int n = g.Poly(ellipse, 24);
            if (fillAlpha > 0f) g.FillPoly(n, tone, fillAlpha);
            g.Path(n, tone, dashed ? 0.6f : 1f, 1f, true, dashed ? 2f : 0f, dashed ? 3f : 0f);
        }

        /// <summary>The window the own RECON PASS or SAT CAMERA opened: the bird parked on the belt, the wedge down to its footprint, the scan line, the contacts and the countdown.</summary>
        private void Sat(ScopeCanvas g)
        {
            float t = now, rem = d.SatLeft, total = d.SatTotal, period = 8f, elapsed = Math.Max(0f, total - rem), cx = 184f, cy = 224f;
            bool late = StrikeMath.SatLate(rem);
            ScopeTone tone = late ? Amber : Ink;
            g.Text(d.SatCamera ? "SAT OPTICAL" : "SAT RADAR", 8f, 186f, Ink, 12f, ScopeAlign.Left, true);
            int contacts = d.SatContacts;
            g.Text(contacts <= 0 ? "NO CONTACTS" : ScopeStr.Cat("", contacts, contacts == 1 ? " CONTACT" : " CONTACTS"), 216f, 186f, Dim, 12f, ScopeAlign.Right);

            // a geostationary bird, parked over the footprint for the whole window
            const float ox = 74f, oy = 330f, R = 126f, a = -7f, gyL = 262f, rw = 33f;
            float bx = ox + R * Mathf.Sin(a * Mathf.Deg2Rad), by = oy - R * Mathf.Cos(a * Mathf.Deg2Rad), xf = bx;
            g.Arc(ox, oy, R, -32f, 32f, Dim, 0.7f, 1f, 3f, 3f);
            wedge[0] = bx; wedge[1] = by + 3f; wedge[2] = xf + rw; wedge[3] = gyL; wedge[4] = xf - rw; wedge[5] = gyL;
            int n = g.Poly(wedge, 3);
            g.FillPoly(n, tone, 0.07f);
            g.Hatch(n, 5f, tone, 0.2f);
            g.Dashed(bx, by + 3f, xf - rw, gyL, tone, 0.9f, 1f, -t * 14f);
            g.Dashed(bx, by + 3f, xf + rw, gyL, tone, 0.9f, 1f, -t * 14f);
            SatGlyph(g, bx, by, Hi, 1f, a);
            g.Line(10f, gyL + 8f, 150f, gyL + 8f, Faint);
            Ellipse(g, xf, gyL + 1f, rw, 6f, tone, 0.1f, false);

            // the scan line sweeps the footprint; each contact of the window latches as it passes
            float sw = elapsed % period / period, sx = xf - rw + 2f * rw * sw;
            g.Line(sx, gyL - 9f, sx, gyL + 9f, Hi, 0.8f);
            int shown = Math.Min(contacts, 6);
            for (int k = 0; k < shown; k++)
            {
                float ct = period * (k + 0.5f) / shown;
                if (elapsed < ct) continue;
                float px = xf + ((k + 0.5f) / shown - 0.5f) * 2f * rw * 0.9f, py = gyL + 1f + (k % 2 == 1 ? 2f : -2f), pk = elapsed - ct;
                g.Diamond(px, py, 3f, Ink, true);
                if (pk < 0.5f) g.Ring(px, py, 3f + 9f * ScopeCanvas.EaseOut(pk / 0.5f), Hi, 1f - pk / 0.5f);
            }

            // the window's countdown
            float frac = StrikeMath.SatFill(rem, total);
            g.Ring(cx, cy, 17f, Faint, 1f, 4f);
            if (frac > 0.003f) g.Arc(cx, cy, 17f, 0f, 360f * frac - 0.01f, tone, 1f, 4f);
            g.Text(ScopeStr.Int((int)Math.Ceiling(rem)), cx, cy + 1f, late ? Amber : Hi, 14f, ScopeAlign.Center, true);
            g.Text("IN VIEW", cx, 252f, late ? Amber : Dim, 12f, ScopeAlign.Center, late);
            g.Text(d.SatCamera ? "R2 CAMERA" : "R1 RECON", cx, 266f, Dim, 12f, ScopeAlign.Center);
        }

        // ------------------------------------------------------------------------------------------------ CYBER chain

        private static void NodeGlyph(ScopeCanvas g, ChainNodeKind kind, float x, float y, ScopeTone tone, bool held)
        {
            switch (kind)
            {
                case ChainNodeKind.Uplink: g.Tri(x, y, 0f, 7f, tone, held); break;
                case ChainNodeKind.Relay: g.Box(x - 5f, y - 5f, 10f, 10f, tone, 1f, 1f, tone, held ? 1f : 0f); break;
                case ChainNodeKind.Radar: g.Ring(x, y, 6f, tone, 1f, 1f, tone, held ? 1f : 0f); break;
                case ChainNodeKind.SamC2: g.Diamond(x, y, 7f, tone, held); break;
                default:
                    g.Box(x - 6f, y - 6f, 12f, 12f, tone);
                    g.Box(x - 2f, y - 2f, 5f, 5f, tone, 1f, 1f, tone, held ? 1f : 0f);
                    break;
            }
        }

        private static float NodeX(int i) => 244f + i * 42f;

        /// <summary>The own intrusion: the chain of nodes (held filled, the hop target ringed and filling with the hop timer), the trace burn and what the hop is doing.</summary>
        private void Cyber(ScopeCanvas g)
        {
            const float ny = 212f;
            float t = now, prog = d.HopProgress;
            bool hopping = d.CyberHopping;
            int held = d.ChainHeld, hopTo = hopping ? held : -1;
            ScopeTone tt = StrikeMath.TraceTone(d.Trace);
            g.Text("CYBER", 228f, 186f, Ink, 12f, ScopeAlign.Left, true);
            g.Text(hopping ? "HOPPING" : "HOLDING", 436f, 186f, hopping ? Ink : Hi, 12f, ScopeAlign.Right, true);

            for (int i = 0; i < 4; i++)
            {
                float xa = NodeX(i) + 9f, xb = NodeX(i + 1) - 9f;
                if (i + 1 < held) g.Line(xa, ny, xb, ny, Ink);
                else if (i + 1 == hopTo)
                {
                    float px = Lerp(xa, xb, prog);
                    g.Line(xa, ny, px, ny, Ink);
                    g.Line(px, ny, xb, ny, Dim, 1f, 1f, 3f, 3f, -t * 12f);
                    g.Ring(px, ny, 2.2f, Hi, 1f, 1f, Hi, 1f);
                }
                else g.Line(xa, ny, xb, ny, Faint, 1f, 1f, 2f, 3f);
            }

            float since = now - heldAt;
            for (int i = 0; i < 5; i++)
            {
                float x = NodeX(i);
                if (i >= d.ChainCount) { g.Box(x - 3f, ny - 3f, 6f, 6f, Faint, 0.9f); continue; }     // a free slot: an intrusion holds at most four nodes and hops to a fifth
                ChainNodeKind kind = d.Chain[i];
                bool isHeld = i < held, target = i == hopTo;
                ScopeTone nt = isHeld ? Ink : target ? Hi : Dim;
                NodeGlyph(g, kind, x, ny, nt, isHeld);
                if (target)
                {
                    g.Ring(x, ny, 12f, Faint, 1f, 2f);
                    g.Arc(x, ny, 12f, 0f, Math.Max(1f, 360f * prog), Hi, 1f, 2f);
                }
                if (i == held - 1 && since >= 0f && since < 0.8f) g.Ring(x, ny, 6f + 12f * ScopeCanvas.EaseOut(since / 0.8f), Hi, 1f - since / 0.8f);
                g.Text(OpsScopeWords.Node(kind), x, 233f, nt, 12f, ScopeAlign.Center, target);
            }

            g.Text("TRACE", 228f, 250f, Dim);
            g.Bar(272f, 245f, 128f, 11f, d.Trace / 100f, tt, 32);
            g.BarTick(272f, 245f, 128f, 11f, StrikeMath.TraceCaution / 100f, d.Trace >= StrikeMath.TraceCaution ? Amber : Dim);
            g.BarTick(272f, 245f, 128f, 11f, StrikeMath.TraceWarning / 100f, d.Trace >= StrikeMath.TraceWarning ? Red : Dim);
            g.Text(ScopeStr.Pct((int)Math.Round(d.Trace)), 436f, 250f, tt == Ink ? Hi : tt, 12f, ScopeAlign.Right, true);
            if (hopping && d.ChainCount > held) g.Text(ScopeStr.Cat(OpsScopeWords.Hop(d.Chain[held]), (int)Math.Ceiling(d.HopLeft), " S"), 228f, 266f, Ink);
            else if (held > 0) g.Text(OpsScopeWords.Held(d.Chain[held - 1]), 228f, 266f, Hi);
            g.Text(ScopeStr.Cat("+", d.TraceRate, 1, "%/S"), 436f, 266f, hopping ? Dim : Faint, 12f, ScopeAlign.Right);
        }

        // ------------------------------------------------------------------------------------------------ perk strip

        public override void PaintStrip(ScopeCanvas g, in PagePaint p)
        {
            float cw = W / 3f;
            for (int i = 0; i < d.PerkCount && i < StrikeMath.MaxTracks; i++)
            {
                PerkTile q = d.Perks[i];
                bool cool = q.State == PerkPhase.Cooling, armed = q.State == PerkPhase.Armed;
                float x = i * cw, cx = x + 17f, cy = 13f, frac = StrikeMath.PerkFill(q);
                if (i > 0) g.Line(Mathf.Round(x), 4f, Mathf.Round(x), 22f, Faint);
                g.Ring(cx, cy, 8.5f, Faint, 1f, 3f);
                g.Arc(cx, cy, 8.5f, 0f, Math.Max(1f, 360f * Math.Max(0.004f, Math.Min(1f, frac)) - 0.01f), cool ? Dim : Hi, 1f, 3f);
                if (armed) g.Ring(cx, cy, 12f, Hi, 0.4f + 0.5f * ScopeCanvas.Pulse(p.Now, 2f));
                float since = p.Now - readyAt[i];
                if (q.State == PerkPhase.Ready && since >= 0f && since < 1.4f) g.Ring(cx, cy, 9f + 5f * ScopeCanvas.EaseOut(since / 1.4f), Hi, 1f - since / 1.4f);
                PerkGlyph(g, q.Front, cx, cy, cool ? Dim : Hi);
                g.Text(q.Name, x + 32f, 14f, cool ? Ink : Hi, 12f, ScopeAlign.Left, !cool);
                g.Text(OpsScopeWords.Perk(q), x + cw - 5f, 14f, cool ? Dim : Hi, 12f, ScopeAlign.Right, !cool);
            }
        }

        private static void PerkGlyph(ScopeCanvas g, PerkFront front, float x, float y, ScopeTone tone)
        {
            if (front == PerkFront.Space)
            {
                g.Diamond(x, y, 2.6f, tone, true);
                g.Line(x - 5f, y, x - 3f, y, tone);
                g.Line(x + 3f, y, x + 5f, y, tone);
            }
            else if (front == PerkFront.Cyber)
            {
                g.Box(x - 3f, y - 3f, 7f, 7f, tone);
                g.Fill(Mathf.Round(x), Mathf.Round(y), 1f, 1f, tone);
                g.Line(x - 5f, y, x - 3f, y, tone);
                g.Line(x + 4f, y, x + 6f, y, tone);
            }
            else g.Chevron(x, y + 0.5f, 0f, 4f, tone, true);
        }
    }
}
