using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// THREAT (prototype: <c>pages/threat.js</c>): "where is it, how long do I have, what do I do" in one glance. A heading-up polar scope with the aircraft in the middle and
    /// time-to-impact rings (5, 10, 20 s, square-root scaled so the 5 s ring is not a dot): every missile inbound on us as a red chevron at its real bearing and time, with its
    /// trail and a dashed line in, tagged with its seeker and seconds; every radar painting us as a spoke from the rim (a lock solid and red, a search dashed and amber, tagged
    /// with its name and range); the notch wedge on the 3/9 line a radar missile is to be steered into and the arrow that turns you there. The column beside it says what to do
    /// (BEAM, CHAFF, FLARE...), lights the dispenser that answers the nearest seeker with its dots draining, shows the jammer and one time bar per missile. A missile that lost us
    /// stays a moment as a hollow ghost marked TRASHED. Fed by <see cref="ThreatScope"/> (Hud's own provider over <c>ThreatWatch</c>); live while anything is inbound, painting us or
    /// has just lost us. Events (a launch, a lock, a defeat) go to the ticker once.
    /// </summary>
    internal sealed class ThreatPage : ScopePageBase
    {
        private const float CX = 150f, CY = 168f, R = 116f, COL = 338f, CW = 94f;
        private const float DefeatBannerS = 2.4f;
        private const int Numbers = 32;

        private ThreatScope d;
        private bool live, primed;
        private AvUnits units;
        private float now;
        private int near = -1, ir = -1, rdr = -1, jam = -1, cueMissile, cueSide;
        private float cueOff;
        private ThreatCue cue;
        private bool recentDefeat;
        private int defeatedIdx = -1;
        private readonly bool[] seenNumber = new bool[Numbers], defeatSaid = new bool[Numbers];
        private readonly System.Collections.Generic.Dictionary<int, float> lockSaid = new System.Collections.Generic.Dictionary<int, float>(8), spikeSaid = new System.Collections.Generic.Dictionary<int, float>(8);
        private readonly int[] lastAmmo = new int[HudCm.MaxStations], baseAmmo = new int[HudCm.MaxStations], fired = new int[HudCm.MaxStations];
        private readonly float[] fireUntil = new float[HudCm.MaxStations];
        private readonly KeepOut keep = new KeepOut();
        private readonly float[] segs = new float[4 * 14], trailXY = new float[2 * 12], wedge = new float[2 * 14];
        private int cueBaseFor = -1;

        private string headlineText = "";
        private int headlineKey = -1;
        private ScopeTone headlineTone = Dim;
        private bool headlineFlash;
        private int cmKey = -1, rwrKey = int.MinValue;
        private string cmText, cmSub, rwrText, rwrSub, jamWord;

        public override ScopePage Id => ScopePage.Threat;

        public override string Title => "THREAT";

        public override char Code => 'T';

        public override bool Fast => live;

        // ------------------------------------------------------------------------------------------------ poll

        public override bool Poll(in ScopeEnv env)
        {
            now = env.Now;
            units = env.Units;
            if (!ScopeFeeds.TryRead(out d)) { live = primed = false; near = ir = rdr = jam = -1; Array.Clear(seenNumber, 0, Numbers); Array.Clear(defeatSaid, 0, Numbers); lockSaid.Clear(); spikeSaid.Clear(); return false; }
            live = true;
            Read();
            if (primed) Edges(env);
            primed = true;
            UpdateHeadline();
            return true;
        }

        /// <summary>The nearest live missile, the cue, the stations that answer it and the dispenses seen since it appeared.</summary>
        private void Read()
        {
            near = ThreatMath.Nearest(d.Missiles, d.MissileCount);
            recentDefeat = false;
            defeatedIdx = -1;
            for (int i = 0; i < d.MissileCount; i++)
                if (!d.Missiles[i].Live && d.Missiles[i].DefeatedS < DefeatBannerS && (defeatedIdx < 0 || d.Missiles[i].DefeatedS < d.Missiles[defeatedIdx].DefeatedS)) { recentDefeat = true; defeatedIdx = i; }
            bool locked = false, spiked = false;
            for (int i = 0; i < d.EmitterCount; i++) { locked |= d.Emitters[i].Lock; spiked |= !d.Emitters[i].Lock; }
            ThreatMissile n = near >= 0 ? d.Missiles[near] : default;
            cue = ThreatMath.Cue(near >= 0, n, recentDefeat, locked, spiked);
            cueMissile = near >= 0 ? n.Number : 0;
            cueSide = 0;
            cueOff = 0f;
            if (near >= 0 && n.Class == ThreatSeeker.Radar)
            {
                cueSide = n.BeamSide == 0 ? ThreatMath.BeamSide(n.BearingDeg, 0) : n.BeamSide;
                cueOff = ThreatMath.BeamOff(n.BearingDeg, cueSide);
            }
            ir = rdr = jam = -1;
            for (int i = 0; i < d.CmCount; i++)
            {
                if (d.Cm[i].Chargeable) { if (jam < 0) jam = i; }
                else if ((d.Cm[i].Mask & HudCm.IrMask) != 0) { if (ir < 0) ir = i; }
                else if ((d.Cm[i].Mask & HudCm.RadarMask) != 0) { if (rdr < 0) rdr = i; }
            }
            // Rounds dispensed since the nearest missile appeared: a cue that was answered reads "x5 FIRED", one that was not "DISPENSE".
            if (cueBaseFor != cueMissile) { cueBaseFor = cueMissile; for (int i = 0; i < d.CmCount && i < baseAmmo.Length; i++) baseAmmo[i] = d.Cm[i].Ammo; }
            for (int i = 0; i < d.CmCount && i < lastAmmo.Length; i++)
            {
                int ammo = d.Cm[i].Ammo;
                if (primed && ammo < lastAmmo[i] && !d.Cm[i].Chargeable) { fired[i] = lastAmmo[i] - ammo; fireUntil[i] = now + 0.45f; }
                lastAmmo[i] = ammo;
            }
        }

        private int Dispensed(int station) => station >= 0 && station < d.CmCount && station < baseAmmo.Length ? Math.Max(0, baseAmmo[station] - d.Cm[station].Ammo) : 0;

        /// <summary>Events once: a missile launched at us, a radar locks us or searches us, a missile lost us.</summary>
        private void Edges(in ScopeEnv env)
        {
            for (int i = 0; i < d.MissileCount; i++)
            {
                ThreatMissile m = d.Missiles[i];
                int k = m.Number % Numbers;
                if (m.Live && !seenNumber[k])
                {
                    seenNumber[k] = true;
                    string word = (string.IsNullOrEmpty(m.Seeker) ? "MISSILE" : m.Seeker.ToUpperInvariant() + " MISSILE");
                    string advice = m.Class == ThreatSeeker.Ir ? "FLARES" : m.Class == ThreatSeeker.Radar ? "BEAM AND CHAFF" : null;
                    env.Notice?.Invoke("missile", HudTone.Warning, word + " INBOUND - " + (float.IsNaN(m.TtiS) ? "" : (int)Math.Ceiling(m.TtiS) + " S"), advice);
                }
                if (!m.Live && !defeatSaid[k])
                {
                    defeatSaid[k] = true;
                    env.Notice?.Invoke("missile", HudTone.Info, "M" + m.Number + " DEFEATED", (string.IsNullOrEmpty(m.Seeker) ? "" : m.Seeker.ToUpperInvariant() + " ") + "TRASHED");
                }
            }
            // The numbers of missiles long gone are free to be told again (a new sortie counts from M1).
            for (int k = 0; k < Numbers; k++)
            {
                bool present = false;
                for (int i = 0; i < d.MissileCount; i++) present |= d.Missiles[i].Number % Numbers == k;
                if (!present) { seenNumber[k] = false; defeatSaid[k] = false; }
            }
            for (int i = 0; i < d.EmitterCount; i++)
            {
                ThreatEmitter e = d.Emitters[i];
                if (New(e.Lock ? lockSaid : spikeSaid, e.Id, now - e.AgeS))
                    env.Notice?.Invoke("rwr", e.Lock ? HudTone.Warning : HudTone.Caution, (e.Lock ? "LOCK - " : "SPIKE - ") + Name(e), ScopeStr.Distance("", e.RangeM, env.Units, 10).ToString());
            }
        }

        /// <summary>A radar's lock (or search) begun at <paramref name="since"/> that has not been told yet: remembered by the moment it began, so a radar that locks again later is told again.</summary>
        private static bool New(System.Collections.Generic.Dictionary<int, float> said, int id, float since)
        {
            if (said.TryGetValue(id, out float was) && Math.Abs(was - since) < 0.5f) return false;
            if (said.Count > 32) said.Clear();
            said[id] = since;
            return true;
        }

        private static string Name(in ThreatEmitter e) => string.IsNullOrEmpty(e.Name) ? "RADAR" : e.Name;

        // ------------------------------------------------------------------------------------------------ header and gauges

        private void UpdateHeadline()
        {
            int key;
            string text;
            ScopeTone tone = Hi;
            bool flash = false;
            if (near >= 0)
            {
                int s = (int)Math.Ceiling(Math.Max(0f, d.Missiles[near].TtiS));
                key = 1000 + s;
                text = key == headlineKey ? headlineText : ScopeStr.Cat("", s, " S").ToString();
                tone = Red;
                flash = ThreatMath.Warn(d.Missiles[near].TtiS);
            }
            else if (recentDefeat) { key = 1; text = "DEFEATED"; }
            else if (cue == ThreatCue.Lock) { key = 2; text = "LOCK"; tone = Red; flash = true; }
            else { key = 3; text = "SPIKE"; tone = Amber; }
            if (key != headlineKey) { headlineKey = key; headlineText = text; }
            headlineTone = tone;
            headlineFlash = flash;
        }

        public override ScopeHeadline Headline() => live ? new ScopeHeadline { Text = headlineText, Tone = headlineTone, Flash = headlineFlash } : default;

        /// <summary>The nearest radar painting us as a needle, the dispenser that answers the nearest missile as a ring, the jammer's charge as an arc.</summary>
        public override int Gauges(GaugeSpec[] into)
        {
            if (!live) return 0;
            int n = 0;
            int e = BestEmitter();
            if (e >= 0)
            {
                ThreatEmitter em = d.Emitters[e];
                int key = (int)(em.RangeM / 100f) * 4 + (em.Lock ? 1 : 0) + em.Id * 1000 + (units == AvUnits.Imperial ? 2 : 0);
                if (key != rwrKey) { rwrKey = key; rwrText = Name(em); rwrSub = ScopeStr.Distance("", em.RangeM, units, 7).ToString(); }
                into[n++] = new GaugeSpec { Id = "rwr", Label = "EMITTER", Kind = GaugeKind.Needle, Angle = em.BearingDeg, Text = rwrText, Sub = rwrSub, Tone = em.Lock ? Red : Amber };
            }
            int st = cue == ThreatCue.Flare ? ir : cue == ThreatCue.Chaff ? rdr : -1;
            if (st >= 0 && d.Cm[st].Max > 0)
            {
                CmStation s = d.Cm[st];
                int key = s.Ammo * 100 + (int)cue;
                if (key != cmKey) { cmKey = key; cmText = cue == ThreatCue.Flare ? "FLARE" : "CHAFF"; cmSub = s.Tag; }
                into[n++] = new GaugeSpec { Id = "cm", Label = "CM", Kind = GaugeKind.Ring, Value = (float)s.Ammo / s.Max, Inner = ScopeStr.Int(s.Ammo), Text = cmText, Sub = cmSub, Tone = Ink };
            }
            if (jam >= 0)
            {
                CmStation s = d.Cm[jam];
                jamWord = SystemsMath.JamWord(s);
                into[n++] = new GaugeSpec { Id = "jam", Label = "JAMMER", Kind = GaugeKind.Arc, Value = s.Charge, Text = jamWord, Sub = "ECM", Tone = Ink };
            }
            return n;
        }

        /// <summary>The radar to put on the needle: a lock before a spike, then the nearest.</summary>
        private int BestEmitter()
        {
            int best = -1;
            for (int i = 0; i < d.EmitterCount; i++)
                if (best < 0 || (d.Emitters[i].Lock && !d.Emitters[best].Lock) || (d.Emitters[i].Lock == d.Emitters[best].Lock && d.Emitters[i].RangeM < d.Emitters[best].RangeM)) best = i;
            return best;
        }

        // ------------------------------------------------------------------------------------------------ paint

        public override void Paint(ScopeCanvas g, in PagePaint p)
        {
            if (!ScopeFeeds.TryRead(out d)) return;      // fresh bearings every frame: the page is Fast
            live = true;
            now = p.Now;
            units = p.Units;
            Read();
            Scope(g);
            Column(g);
        }

        private static void At(float bearing, float r, out float x, out float y)
        {
            ScopeCanvas.Polar(bearing, r, out float dx, out float dy);
            x = CX + dx;
            y = CY + dy;
        }

        private static float Rr(float ttiS) => R * ThreatMath.RingFrac(ttiS);

        private void Scope(ScopeCanvas g)
        {
            float t = now;
            keep.Clear();
            keep.Add(CX - 13f, CY - 13f, 26f, 26f);
            Wedge(g);
            Rings(g);
            for (int i = 0; i < d.EmitterCount; i++) Spoke(g, d.Emitters[i]);
            BeamArrow(g);
            // the nearest missile last, so it is on top
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < d.MissileCount; i++)
                    if ((i == near) == (pass == 1)) Missile(g, i, i == near);
            g.Jet(CX, CY, 0f, 9f, Ink, true);
            if (near >= 0 && d.Missiles[near].TtiS <= ThreatMath.WarnS) g.Ring(CX, CY, 15f, Red, 0.35f + 0.5f * ScopeCanvas.Pulse(t, 3f));
            g.Text("TTI RINGS", 5f, 41f, Dim);
            g.Text("HDG UP", 5f, 56f, Faint);
            if (d.HasSam)
                g.Text(ScopeStr.Cat("SAM ENV x", d.SamRings, null), 5f, 286f, d.SamRings > 0 ? Amber : Dim, 12f, ScopeAlign.Left, d.SamRings > 0);
        }

        /// <summary>The notch wedge on the 3/9 line the nearest radar missile is to be steered into: hatched, dashed until it is beamed.</summary>
        private void Wedge(ScopeCanvas g)
        {
            if (cueSide == 0 || (cue != ThreatCue.Beam && cue != ThreatCue.Chaff)) return;
            float side = cueSide > 0 ? 90f : 270f;
            bool on = ThreatMath.Beamed(cueOff);
            int n = 0;
            for (float a = side - 9f; a <= side + 9f; a += 3f) { At(a, R, out wedge[2 * n], out wedge[2 * n + 1]); n++; }
            for (float a = side + 9f; a >= side - 9f; a -= 3f) { At(a, R * 0.3f, out wedge[2 * n], out wedge[2 * n + 1]); n++; }
            int path = g.Poly(wedge, n);
            g.FillPoly(path, Ink, on ? 0.3f : 0.08f + 0.06f * ScopeCanvas.Pulse(now, 1.5f));
            g.Hatch(path, 5f, Ink, on ? 0.55f : 0.35f);
            g.Path(path, Ink, on ? 1f : 0.7f, 1f, true, on ? 0f : 3f, on ? 0f : 3f);
            At(side, R * 0.55f, out float lx, out _);
            ScopeRect r = g.PlateTag(on ? "BEAMED" : "BEAM", lx, CY + 21f, on ? Hi : Ink, 12f, ScopeAlign.Center);
            keep.Add(r.X, r.Y, r.W, r.H);
        }

        private void Rings(ScopeCanvas g)
        {
            for (int k = 0; k < 3; k++)
            {
                int v = k == 0 ? 5 : k == 1 ? 10 : 20;
                float r = Rr(v);
                g.Ring(CX, CY, r, v == 20 ? Dim : Faint, v == 20 ? 0.85f : 1f, 1f, Ink, 0f, v == 20 ? 0f : 4f, v == 20 ? 0f : 3f);
                At(152f, r, out float x, out float y);
                g.Text(ScopeStr.Cat("", v, "S"), x + 3f, y + 1f, Dim, 12f, ScopeAlign.Left, false, 0.9f);
            }
            for (int b = 0; b < 360; b += 10)
            {
                bool major = b % 30 == 0;
                At(b, R, out float ax, out float ay);
                At(b, R - (major ? 6f : 3f), out float cx, out float cy);
                g.Line(ax, ay, cx, cy, major ? Dim : Faint, major ? 0.9f : 1f);
            }
            g.Dashed(CX - R, CY, CX + R, CY, Faint, 0.9f);            // the 3/9 line
            g.Line(CX, CY - R, CX, CY - 12f, Faint);
            At(0f, R + 2f, out float nx, out float ny);
            g.Tri(nx, ny, 0f, 4f, Ink);
        }

        /// <summary>A radar painting us: a spoke in from the rim (solid and red for a lock, dashed and amber for a search), a tag with its name and range outside the rim.</summary>
        private void Spoke(ScopeCanvas g, in ThreatEmitter e)
        {
            bool lockOn = e.Lock;
            ScopeTone tone = lockOn ? Red : Amber;
            float a = ScopeCanvas.Seg(e.AgeS, 0f, 0.3f), b = e.BearingDeg;
            At(b, R, out float x0, out float y0);
            At(b, R * 0.56f, out float x1, out float y1);
            At(b, R + 1f, out float tx, out float ty);
            g.Line(x0, y0, x1, y1, tone, a, lockOn ? 2f : 1f, lockOn ? 0f : 5f, lockOn ? 0f : 3f, -now * 10f);
            if (lockOn) g.Line(x0, y0, x1, y1, Red, 0.35f * a);
            g.Tri(tx, ty, b + 180f, 4f, tone, true, a);
            if (lockOn && e.AgeS < 0.7f) g.Ring(tx, ty, 3f + 22f * ScopeCanvas.EaseOut(e.AgeS / 0.7f), Hi, 1f - e.AgeS / 0.7f);
            // a two-line tag outside the rim, inside it when there is no room: the name bold, the range under it
            string name = Name(e);
            ReadOnlySpan<char> range = ScopeStr.Distance("", e.RangeM, units, 8);
            float w = Math.Max(ScopeCanvas.TextW(name, 12f), ScopeCanvas.TextW(range, 12f)) + 8f, h = 31f;
            At(b, R + 4f, out float anx, out float any);
            float sb = Mathf.Sin(b * Mathf.Deg2Rad), cb = -Mathf.Cos(b * Mathf.Deg2Rad);
            float bx = anx + sb * (w / 2f) - w / 2f, by = any + cb * (h / 2f) - h / 2f;
            float cx2 = Mathf.Clamp(bx, 2f, COL - 10f - w), cy2 = Mathf.Clamp(by, 30f, 296f - h);
            if (Math.Abs(cx2 - bx) + Math.Abs(cy2 - by) > 8f) { At(b, R - 22f, out float qx, out float qy); bx = qx - w / 2f; by = qy - h / 2f; }
            else { bx = cx2; by = cy2; }
            bx = Mathf.Round(bx); by = Mathf.Round(by);
            keep.Add(bx, by, w, h);
            g.Fill(bx, by, w, h, Bg, a);
            g.Box(bx, by, w, h, tone, a, 1f, tone, lockOn ? 0.25f : 0.14f);
            g.FitText(name, bx + w / 2f, by + 9f, w - 4f, tone, 12f, 12f, ScopeAlign.Center, true, a);
            g.Text(range, bx + w / 2f, by + 22f, tone, 12f, ScopeAlign.Center, false, a * 0.95f);
            At(b, R + 1f, out float attx, out float atty);
            g.Line(attx, atty, Mathf.Clamp(attx, bx, bx + w), Mathf.Clamp(atty, by, by + h), tone, a * 0.7f);
        }

        /// <summary>The rim arrow that turns the aircraft to put the missile on its wing line.</summary>
        private void BeamArrow(ScopeCanvas g)
        {
            if (cue != ThreatCue.Beam || near < 0) return;
            float b = d.Missiles[near].BearingDeg, side = cueSide > 0 ? 90f : 270f;
            // the arc runs from the missile's bearing toward the wing line, the short way
            float a0 = cueOff > 0f ? b + 6f : side + 4f, a1 = cueOff > 0f ? side - 4f : b - 6f;
            if (cueOff > 0f) { if (a1 < a0) a1 += 360f; } else if (a1 < a0) a0 -= 360f;
            if (Math.Abs(a1 - a0) < 3f || Math.Abs(a1 - a0) > 180f) return;
            g.Arc(CX, CY, R - 8f, a0, a1, Hi, 1f, 2f, 6f, 3f, (cueOff > 0f ? -1f : 1f) * now * 14f);
            float e = cueOff > 0f ? a1 : a0;
            At(e, R - 8f, out float qx, out float qy);
            g.Tri(qx, qy, cueOff > 0f ? e + 90f : e - 90f, 5f, Hi);
        }

        private void Missile(ScopeCanvas g, int i, bool nearest)
        {
            ThreatMissile m = d.Missiles[i];
            float t = now, b = m.BearingDeg, r = Rr(m.TtiS);
            At(b, r, out float px, out float py);
            // the trail on the scope: where it has been, oldest first, ending at the marker
            int n = 0;
            for (int k = 0; k < m.TrailCount && n < 11; k++)
            {
                At(m.Trail[3 * k], Rr(m.Trail[3 * k + 1]), out trailXY[2 * n], out trailXY[2 * n + 1]);
                n++;
            }
            trailXY[2 * n] = px; trailXY[2 * n + 1] = py;
            n++;
            if (m.Live)
            {
                float al = nearest ? 1f : 0.6f;
                if (nearest)
                {
                    g.Arc(CX, CY, r, b - 38f, b + 38f, Red, 0.7f);
                    g.Ring(CX, CY, r, Red, 0.16f);
                }
                g.Dashed(px, py, CX, CY, Red, nearest ? 0.8f : 0.45f, 1f, -t * 14f);
                g.Trail(trailXY, n, Red, al, nearest ? 2f : 1f, 6);
                g.Chevron(px, py, b + 180f, nearest ? 10f : 8f, Red, true);
                if (nearest) g.Ring(px, py, 13f + 2f * ScopeCanvas.Pulse(t, 2f), Red, 0.55f);
                if (m.AgeS < 0.6f) g.Ring(px, py, 4f + 26f * ScopeCanvas.EaseOut(m.AgeS / 0.6f), Hi, 1f - m.AgeS / 0.6f);
                int sc = 0;
                segs[sc++] = px; segs[sc++] = py; segs[sc++] = CX; segs[sc++] = CY;
                for (int k = 1; k < n && sc + 4 <= segs.Length; k++) { segs[sc++] = trailXY[2 * k - 2]; segs[sc++] = trailXY[2 * k - 1]; segs[sc++] = trailXY[2 * k]; segs[sc++] = trailXY[2 * k + 1]; }
                Tag(g, m, px, py, nearest, sc);
            }
            else
            {
                float fade = ThreatMath.GhostFade(m.DefeatedS);
                if (fade <= 0f) return;
                g.Trail(trailXY, n, Dim, 0.7f * fade, 1f, 4);
                g.Chevron(px, py, b + 180f, 7f, Dim, false, fade);
                bool right = px >= CX;
                g.Text("TRASHED", px + (right ? 11f : -11f), py + 10f, Dim, 12f, right ? ScopeAlign.Left : ScopeAlign.Right, true, fade);
                g.Cross(px, py, 5f, Dim, fade * 0.8f);
            }
        }

        /// <summary>The missile's tag (seeker and seconds for the nearest, seconds for the rest), on the first of six spots that sits on no other label and no track line.</summary>
        private void Tag(ScopeCanvas g, in ThreatMissile m, float px, float py, bool nearest, int segCount)
        {
            int s = (int)Math.Ceiling(Math.Max(0f, m.TtiS));
            ReadOnlySpan<char> label = nearest && !string.IsNullOrEmpty(m.Seeker) ? ScopeStr.Cat(m.Seeker, " ", s, "S") : ScopeStr.Cat("", s, "S");
            float tw = (float)Math.Round(ScopeCanvas.TextW(label, 12f) + 8f), th = 17f;
            keep.Add(px - 10f, py - 10f, 20f, 20f);
            keep.Place(LabelSpots.Threat, px, py, tw, th, new ReadOnlySpan<float>(segs, 0, segCount), 2f, COL - 10f, 32f, 292f, out float x, out float y);
            if (nearest)
            {
                g.Fill(x, y, tw, th, Red);
                g.Text(label, x + tw / 2f, y + th / 2f + 1f, Bg, 12f, ScopeAlign.Center, true);
            }
            else
            {
                g.Fill(x, y, tw, th, Bg);
                g.Box(x, y, tw, th, Red, 0.8f);
                g.Text(label, x + tw / 2f, y + th / 2f + 1f, Red, 12f, ScopeAlign.Center, true);
            }
        }

        // ------------------------------------------------------------------------------------------------ the column

        private void Column(ScopeCanvas g)
        {
            float t = now, x0 = COL;
            bool pulseOn = ScopeCanvas.Blink(t, 2.5f, 0.55f);
            g.Line(x0 - 7f, 34f, x0 - 7f, 296f, Faint);

            // the action cue
            bool hot = cue == ThreatCue.Beam || cue == ThreatCue.Chaff || cue == ThreatCue.Flare;
            int st = cue == ThreatCue.Flare ? ir : cue == ThreatCue.Chaff ? rdr : -1;
            int firedNow = Dispensed(st);
            bool armed = hot && (cue == ThreatCue.Beam || firedNow == 0);
            bool inv = armed && pulseOn;
            ScopeTone tone = cue == ThreatCue.Clear ? Dim : cue == ThreatCue.Spike ? Amber : cue == ThreatCue.Lock ? Red : Hi;
            g.Box(x0, 34f, CW, 46f, tone, 1f, 1f, tone, inv ? 0.9f : hot ? 0.18f : 0.08f, chamTL: 8f);
            ScopeTone ink = inv ? Bg : tone;
            g.FitText(CueWord(), x0 + CW / 2f, 52f, CW - 8f, ink, 18f, 12f, ScopeAlign.Center, true);
            CueSub(g, x0 + CW / 2f, 70f, CW - 6f, inv ? Bg : cue == ThreatCue.Clear ? Dim : Ink, firedNow);

            // the dispensers: the one that answers the nearest seeker is boxed and pulses
            if (ir >= 0) Dispenser(g, ir, x0, 94f, 52f, cue == ThreatCue.Flare, pulseOn, false);
            if (rdr >= 0) Dispenser(g, rdr, x0, 154f, 44f, cue == ThreatCue.Chaff, pulseOn, true);
            if (jam >= 0)
            {
                CmStation s = d.Cm[jam];
                float y3 = 206f;
                bool on = s.Active;
                g.Text("JAMMER", x0, y3, Dim);
                g.Text(SystemsMath.JamWord(s), x0 + CW, y3, on ? Hi : Dim, 12f, ScopeAlign.Right, on);
                g.Bar(x0, y3 + 8f, CW, 9f, s.Charge, Ink);
            }
            g.Line(x0, 232f, x0 + CW, 232f, Faint);

            // one time bar per missile: 20 s full scale with ticks at 5 and 10 s (the two soonest)
            int row = 0;
            for (int pass = 0; pass < 2 && row < 2; pass++)
                for (int i = 0; i < d.MissileCount && row < 2; i++)
                {
                    ThreatMissile m = d.Missiles[i];
                    bool liveRow = m.Live;
                    if (liveRow != (pass == 0)) continue;
                    float y = 244f + row * 26f;
                    row++;
                    if (liveRow)
                    {
                        bool warn = ThreatMath.Warn(m.TtiS);
                        float lw = g.Text(ScopeStr.Cat("M", m.Number, " "), x0, y, Red, 12f, ScopeAlign.Left, true);
                        if (!string.IsNullOrEmpty(m.Seeker)) g.Text(m.Seeker, x0 + lw, y, Red, 12f, ScopeAlign.Left, true);
                        g.Text(ScopeStr.Cat("", (int)Math.Ceiling(Math.Max(0f, m.TtiS)), "S"), x0 + CW, y, Red, 12f, ScopeAlign.Right, true, warn && !pulseOn ? 0.6f : 1f);
                        g.Bar(x0, y + 8f, CW, 9f, float.IsNaN(m.TtiS) ? 0f : m.TtiS / ThreatMath.RingSeconds, Red);
                        g.BarTick(x0, y + 8f, CW, 9f, 0.25f, Dim);
                        g.BarTick(x0, y + 8f, CW, 9f, 0.5f, Dim);
                    }
                    else
                    {
                        float fade = ThreatMath.GhostFade(m.DefeatedS);
                        g.Text(ScopeStr.Cat("M", m.Number, null), x0, y, Dim, 12f, ScopeAlign.Left, false, fade);
                        g.Text("TRASHED", x0 + CW, y, Dim, 12f, ScopeAlign.Right, true, fade);
                        g.Box(x0, y + 8f, CW, 9f, Dim, 0.6f * fade);
                    }
                }
        }

        private string CueWord()
        {
            switch (cue)
            {
                case ThreatCue.Beam: return "BEAM";
                case ThreatCue.Chaff: return "CHAFF";
                case ThreatCue.Flare: return "FLARE";
                case ThreatCue.Done: return "DEFEATED";
                case ThreatCue.Lock: return "LOCK";
                case ThreatCue.Spike: return "SPIKE";
                case ThreatCue.Inbound: return "INBOUND";
                default: return "CLEAR";
            }
        }

        private void CueSub(ScopeCanvas g, float x, float y, float w, ScopeTone tone, int firedNow)
        {
            switch (cue)
            {
                case ThreatCue.Beam:
                    g.FitText(ScopeStr.Cat(cueOff < 0f ? "TURN R " : "TURN L ", (int)Math.Round(Math.Abs(cueOff)), "°"), x, y, w, tone, 12f, 12f, ScopeAlign.Center);
                    break;
                case ThreatCue.Chaff:
                case ThreatCue.Flare:
                    if (firedNow == 0) g.FitText("DISPENSE", x, y, w, tone, 12f, 12f, ScopeAlign.Center);
                    else g.FitText(ScopeStr.Cat("x", firedNow, " FIRED"), x, y, w, tone, 12f, 12f, ScopeAlign.Center);
                    break;
                case ThreatCue.Done:
                    g.FitText(ScopeStr.Cat("M", defeatedIdx >= 0 ? d.Missiles[defeatedIdx].Number : 0, " TRASHED"), x, y, w, tone, 12f, 12f, ScopeAlign.Center);
                    break;
                case ThreatCue.Lock:
                case ThreatCue.Spike:
                    {
                        int e = BestEmitter();
                        if (e >= 0) g.FitText(ScopeStr.Distance(Name(d.Emitters[e]), d.Emitters[e].RangeM, units, 12), x, y, w, tone, 12f, 12f, ScopeAlign.Center);
                        break;
                    }
                case ThreatCue.Inbound:
                    g.FitText(near >= 0 ? ScopeStr.Cat(d.Missiles[near].Seeker, " ", (int)Math.Ceiling(Math.Max(0f, d.Missiles[near].TtiS)), "S") : "", x, y, w, tone, 12f, 12f, ScopeAlign.Center);
                    break;
                default:
                    g.FitText("NO EMITTERS", x, y, w, tone, 12f, 12f, ScopeAlign.Center);
                    break;
            }
        }

        /// <summary>One dispenser: its name and count, its rounds as dots (the ones just fired flash), boxed and pulsing while it is the answer.</summary>
        private void Dispenser(ScopeCanvas g, int s, float x0, float y, float boxH, bool answer, bool pulseOn, bool square)
        {
            CmStation c = d.Cm[s];
            CmLevel level = HudCm.Level(c);
            if (answer) g.Box(x0 - 4f, y - 9f, CW + 8f, boxH, Hi, pulseOn ? 1f : 0.45f, 1f, Ink, 0.08f);
            g.Text(c.Tag == "IR" ? "IR FLARE" : c.Tag == "RDR" ? "RDR CHAFF" : c.Tag, x0, y, answer ? Hi : Dim, 12f, ScopeAlign.Left, answer);
            g.Text(ScopeStr.Int(c.Ammo), x0 + CW, y, level != CmLevel.Ok ? Amber : answer ? Hi : Ink, 12f, ScopeAlign.Right, true);
            SystemsMath.GridDims(c.Max > 0 ? c.Max : c.Ammo, out int cols, out int rows);
            float pitch = Math.Min(7f, Math.Min((CW - 4f) / cols, (boxH - 18f) / rows));
            int flash = s < fired.Length && now < fireUntil[s] ? fired[s] : 0;
            g.DotGrid(x0 + 2f, y + 8f, cols, rows, c.Ammo, Ink, pitch, Math.Max(1f, pitch * 0.29f), square, flash);
        }
    }
}
