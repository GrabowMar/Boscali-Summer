using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// WING (prototype: <c>pages/wing.js</c>): the player's wing against the player. One dominant shape, a heading-up formation plot (you at the centre, the rim 700 m): slot
    /// diamonds at the formation offsets, each wingman's chevron at its REAL relative position with a thin tether to its slot (who is out of slot, by how much), wingmen past
    /// the rim clamped onto it with a range tag, engaging wingmen with a dashed ray to a target diamond on the rim, a missile guiding on a wingman with a line to it. Right: one
    /// row per wingman (state word, fuel and ammunition bars, a task line) and the order acknowledgements. Top-left: the order (FORM / ENGAGE) with a pictogram of the formation.
    /// Feed: <see cref="WingBoardScope"/> (the Wing module's provider: the live wing on the host and solo, the snapshot mirror on a client). Live while the wing has something to
    /// say: someone out of slot, fighting or defending (and for a few seconds after a hit, running dry or a turn for home). Events (hit, a missile on a wingman, winchester, an order acknowledged) go to the ticker once.
    /// </summary>
    internal sealed class WingPage : ScopePageBase
    {
        /// <summary>The page stays up this long after a hit, running dry or a turn for home.</summary>
        private const float HoldSeconds = 8f;
        private const float RowX = 244f, RowW = 192f, AckY = 234f, InSlotFilledM = 40f;
        private const int Cap = 8, TrailLen = 6, LabelCap = 24, LabelChars = 28;

        private WingBoardScope d;
        private bool live, primed;
        private AvUnits units;
        private float now, lastSample = -1000f, flashUntil, flashFor = -1f, holdUntil;
        private readonly int[] seen = new int[Cap];
        private readonly WingState[] prev = new WingState[Cap];
        private readonly float[] trailX = new float[Cap * TrailLen], trailY = new float[Cap * TrailLen], trailXY = new float[TrailLen * 2];
        private readonly string[] seekerPre = new string[Cap], replyPost = new string[Cap], seekerFor = new string[Cap], replyFor = new string[Cap];
        private readonly float[] slotX = new float[Cap], slotY = new float[Cap];
        private readonly int[] parent = new int[Cap];
        private readonly LabelPlacer placer = new LabelPlacer();
        private float lastAckId = float.NaN;
        private bool engagedBefore;

        // Deferred plot labels (placed last so none sits on another): text copied out of the shared scratch.
        private readonly char[] labelText = new char[LabelCap * LabelChars];
        private readonly int[] labelLen = new int[LabelCap];
        private readonly float[] labelX = new float[LabelCap], labelY = new float[LabelCap];
        private readonly ScopeTone[] labelTone = new ScopeTone[LabelCap];
        private readonly bool[] labelBold = new bool[LabelCap];
        private readonly ScopeAlign[] labelAlign = new ScopeAlign[LabelCap];
        private int labels;

        private string headlineText = "IN SLOT";
        private int headlineKey = -1;
        private ScopeTone headlineTone = Dim;
        private bool headlineFlash;
        private int fuelKey = int.MinValue, ammoKey = int.MinValue;
        private string fuelText = "", fuelSub = "", ammoText = "", ammoSub = "";

        public override ScopePage Id => ScopePage.Wing;

        public override string Title => "WING";

        public override char Code => 'W';

        public override bool Fast => live;

        // ------------------------------------------------------------------------------------------------ poll

        public override bool Poll(in ScopeEnv env)
        {
            now = env.Now;
            units = env.Units;
            bool have = ScopeFeeds.TryRead(out d) && d.Count > 0;
            if (!have) { live = false; primed = false; return false; }
            Edges(env);
            primed = true;
            live = d.Engaged || WingBoardMath.NeedsAttention(d.Members, d.Count) || now < holdUntil;
            if (live) UpdateHeadline();
            return live;
        }

        /// <summary>Events once: a wingman is hit, has a missile on it, runs dry, turns for home; an order is answered.</summary>
        private void Edges(in ScopeEnv env)
        {
            int n = Math.Min(d.Count, Cap);
            for (int i = 0; i < n; i++)
            {
                WingBoardMember m = d.Members[i];
                if (primed && seen[i] == m.Number && prev[i] != m.State)
                {
                    string who = m.Name ?? "WINGMAN";
                    switch (m.State)
                    {
                        case WingState.Damaged:
                            env.Notice?.Invoke("wing", HudTone.Warning, who + " HIT", m.HasHull ? "HULL " + (int)Math.Round(m.Hull * 100f) + "%" : null);
                            flashUntil = now + 1.5f; flashFor = m.Number; holdUntil = now + HoldSeconds;
                            break;
                        case WingState.Defend:
                            env.Notice?.Invoke("wing", HudTone.Caution, who + " DEFENDING", m.HasThreat ? (m.ThreatSeeker ?? "MISSILE") + " INBOUND" : "MISSILE INBOUND");
                            flashUntil = now + 1.2f; flashFor = m.Number;
                            break;
                        case WingState.Winch:
                            env.Notice?.Invoke("wing", HudTone.Caution, who + " WINCHESTER", "NO AMMUNITION");
                            flashUntil = now + 1.5f; flashFor = m.Number; holdUntil = now + HoldSeconds;
                            break;
                        case WingState.Rtb: case WingState.Refit:
                            env.Notice?.Invoke("wing", HudTone.Info, who + (m.State == WingState.Rtb ? " RTB" : " REFIT"), m.HasField ? m.Field : null);
                            holdUntil = now + HoldSeconds;
                            break;
                    }
                }
                seen[i] = m.Number;
                prev[i] = m.State;
            }
            for (int i = n; i < Cap; i++) seen[i] = 0;
            if (d.Engaged && !engagedBefore && primed) env.Notice?.Invoke("wing", HudTone.Info, "ENGAGING", d.Shape);
            engagedBefore = d.Engaged;
            if (d.AckCount > 0)
            {
                WingBoardAck a = d.Acks[0];
                if (!float.IsNaN(lastAckId) && a.Id != lastAckId && a.AgeS < 3f)
                    env.Notice?.Invoke("wing", a.Accepted ? HudTone.Info : HudTone.Caution, (a.Who ?? "WING") + " " + a.Text, null);
                lastAckId = a.Id;
            }
        }

        private void UpdateHeadline()
        {
            int who = -1, kind = 0;
            for (int i = 0; i < d.Count && kind == 0; i++) if (d.Members[i].State == WingState.Damaged) { who = i; kind = 1; }
            for (int i = 0; i < d.Count && kind == 0; i++) if (d.Members[i].State == WingState.Defend) { who = i; kind = 2; }
            for (int i = 0; i < d.Count && kind == 0; i++) if (d.Members[i].State == WingState.Winch) { who = i; kind = 3; }
            int out_ = WingBoardMath.OutCount(d.Members, d.Count);
            int key;
            string text = null;
            ScopeTone tone = kind > 0 ? Amber : out_ > 0 || d.Engaged ? Hi : Dim;
            bool flash = false;
            if (kind > 0)
            {
                int number = d.Members[who].Number;
                key = 100 + number * 10 + kind;
                if (key != headlineKey) text = "#" + number + (kind == 1 ? " DAMAGED" : kind == 2 ? " DEFEND" : " WINCH");
                flash = now < flashUntil && flashFor == number;
            }
            else if (d.Engaged) { key = 1; if (key != headlineKey) text = "ENGAGE"; }
            else if (out_ > 0) { key = 1000 + out_; if (key != headlineKey) text = out_ + " OUT"; }
            else { key = 2; if (key != headlineKey) text = "IN SLOT"; }
            if (key != headlineKey) { headlineKey = key; headlineText = text; }
            headlineTone = tone;
            headlineFlash = flash;
        }

        public override ScopeHeadline Headline() => live ? new ScopeHeadline { Text = headlineText, Tone = headlineTone, Flash = headlineFlash } : default;

        public override int Gauges(GaugeSpec[] into)
        {
            if (d.Count <= 0 || d.Members == null) return 0;
            int n = 0, count = Math.Min(d.Count, Cap), low = 0, armed = 0;
            float minFuel = 2f, sumAmmo = 0f;
            for (int i = 0; i < count; i++)
            {
                WingBoardMember m = d.Members[i];
                if (m.Fuel < minFuel) { minFuel = m.Fuel; low = i; }
                sumAmmo += m.Ammo;
                if (m.Ammo > 0f) armed++;
            }
            if (d.HasAutopilot)
            {
                into[n++] = new GaugeSpec { Id = "ap", Label = "AUTOPILOT", Kind = GaugeKind.Mode, Text = d.ApText, Sub = d.ApSub, Value = d.ApProgress / 3f, Tone = Ink };
            }
            WingBoardMember lowest = d.Members[low];
            int fk = (int)Math.Round(minFuel * 100f) * 100 + lowest.Number * 3 + (lowest.Bingo ? 1 : lowest.Joker ? 2 : 0);
            if (fk != fuelKey)
            {
                fuelKey = fk;
                fuelText = ScopeStr.Pct((int)Math.Round(minFuel * 100f)) + " #" + lowest.Number;
                fuelSub = lowest.Bingo ? "BINGO #" + lowest.Number : lowest.Joker ? "JOKER #" + lowest.Number : "";
            }
            ScopeTone fuelTone = lowest.Bingo || minFuel < 0.25f ? Amber : Ink;
            into[n++] = new GaugeSpec { Id = "wfuel", Label = "WING FUEL", Kind = GaugeKind.Bar, Value = minFuel, Text = fuelText, Sub = fuelSub, Tone = fuelTone };
            float avg = sumAmmo / count;
            int ak = (int)Math.Round(avg * 100f) * 100 + armed * 10 + count;
            if (ak != ammoKey)
            {
                ammoKey = ak;
                ammoText = ScopeStr.Pct((int)Math.Round(avg * 100f));
                ammoSub = "OF " + count + " AC";
            }
            into[n++] = new GaugeSpec { Id = "waam", Label = "WING AMMO", Kind = GaugeKind.Ring, Value = avg, Inner = ScopeStr.Int(armed), Text = ammoText, Sub = ammoSub, Tone = armed < count ? Amber : Ink };
            return n;
        }

        // ------------------------------------------------------------------------------------------------ paint

        public override void Paint(ScopeCanvas g, in PagePaint p)
        {
            if (!ScopeFeeds.TryRead(out d) || d.Count <= 0) return;
            live = true;
            now = p.Now;
            units = p.Units;
            int n = Math.Min(d.Count, Cap);
            SampleTrails(n);
            labels = 0;
            placer.Reset();
            Plot(g, p.Now, n);
            Order(g, p.Now, n);
            Roster(g, p.Now, n);
            Flush(g);
        }

        private void SampleTrails(int n)
        {
            bool step = now - lastSample >= 0.18f || now < lastSample;
            if (step) lastSample = now;
            for (int i = 0; i < n; i++)
            {
                WingBoardMember m = d.Members[i];
                int o = i * TrailLen;
                bool same = trailOwner[i] == m.Number;
                trailOwner[i] = m.Number;
                if (!same) { for (int k = 0; k < TrailLen; k++) { trailX[o + k] = m.X; trailY[o + k] = m.Y; } }
                else if (step) { for (int k = 0; k < TrailLen - 1; k++) { trailX[o + k] = trailX[o + k + 1]; trailY[o + k] = trailY[o + k + 1]; } }
                trailX[o + TrailLen - 1] = m.X; trailY[o + TrailLen - 1] = m.Y;
            }
        }

        private readonly int[] trailOwner = new int[Cap];

        // ---------------------------------------------------------------- deferred labels

        /// <summary>Start a deferred label (text is appended with <see cref="Append"/>, so two scratch-backed words can join); returns its slot or -1 when full.</summary>
        private int Begin(float x, float y, ScopeTone tone, bool bold, ScopeAlign align)
        {
            if (labels >= LabelCap) return -1;
            int l = labels++;
            labelLen[l] = 0; labelX[l] = x; labelY[l] = y; labelTone[l] = tone; labelBold[l] = bold; labelAlign[l] = align;
            return l;
        }

        private void Append(int l, ReadOnlySpan<char> text)
        {
            if (l < 0) return;
            int len = Math.Min(text.Length, LabelChars - labelLen[l]);
            if (len <= 0) return;
            text.Slice(0, len).CopyTo(new Span<char>(labelText, l * LabelChars + labelLen[l], len));
            labelLen[l] += len;
        }

        private void Label(ReadOnlySpan<char> text, float x, float y, ScopeTone tone, bool bold, ScopeAlign align) => Append(Begin(x, y, tone, bold, align), text);

        private void Flush(ScopeCanvas g)
        {
            for (int i = 0; i < labels; i++)
            {
                var text = new ReadOnlySpan<char>(labelText, i * LabelChars, labelLen[i]);
                float w = ScopeCanvas.TextW(text, 12f), x0 = labelAlign[i] == ScopeAlign.Right ? labelX[i] - w : labelAlign[i] == ScopeAlign.Center ? labelX[i] - w / 2f : labelX[i];
                float y = placer.Place(x0, x0 + w, labelY[i]);
                g.Fill(Mathf.Round(x0) - 2f, Mathf.Round(y) - 8f, (float)Math.Ceiling(w) + 4f, 16f, Bg, 0.85f);
                g.Text(text, labelX[i], y, labelTone[i], 12f, labelAlign[i], labelBold[i]);
            }
        }

        // ---------------------------------------------------------------- the plot

        private const float Px = WingBoardMath.PlotX, Py = WingBoardMath.PlotY, R = WingBoardMath.PlotR;

        private void Plot(ScopeCanvas g, float t, int n)
        {
            // Frame: rim, 350 m ring, axes, bearing ticks, the scale.
            g.Line(Px, Py - R, Px, Py + R, Faint, 1f, 1f, 2f, 4f);
            g.Line(Px - R, Py, Px + R, Py, Faint, 1f, 1f, 2f, 4f);
            g.Ring(Px, Py, R / 2f, Faint, 1f, 1f, Ink, 0f, 3f, 3f);
            g.Ring(Px, Py, R, Dim, 0.85f);
            for (int a = 0; a < 360; a += 30)
            {
                ScopeCanvas.Polar(a, R, out float ox, out float oy);
                ScopeCanvas.Polar(a, R - (a % 90 == 0 ? 7f : 4f), out float ix, out float iy);
                g.Line(Px + ox, Py + oy, Px + ix, Py + iy, Dim);
            }
            bool imperial = units == AvUnits.Imperial;
            g.Text(ScopeStr.Cat("", Scale(WingBoardMath.RimM / 2f), null), Px + 4f, Py + R / 2f + 8f, Dim, 12f, ScopeAlign.Left, false, 0.8f);
            g.Text(ScopeStr.Cat("", Scale(WingBoardMath.RimM), imperial ? " FT" : " M"), Px + 4f, Py + R - 8f, Dim, 12f, ScopeAlign.Left, false, 0.8f);

            // Formation skeleton and slots (a client has none: the snapshot does not carry the shape).
            int slots = 0;
            for (int i = 0; i < n; i++)
                if (d.Members[i].HasSlot) { slotX[slots] = d.Members[i].SlotX; slotY[slots] = d.Members[i].SlotY; slots++; }
            if (slots > 0)
            {
                WingBoardMath.Skeleton(slotX, slotY, slots, parent);
                for (int i = 0; i < slots; i++)
                {
                    WingBoardMath.At(slotX[i], slotY[i], out float x1, out float y1);
                    float x0 = Px, y0 = Py;
                    if (parent[i] >= 0) WingBoardMath.At(slotX[parent[i]], slotY[parent[i]], out x0, out y0);
                    g.Line(x0, y0, x1, y1, Faint, 0.95f);
                }
            }
            for (int i = 0; i < n; i++)
            {
                WingBoardMember m = d.Members[i];
                if (!m.HasSlot) continue;
                bool inSlot = m.SlotErrM < InSlotFilledM;
                WingBoardMath.At(m.SlotX, m.SlotY, out float x, out float y);
                g.Diamond(x, y, 5f, inSlot ? Ink : Dim, inSlot, inSlot ? 0.5f : 0.95f);
                if (!inSlot) g.Text(ScopeStr.Int(m.Number), x, y + 13f, Dim, 12f, ScopeAlign.Center, false, 0.9f);
            }

            // Tethers: a thin line wingman -> slot (amber when it is an involuntary miss).
            for (int i = 0; i < n; i++)
            {
                WingBoardMember m = d.Members[i];
                if (!m.HasSlot || m.SlotErrM < WingBoardMath.TetherM) continue;
                WingBoardMath.Place(m.X, m.Y, out float ax, out float ay, out bool rim);
                if (rim) continue;
                bool voluntary = m.State != WingState.Join && m.State != WingState.Form;
                WingBoardMath.At(m.SlotX, m.SlotY, out float sx, out float sy);
                g.Line(ax, ay, sx, sy, voluntary || m.SlotErrM < WingBoardMath.TetherFarM ? Dim : Amber, voluntary ? 0.55f : 0.9f, 1f, 2f, 3f, t * 4f);
            }

            // Engage rays and the target diamonds on the rim.
            int tag = 0;
            for (int i = 0; i < n; i++)
            {
                WingBoardMember m = d.Members[i];
                if (m.State != WingState.Engage || !m.HasTarget) continue;
                WingBoardMath.Place(m.X, m.Y, out float ax, out float ay, out _);
                ScopeCanvas.Polar(m.TargetBearingDeg, R, out float ox, out float oy);
                float tx = Px + ox, ty = Py + oy;
                g.Line(ax, ay, tx, ty, Hi, 0.85f, 1f, 5f, 3f, -t * 16f);
                g.Diamond(tx, ty, 5f, Hi, false);
                g.Ring(tx, ty, 9f + 2f * ScopeCanvas.Pulse(t, 2f), Hi, 0.4f);
                if (tag < 2)
                {
                    float ly = 41f + tag * 17f;
                    ReadOnlySpan<char> label = ScopeStr.Distance(m.TargetName, m.TargetRangeM, units, 16);
                    float lw = ScopeCanvas.TextW(label, 12f);
                    g.Line(238f - lw - 4f, ly, tx, ty, Dim, 0.6f, 1f, 2f, 3f);
                    g.Text(label, 238f, ly, Hi, 12f, ScopeAlign.Right);
                    tag++;
                }
            }

            // A missile guiding on a wingman.
            for (int i = 0; i < n; i++)
            {
                WingBoardMember m = d.Members[i];
                if (!m.HasThreat) continue;
                WingBoardMath.Place(m.ThreatX, m.ThreatY, out float ax, out float ay, out bool rim);
                WingBoardMath.Place(m.X, m.Y, out float bx, out float by, out _);
                g.Line(ax, ay, bx, by, Red, 0.7f, 1f, 4f, 3f, -t * 14f);
                if (rim) g.Tri(ax, ay, WingBoardMath.BearingDeg(m.ThreatX, m.ThreatY) + 180f, 5f, Red);
                else g.Chevron(ax, ay, (float)(Math.Atan2(bx - ax, -(by - ay)) * 180.0 / Math.PI), 5f, Red, true);
                if (!ReferenceEquals(seekerFor[i], m.ThreatSeeker)) { seekerFor[i] = m.ThreatSeeker; seekerPre[i] = (m.ThreatSeeker ?? "MSL") + " "; }
                Label(ScopeStr.Cat(seekerPre[i], (int)Math.Round(m.ThreatTtiS), "S"), ax + (ax > Px ? -8f : 8f), ay + 14f, Red, true, ax > Px ? ScopeAlign.Right : ScopeAlign.Left);
            }

            // The wingmen.
            for (int i = 0; i < n; i++)
            {
                WingBoardMember m = d.Members[i];
                WingBoardMath.Place(m.X, m.Y, out float ax, out float ay, out bool rim);
                ScopeTone tone = WingBoardMath.ToneOf(m.State);
                bool hollow = WingBoardMath.Hollow(m.State);
                if (!rim)
                {
                    int o = i * TrailLen;
                    for (int k = 0; k < TrailLen; k++) { WingBoardMath.At(trailX[o + k], trailY[o + k], out trailXY[2 * k], out trailXY[2 * k + 1]); }
                    g.Trail(trailXY, TrailLen, tone, 0.7f, 1f, 5);
                    g.Chevron(ax, ay, m.HeadingDeg, 7f, tone, !hollow, 1f);
                    if (m.State == WingState.Damaged)
                        for (int k = 0; k < 5; k++)
                        {
                            float u = (t * 1.4f + k / 5f) % 1f;
                            ScopeCanvas.Polar(m.HeadingDeg + 180f, 8f + u * 22f, out float qx, out float qy);
                            g.Fill(Mathf.Round(ax + qx), Mathf.Round(ay + qy), 2f, 2f, Amber, 1f - u);
                        }
                    if (m.State == WingState.Defend) g.Ring(ax, ay, 11f + 2f * ScopeCanvas.Pulse(t, 3f), Amber, 0.7f);
                    Label(ScopeStr.Cat("#", m.Number, null), ax + (ax > Px ? 9f : -9f), ay - 10f, tone, true, ax > Px ? ScopeAlign.Left : ScopeAlign.Right);
                }
                else
                {
                    g.Tri(ax, ay, WingBoardMath.BearingDeg(m.X, m.Y), 6f, tone, !hollow);
                    float dist = (float)Math.Sqrt(m.X * m.X + m.Y * m.Y);
                    int l = Begin(Mathf.Clamp(ax, 40f, 208f), ay + (ay < Py ? 14f : -14f), tone, true, ScopeAlign.Center);
                    Append(l, ScopeStr.Cat("#", m.Number, " "));
                    Append(l, ScopeStr.Distance(null, dist, units, 10));
                }
            }

            // You.
            g.Jet(Px, Py, 0f, 9f, Ink, true);
            g.Ring(Px, Py, 14f, Ink, 0.35f);
            Label(ScopeStr.Cat("RNG ", Scale(WingBoardMath.RimM), imperial ? " FT" : " M"), 8f, 289f, Dim, false, ScopeAlign.Left);
        }

        /// <summary>A plot distance in the player's units, rounded to 50 (700 m is 700, 2300 ft).</summary>
        private int Scale(float metres) => (int)(Math.Round(AvUnitTable.Altitude(metres, units) / 50f) * 50f);

        // ---------------------------------------------------------------- the order block

        private void Order(ScopeCanvas g, float t, int n)
        {
            bool engage = d.Engaged;
            float s = 0f;
            int slots = 0;
            for (int i = 0; i < n; i++) if (d.Members[i].HasSlot) { slotX[slots] = d.Members[i].SlotX; slotY[slots] = d.Members[i].SlotY; slots++; }
            if (slots > 0) s = WingBoardMath.GlyphScale(slotX, slotY, slots);
            if (slots > 0)
            {
                float lx = 4f + 8f + 11f, ly = 33f + 2f;
                g.Diamond(lx, ly, 3f, Hi, true);
                for (int i = 0; i < n; i++)
                {
                    WingBoardMember m = d.Members[i];
                    if (!m.HasSlot) continue;
                    float px = lx + m.SlotX * s, py = ly - m.SlotY * s;
                    bool arrow = engage && m.State == WingState.Engage;
                    if (arrow) g.Tri(px, py - (6f * ScopeCanvas.Pulse(t, 1.5f) + 1f), 0f, 3.2f, Hi);
                    else g.Diamond(px, py, 3f, Ink, false);
                }
            }
            g.Text(engage ? "ENGAGE" : "FORM", 44f, 40f, Hi, 12f, ScopeAlign.Left, true);
            if (d.Shape != null) g.FitText(d.Shape, 44f, 56f, 120f, Dim);
        }

        // ---------------------------------------------------------------- roster and acknowledgements

        private void Roster(ScopeCanvas g, float t, int n)
        {
            WingBoardMath.Rows(n, out int shown, out float rowH, out float step, out bool task, out bool acks);
            for (int i = 0; i < shown; i++) Row(g, d.Members[i], i, RowX, WingBoardMath.RowsTop + i * step, rowH, task, t);
            if (n > shown) g.Text(ScopeStr.Cat("+", n - shown, " MORE"), RowX + RowW - 4f, 294f, Dim, 12f, ScopeAlign.Right);
            if (acks) AckLog(g, RowX, AckY, t);
        }

        private void Row(ScopeCanvas g, in WingBoardMember m, int index, float x, float y, float h, bool taskLine, float t)
        {
            ScopeTone tone = WingBoardMath.ToneOf(m.State);
            bool sev = tone == Amber, flash = m.State == WingState.Damaged && (int)(t * 3f) % 2 == 0;
            g.Box(x, y, RowW, h, sev ? Amber : Dim, sev ? 1f : 0.8f, 1f, Amber, sev ? (flash ? 0.28f : 0.1f) : 0f, chamTL: 6f);
            g.Chevron(x + 13f, y + 14f, m.HeadingDeg > 90f || m.HeadingDeg < -90f ? 180f : 0f, 5f, tone, !WingBoardMath.Hollow(m.State), 1f);
            string word = WingBoardMath.Word(m.State);
            float wordW = ScopeCanvas.TextW(word, 12f) + 6f;
            g.FitText(m.Name ?? "", x + 24f, y + 13f, RowW - 24f - 8f - wordW, Hi, 12f, 12f, ScopeAlign.Left, true);
            ScopeTone wordTone = tone == Ink ? (m.State == WingState.Engage ? Hi : Ink) : tone;
            g.Text(word, x + RowW - 8f, y + 13f, wordTone, 12f, ScopeAlign.Right, m.State != WingState.Form, m.State == WingState.Rtb ? 0.85f : 1f);

            float by = y + (taskLine ? 26f : Math.Max(20f, Math.Min(26f, h - 18f)));
            bool low = m.Fuel < 0.25f || m.Bingo || m.Joker;
            ScopeTone fuelTone = low ? Amber : Ink, ammoTone = m.Ammo <= 0f ? Amber : Ink;
            g.Text("F", x + 8f, by + 5f, Dim);
            g.Bar(x + 17f, by, 36f, 10f, m.Fuel, fuelTone, 8);
            g.Text(ScopeStr.Pct((int)Math.Round(m.Fuel * 100f)), x + 58f, by + 5f, low ? Amber : Hi, 12f, ScopeAlign.Left, low);
            g.Text("A", x + 92f, by + 5f, Dim);
            g.Bar(x + 101f, by, 36f, 10f, m.Ammo, ammoTone, 4);
            if (ammoTone == Amber) g.Text("WINCH", x + 142f, by + 5f, Amber, 12f, ScopeAlign.Left, true);
            else g.Text(ScopeStr.Pct((int)Math.Round(m.Ammo * 100f)), x + 142f, by + 5f, Hi);
            if (taskLine) TaskLine(g, m, index, x + 8f, y + 49f, RowW - 14f, sev ? Amber : Dim);
        }

        /// <summary>The row's third line: what the wingman is doing, from what is known of it.</summary>
        private void TaskLine(ScopeCanvas g, in WingBoardMember m, int index, float x, float y, float w, ScopeTone tone)
        {
            string unit = units == AvUnits.Imperial ? " FT" : " M";
            switch (m.State)
            {
                case WingState.Form:
                    g.FitText(ScopeStr.Cat("SLOT ", m.Number, " - ERR ", Round5(m.SlotErrM), unit), x, y, w, tone);
                    break;
                case WingState.Join:
                    g.FitText(ScopeStr.Cat("REJOIN ", Round5(m.SlotErrM), units == AvUnits.Imperial ? " FT TO SLOT" : " M TO SLOT"), x, y, w, tone);
                    break;
                case WingState.Engage:
                    if (m.HasTarget) { g.Text("TGT", x, y, tone); g.FitText(ScopeStr.Distance(m.TargetName, m.TargetRangeM, units, 20), x + 31f, y, w - 31f, tone); }
                    else g.Text("ENGAGED", x, y, tone);
                    break;
                case WingState.Winch: g.Text("NO AMMUNITION", x, y, tone); break;
                case WingState.Rtb:
                case WingState.Refit:
                    if (m.HasField) { g.Text(m.State == WingState.Rtb ? "RTB" : "REFIT", x, y, tone); float lw = m.State == WingState.Rtb ? 31f : 47f; g.FitText(ScopeStr.Distance(m.Field, m.FieldM, units, 22), x + lw, y, w - lw, tone); }
                    else g.Text(m.State == WingState.Rtb ? "RETURNING" : "REFIT", x, y, tone);
                    break;
                case WingState.Damaged:
                    if (m.HasHull) g.Text(ScopeStr.Cat("HIT - HULL ", (int)Math.Round(m.Hull * 100f), "%"), x, y, tone); else g.Text("HIT", x, y, tone);
                    break;
                case WingState.Defend:
                    if (m.HasThreat)
                    {
                        if (!ReferenceEquals(replyFor[index], m.ThreatReply)) { replyFor[index] = m.ThreatReply; replyPost[index] = "S - " + (m.ThreatReply ?? "EVADE"); }
                        if (!ReferenceEquals(seekerFor[index], m.ThreatSeeker)) { seekerFor[index] = m.ThreatSeeker; seekerPre[index] = (m.ThreatSeeker ?? "MSL") + " "; }
                        g.FitText(ScopeStr.Cat(seekerPre[index], (int)Math.Round(m.ThreatTtiS), replyPost[index]), x, y, w, tone);
                    }
                    else g.Text("MISSILE - DEFENDING", x, y, tone);
                    break;
                case WingState.Ground: g.Text("ON THE GROUND", x, y, tone); break;
                default: g.Text("LANDING", x, y, tone); break;
            }
        }

        private int Round5(float metres) => (int)(Math.Round(AvUnitTable.Altitude(metres, units) / 5f) * 5f);

        private void AckLog(ScopeCanvas g, float x, float y, float t)
        {
            g.Box(x, y, RowW, 60f, Faint, 0.9f);
            if (d.AckCount <= 0) { g.Text("RADIO QUIET", x + 8f, y + 30f, Dim); return; }
            for (int k = 0; k < d.AckCount && k < 3; k++)
            {
                WingBoardAck a = d.Acks[k];
                float yy = y + 12f + k * 17f, al = k == 0 ? 1f : k == 1 ? 0.75f : 0.5f;
                bool fresh = k == 0 && a.AgeS < 1.4f, bad = !a.Accepted;
                if (fresh) g.Fill(x + 2f, yy - 8f, 188f, 16f, bad ? Amber : Ink, 0.2f + 0.1f * (float)Math.Sin(a.AgeS * 12f));
                g.Text(a.Who ?? "", x + 8f, yy, bad ? Amber : Hi, 12f, ScopeAlign.Left, true, al);
                g.FitText(a.Text ?? "", x + 34f, yy, 130f, bad ? Amber : Ink, 12f, 12f, ScopeAlign.Left, bad, al);
                g.Text(ScopeStr.Cat("", Math.Max(0, (int)Math.Round(a.AgeS)), "S"), x + 184f, yy, Dim, 12f, ScopeAlign.Right, false, al);
            }
        }
    }
}
