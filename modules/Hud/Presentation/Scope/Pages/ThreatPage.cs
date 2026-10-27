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
    /// with its name and range); the notch wedge on the 3/9 line a radar missile is to be steered into and the arrow that turns you there. Two boxes in the right-hand corners say what to
    /// do (the call word: BEAM, CHAFF, FLARE...) and how long every missile has (one time bar each); while a dispenser is called for, the stores header pulses DISPENSE on it
    /// (<see cref="CallMask"/>). A missile that lost us stays a moment as a hollow ghost marked TRASHED. Fed by <see cref="ThreatScope"/> (Hud's own provider over <c>ThreatWatch</c>); live while anything is inbound, painting us or
    /// has just lost us. Events (a launch, a lock, a defeat) go to the ticker once.
    /// </summary>
    internal sealed class ThreatPage : ScopePageBase
    {
        // The plot is centred; the call box takes the top-right corner (COL..), the time bars the bottom-right; tags may reach RB.
        private const float CX = 220f, CY = 164f, R = 130f, COL = 332f, CW = 90f, RB = 436f;      // COL: LAYOUT.md 12.2 moved the call column 14 left, off the page's corner bracket
        private const float DefeatBannerS = 2.4f;
        private const int Numbers = 32;

        private ThreatScope d;
        private bool live, primed;
        private AvUnits units;
        private float now;
        private int near = -1, ir = -1, rdr = -1, cueMissile, cueSide;
        private float cueOff;
        private ThreatCue cue;
        private bool recentDefeat;
        private int defeatedIdx = -1;
        private readonly bool[] seenNumber = new bool[Numbers], defeatSaid = new bool[Numbers];
        private readonly System.Collections.Generic.Dictionary<int, float> lockSaid = new System.Collections.Generic.Dictionary<int, float>(8), spikeSaid = new System.Collections.Generic.Dictionary<int, float>(8);
        private readonly int[] baseAmmo = new int[HudCm.MaxStations];
        private readonly KeepOut keep = new KeepOut();
        private ScopeRect beam;
        private bool hasBeam;
        private readonly float[] segs = new float[4 * 14], trailXY = new float[2 * 12], wedge = new float[2 * 14];
        /// <summary>The radars' spokes (x1, y1, x2, y2 each, at most eight radars), so a tag that has to move off the others stays off the lines too.</summary>
        private readonly float[] spokeSegs = new float[4 * 8];
        private int spokeSegCount;
        /// <summary>The leader each radar's tag asks for (from its spoke's rim point to its box), kept until every label of the scope is down: a leader is drawn only if it crosses none (<see cref="Leaders"/>).</summary>
        private struct Leader { public float X0, Y0, X1, Y1, Alpha, Fade; public int Own, TopLen, SubLen; public bool Moved, Full, Lock; public ScopeTone Tone; }
        private readonly Leader[] leaders = new Leader[8];
        /// <summary>Each radar tag's words (two lines of <see cref="TagStride"/> per tag), kept from placing to drawing.</summary>
        private readonly char[] tagText = new char[8 * 2 * TagStride];
        private readonly float[] anchorXY = new float[2 * 8];
        private readonly int[] ownIdx = new int[8];
        private readonly bool[] drawLeader = new bool[8];
        private int leaderCount;
        private int cueBaseFor = -1;

        private string headlineText = "";
        private int headlineKey = -1;
        private ScopeTone headlineTone = Dim;
        private bool headlineFlash;
        private int mslKey = int.MinValue, rwrKey = int.MinValue;
        private string mslText, mslSub, rwrText, rwrSub;

        /// <summary>The seeker classes (<see cref="HudCm.IrMask"/> | <see cref="HudCm.RadarMask"/>) whose dispenser the page calls for right now and has not seen fire yet: the stores header pulses DISPENSE on it.</summary>
        public byte CallMask { get; private set; }

        public override ScopePage Id => ScopePage.Threat;

        public override string Title => "THREAT";

        public override char Code => 'T';

        public override string IdleWord => "CLEAR";

        public override string IdleHint => "NO RADAR OR MISSILE WARNING";

        public override bool Fast => live;

        // ------------------------------------------------------------------------------------------------ poll

        public override bool Poll(in ScopeEnv env)
        {
            now = env.Now;
            units = env.Units;
            if (!ScopeFeeds.TryRead(out d)) { live = primed = false; CallMask = 0; near = ir = rdr = -1; Array.Clear(seenNumber, 0, Numbers); Array.Clear(defeatSaid, 0, Numbers); lockSaid.Clear(); spikeSaid.Clear(); return false; }
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
            ir = rdr = -1;
            for (int i = 0; i < d.CmCount; i++)
            {
                if (d.Cm[i].Chargeable) continue;
                if ((d.Cm[i].Mask & HudCm.IrMask) != 0) { if (ir < 0) ir = i; }
                else if ((d.Cm[i].Mask & HudCm.RadarMask) != 0) { if (rdr < 0) rdr = i; }
            }
            // Rounds dispensed since the nearest missile appeared: a cue that was answered reads "x5 FIRED", one that was not "DISPENSE".
            if (cueBaseFor != cueMissile) { cueBaseFor = cueMissile; for (int i = 0; i < d.CmCount && i < baseAmmo.Length; i++) baseAmmo[i] = d.Cm[i].Ammo; }
            int answer = cue == ThreatCue.Flare ? ir : cue == ThreatCue.Chaff ? rdr : -1;
            CallMask = answer >= 0 && Dispensed(answer) == 0 ? (cue == ThreatCue.Flare ? HudCm.IrMask : HudCm.RadarMask) : (byte)0;
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
                if (!e.Held && New(e.Lock ? lockSaid : spikeSaid, e.Id, now - e.AgeS))
                    env.Notice?.Invoke("rwr", e.Lock ? HudTone.Warning : HudTone.Caution, (e.Lock ? "LOCK - " : "SPIKE - ") + Name(e), ThreatMath.RangeDetail(e.RangeM, env.Units));
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
            else
            {
                // the call box already says SPIKE: the header counts the radars painting us instead of saying it twice
                key = 3000 + d.EmitterCount;
                text = key == headlineKey ? headlineText : ScopeStr.Cat("", d.EmitterCount, " RDR").ToString();
                tone = Amber;
            }
            if (key != headlineKey) { headlineKey = key; headlineText = text; }
            headlineTone = tone;
            headlineFlash = flash;
        }

        public override ScopeHeadline Headline() => live ? new ScopeHeadline { Text = headlineText, Tone = headlineTone, Flash = headlineFlash } : default;

        /// <summary>The nearest radar painting us as a needle and the nearest missile inbound as a needle (its bearing, seeker and seconds out).</summary>
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
            if (near >= 0)
            {
                ThreatMissile m = d.Missiles[near];
                int s = Sec(m.TtiS), key = m.Number * 1000 + s;
                if (key != mslKey) { mslKey = key; mslText = string.IsNullOrEmpty(m.Seeker) ? "MSL" : m.Seeker; mslSub = float.IsNaN(m.TtiS) ? "INBOUND" : ScopeStr.Cat("", s, "S OUT").ToString(); }
                into[n++] = new GaugeSpec { Id = "msl", Label = "INBOUND", Kind = GaugeKind.Needle, Angle = m.BearingDeg, Text = mslText, Sub = mslSub, Tone = Red };
            }
            return n;
        }

        /// <summary>The radar to put on the needle: a lock before a spike, then the nearest.</summary>
        private int BestEmitter()
        {
            int best = -1;
            for (int i = 0; i < d.EmitterCount; i++)
                if (best < 0 || ThreatMath.Outranks(d.Emitters[i], d.Emitters[best])) best = i;
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

        /// <summary>Nothing inbound, nothing painting us: the empty scope (rings, the aircraft, its words) and CLEAR where the call box goes, so a quiet sky reads as watched, not as no data.</summary>
        public override void PaintIdle(ScopeCanvas g, in PagePaint p)
        {
            keep.Clear();
            Rings(g);
            g.Jet(CX, CY, 0f, 9f, Ink, true);
            g.Text("TTI RINGS", 5f, 41f, Dim);
            g.Text("HDG UP", 5f, 56f, Faint);
            g.Text(IdleWord, COL + CW / 2f, 52f, Hi, 14f, ScopeAlign.Center, true);
            g.Text("NO WARNING", COL + CW / 2f, 70f, Dim, 12f, ScopeAlign.Center);
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
            hasBeam = false;
            keep.Add(CX - 13f, CY - 13f, 26f, 26f);
            keep.Add(COL - 4f, 30f, CW + 8f, 54f);          // the call box
            keep.Add(COL - 4f, 230f, CW + 8f, 66f);         // the time bars
            keep.Add(3f, 34f, 76f, 30f);                    // TTI RINGS and HDG UP: the radars' tags dodge the page's own words too
            bool sam = d.HasSam && d.SamRings > 0;
            if (sam) keep.Add(3f, 281f, 90f, 14f);          // SAM ENV xN
            for (int i = 0; i < d.MissileCount; i++)
            {
                At(d.Missiles[i].BearingDeg, Rr(d.Missiles[i].TtiS), out float mx, out float my);
                keep.Add(mx - 11f, my - 11f, 22f, 22f);
            }
            spokeSegCount = leaderCount = 0;
            for (int i = 0; i < d.EmitterCount && spokeSegCount + 4 <= spokeSegs.Length; i++, spokeSegCount += 4)
            {
                At(d.Emitters[i].BearingDeg, R, out spokeSegs[spokeSegCount], out spokeSegs[spokeSegCount + 1]);
                At(d.Emitters[i].BearingDeg, R * ThreatMath.SpokeIn(d.Emitters[i].RangeM), out spokeSegs[spokeSegCount + 2], out spokeSegs[spokeSegCount + 3]);
            }
            Wedge(g);
            Rings(g);
            // the radars that matter most place their full tags first, so the one-line tags of the rest take what room is left
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < d.EmitterCount; i++)
                {
                    bool full = ThreatMath.Rank(d.Emitters, d.EmitterCount, i) < ThreatMath.FullTags;
                    if (full == (pass == 0)) Spoke(g, d.Emitters[i], full);
                }
            BeamArrow(g);
            // the nearest missile last, so it is on top
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < d.MissileCount; i++)
                    if ((i == near) == (pass == 1)) Missile(g, i, i == near);
            Leaders(g);
            g.Jet(CX, CY, 0f, 9f, Ink, true);
            if (near >= 0 && d.Missiles[near].TtiS <= ThreatMath.WarnS) g.Ring(CX, CY, 15f, Red, 0.35f + 0.5f * ScopeCanvas.Pulse(t, 3f));
            g.Text("TTI RINGS", 5f, 41f, Dim);
            g.Text("HDG UP", 5f, 56f, Faint);
            // Only inside an envelope: "x0" said nothing and stood on the radars' tags (observed in game, where no SAM covers the sky; the prototype's mock is always inside one or two).
            if (sam) g.Text(ScopeStr.Cat("SAM ENV x", d.SamRings, null), 5f, 288f, Amber, 12f, ScopeAlign.Left, true);
        }

        /// <summary>
        /// The notch wedge on the 3/9 line the nearest radar missile is to be steered into: hatched, dashed until it is beamed (LAYOUT.md 12.2). Its box grown by 8 is a keep-out (labels stay off the hatch) and the
        /// BEAM / BEAMED tag hangs 8 under its lowest point, 14 left of its inner end, which keeps it clear of the nearest missile's arc.
        /// </summary>
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
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                x0 = Math.Min(x0, wedge[2 * i]); x1 = Math.Max(x1, wedge[2 * i]);
                y0 = Math.Min(y0, wedge[2 * i + 1]); y1 = Math.Max(y1, wedge[2 * i + 1]);
            }
            keep.AddWedge(x0 - 8f, y0 - 8f, x1 - x0 + 16f, y1 - y0 + 16f);
            At(side, R * 0.38f, out float lx, out _);
            beam = g.PlateTag(on ? "BEAMED" : "BEAM", lx - 14f, Mathf.Floor(y1 + 17f), on ? Hi : Ink, 12f, ScopeAlign.Center);
            hasBeam = true;
            keep.Add(beam.X, beam.Y, beam.W, beam.H);
        }

        /// <summary>Whether the point of the TTI ring of radius <paramref name="r"/> at bearing <paramref name="a"/> is within 6 of the BEAM tag.</summary>
        private bool OnBeamTag(float a, float r)
        {
            At(a, r, out float x, out float y);
            return x > beam.X - 6f && x < beam.X + beam.W + 6f && y > beam.Y - 6f && y < beam.Y + beam.H + 6f;
        }

        private void Rings(ScopeCanvas g)
        {
            for (int k = 0; k < 3; k++)
            {
                int v = k == 0 ? 5 : k == 1 ? 10 : 20;
                float r = Rr(v);
                g.Ring(CX, CY, r, v == 20 ? Dim : Faint, v == 20 ? 0.85f : 1f, 1f, Ink, 0f, v == 20 ? 0f : 4f, v == 20 ? 0f : 3f);
                At(152f, r, out float x, out float y);
                ReadOnlySpan<char> label = ScopeStr.Cat("", v, "S");
                g.Text(label, x + 3f, y + 1f, Dim, 12f, ScopeAlign.Left, false, 0.9f);
                keep.Add(x - 4f, y - 8f, (float)Math.Round(ScopeCanvas.TextW(label)) + 16f, 18f);      // a ring's label is a keep-out too: the tags dodge it (LAYOUT.md 12.2)
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

        /// <summary>
        /// A radar painting us: a spoke in from the rim, the deeper the nearer it is (solid and red for a lock, dashed and amber for a search), a tag with its name and range outside the rim. Only the
        /// <see cref="ThreatMath.FullTags"/> that matter most get the boxed two-line tag; the rest are dimmer, with one unboxed line ("HLT 36KM").
        /// </summary>
        private void Spoke(ScopeCanvas g, in ThreatEmitter e, bool full)
        {
            bool lockOn = e.Lock;
            ScopeTone tone = lockOn ? Red : Amber;
            // a one-line tag's spoke steps back (0.6); its words do not drop below 0.9 on their own plate, which is what keeps them readable over a bright sky
            float fade = ScopeCanvas.Seg(e.AgeS, 0f, 0.3f), a = fade * (full ? 1f : 0.6f), b = e.BearingDeg;
            At(b, R, out float x0, out float y0);
            At(b, R * ThreatMath.SpokeIn(e.RangeM), out float x1, out float y1);
            At(b, R + 1f, out float tx, out float ty);
            g.Line(x0, y0, x1, y1, tone, a, lockOn ? 2f : 1f, lockOn ? 0f : 5f, lockOn ? 0f : 3f, -now * 10f);
            if (lockOn) g.Line(x0, y0, x1, y1, Red, 0.35f * a);
            g.Tri(tx, ty, b + 180f, 4f, tone, true, a);
            if (lockOn && e.AgeS < 0.7f) g.Ring(tx, ty, 3f + 22f * ScopeCanvas.EaseOut(e.AgeS / 0.7f), Hi, 1f - e.AgeS / 0.7f);
            At(b, R + 1f, out float attx, out float atty);
            int own = keep.Count, k = leaderCount;
            if (k >= leaders.Length) return;
            // the tag is placed now (later labels dodge it) and drawn in Leaders, after the crossing leaders have traded places
            Span<char> top = new Span<char>(tagText, k * 2 * TagStride, TagStride), sub = new Span<char>(tagText, (k * 2 + 1) * TagStride, TagStride);
            int topLen, subLen;
            float w, h;
            bool moved;
            if (!full)
            {
                // one line, the call box's own form: the whole words that fit with the range whole
                topLen = Copy(ScopeStr.Distance(Name(e), e.RangeM, units, 10, true), top);
                subLen = 0;
                w = (float)Math.Round(ScopeCanvas.TextW(top.Slice(0, topLen), 12f)) + 6f;
                h = 16f;
                moved = keep.PlaceRadar(CX, CY, R, b, w, h, new ReadOnlySpan<float>(spokeSegs, 0, spokeSegCount), 2f, RB, 30f, 296f, out _, out _, atRim: false);
            }
            else
            {
                // a two-line tag outside the rim: the name bold (the whole words that fit TagChars: the game's run to 22 characters), the range under it
                topLen = ThreatMath.TagName(Name(e), top);
                // a lock says so in words: the open visor style draws every plate as brackets, so red against amber would be the only difference (colour alone)
                subLen = Copy(ScopeStr.Distance(lockOn ? "LOCK" : "", e.RangeM, units, lockOn ? 12 : 8), sub);
                w = Math.Max(ScopeCanvas.TextW(top.Slice(0, topLen), 12f), ScopeCanvas.TextW(sub.Slice(0, subLen), 12f)) + 8f;
                h = 31f;
                moved = keep.PlaceRadar(CX, CY, R, b, w, h, new ReadOnlySpan<float>(spokeSegs, 0, spokeSegCount), 2f, RB, 30f, 296f, out _, out _);
            }
            if (keep.Count == own) return;      // the keep-out is full: no room to register the tag
            leaders[leaderCount++] = new Leader { X0 = attx, Y0 = atty, Alpha = a * 0.7f, Fade = fade, Own = own, Moved = moved, Tone = tone, Full = full, Lock = lockOn, TopLen = topLen, SubLen = subLen };
        }

        private const int TagStride = 16;

        private static int Copy(ReadOnlySpan<char> s, Span<char> into)
        {
            int n = Math.Min(s.Length, into.Length);
            s.Slice(0, n).CopyTo(into);
            return n;
        }

        /// <summary>
        /// The radars' tags and leaders, once every label is down: two leaders that cross have their tags trade places first (<see cref="KeepOut.Untangle"/>: a stack of tags ends up in the order of its spokes), then each tag is
        /// drawn, then a tag that had to move away from its spoke is tied to it only by a line that crosses no label (one drawn as its tag was placed ran through the tags placed after it: "HLT R|ADAR" in game); a tag
        /// that stands at its spoke always has its short line.
        /// </summary>
        private void Leaders(ScopeCanvas g)
        {
            for (int i = 0; i < leaderCount; i++)
            {
                anchorXY[2 * i] = leaders[i].X0; anchorXY[2 * i + 1] = leaders[i].Y0; ownIdx[i] = leaders[i].Own;
                keep.Get(leaders[i].Own, out leaders[i].X1, out leaders[i].Y1, out _, out _);      // where it was placed, to tell a traded tag below
            }
            keep.Untangle(anchorXY, ownIdx, leaderCount, new ReadOnlySpan<float>(spokeSegs, 0, spokeSegCount), CX, CY, R);
            for (int i = 0; i < leaderCount; i++)
            {
                ref Leader l = ref leaders[i];
                keep.Get(l.Own, out float bx, out float by, out float w, out float h);
                l.Moved |= bx != l.X1 || by != l.Y1;      // a traded tag is off its own spot: its leader obeys the crossing rule too
                l.X1 = Mathf.Clamp(l.X0, bx, bx + w);
                l.Y1 = Mathf.Clamp(l.Y0, by, by + h);
                ReadOnlySpan<char> top = new ReadOnlySpan<char>(tagText, i * 2 * TagStride, l.TopLen), sub = new ReadOnlySpan<char>(tagText, (i * 2 + 1) * TagStride, l.SubLen);
                if (!l.Full)
                {
                    g.Fill(bx, by, w, h, Bg, 0.85f * l.Fade);
                    g.Text(top, bx + w / 2f, by + h / 2f + 1f, l.Tone, 12f, ScopeAlign.Center, false, 0.9f * l.Fade);
                    continue;
                }
                g.Fill(bx, by, w, h, Bg, l.Fade);
                g.Box(bx, by, w, h, l.Tone, l.Fade, 1f, l.Tone, l.Lock ? 0.25f : 0.14f);
                g.Text(top, bx + w / 2f, by + 9f, l.Tone, 12f, ScopeAlign.Center, true, l.Fade);
                g.Text(sub, bx + w / 2f, by + 22f, l.Tone, 12f, ScopeAlign.Center, l.Lock, l.Fade * 0.95f);
            }
            for (int i = 0; i < leaderCount; i++) drawLeader[i] = !leaders[i].Moved || !keep.LeaderCrosses(leaders[i].X0, leaders[i].Y0, leaders[i].X1, leaders[i].Y1, leaders[i].X0, leaders[i].Y0, leaders[i].Own);
            keep.DropCrossing(anchorXY, ownIdx, leaderCount, drawLeader);
            for (int i = 0; i < leaderCount; i++)
            {
                Leader l = leaders[i];
                if (drawLeader[i]) g.Line(l.X0, l.Y0, l.X1, l.Y1, l.Tone, l.Alpha);
            }
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
                    // the arc of its TTI ring stops 2 degrees short of the BEAM tag instead of running through its underline
                    float a0 = b - 38f, a1 = b + 38f;
                    if (hasBeam)
                    {
                        for (float a = b + 1f; a <= b + 38f; a++) if (OnBeamTag(a, r)) { a1 = a - 2f; break; }
                        for (float a = b - 1f; a >= b - 38f; a--) if (OnBeamTag(a, r)) { a0 = a + 2f; break; }
                    }
                    if (a1 > a0 + 1f) g.Arc(CX, CY, r, a0, a1, Red, 0.7f);
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
            keep.Place(LabelSpots.Threat, px, py, tw, th, new ReadOnlySpan<float>(segs, 0, segCount), 2f, RB, 32f, 292f, out float x, out float y);
            // a tag that had to move away from its missile is tied to it by a short leader that never runs through another label (LAYOUT.md 12.2)
            float nx = Mathf.Clamp(px, x, x + tw), ny = Mathf.Clamp(py, y, y + th), dd = Mathf.Sqrt((px - nx) * (px - nx) + (py - ny) * (py - ny));
            if (dd > 18f)
            {
                float ex = px - (px - nx) / dd * 12f, ey = py - (py - ny) / dd * 12f;
                if (!keep.LeaderCrosses(nx, ny, ex, ey, px, py)) g.Line(nx, ny, ex, ey, Red, nearest ? 0.8f : 0.5f);
            }
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

        // ------------------------------------------------------------------------------------------------ the corner boxes

        /// <summary>
        /// Two boxes in the right-hand corners and nothing else: the call word at the top (BEAM, CHAFF, FLARE...; the matching dispenser in the stores header pulses DISPENSE at the same
        /// time) and one time bar per missile at the bottom. The dispensers and the jammer live in the stores header, so the polar scope has the whole width.
        /// </summary>
        private void Column(ScopeCanvas g)
        {
            float t = now, x0 = COL;
            bool pulseOn = ScopeCanvas.Blink(t, 2.5f, 0.55f);

            bool hot = cue == ThreatCue.Beam || cue == ThreatCue.Chaff || cue == ThreatCue.Flare;
            int st = cue == ThreatCue.Flare ? ir : cue == ThreatCue.Chaff ? rdr : -1;
            int firedNow = Dispensed(st);
            bool armed = hot && (cue == ThreatCue.Beam || firedNow == 0);
            bool inv = armed && pulseOn;
            ScopeTone tone = cue == ThreatCue.Clear ? Dim : cue == ThreatCue.Spike ? Amber : cue == ThreatCue.Lock ? Red : Hi;
            g.Fill(x0 - 4f, 31f, CW + 8f, 52f, Bg, 0.92f);
            g.Box(x0, 34f, CW, 46f, tone, 1f, 1f, tone, inv ? 0.9f : hot ? 0.18f : 0.08f, chamTL: 8f);
            ScopeTone ink = inv ? Bg : tone;
            g.FitText(CueWord(), x0 + CW / 2f, 52f, CW - 8f, ink, 18f, 12f, ScopeAlign.Center, true);
            CueSub(g, x0 + CW / 2f, 70f, CW - 6f, inv ? Bg : cue == ThreatCue.Clear ? Dim : Ink, firedNow);

            // one time bar per missile: 20 s full scale with ticks at 5 and 10 s (the two soonest)
            g.Fill(x0 - 4f, 231f, CW + 8f, 64f, Bg, 0.92f);
            int row = 0;
            for (int pass = 0; pass < 2 && row < 2; pass++)
                for (int i = 0; i < d.MissileCount && row < 2; i++)
                {
                    ThreatMissile m = d.Missiles[i];
                    bool liveRow = m.Live;
                    if (liveRow != (pass == 0)) continue;
                    float y = 242f + row * 29f;
                    row++;
                    if (liveRow)
                    {
                        bool warn = ThreatMath.Warn(m.TtiS);
                        float lw = g.Text(ScopeStr.Cat("M", m.Number, " "), x0, y, Red, 12f, ScopeAlign.Left, true);
                        if (!string.IsNullOrEmpty(m.Seeker)) g.Text(m.Seeker, x0 + lw, y, Red, 12f, ScopeAlign.Left, true);
                        g.Text(ScopeStr.Cat("", Sec(m.TtiS), "S"), x0 + CW, y, Red, 12f, ScopeAlign.Right, true, warn && !pulseOn ? 0.6f : 1f);
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

        /// <summary>Whole seconds to go for a figure (never below 0); an unknown time reads 0.</summary>
        private static int Sec(float ttiS) => float.IsNaN(ttiS) ? 0 : (int)Math.Ceiling(Math.Max(0f, ttiS));

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
                        // The characters the box holds (10 in 84 units): the name gives way to the range, never the other way (12 overran the box in game and FitText cut "HYPERI... 3.."), and it gives way by whole words,
                        // not an ellipsis (the game's font draws one two and a half cells wide: "HYPE... 37KM" ran to the box's brackets, and a cut after a space read "MSV ... 19KM").
                        if (e >= 0) g.FitText(ScopeStr.Distance(Name(d.Emitters[e]), d.Emitters[e].RangeM, units, (int)(w / (ScopeText.DesignAdvance * 12f)), true), x, y, w, tone, 12f, 12f, ScopeAlign.Center);
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
    }
}
