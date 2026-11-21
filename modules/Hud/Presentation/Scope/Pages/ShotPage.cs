using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// SHOT (prototype: <c>pages/shot.js</c>, the top-down plan): our own missiles and bombs from launch to result. Heading up, the aircraft bottom-centre, range rings that
    /// pick their own scale (5 to 320 km, sliding when it changes). Each delivery is an arrowhead with a fading trail and a line to the place it is heading: dashed while it
    /// flies on the data-link or its seeker has not locked, solid once it has (an ARH carries an <b>A</b> tag and flashes at its pitbull), a bomb dashed to its splash point; beads
    /// travel along a dashed line; a shrinking ring around the target counts the time; tags carry the seconds and the phase (BOOST, MID, ACTIVE, TERM, FALL) over the target's name.
    /// A weapon that lost its living target turns hollow and flies off; results are stamped where they happened (HIT with a burst, MISS, DEFEATED in amber). Under the plan the
    /// SHOT TIMELINE holds the newest seven shots with a glyph for each result. Fed by <see cref="ShotScope"/> (Hud's own provider over the tracker, the book and the ledger);
    /// live while a delivery is in the air or a result is fresh. Events (away, pitbull, results) go to the ticker once.
    /// </summary>
    internal sealed class ShotPage : ScopePageBase
    {
        private const float OX = 220f;
        /// <summary>The aircraft's y on the plan: 16 above the body's foot (258 on a full page; the FIRES panel is shorter).</summary>
        private float OY = 258f;
        /// <summary>The right edge a tag may reach: the page's width less 2 (was a fixed 438 for a 440 page).</summary>
        private float xMax = 438f;
        private const int Cap = 8, Chips = 7;

        private ShotScope d;
        private bool live;
        private AvUnits units;
        private float now;
        private int lead = -1;
        private readonly RangeScale scale = new RangeScale();
        private readonly KeepOut keep = new KeepOut();
        private readonly float[] segs = new float[4 * 2], trailXY = new float[2 * (MissilePath.RecentSamples + 2)], ringKm = new float[8];
        private readonly int[] seenId = new int[Cap], pitSaid = new int[Cap];
        private readonly ShotState[] resultState = new ShotState[ShotLedger.Capacity];
        private string headlineText = "";
        private int headlineKey = -1, hitsKey = int.MinValue, targetKey = int.MinValue, leadTarget = -1;
        private bool leadOff;
        private ScopeTone headlineTone = Hi;
        private string hitsText, tgtText, tgtSub;
        private readonly float[] hitTicks = new float[ShotLedger.Capacity];

        public override ScopePage Id => ScopePage.Shot;

        public override string Title => "SHOT";

        public override string IdleWord => "NO SHOTS";

        public override string IdleHint => "NONE OF YOUR WEAPONS IN FLIGHT";

        public override bool Fast => live;

        public override bool HasStrip => live;

        // ------------------------------------------------------------------------------------------------ poll

        public override bool Poll(in ScopeEnv env)
        {
            now = env.Now;
            units = env.Units;
            if (!ScopeFeeds.TryRead(out d)) { live = false; lead = -1; Array.Clear(seenId, 0, Cap); Array.Clear(pitSaid, 0, Cap); Array.Clear(resultState, 0, resultState.Length); scale.Reset(); return false; }
            live = true;
            lead = ShotMath.Lead(d.Missiles, d.MissileCount);
            Edges(env);
            ReadHeadline();
            return true;
        }

        /// <summary>Events once: a delivery away, an ARH going active, a result.</summary>
        private void Edges(in ScopeEnv env)
        {
            for (int i = 0; i < d.MissileCount; i++)
            {
                ShotMissile m = d.Missiles[i];
                if (!Has(seenId, m.Id))
                {
                    Add(seenId, m.Id);
                    env.Notice?.Invoke("shot", HudTone.Info, (m.Kind == ShotKind.Bomb ? "RELEASE " : "AWAY ") + m.Full, m.Target >= 0 ? d.Targets[m.Target].Name : null);
                }
                if (m.Pitbull && m.Arh && !Has(pitSaid, m.Id))
                {
                    Add(pitSaid, m.Id);
                    if (m.PitbullS < 2f) env.Notice?.Invoke("shot", HudTone.Info, "PITBULL - " + m.Short + " ACTIVE", m.Target >= 0 ? d.Targets[m.Target].Name : null);
                }
            }
            for (int k = 0; k < d.CellCount; k++)
            {
                ShotCell c = d.Cells[k];
                if (k >= resultState.Length) break;
                bool resolved = c.State == ShotState.Hit || c.State == ShotState.Miss || c.State == ShotState.Defeated;
                if (resolved && c.AgeS < 1f && resultState[k] != c.State && c.State != ShotState.Miss)
                    env.Notice?.Invoke("shot", c.State == ShotState.Hit ? HudTone.Info : HudTone.Caution, c.Name + (c.State == ShotState.Hit ? " - HIT" : " - DEFEATED"), null);
                resultState[k] = c.State;
            }
        }

        private static bool Has(int[] set, int id)
        {
            for (int i = 0; i < set.Length; i++) if (set[i] == id) return true;
            return false;
        }

        private static void Add(int[] set, int id)
        {
            for (int i = 0; i < set.Length; i++) if (set[i] == 0) { set[i] = id; return; }
            Array.Copy(set, 1, set, 0, set.Length - 1);
            set[set.Length - 1] = id;
        }

        private void ReadHeadline()
        {
            int key;
            string text = null;
            ScopeTone tone = Hi;
            if (lead >= 0)
            {
                ShotMissile m = d.Missiles[lead];
                if (!float.IsNaN(m.TtiS)) { key = 1000 + (int)Math.Ceiling(m.TtiS); if (key != headlineKey) text = ScopeStr.Cat("", (int)Math.Ceiling(m.TtiS), "S").ToString(); }
                else { key = 5000 + (int)m.Phase; if (key != headlineKey) text = ShotMath.PhaseWord(m.Phase); }
            }
            else
            {
                ShotState st = ShotState.Empty;
                for (int k = d.CellCount - 1; k >= 0 && st == ShotState.Empty; k--)
                    if (d.Cells[k].State != ShotState.InFlight && d.Cells[k].AgeS < ShotMath.ResultS) st = d.Cells[k].State;
                key = 10 + (int)st;
                if (key != headlineKey) text = st == ShotState.Hit ? "HIT" : st == ShotState.Defeated ? "DEFEATED" : st == ShotState.Miss ? "MISS" : "SHOT";
                tone = st == ShotState.Defeated ? Amber : st == ShotState.Miss ? Dim : Hi;
            }
            if (key != headlineKey) { headlineKey = key; headlineText = text ?? headlineText; }
            headlineTone = tone;
        }

        public override ScopeHeadline Headline() => live ? new ScopeHeadline { Text = headlineText, Tone = headlineTone } : default;

        /// <summary>How the last shots ended, and the lead shot's target as a needle only while it is off the plan. The seconds to impact are the headline's and the strip's; the weapon and its rounds are the stores header's.</summary>
        public override int Gauges(GaugeSpec[] into)
        {
            if (!live) return 0;
            int n = 0;
            int hits = 0, shots = 0;
            for (int i = 0; i < d.CellCount; i++)
            {
                if (d.Cells[i].State == ShotState.InFlight || d.Cells[i].State == ShotState.Empty) continue;
                shots++;
                if (d.Cells[i].State == ShotState.Hit) hits++;
            }
            if (shots > 0)
            {
                int key = hits * 100 + shots;
                if (key != hitsKey)
                {
                    hitsKey = key; hitsText = ScopeStr.Cat("", hits, "/", shots, null).ToString();
                    for (int t = 1; t < shots && t <= hitTicks.Length; t++) hitTicks[t - 1] = (float)t / shots;      // one notch between shots
                }
                into[n++] = new GaugeSpec { Id = "hits", Label = "HITS", Kind = GaugeKind.Bar, Value = (float)hits / shots, Text = hitsText, Tone = Ink, Ticks = hitTicks, TickCount = Math.Min(shots - 1, hitTicks.Length), TickTone = Bg };
            }
            if (d.HasLead && leadOff)
            {
                int key = d.LeadName.GetHashCode() * 31 + (int)(d.LeadRangeM / 100f) + (units == AvUnits.Imperial ? 1 : 0);
                if (key != targetKey) { targetKey = key; tgtText = d.LeadName; tgtSub = ScopeStr.Distance("", d.LeadRangeM, units, 7).ToString(); }
                into[n++] = new GaugeSpec { Id = "tgt", Label = "TARGET", Kind = GaugeKind.Needle, Angle = d.LeadBearingDeg, Text = tgtText, Sub = tgtSub, Tone = Ink };
            }
            return n;
        }

        // ------------------------------------------------------------------------------------------------ paint

        public override void Paint(ScopeCanvas g, in PagePaint p)
        {
            live = ScopeFeeds.TryRead(out d);      // a shot can expire between the slower Poll and this frame's paint
            if (!live) { lead = -1; return; }
            now = p.Now;
            units = p.Units;
            lead = ShotMath.Lead(d.Missiles, d.MissileCount);
            scale.Update(d.NeedKm, now);
            OY = p.H - 16f;
            xMax = p.W - 2f;
            float k = (OY - 36f) / scale.Km;
            keep.Clear();
            keep.Add(OX - 12f, OY - 12f, 24f, 24f);      // own ship first: the target names placed below dodge it too (they were placed before it)
            leadTarget = lead >= 0 ? d.Missiles[lead].Target : -1;
            leadOff = false;
            Grid(g, k);
            for (int i = 0; i < d.TargetCount; i++) Target(g, i, k);
            for (int i = 0; i < d.MissileCount; i++) if (i != lead) Shot(g, i, k);
            if (lead >= 0) Shot(g, lead, k);
            g.Jet(OX, OY, 0f, 9f, Ink, true);
            for (int i = 0; i < d.MissileCount; i++) if (!d.Missiles[i].Ghost) TtiRing(g, i, k);
            if (lead >= 0) Tags(g, lead, k, p.H - 4f);      // the lead's tags first: it always gets a free spot, the rest drop out when none is left
            for (int i = 0; i < d.MissileCount; i++) if (i != lead) Tags(g, i, k, p.H - 4f);
            for (int i = 0; i < d.ResultCount; i++) Result(g, d.Results[i], k, p.H);
        }

        /// <summary>A global place on the plan: pixels from the aircraft's pose.</summary>
        private void Plan(in GPos pos, float k, out float x, out float y)
        {
            d.Frame.Project(pos.X, pos.Z, out float right, out float fwd);
            x = OX + right / 1000f * k;
            y = OY - fwd / 1000f * k;
        }

        private void Grid(ScopeCanvas g, float k)
        {
            int rings = ShotMath.RingValues(scale.Km, k, OY - 28f, ringKm);
            int shown = (int)Math.Round(scale.Km);
            for (int i = 0; i < rings; i++)
            {
                float v = ringKm[i], r = v * k;
                bool current = Math.Abs(v - shown) < 0.5f;
                g.Ring(OX, OY, r, current ? Dim : Faint);
                if (OY - r > 30f)
                {
                    ReadOnlySpan<char> label = current ? ScopeStr.Cat("", (int)v, "KM") : v == Math.Floor(v) ? ScopeStr.Cat("", (int)v, null) : ScopeStr.Cat("", v, 1, null);
                    g.Text(label, OX - 4f, OY - r + 1f, current ? Ink : Dim, 12f, ScopeAlign.Right);
                    float lw = Mathf.Round(ScopeCanvas.TextW(label));
                    keep.Add(OX - 4f - lw - 2f, OY - r - 8f, lw + 4f, 16f);      // a ring's label is a keep-out: the target tags and the missile tags dodge it (LAYOUT.md 9: it was under them)
                }
            }
            g.Line(OX, OY, OX, 30f, Faint);
            for (int b = -60; b <= 60; b += 30)
            {
                ScopeCanvas.Polar(b, 14f, out float ax, out float ay);
                ScopeCanvas.Polar(b, 20f, out float cx, out float cy);
                g.Line(OX + ax, OY + ay, OX + cx, OY + cy, Faint);
            }
        }

        private void Target(ScopeCanvas g, int i, float k)
        {
            ShotTarget t = d.Targets[i];
            Plan(t.Pos, k, out float x, out float y);
            if (x < -10f || x > 450f || y < 28f) { if (i == leadTarget) leadOff = true; return; }          // off the plan: the gauge's needle says where
            ScopeTone tone = t.Hot ? Hi : Ink;
            if (t.Air) g.Diamond(x, y, 5f, tone, false);
            else g.Box(Mathf.Round(x - 4f), Mathf.Round(y - 4f), 9f, 9f, tone);
            if (t.Hot) g.Brackets(Mathf.Round(x - 10f), Mathf.Round(y - 10f), 20f, 20f, 5f, Hi);
            if (t.Air)
            {
                // the aspect tick: where it is going (its ground velocity turned into the plan)
                float vr = t.VelX * d.Frame.Cos - t.VelZ * d.Frame.Sin, vf = t.VelX * d.Frame.Sin + t.VelZ * d.Frame.Cos, len = Mathf.Sqrt(vr * vr + vf * vf);
                if (len > 20f) g.Line(x + vr / len * 6f, y - vf / len * 6f, x + vr / len * 15f, y - vf / len * 15f, Dim);
            }
            keep.Add(x - 9f, y - 9f, 18f, 18f);
            if (!t.Hot)
            {
                float w = Mathf.Round(ScopeCanvas.TextW(t.Name, 12f)) + 4f;
                int at = keep.Count;
                keep.Place(LabelSpots.Shot, x, y, w, 17f, default, 2f, xMax, 32f, OY + 12f, out float lx, out float ly);
                if (Free(at)) g.Text(t.Name, lx + 2f, ly + 9f, Dim, 12f);
            }
        }

        private void Shot(ScopeCanvas g, int i, float k)
        {
            ShotMissile m = d.Missiles[i];
            bool isLead = i == lead, ghost = m.Ghost;
            Plan(m.Pos, k, out float mx, out float my);
            // the trail, oldest first, ending at the marker
            int n = 0;
            for (int s = 0; s < m.TrailCount && n < MissilePath.RecentSamples; s++)
            {
                d.Frame.Project(m.Trail[3 * s], m.Trail[3 * s + 2], out float right, out float fwd);
                trailXY[2 * n] = OX + right / 1000f * k;
                trailXY[2 * n + 1] = OY - fwd / 1000f * k;
                n++;
            }
            trailXY[2 * n] = mx; trailXY[2 * n + 1] = my;
            n++;
            float fade = ghost ? 1f - ScopeCanvas.Seg(m.GhostS, 0f, ShotMath.GhostS) : 1f;
            g.Trail(trailXY, n, ghost ? Dim : Ink, ghost ? 0.6f * fade : isLead ? 1f : 0.75f, isLead ? 2f : 1f, 6);
            if (ghost)
            {
                g.Chevron(mx, my, m.HeadingDeg, 6f, Dim, false, fade);
                return;
            }
            bool active = m.Kind == ShotKind.Missile && (m.Arh ? m.Pitbull : m.Phase == ShotPhase.Active || m.Phase == ShotPhase.Term);
            float tx = 0f, ty = 0f;
            bool aim = m.HasAim;
            if (aim) Plan(m.Aim, k, out tx, out ty);
            if (aim)
            {
                float al = isLead ? 0.95f : 0.8f;
                if (m.Kind == ShotKind.Bomb) g.Line(mx, my, tx, ty, Ink, 0.9f, 1f, 3f, 3f);
                else if (active)
                {
                    bool flash = m.Arh && m.PitbullS < 0.6f && (int)(now * 12f) % 2 == 0;
                    g.Line(mx, my, tx, ty, flash ? Hi : Ink, al, isLead ? 2f : 1f);
                }
                else
                {
                    g.Line(mx, my, tx, ty, Dim, 0.95f, 1f, 5f, 3f);
                    Flow(g, mx, my, tx, ty);
                }
                float len = Mathf.Sqrt((tx - mx) * (tx - mx) + (ty - my) * (ty - my));
                if (isLead && !float.IsNaN(m.RangeM) && len > 90f)
                {
                    ReadOnlySpan<char> lab = ScopeStr.Distance("", m.RangeM, units, 8);
                    float lx = (mx + tx) / 2f, ly = (my + ty) / 2f, tw = Mathf.Round(ScopeCanvas.TextW(lab, 12f)) + 6f;
                    g.Text(lab, lx, ly - 9f, Dim, 12f, ScopeAlign.Center);
                    keep.Add(lx - tw / 2f, ly - 17f, tw, 16f);
                }
            }
            if (m.Phase == ShotPhase.Boost)
            {
                ScopeCanvas.Polar(m.HeadingDeg + 180f, 8f + 4f * ScopeCanvas.Hash(now * 20f), out float fx, out float fy);
                g.Line(mx, my, mx + fx, my + fy, Hi, 1f, 2f);
            }
            if (m.Kind == ShotKind.Bomb) g.Box(Mathf.Round(mx - 3f), Mathf.Round(my - 3f), 7f, 7f, Hi, 1f, 1f, Hi, 1f);
            else g.Chevron(mx, my, m.HeadingDeg, isLead ? 8f : 6f, Hi, true);
            keep.Add(mx - 9f, my - 9f, 18f, 18f);
            if (m.Arh && m.PitbullS < 0.8f)
            {
                float u = m.PitbullS / 0.8f;
                g.Ring(mx, my, 4f + 30f * ScopeCanvas.EaseOut(u), Hi, 1f - u, 2f);
                g.Ring(mx, my, 4f + 18f * ScopeCanvas.EaseOut(u), Hi, (1f - u) * 0.6f);
            }
        }

        /// <summary>Beads travel from the weapon to its target along the data-link line.</summary>
        private void Flow(ScopeCanvas g, float ax, float ay, float bx, float by)
        {
            int n = (int)Math.Round(Mathf.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)) / 26f);
            for (int i = 0; i < n; i++)
            {
                float u = (now * 0.9f + (float)i / n) % 1f;
                g.Fill(Mathf.Round(ax + (bx - ax) * u) - 1f, Mathf.Round(ay + (by - ay) * u) - 1f, 3f, 3f, Ink, 0.9f);
            }
        }

        private void TtiRing(ScopeCanvas g, int i, float k)
        {
            ShotMissile m = d.Missiles[i];
            if (!m.HasAim || float.IsNaN(m.TtiS)) return;
            bool isLead = i == lead;
            Plan(m.Aim, k, out float x, out float y);
            float r = ShotMath.TtiRingRadius(m.TtiS);
            g.Ring(x, y, r, isLead ? Ink : Dim, isLead ? 0.6f : 0.4f);
            if (isLead && m.TtiS < 3.2f) g.Ring(x, y, r + 2f, Hi, 0.4f + 0.5f * ScopeCanvas.Pulse(now, 3f));
        }

        /// <summary>Tags last, so they can dodge everything: the phase word over the target's name at the target (the seconds are the headline's and the strip's), the weapon's name (and A) at the weapon when it is far from us.</summary>
        private void Tags(ScopeCanvas g, int i, float k, float yMax)
        {
            ShotMissile m = d.Missiles[i];
            if (m.Ghost) return;
            bool isLead = i == lead;
            Plan(m.Pos, k, out float mx, out float my);
            if (m.HasAim && OwnsTargetTag(i))
            {
                Plan(m.Aim, k, out float tx, out float ty);
                segs[0] = mx; segs[1] = my; segs[2] = tx; segs[3] = ty;
                string name = m.Target >= 0 ? d.Targets[m.Target].Name : "";
                string label = PhaseLabel(m);
                float lw = Mathf.Round(ScopeCanvas.TextW(label, 12f)), nw = Mathf.Round(ScopeCanvas.TextW(name, 12f));
                float w = Math.Max(lw, nw) + 6f, h = 30f;
                int at = keep.Count;
                keep.Place(LabelSpots.Shot, tx, ty, w, h, new ReadOnlySpan<float>(segs, 0, 4), 2f, xMax, 32f, yMax, out float x, out float y);
                if (isLead || Free(at))
                {
                    bool seeker = m.Phase == ShotPhase.Active || m.Phase == ShotPhase.Term;
                    g.Text(label, x + 3f, y + 8f, seeker ? Hi : Dim, 12f, ScopeAlign.Left, seeker);
                    g.Text(name, x + 3f, y + 22f, isLead ? Ink : Dim);
                }
            }
            float dx = mx - OX, dy = my - OY;
            if (Mathf.Sqrt(dx * dx + dy * dy) > 46f)
            {
                bool a = m.Arh && m.Pitbull;
                float w = Mathf.Round(ScopeCanvas.TextW(m.Short, 12f)) + 4f + (a ? 18f : 0f), h = 17f;
                int at = keep.Count;
                keep.Place(LabelSpots.Shot, mx, my, w, h, default, 2f, xMax, 32f, yMax, out float x, out float y);
                if (!isLead && !Free(at)) return;
                g.Text(m.Short, x + 2f, y + h / 2f + 1f, Ink, 12f, ScopeAlign.Left, false, 0.9f);
                if (a) g.PlateTag("A", x + w - 15f, y + h / 2f, Hi, 12f, ScopeAlign.Left, 3f);
            }
        }

        /// <summary>
        /// The label just placed at <paramref name="at"/> found a spot of its own. Seven shots on one corvette group put seven "ACTIVE / SHARD CLASS CORVETTE" tags into the same few spots in game:
        /// a tag that could only sit on another is left out (THREAT's rule), the lead's never.
        /// </summary>
        private bool Free(int at) => keep.Count > at && !keep.Overprints(at);

        /// <summary>
        /// One tag per target: of the missiles aimed at the same target, the lead owns it (else the first). Two missiles on one target stacked two "BOOST FS-20 VORTEX" tags on top of each other in game.
        /// </summary>
        private bool OwnsTargetTag(int i)
        {
            ShotMissile m = d.Missiles[i];
            if (m.Target < 0) return true;
            for (int j = 0; j < d.MissileCount; j++)
            {
                if (j == i) continue;
                ShotMissile o = d.Missiles[j];
                if (o.Ghost || !o.HasAim || o.Target != m.Target) continue;
                if (j == lead || (i != lead && j < i)) return false;
            }
            return true;
        }

        private static string PhaseLabel(in ShotMissile m) => ShotMath.PhaseWord(m.Phase);

        private void Result(ScopeCanvas g, in ShotResult r, float k, float h)
        {
            if (r.AgeS > ShotMath.ResultS) return;
            Plan(r.Pos, k, out float x, out float y);
            float fade = 1f - ScopeCanvas.Seg(r.AgeS, 3.4f, ShotMath.ResultS);
            ScopeTone tone = r.State == ShotState.Defeated ? Amber : r.State == ShotState.Miss ? Dim : Hi;
            if (r.State == ShotState.Hit && r.AgeS < 1f)
            {
                float u = r.AgeS;
                for (int i = 0; i < 10; i++)
                {
                    float a = i * 36f + 12f;
                    ScopeCanvas.Polar(a, 3f + 7f * u, out float q0x, out float q0y);
                    ScopeCanvas.Polar(a, 6f + 20f * ScopeCanvas.EaseOut(u) + (i % 2) * 6f, out float q1x, out float q1y);
                    g.Line(x + q0x, y + q0y, x + q1x, y + q1y, Hi, 1f - u, 2f);
                }
                g.Ring(x, y, 4f + 26f * ScopeCanvas.EaseOut(u), Hi, (1f - u) * 0.7f);
                g.Fill(Mathf.Round(x) - 3f, Mathf.Round(y) - 3f, 7f, 7f, Hi, 1f - u);
            }
            if (r.State == ShotState.Defeated)
            {
                float u = Math.Min(1f, r.AgeS / 0.8f);
                g.Cross(x, y, 9f, Amber, fade, 2f);
                g.Ring(x, y, 8f + 14f * ScopeCanvas.EaseOut(u), Amber, (1f - u) * fade);
            }
            else if (r.State == ShotState.Miss) g.Cross(x, y, 7f, Dim, fade, 1f);
            // the stamp is a placed label: on the short FIRES plan the clamped stamp landed on the own-ship glyph and the ring numerals
            string word = r.State == ShotState.Defeated ? "DEFEATED" : r.State == ShotState.Miss ? "MISS" : "HIT";
            float sw = Mathf.Round(ScopeCanvas.TextW(word, 14f) + 14f), sh = 22f;      // the Stamp's own bracket box
            keep.Place(LabelSpots.Shot, x, y, sw, sh, default, 2f, xMax, 32f, h - 2f, out float lx, out float ly);
            g.Stamp(word, lx + sw / 2f, ly + sh / 2f, tone, 14f, r.State == ShotState.Defeated ? -4f : 0f, fade);
        }

        // ------------------------------------------------------------------------------------------------ strip: the SHOT TIMELINE

        /// <summary>
        /// The newest seven shots (only the ones that exist) at a pitch of 62 from x 8, no boxes: the glyph (centre x + 4.5), 13 in the name of at most five characters (only where the weapon changes) or, in flight, the seconds to go, and under it a
        /// 3 high journey track filled as the shot closes. The lead shot is Hi, the others Ink; a result is a glyph and a name with no track.
        /// </summary>
        public override void PaintStrip(ScopeCanvas g, in PagePaint p)
        {
            if (!live) return;
            int first = Math.Max(0, d.CellCount - Chips), leadId = lead >= 0 ? d.Missiles[lead].Id : -1;
            Span<char> name = stackalloc char[ShotMath.ChipChars];
            for (int i = 0; i < Chips && first + i < d.CellCount; i++)
            {
                float x = 8f + i * 62f, cy = 11f;
                ShotCell c = d.Cells[first + i];
                bool fly = c.State == ShotState.InFlight, isLead = fly && c.Id == leadId;
                Glyph(g, x + 4.5f, cy, c.State, fly, isLead ? Hi : Ink);
                // the chain reads by shape: a name only where the weapon changes, in flight the seconds to go instead of it
                float tti = fly ? TtiOf(c.Id) : float.NaN;
                if (!float.IsNaN(tti)) g.Text(ScopeStr.Cat("", (int)Math.Ceiling(tti), "S"), x + 13f, cy + 1f, isLead ? Hi : Ink, 12f, ScopeAlign.Left);
                else if (i == 0 || fly || c.Name != d.Cells[first + i - 1].Name)
                    g.Text(name.Slice(0, ShotMath.ChipName(c.Name, name)), x + 13f, cy + 1f, fly ? (isLead ? Hi : Ink) : c.State == ShotState.Defeated ? Amber : c.State == ShotState.Miss ? Dim : Ink, 12f, ScopeAlign.Left);
                if (fly) g.Bar(x + 1f, 18f, 51f, 3f, c.Frac, isLead ? Hi : Ink);      // the track: the shot's journey, filled as it closes
            }
        }

        private float TtiOf(int id)
        {
            for (int i = 0; i < d.MissileCount; i++) if (d.Missiles[i].Id == id) return d.Missiles[i].TtiS;
            return float.NaN;
        }

        /// <summary>HIT: a filled diamond with a cross cut out; MISS: a hollow dim diamond; DEFEATED: a hollow amber diamond struck through; in flight: an arrowhead.</summary>
        private static void Glyph(ScopeCanvas g, float x, float y, ShotState st, bool fly, ScopeTone flyTone)
        {
            if (fly) { g.Chevron(x, y + 1f, 0f, 5f, flyTone, true); return; }
            if (st == ShotState.Hit) { g.Diamond(x, y, 4.5f, Hi, true); g.Cross(x, y, 4f, Bg); }
            else if (st == ShotState.Defeated) { g.Diamond(x, y, 4.5f, Amber, false); g.Line(x - 3f, y + 3f, x + 3f, y - 3f, Amber); }
            else g.Diamond(x, y, 4.5f, Dim, false);
        }
    }
}
