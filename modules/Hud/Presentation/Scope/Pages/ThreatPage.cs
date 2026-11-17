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
    /// with its name and range); the notch wedge on the 3/9 line a radar missile is to be steered into and the arrow that turns you there. One outlined Call box in the top-right corner says what to
    /// do (BEAM, CHAFF, FLARE) and only while there is an action; each missile's time is its ring radius and the soonest is the shelf headline; while a dispenser is called for, the stores header pulses DISPENSE on it
    /// (<see cref="CallMask"/>). A missile that lost us stays a moment as a hollow ghost (the shelf says DEFEATED). Fed by <see cref="ThreatScope"/> (Hud's own provider over <c>ThreatWatch</c>); live while anything is inbound, painting us or
    /// has just lost us. Events (a launch, a lock, a defeat) go to the ticker once.
    /// </summary>
    internal sealed class ThreatPage : ScopePageBase
    {
        // The plot is centred; the Call box takes the top-right corner (COL..) while an action is called for; tags may reach RB.
        private const float CX = 220f, CY = 164f, R = 130f, COL = 324f, CW = 108f, RB = 436f;      // the call column holds 11 LABEL letters at the board's type boost ("NO EMITTERS", "M2 SARH" and its seconds) and ends 8 short of the page's corner bracket
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
        private readonly bool[] seenNumber = new bool[Numbers], defeatSaid = new bool[Numbers];
        private readonly System.Collections.Generic.Dictionary<int, float> lockSaid = new System.Collections.Generic.Dictionary<int, float>(8), spikeSaid = new System.Collections.Generic.Dictionary<int, float>(8);
        private readonly int[] baseAmmo = new int[HudCm.MaxStations];
        private readonly KeepOut keep = new KeepOut();
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

        /// <summary>The seeker classes (<see cref="HudCm.IrMask"/> | <see cref="HudCm.RadarMask"/>) whose dispenser the page calls for right now and has not seen fire yet: the stores header pulses DISPENSE on it.</summary>
        public byte CallMask { get; private set; }

        public override ScopePage Id => ScopePage.Threat;

        public override string Title => "THREAT";

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
            for (int i = 0; i < d.MissileCount; i++)
                if (!d.Missiles[i].Live && d.Missiles[i].DefeatedS < DefeatBannerS) recentDefeat = true;
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
                    env.Notice?.Invoke("missile", HudTone.Warning, word + " INBOUND - " + (float.IsNaN(m.TtiS) ? "" : (int)Math.Ceiling(m.TtiS) + "S"), advice);
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
                text = key == headlineKey ? headlineText : ScopeStr.Cat("", s, "S").ToString();
                tone = Red;
                flash = ThreatMath.Warn(d.Missiles[near].TtiS);
            }
            else if (recentDefeat) { key = 1; text = "DEFEATED"; }
            else if (cue == ThreatCue.Lock) { key = 2; text = "LOCK"; tone = Red; flash = true; }
            else
            {
                // the radars painting us are counted here once (the scope draws each one)
                key = 3000 + d.EmitterCount;
                text = key == headlineKey ? headlineText : ScopeStr.Cat("", d.EmitterCount, " RDR").ToString();
                tone = Amber;
            }
            if (key != headlineKey) { headlineKey = key; headlineText = text; }
            headlineTone = tone;
            headlineFlash = flash;
        }

        public override ScopeHeadline Headline() => live ? new ScopeHeadline { Text = headlineText, Tone = headlineTone, Flash = headlineFlash } : default;

        // ------------------------------------------------------------------------------------------------ paint

        public override void Paint(ScopeCanvas g, in PagePaint p)
        {
            if (!ScopeFeeds.TryRead(out d)) return;      // fresh bearings every frame: the page is Fast
            live = true;
            now = p.Now;
            units = p.Units;
            Read();
            Scope(g);
            CallBox(g);
        }

        /// <summary>Nothing inbound, nothing painting us: the empty scope (rings and the aircraft); the shelf says CLEAR.</summary>
        public override void PaintIdle(ScopeCanvas g, in PagePaint p)
        {
            keep.Clear();
            Rings(g);
            g.Jet(CX, CY, 0f, 9f, Ink, true);
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
            if (cue == ThreatCue.Beam || cue == ThreatCue.Chaff || cue == ThreatCue.Flare) keep.Add(COL - 4f, 30f, CW + 8f, 58f);      // the call box, only while it is drawn
            bool sam = d.HasSam && d.SamRings > 0;
            if (sam) keep.Add(3f, 281f, 100f, 14f);          // SAM ENV xN
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
                    int rank = ThreatMath.Rank(d.Emitters, d.EmitterCount, i); bool full = rank < ThreatMath.FullTags;
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
            // the rings carry their own seconds and the nose tick at the top is heading up: no legend words while something is on the scope
            // Only inside an envelope: "x0" said nothing and stood on the radars' tags (observed in game, where no SAM covers the sky; the prototype's mock is always inside one or two).
            if (sam) g.Text(ScopeStr.Cat("SAM ENV x", d.SamRings, null), 5f, 288f, Amber, 12f, ScopeAlign.Left, true);
        }

        /// <summary>
        /// The notch wedge on the 3/9 line the nearest radar missile is to be steered into: hatched, dashed until it is beamed (LAYOUT.md 12.2). Its box grown by 8 is a keep-out (labels stay off the hatch).
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
        }

        private void Rings(ScopeCanvas g)
        {
            for (int k = 0; k < 3; k++)
            {
                int v = k == 0 ? 5 : k == 1 ? 10 : 20;
                float r = Rr(v);
                g.Ring(CX, CY, r, v == 20 ? Dim : Faint, v == 20 ? 0.85f : 1f, 1f, Ink, 0f, v == 20 ? 0f : 4f, v == 20 ? 0f : 3f);
                At(30f, r, out float x, out float y);      // upper right, under the call box's keep-out: at 152 the labels sat in the tags' and missiles' lower half (sim-hud-brow a06/a10); 20S ends at x 312, short of the box (320)
                ReadOnlySpan<char> label = ScopeStr.Cat("", v, "S");
                g.Text(label, x + 3f, y + 1f, Dim, 12f, ScopeAlign.Left, false, 0.55f);
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
        /// <see cref="ThreatMath.FullTags"/> that matter most get the boxed two-line tag; the rest are dimmer, with one unboxed line ("HLT 36KM"), and no words at all while a missile is live.
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
            g.Diamond(tx, ty, 4f, tone, true, a);      // a radar is a diamond, a missile a chevron
            if (lockOn && e.AgeS < 0.7f) g.Ring(tx, ty, 3f + 22f * ScopeCanvas.EaseOut(e.AgeS / 0.7f), Hi, 1f - e.AgeS / 0.7f);
            if (!full && near >= 0) return;      // a missile is live: the lesser radars keep their spokes (bearing) and lose their words, so the missile tags read alone (sim-hud-brow a06/a10)
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
                // a lock is the headline word and the solid red spoke: the tag is the name over the range
                subLen = Copy(ScopeStr.Distance("", e.RangeM, units, 8), sub);
                w = Math.Max(ScopeCanvas.TextW(top.Slice(0, topLen), 12f), ScopeCanvas.TextW(sub.Slice(0, subLen), 12f)) + 8f;
                h = 36f;
                moved = keep.PlaceRadar(CX, CY, R, b, w, h, new ReadOnlySpan<float>(spokeSegs, 0, spokeSegCount), 2f, RB, 30f, 296f, out _, out _);
            }
            if (keep.Count == own) return;      // the keep-out is full: no room to register the tag
            leaders[leaderCount++] = new Leader { X0 = attx, Y0 = atty, Alpha = a * 0.5f, Fade = fade, Own = own, Moved = moved, Tone = tone, Full = full, Lock = lockOn, TopLen = topLen, SubLen = subLen };
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
            keep.Untangle(anchorXY, ownIdx, leaderCount, new ReadOnlySpan<float>(spokeSegs, 0, spokeSegCount), CX, CY, R, 2f, RB);
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
                    if (keep.Overprints(l.Own)) { l.Alpha = 0f; continue; }      // no free spot left: a lesser radar keeps its spoke and loses its words (and its leader)
                    g.Fill(bx, by, w, h, Bg, 0.85f * l.Fade);
                    g.Text(top, bx + w / 2f, by + h / 2f + 1f, l.Lock ? Red : Dim, 12f, ScopeAlign.Center, false, 0.9f * l.Fade);
                    continue;
                }
                g.Fill(bx, by, w, h, Bg, 0.6f * l.Fade);
                g.Text(top, bx + w / 2f, by + 10f, l.Tone, 12f, ScopeAlign.Center, true, l.Fade);
                g.Text(sub, bx + w / 2f, by + 26f, Ink, 12f, ScopeAlign.Center, false, l.Fade * 0.8f);
            }
            for (int i = 0; i < leaderCount; i++) drawLeader[i] = leaders[i].Alpha > 0f && (!leaders[i].Moved || !keep.LeaderCrosses(leaders[i].X0, leaders[i].Y0, leaders[i].X1, leaders[i].Y1, leaders[i].X0, leaders[i].Y0, leaders[i].Own));
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
                    g.Arc(CX, CY, r, b - 38f, b + 38f, Red, 0.7f);      // its time, as its radius
                }
                g.Dashed(px, py, CX, CY, Red, nearest ? 0.8f : 0.45f, 1f, -t * 14f);
                g.Trail(trailXY, n, Red, al, nearest ? 2f : 1f, 6);
                g.Chevron(px, py, b + 180f, nearest ? 10f : 8f, Red, true);
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
                g.Cross(px, py, 5f, Dim, fade * 0.8f);
            }
        }

        /// <summary>The missile's tag (its number, M1 / M2, the same name as its tape mark), on the first of six spots that sits on no other label and no track line.</summary>
        private void Tag(ScopeCanvas g, in ThreatMissile m, float px, float py, bool nearest, int segCount)
        {
            ReadOnlySpan<char> label = ScopeStr.Cat("M", m.Number, null);
            float tw = (float)Math.Round(ScopeCanvas.TextW(label, 12f) + 8f), th = 17f;
            keep.Add(px - 10f, py - 10f, 20f, 20f);
            keep.Place(LabelSpots.Threat, px, py, tw, th, new ReadOnlySpan<float>(segs, 0, segCount), 2f, RB, 32f, 292f, out float x, out float y);
            if (!nearest && keep.Overprints(keep.Count - 1)) return;
            // a tag that had to move away from its missile is tied to it by a short leader that never runs through another label (LAYOUT.md 12.2)
            float nx = Mathf.Clamp(px, x, x + tw), ny = Mathf.Clamp(py, y, y + th), dd = Mathf.Sqrt((px - nx) * (px - nx) + (py - ny) * (py - ny));
            if (dd > 18f)
            {
                float ex = px - (px - nx) / dd * 12f, ey = py - (py - ny) / dd * 12f;
                if (!keep.LeaderCrosses(nx, ny, ex, ey, px, py)) g.Line(nx, ny, ex, ey, Red, nearest ? 0.8f : 0.5f);
            }
            if (nearest)
            {
                g.OpenTag(label, x, y + th / 2f, Red, 12f, ScopeAlign.Left, 4f);
            }
            else
            {
                g.Fill(x, y, tw, th, Bg, 0.6f);
                g.Text(label, x + tw / 2f, y + th / 2f + 1f, Red, 12f, ScopeAlign.Center, true, 0.6f);
            }
        }

        // ------------------------------------------------------------------------------------------------ the call box

        /// <summary>The one box in the corner: what to do (BEAM, CHAFF, FLARE), only while there is an action. An outline, pulsing while it is still to be done; the matching dispenser in the stores header pulses DISPENSE at the same time.</summary>
        private void CallBox(ScopeCanvas g)
        {
            if (cue != ThreatCue.Beam && cue != ThreatCue.Chaff && cue != ThreatCue.Flare) return;
            int fired = Dispensed(cue == ThreatCue.Flare ? ir : cue == ThreatCue.Chaff ? rdr : -1);
            bool armed = cue == ThreatCue.Beam || fired == 0;
            float mid = COL + CW / 2f;
            g.Fill(COL - 4f, 31f, CW + 8f, 56f, Bg, 0.6f);      // knock-out of scope ink behind the box only (not a plate: it is Bg, the box is an outline)
            g.Box(COL, 34f, CW, 50f, armed ? Hi : Dim, armed ? (ScopeCanvas.Blink(now, 2.5f, 0.55f) ? 1f : 0.4f) : 0.8f);
            g.FitText(cue == ThreatCue.Beam ? "BEAM" : cue == ThreatCue.Chaff ? "CHAFF" : "FLARE", mid, 48f, CW - 8f, armed ? Hi : Ink, HudType.Value, 12f, ScopeAlign.Center, true);
            if (cue == ThreatCue.Beam) g.FitText(ScopeStr.Cat(cueOff < 0f ? "TURN R " : "TURN L ", (int)Math.Round(Math.Abs(cueOff)), "°"), mid, 72f, CW - 6f, Ink, 12f, 12f, ScopeAlign.Center, true);
            else if (fired == 0) g.FitText("DISPENSE", mid, 72f, CW - 6f, Ink, 12f, 12f, ScopeAlign.Center, true);
            else g.FitText(ScopeStr.Cat("x", fired, " FIRED"), mid, 72f, CW - 6f, Dim, 12f, 12f, ScopeAlign.Center);
        }
    }
}
