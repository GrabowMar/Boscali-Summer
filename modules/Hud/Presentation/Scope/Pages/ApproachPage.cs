using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// APPROACH (prototype: <c>pages/approach.js</c>): ILS and autoland. One dominant shape, the runway funnel: a perspective projection of the runway, a ground grid,
    /// the approach lights and one glide-path gate per ladder step, seen from the aircraft (<see cref="ApproachCam"/>: runway-aligned, pitched to the flight path, in
    /// the anamorphic scale that makes the diamond move like the ILS dot scales). The diamond (where the aim point is against the flight path) sits right of and below
    /// the reticle when the aircraft is left and high, cross needles run through it. In the flare the ILS drops out and the horizon swings down; on the roll-out the
    /// funnel becomes a plan view (runway left, the predicted stop point, the margin). Right: the distance ladder, radar altitude, sink rate, crosswind, the speed delta
    /// bar and the gear and airbrake checks. The strip carries the autoland chain INTERCEPT, GLIDE, FLARE, ROLLOUT (a vertical lander over a pad: APPROACH, DESCENT,
    /// TOUCHDOWN, with a plan view of the pad instead of the funnel). Feed: <see cref="ApproachScope"/> (Autopilot's provider: the landing autopilot's runway or, by hand,
    /// the nearest field's). Live while the gear is out near a runway or pad, or while the autopilot flies. It moves every frame, so it repaints every frame while live.
    /// </summary>
    internal sealed class ApproachPage : ScopePageBase
    {
        // The funnel's panel (design units) at the standard body height; a taller body (no strip) moves the lower edge and the reference down.
        private const float FbX = 4f, FbY = 32f, FbW = 292f, FbH = 236f, BodyStd = 274f;
        private const float VpX0 = FbX, VpX1 = FbX + 266f, VpY0 = FbY + 34f, CenterX = 137f, CenterY = 156f;
        private const float Gw = 70f, Gh = 30f;
        private static readonly string[] RunwayChain = { "INTERCEPT", "GLIDE", "FLARE", "ROLLOUT" };
        private static readonly string[] PadChain = { "APPROACH", "DESCENT", "TOUCHDOWN" };
        private static readonly float[] SinkTick = { 0.8f };
        private static readonly char[] Scratch = new char[24];

        private ApproachScope d;
        private bool live, primed;
        private AvUnits units;
        private float now, lastPaint, pitch, rolloutAt = -1000f, rolloutRef;
        private float extra, vpY1, cy;
        private ApproachStage lastStage;
        private bool lastCautionSpeed, lastCautionIls, announced;
        private readonly ApproachCam cam = new ApproachCam();
        private readonly float[] world = new float[3 * 8], screen = new float[2 * 20], clipped = new float[2 * 24], tmp = new float[2 * 24];
        private readonly float[] quad = new float[8], strip = new float[16];

        private string headlineText = "";
        private int headlineKey = -1;
        private ScopeTone headlineTone = Dim;
        private bool headlineFlash;
        private int gaugeKey = int.MinValue, xwKey = int.MinValue;
        private string sinkText = "", apSub = "", xwValue = "", xwFull = "", apFor;
        private AvUnits gaugeUnits = (AvUnits)(-1);

        public override ScopePage Id => ScopePage.Approach;

        public override string Title => "APPROACH";

        public override char Code => 'A';

        public override bool Fast => live;

        public override bool HasStrip => live && d.Autoland;

        // ------------------------------------------------------------------------------------------------ poll

        public override bool Poll(in ScopeEnv env)
        {
            now = env.Now;
            units = env.Units;
            live = ScopeFeeds.TryRead(out d);
            if (!live) { primed = false; announced = false; return false; }
            if (!primed)
            {
                pitch = d.PathDeg;
                lastStage = d.Stage;
                lastCautionIls = lastCautionSpeed = false;
                rolloutRef = 0f;
            }
            else Edges(env);

            if (d.Stage == ApproachStage.Rollout) rolloutRef = Math.Max(rolloutRef, d.GroundMps);
            if (d.Stage == ApproachStage.Rollout && lastStage != ApproachStage.Rollout) rolloutAt = now;
            lastStage = d.Stage;
            primed = true;
            UpdateHeadline(env.Units);
            return true;
        }

        /// <summary>Events once: the autopilot takes the aircraft, a stage is entered, a deviation or the speed goes out.</summary>
        private void Edges(in ScopeEnv env)
        {
            if (!announced && d.Autoland)
            {
                announced = true;
                env.Notice?.Invoke("ils", HudTone.Info, "AUTOLAND " + (d.Pad ? d.Field ?? "PAD" : d.Runway ?? d.Field ?? "RUNWAY"), null);
            }
            if (d.Stage != lastStage && d.Autoland)
            {
                switch (d.Stage)
                {
                    case ApproachStage.Glide: env.Notice?.Invoke("ils", HudTone.Info, d.Pad ? "PAD DESCENT" : "GLIDE", DistanceWord(d.DistanceM, env.Units) + (d.Pad ? " TO PAD" : " TO THRESHOLD")); break;
                    case ApproachStage.Flare: env.Notice?.Invoke("ils", HudTone.Info, d.Pad ? "TOUCHDOWN" : "FLARE", null); break;
                    case ApproachStage.Rollout: env.Notice?.Invoke("land", HudTone.Info, "TOUCHDOWN - ROLLOUT", d.Autoland ? "BRAKES AUTO" : null); break;
                }
            }
            bool cSpd = CautionSpeed;
            if (cSpd && !lastCautionSpeed)
            {
                float dv = d.SpeedMps - d.TargetMps;
                env.Notice?.Invoke("spd", HudTone.Caution, (dv > 0f ? "FAST +" : "SLOW -") + (int)Math.Round(Math.Abs(AvUnitTable.Speed(dv, env.Units))) + " " + AvUnitTable.SpeedUnit(env.Units).ToUpperInvariant(),
                    dv > 0f ? (d.Airbrake ? "AIRBRAKE OUT" : "REDUCE POWER") : "ADD POWER");
            }
            lastCautionSpeed = cSpd;
            bool cIls = d.Stage <= ApproachStage.Glide && (CautionGs || CautionLoc);
            if (cIls && !lastCautionIls)
                env.Notice?.Invoke("ils", HudTone.Caution, CautionGs ? (d.GsDeg > 0f ? "GS HIGH" : "GS LOW") : (d.LocDeg < 0f ? "LOC LEFT" : "LOC RIGHT"), null);
            lastCautionIls = cIls;
        }

        private static string DistanceWord(float m, AvUnits u) => new string(Scratch, 0, AvUnitTable.DistanceReading(Scratch, 0, m, u)).ToUpperInvariant();

        // ------------------------------------------------------------------------------------------------ header

        private bool CautionLoc => d.HasIls && ApproachMath.LocCaution(d.LocDeg);

        private bool CautionGs => d.HasIls && ApproachMath.GsCaution(d.GsDeg);

        private bool CautionSpeed => d.Stage != ApproachStage.Rollout && d.HasTarget && ApproachMath.SpeedCaution(d.SpeedMps - d.TargetMps);

        private void UpdateHeadline(AvUnits u)
        {
            int key;
            string text;
            ScopeTone tone = Hi;
            bool flash = false;
            float dv = d.SpeedMps - d.TargetMps;
            if (!d.GearDown && d.Stage != ApproachStage.Rollout) { key = 1; text = "GEAR"; tone = Amber; flash = true; }
            else if (CautionSpeed)
            {
                int v = (int)Math.Round(Math.Abs(AvUnitTable.Speed(dv, u)));
                key = 2000 + v * 2 + (dv > 0f ? 1 : 0) + (u == AvUnits.Imperial ? 1000 : 0);
                text = (dv > 0f ? "FAST +" : "SLOW -") + v;
                tone = Amber; flash = true;
            }
            else if (d.Stage == ApproachStage.Intercept && d.HasIls)
            {
                int v = (int)Math.Round(Math.Abs(d.LocDeg) * 10f);
                key = 20000 + v + (d.LocDeg < 0f ? 1000 : 0);
                text = (d.LocDeg < 0f ? "LOC L " : "LOC R ") + (v / 10) + "." + (v % 10);
                tone = CautionLoc ? Amber : Hi;
            }
            else if (d.Stage <= ApproachStage.Glide && d.HasIls)
            {
                int v = (int)Math.Round(d.GsDeg * 10f);
                key = 30000 + v;
                text = "GS " + (v < 0 ? "-" : "+") + (Math.Abs(v) / 10) + "." + (Math.Abs(v) % 10);
                tone = CautionGs ? Amber : Hi;
            }
            else if (d.Stage <= ApproachStage.Glide) { key = 4; text = d.Pad ? "PAD" : "FINAL"; }
            else if (d.Stage == ApproachStage.Flare) { key = 5; text = d.Pad ? "TOUCHDOWN" : "FLARE"; }
            else { bool td = now < rolloutAt + 1.2f; key = td ? 6 : 7; text = td ? "TOUCHDOWN" : "ROLLOUT"; }
            if (key != headlineKey) { headlineKey = key; headlineText = text; }
            headlineTone = tone;
            headlineFlash = flash;
        }

        public override ScopeHeadline Headline() => live ? new ScopeHeadline { Text = headlineText, Tone = headlineTone, Flash = headlineFlash } : default;

        // ------------------------------------------------------------------------------------------------ gauges

        public override int Gauges(GaugeSpec[] into)
        {
            if (!live) return 0;
            int n = 0;
            if (d.Autoland)
            {
                float overall = ((int)d.Stage + Progress()) / (d.Pad ? 3f : 4f);
                string name = d.Pad ? d.Field : d.Runway ?? d.Field;
                if (!ReferenceEquals(name, apFor)) { apFor = name; apSub = name ?? ""; }
                into[n++] = new GaugeSpec { Id = "ap", Label = "AUTOPILOT", Kind = GaugeKind.Mode, Text = "LAND", Sub = apSub, Value = overall / 3f, Tone = Ink };
            }
            {
                int key = (int)Math.Round(d.SinkMps * 10f);
                if (key != gaugeKey || units != gaugeUnits) { gaugeKey = key; gaugeUnits = units; sinkText = ScopeStr.Cat("", AvUnitTable.Climb(-d.SinkMps, units), units == AvUnits.Imperial ? 0 : 1, units == AvUnits.Imperial ? " FPM" : " M/S").ToString(); }
                into[n++] = new GaugeSpec
                {
                    Id = "sink", Label = "SINK RATE", Kind = GaugeKind.Bar, Value = ApproachMath.Clamp(d.SinkMps / 10f, 0f, 1f), Text = sinkText, Sub = "MAX 8",
                    Tone = d.SinkMps > ApproachMath.SinkCautionMps ? Amber : Ink, Ticks = SinkTick, TickCount = 1, TickTone = d.SinkMps > 7f ? Amber : Dim,
                };
            }
            return n;
        }

        private float Progress() =>
            ApproachMath.StageProgress(d.Stage, d.Pad, d.DistanceM, d.Pad ? d.RadarAltM : Math.Max(0f, d.AimM - d.AlongM), d.TouchdownS, d.GroundMps, rolloutRef);

        // ------------------------------------------------------------------------------------------------ paint

        public override void Paint(ScopeCanvas g, in PagePaint p)
        {
            if (!ScopeFeeds.TryRead(out d)) return;
            live = true;
            now = p.Now;
            units = p.Units;
            float dt = primed ? Math.Min(0.2f, Math.Max(0f, now - lastPaint)) : 1f;
            pitch += (d.PathDeg - pitch) * (1f - (float)Math.Exp(-dt / 0.35f));
            lastPaint = now;
            extra = Math.Max(0f, p.H - BodyStd);
            cy = CenterY + extra * 0.5f;
            vpY1 = FbY + FbH + extra - 22f;

            if (d.Pad) PlanPad(g, p.Now); else if (d.Stage == ApproachStage.Rollout) Rollout(g, p.Now); else Scene(g, p.Now);
            Chrome(g);
            RightColumn(g, p.Now);
        }

        // ---------------------------------------------------------------- clipped drawing in the viewport

        private void Line(ScopeCanvas g, float x1, float y1, float x2, float y2, ScopeTone tone, float alpha = 1f, float w = 1f, float dash = 0f, float gap = 0f, float offset = 0f)
        {
            if (ScreenClip.Line(ref x1, ref y1, ref x2, ref y2, VpX0, VpY0, VpX1, vpY1)) g.Line(x1, y1, x2, y2, tone, alpha, w, dash, gap, offset);
        }

        private void WorldLine(ScopeCanvas g, float x1, float y1, float z1, float x2, float y2, float z2, ScopeTone tone, float alpha = 1f, float w = 1f, float dash = 0f, float gap = 0f, float offset = 0f)
        {
            if (cam.Segment(x1, y1, z1, x2, y2, z2, out float a, out float b, out float c, out float e)) Line(g, a, b, c, e, tone, alpha, w, dash, gap, offset);
        }

        /// <summary>The runway-plane quad in <see cref="world"/> filled and/or outlined, cut to the viewport.</summary>
        private void WorldQuad(ScopeCanvas g, float x0, float z0, float x1, float z1, ScopeTone fill, float fillAlpha, ScopeTone line, float lineAlpha)
        {
            world[0] = x0; world[1] = 0f; world[2] = z0;
            world[3] = x1; world[4] = 0f; world[5] = z0;
            world[6] = x1; world[7] = 0f; world[8] = z1;
            world[9] = x0; world[10] = 0f; world[11] = z1;
            int m = cam.Polygon(world, 4, screen);
            if (m < 3) return;
            m = ScreenClip.Polygon(screen, m, clipped, tmp, VpX0, VpY0, VpX1, vpY1);
            if (m < 3) return;
            int path = g.Poly(clipped, m);
            if (fillAlpha > 0f) g.FillPoly(path, fill, fillAlpha);
            if (lineAlpha > 0f) g.Path(path, line, lineAlpha);
        }

        private void WorldDot(ScopeCanvas g, float x, float z, ScopeTone tone, float alpha)
        {
            if (!cam.Point(x, 0f, z, out float sx, out float sy)) return;
            float rx = Mathf.Round(sx) - 1f, ry = Mathf.Round(sy) - 1f;
            if (rx >= VpX0 && rx + 2f <= VpX1 && ry >= VpY0 && ry + 2f <= vpY1) g.Fill(rx, ry, 2f, 2f, tone, alpha);
        }

        // ---------------------------------------------------------------- the runway funnel

        private void Scene(ScopeCanvas g, float t)
        {
            float length = d.RunwayLength > 0f ? d.RunwayLength : 1800f, hw = (d.RunwayWidth > 0f ? d.RunwayWidth : 60f) * 0.5f, aim = d.AimM, az = d.AlongM;
            float rung = units == AvUnits.Imperial ? 1852f : 1000f;
            bool glide = d.Stage <= ApproachStage.Glide;
            cam.Set(CenterX, cy, d.CrossM, d.HeightM, az, pitch);
            float yh = cam.Horizon;

            // Ground tint below the horizon and the horizon with ticks (it swings down through the frame in the flare).
            if (yh < vpY1) { float y0 = Math.Max(VpY0, yh); g.Fill(VpX0, y0, VpX1 - VpX0, vpY1 - y0, Faint, 0.2f); }
            if (yh > VpY0 && yh < vpY1)
            {
                g.Line(VpX0, yh, VpX1, yh, Dim, 0.7f);
                for (float x = VpX0 + 14f; x < VpX1; x += 28f) g.Line(x, yh - 2f, x, yh + 2f, Dim, 0.5f);
            }

            // Ground grid: transverse lines stream toward you, longitudinal lines converge on the runway.
            for (float z = (float)Math.Ceiling((az + 80f) / 400f) * 400f; z < az + 7600f; z += 400f)
                WorldLine(g, -1400f, 0f, z, 1400f, 0f, z, Dim, 0.3f * (1f - (z - az) / 8000f));
            for (int k = 0; k < 4; k++)
            {
                float x = k == 0 ? -900f : k == 1 ? -380f : k == 2 ? 380f : 900f;
                WorldLine(g, x, 0f, az + 60f, x, 0f, az + 8000f, Dim, 0.28f);
            }

            // The runway: outline, centre-line dashes, threshold bar, aim-point blocks, edge dots.
            WorldQuad(g, -hw, 0f, hw, length, Ink, 0.13f, Ink, 0.95f);
            for (float z = 40f; z < length - 40f; z += 120f) WorldLine(g, 0f, 0f, z, 0f, 0f, z + 50f, Dim, 0.9f);
            WorldQuad(g, -hw, 0f, hw, 22f, Hi, 0.9f, Hi, 0f);
            WorldQuad(g, -0.7f * hw, aim, -0.35f * hw, aim + 45f, Ink, 0.55f, Ink, 0f);
            WorldQuad(g, 0.35f * hw, aim, 0.7f * hw, aim + 45f, Ink, 0.55f, Ink, 0f);
            for (float z = 0f; z <= length; z += 150f) { WorldDot(g, -hw, z, Hi, 0.85f); WorldDot(g, hw, z, Hi, 0.85f); }

            // Approach lights: a centre line of dots every 30 m back from the threshold, crossbars at 150 / 300 / 420 m.
            for (float z = -420f; z < 0f; z += 30f) WorldDot(g, 0f, z, Hi, 0.8f);
            for (int k = 1; k <= 3; k++) { float z = k == 1 ? -150f : k == 2 ? -300f : -420f; WorldLine(g, -14f, 0f, z, 14f, 0f, z, Hi, 0.7f); }

            // Glide-path tunnel: four edges and one frame per ladder step; frames fade as they grow.
            float slope = (float)Math.Tan(d.SlopeDeg * Math.PI / 180.0);
            if (glide)
            {
                float z0 = az + 90f;
                for (int k = 0; k < 4; k++)
                {
                    float sx = (k & 1) == 0 ? -1f : 1f, sy = k < 2 ? 1f : -1f;
                    WorldLine(g, sx * Gw, (aim - z0) * slope + sy * Gh, z0, sx * Gw, (aim + 500f) * slope + sy * Gh, -500f, Ink, 0.2f);
                }
                for (int step = 6; step >= 1; step--)
                {
                    float zk = -step * rung, yk = (aim - zk) * slope;
                    float depth = cam.Depth(0f, yk, zk);
                    if (depth <= 60f) continue;
                    if (!cam.Point(-Gw, yk + Gh, zk, out float ax, out float ay) || !cam.Point(Gw, yk - Gh, zk, out float bx, out float by)) continue;
                    float half = (bx - ax) / 2f;
                    float fade = ApproachMath.Clamp(1f - (half - 58f) / 80f, 0f, 1f) * (0.45f + 0.55f * ApproachMath.Clamp(1f - depth / 5200f, 0f, 1f));
                    if (fade <= 0.03f) continue;
                    ax = Mathf.Round(ax); ay = Mathf.Round(ay); bx = Mathf.Round(bx); by = Mathf.Round(by);
                    Line(g, ax, ay, bx, ay, Ink, fade * 0.45f); Line(g, bx, ay, bx, by, Ink, fade * 0.45f); Line(g, bx, by, ax, by, Ink, fade * 0.45f); Line(g, ax, by, ax, ay, Ink, fade * 0.45f);
                    float len = ApproachMath.Clamp(half * 0.35f, 3f, 10f);
                    Bracket(g, ax, ay, 1f, 1f, len, fade); Bracket(g, bx, ay, -1f, 1f, len, fade); Bracket(g, ax, by, 1f, -1f, len, fade); Bracket(g, bx, by, -1f, -1f, len, fade);
                }
            }

            // The aim point (diamond) and cross needles, until the flare takes the ILS away.
            if (glide && cam.Point(0f, 0f, aim, out float pax, out float pay))
            {
                float dx = ApproachMath.Clamp(pax, VpX0 + 16f, VpX1 - 16f), dy = ApproachMath.Clamp(pay, VpY0 + 14f, vpY1 - 14f);
                bool off = dx != pax || dy != pay, caution = CautionGs || CautionLoc;
                ScopeTone tone = caution ? Amber : Ink;
                bool onPath = Math.Abs(dx - CenterX) < 9f && Math.Abs(dy - cy) < 7f;
                float len = (float)Math.Sqrt((dx - CenterX) * (dx - CenterX) + (dy - cy) * (dy - cy));
                // The path line toward the viewer (where the glide path runs when you are off it).
                WorldLine(g, 0f, 0f, aim, 0f, (aim - (az + 140f)) * slope, az + 140f, tone, 0.5f, 1f, 5f, 4f, -t * 14f);
                if (len > 14f)
                {
                    float ux = (dx - CenterX) / len, uy = (dy - cy) / len;
                    ScopeTone tn = caution ? Amber : Hi;
                    g.Dashed(CenterX + ux * 9f, cy + uy * 9f, dx - ux * 8f, dy - uy * 8f, tn, 0.8f, 1f, -t * 12f);
                    g.Chevron(dx - ux * 13f, dy - uy * 13f, (float)(Math.Atan2(ux, -uy) * 180.0 / Math.PI), 3.5f, tn, true, 0.95f);
                }
                Line(g, dx, dy - 44f, dx, dy - 9f, tone, 0.9f); Line(g, dx, dy + 9f, dx, dy + 44f, tone, 0.9f);
                Line(g, dx - 56f, dy, dx - 9f, dy, tone, 0.9f); Line(g, dx + 9f, dy, dx + 56f, dy, tone, 0.9f);
                g.Diamond(dx, dy, 7f, onPath ? Hi : tone, onPath || caution, off ? 0.8f : 1f);
                if (onPath) g.Diamond(dx, dy, 11f + 2f * ScopeCanvas.Pulse(t, 2f), Hi, false, 0.45f);
            }

            // Flare: the radar altitude counts down under the reticle.
            if (d.Stage == ApproachStage.Flare)
            {
                g.Text(ScopeStr.Cat("", (int)Math.Max(0f, Math.Round(AvUnitTable.Altitude(d.RadarAltM, units))), null), CenterX, cy + 40f, Hi, 22f, ScopeAlign.Center, true);
                g.Text(units == AvUnits.Imperial ? "RA FT" : "RA M", CenterX, cy + 58f, Dim, 12f, ScopeAlign.Center);
            }

            // Ownship reference (the flight path marker), fixed.
            g.Ring(CenterX, cy, 5f, Hi);
            g.Line(CenterX - 13f, cy, CenterX - 5f, cy, Hi); g.Line(CenterX + 5f, cy, CenterX + 13f, cy, Hi); g.Line(CenterX, cy - 5f, CenterX, cy - 11f, Hi);
        }

        private void Bracket(ScopeCanvas g, float x, float y, float sx, float sy, float len, float alpha)
        {
            Line(g, x, y, x + sx * len, y, Hi, alpha);
            Line(g, x, y, x, y + sy * len, Hi, alpha);
        }

        // ---------------------------------------------------------------- the roll-out plan view

        private void Rollout(ScopeCanvas g, float t)
        {
            float length = d.RunwayLength > 0f ? d.RunwayLength : 1800f, x = CenterX, top = VpY0 + 14f, bot = vpY1 - 12f, ppm = (bot - top) / length, hw = 17f;
            float along = ApproachMath.Clamp(d.AlongM, 0f, length);
            float stop = ApproachMath.StopM(along, d.GroundMps, length), remain = ApproachMath.RemainM(along, length), margin = Math.Max(0f, length - stop);
            float y0 = bot, yEnd = bot - length * ppm, yAlong = bot - along * ppm, yStop = bot - stop * ppm;

            quad[0] = x - hw; quad[1] = y0; quad[2] = x + hw; quad[3] = y0; quad[4] = x + hw; quad[5] = yEnd; quad[6] = x - hw; quad[7] = yEnd;
            int n = g.Poly(quad, 4);
            g.FillPoly(n, Ink, 0.1f);
            g.Path(n, Ink, 0.9f);
            for (float z = 60f; z < length - 60f; z += 120f) g.Line(x, bot - z * ppm, x, bot - (z + 50f) * ppm, Dim, 0.9f);
            g.Line(x - hw, y0 - 1f, x + hw, y0 - 1f, Hi, 1f, 2f);
            g.Line(x - hw, yEnd + 1f, x + hw, yEnd + 1f, Dim, 1f, 2f);
            for (float z = 500f; z < length; z += 500f) g.Line(x - hw - 6f, bot - z * ppm, x - hw, bot - z * ppm, Dim);

            // Braking stretch: the path flown, a dashed line from the aircraft to the stop point, the stop tag.
            g.Line(x, bot - Math.Min(d.AimM, along) * ppm, x, yAlong, Hi, 0.55f, 2f);
            g.Dashed(x, yAlong - 8f, x, yStop, Hi, 1f, 1f, t * 10f);
            g.Line(x - hw - 3f, yStop, x + hw + 3f, yStop, Hi);
            g.PlateTag("STOP", x + hw + 8f, yStop, Ink);
            g.Jet(x, yAlong, 0f, 11f, Hi, true);

            // Readouts either side of the strip.
            string u = units == AvUnits.Imperial ? "FT" : "M";
            g.Text("REMAIN", VpX0 + 8f, VpY0 + 16f, Dim);
            float rw = g.Text(ScopeStr.Cat("", Round10(AvUnitTable.Altitude(remain, units)), null), VpX0 + 8f, VpY0 + 36f, Hi, 22f, ScopeAlign.Left, true);
            g.Text(u, VpX0 + 11f + rw, VpY0 + 38f, Dim);
            g.Text("MARGIN", VpX1 - 8f, vpY1 - 38f, Dim, 12f, ScopeAlign.Right);
            g.Text(ScopeStr.Cat("", Round10(AvUnitTable.Altitude(margin, units)), null), VpX1 - 26f, vpY1 - 20f, margin < 250f ? Amber : Hi, 16f, ScopeAlign.Right, true);
            g.Text(u, VpX1 - 8f, vpY1 - 19f, Dim, 12f, ScopeAlign.Right);

            float k = ScopeCanvas.Seg(t, rolloutAt, rolloutAt + 0.9f);
            if (k < 1f)
            {
                g.Ring(x, yAlong, 4f + 40f * ScopeCanvas.EaseOut(k), Hi, 1f - k);
                g.Stamp("TOUCHDOWN", x, VpY0 + 20f, Hi, 14f, 0f, 1f - 0.6f * k);
            }
        }

        private static int Round10(float v) => ApproachMath.Round10(v);

        // ---------------------------------------------------------------- a vertical lander over its pad

        private void PlanPad(ScopeCanvas g, float t)
        {
            // Heading-up plan: the pad at its place from the aircraft, rings at a doubling range that holds it.
            float range = 50f, dist = (float)Math.Sqrt(d.PadX * d.PadX + d.PadY * d.PadY);
            while (range < dist * 1.15f && range < 6400f) range *= 2f;
            float cx = CenterX, r = Math.Min(VpX1 - VpX0, vpY1 - VpY0) * 0.5f - 6f, s = r / range;
            g.Ring(cx, cy, r, Dim, 0.85f);
            g.Ring(cx, cy, r * 0.5f, Faint, 1f, 1f, Ink, 0f, 3f, 3f);
            g.Line(cx, cy - r, cx, cy + r, Faint, 1f, 1f, 2f, 4f);
            g.Line(cx - r, cy, cx + r, cy, Faint, 1f, 1f, 2f, 4f);
            g.Text(ScopeStr.Cat("", Round10(AvUnitTable.Altitude(range, units)), units == AvUnits.Imperial ? " FT" : " M"), cx + 4f, cy + r - 8f, Dim, 12f, ScopeAlign.Left, false, 0.8f);
            float px = cx + d.PadX * s, py = cy - d.PadY * s;
            float pd = (float)Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
            if (pd > r - 6f) { px = cx + (px - cx) * (r - 6f) / pd; py = cy + (py - cy) * (r - 6f) / pd; }
            g.Dashed(cx, cy - 6f, px, py, Hi, 0.8f, 1f, -t * 12f);
            g.Box(px - 7f, py - 7f, 14f, 14f, Hi, 1f, 1f, Hi, d.Stage == ApproachStage.Flare ? 0.5f : 0.12f);
            g.Line(px - 4f, py, px + 4f, py, Hi); g.Line(px, py - 4f, px, py + 4f, Hi);
            g.Jet(cx, cy, 0f, 9f, Ink, true);
            g.Ring(cx, cy, 14f, Ink, 0.35f);
        }

        // ---------------------------------------------------------------- frame, dot scales, labels

        private void Chrome(ScopeCanvas g)
        {
            g.Box(FbX, FbY, FbW, FbH + extra, Faint, 0.9f);
            g.Line(FbX, VpY0, FbX + FbW, VpY0, Faint, 0.9f);
            g.Line(FbX, vpY1, FbX + FbW, vpY1, Faint, 0.9f);
            g.Line(VpX1, VpY0, VpX1, vpY1, Faint, 0.9f);
            bool scales = d.Stage <= ApproachStage.Glide && d.HasIls && !d.Pad;
            float right = FbX + FbW - 6f;
            if (scales)
            {
                // The LOC (bottom) and GS (right) dot scales: the diamond uses the same pixels per degree, so scale and picture agree.
                ScopeTone lc = CautionLoc ? Amber : Ink, gc = CautionGs ? Amber : Ink;
                float by = vpY1 + 11f, rx = VpX1 + 13f;
                g.Text("LOC", FbX + 6f, by, Dim);
                for (int k = -2; k <= 2; k++) g.Ring(CenterX + k * ApproachMath.LocDotPx, by, k == 0 ? 3f : 2f, Dim, 0.9f, 1f, Dim, k == 0 ? 1f : 0f);
                g.Diamond(CenterX - ApproachMath.LocDots(d.LocDeg) * ApproachMath.LocDotPx, by, 5f, lc, true);
                g.Text("GS", rx, cy - 3f * ApproachMath.GsDotPx - 9f, Dim, 12f, ScopeAlign.Center);
                for (int k = -2; k <= 2; k++) g.Ring(rx, cy + k * ApproachMath.GsDotPx, k == 0 ? 3f : 2f, Dim, 0.9f, 1f, Dim, k == 0 ? 1f : 0f);
                g.Diamond(rx, cy + ApproachMath.GsDots(d.GsDeg) * ApproachMath.GsDotPx, 5f, gc, true);
                float loc = d.LocDeg;
                g.Text(ScopeStr.Cat("LOC ", Math.Abs(loc), 1, loc < -0.05f ? " L" : loc > 0.05f ? " R" : ""), right, FbY + 11f, lc, 12f, ScopeAlign.Right, CautionLoc);
                g.Text(ScopeStr.Cat("GS ", d.GsDeg, 1, Math.Abs(d.GsDeg) < 0.15f ? "" : d.GsDeg > 0f ? " HIGH" : " LOW", true), right, FbY + 27f, gc, 12f, ScopeAlign.Right, CautionGs);
            }
            else if (d.Pad) g.Text(d.Stage == ApproachStage.Intercept ? "APPROACH" : d.Stage == ApproachStage.Glide ? "DESCENT" : "TOUCHDOWN", right, FbY + 11f, Hi, 12f, ScopeAlign.Right, true);
            else
            {
                g.Text("ILS OFF", right, FbY + 11f, Dim, 12f, ScopeAlign.Right);
                g.Text(d.Stage == ApproachStage.Rollout ? "ROLLOUT" : d.Stage == ApproachStage.Flare ? "FLARE" : "NO ILS", right, FbY + 27f, Hi, 12f, ScopeAlign.Right, true);
            }

            // Field and runway (top-left); the crosswind rides the second line.
            g.FitText(d.Field ?? "", FbX + 6f, FbY + 11f, 205f, Ink, 12f, 12f);
            if (d.Pad) g.Text(d.Occupied ? "PAD OCCUPIED" : "PAD", FbX + 6f, FbY + 27f, d.Occupied ? Amber : Dim, 12f, ScopeAlign.Left, d.Occupied);
            else
            {
                string rwy = d.Runway ?? "RUNWAY";
                if (d.Occupied) g.Text(ScopeStr.Cat(rwy, " - OCCUPIED"), FbX + 6f, FbY + 27f, Amber, 12f, ScopeAlign.Left, true);
                else if (d.HasWind) { Crosswind(); g.Text(ScopeStr.Cat(rwy, " - XW ", xwFull), FbX + 6f, FbY + 27f, Dim); }
                else g.Text(rwy, FbX + 6f, FbY + 27f, Dim);
            }
        }

        /// <summary>The crosswind as words ("L6" and "L6KT"), rebuilt only when it or the units change.</summary>
        private void Crosswind()
        {
            int v = (int)Math.Round(AvUnitTable.Speed(Math.Abs(d.CrosswindMps), units));
            int key = (d.CrosswindMps >= 0f ? v + 1 : -(v + 1)) * 2 + (units == AvUnits.Imperial ? 1 : 0);
            if (key == xwKey) return;
            xwKey = key;
            xwValue = (d.CrosswindMps >= 0f ? "L" : "R") + v;
            xwFull = xwValue + AvUnitTable.SpeedUnit(units).ToUpperInvariant();
        }

        // ---------------------------------------------------------------- right column

        private void Stat(ScopeCanvas g, string label, ReadOnlySpan<char> value, string unit, float x, float y)
        {
            g.Text(label, x, y, Dim);
            float w = g.Text(value, x, y + 16f, Hi, 16f, ScopeAlign.Left, true);
            if (!string.IsNullOrEmpty(unit)) g.Text(unit, x + w + 4f, y + 17f, Dim);
        }

        private void RightColumn(ScopeCanvas g, float t)
        {
            const float x0 = 304f, xr = 436f, lx = 316f, sx = 358f, ppk = 22f, ly = 180f;
            bool imperial = units == AvUnits.Imperial;
            float rungM = imperial ? 1852f : 1000f;

            // Distance to the threshold (or the pad): the big figure, and the ladder.
            float toThr = d.Pad ? d.DistanceM : -d.AlongM, steps = d.Pad ? d.RadarAltM / (imperial ? 9.144f : 10f) : toThr / rungM;   // over a pad the ladder is the height: 10 m (30 ft) a rung
            double figure = imperial ? Math.Abs(toThr) * 0.000539957 : Math.Abs(toThr) / 1000.0;
            float dw = g.Text(ScopeStr.Cat("", figure, 1, null), x0 + 2f, 46f, Hi, 22f, ScopeAlign.Left, true);
            g.Text(imperial ? "NM" : "KM", x0 + 6f + dw, 49f, Dim);
            g.Text(d.Pad ? "TO PAD" : toThr < 0f ? "ON RWY" : "TO THR", xr - 4f, 49f, Dim, 12f, ScopeAlign.Right);

            // Ladder 5-4-3-2-1: the flown part dim and dashed, the remaining part solid, a dot filling as you pass it.
            float ym = ly - ppk * ApproachMath.LadderKm(steps);
            g.Dashed(lx, ly - ppk * 5.45f, lx, ym, Dim, 0.5f);
            g.Line(lx, ym, lx, ly, Ink, 0.9f);
            for (int k = 1; k <= 5; k++)
            {
                float y = ly - ppk * k;
                bool passed = steps < k - 0.02f;
                g.Ring(lx, y, 3f, passed ? Ink : Dim, passed ? 1f : 0.9f, 1f, Ink, passed ? 1f : 0f);
                g.Text(ScopeStr.Int(d.Pad ? k * (imperial ? 30 : 10) : k), lx + 11f, y, passed ? Ink : Dim, 12f, ScopeAlign.Left, false, passed ? 0.8f : 1f);
            }
            g.Line(lx - 10f, ly, lx + 10f, ly, Hi, 1f, 2f);
            g.Text(d.Pad ? "GND" : "THR", lx + 14f, ly, Dim);
            g.Chevron(lx, ym - 1f, 180f, 6f, Hi, true);

            // Stats right of the ladder: radar altitude, sink rate, crosswind.
            Stat(g, "RA", ScopeStr.Cat("", (int)Math.Max(0f, Math.Round(AvUnitTable.Altitude(d.RadarAltM, units))), null), imperial ? "FT" : "M", sx, 70f);
            Stat(g, "V/S", ScopeStr.Cat("", AvUnitTable.Climb(-d.SinkMps, units), imperial ? 0 : 1, null), imperial ? "FPM" : "M/S", sx, 108f);
            if (d.HasWind) { Crosswind(); Stat(g, "XW", xwValue, AvUnitTable.SpeedUnit(units).ToUpperInvariant(), sx, 146f); }
            g.Line(346f, 66f, 346f, 176f, Faint);

            // Speed block: the delta bar around the speed the approach wants (centre = target, +-20 km/h); on the ground the ground speed and the brakes.
            g.Line(x0, 188f, xr, 188f, Faint);
            float by = 206f + extra, bw = 128f, bx = x0 + 2f, labelY = 224f + extra;
            string speedUnit = " " + AvUnitTable.SpeedUnit(units).ToUpperInvariant();
            if (d.Stage != ApproachStage.Rollout)
            {
                g.Text("SPD", x0 + 2f, 197f, Dim);
                if (d.HasTarget)
                {
                    float dv = d.SpeedMps - d.TargetMps;
                    bool caution = CautionSpeed, on = ApproachMath.OnSpeed(dv);
                    ScopeTone tone = caution ? Amber : Ink;
                    g.Text(on ? "ON SPD" : ScopeStr.Cat(dv > 0f ? "FAST " : "SLOW ", AvUnitTable.Speed(dv, units), 0, null, true), xr - 4f, 197f, on ? Hi : tone, 12f, ScopeAlign.Right, caution);
                    g.Box(bx, by, bw, 10f, Dim, 0.85f);
                    g.Fill(bx + 2f + Mathf.Round((bw - 4f) * 0.375f), by + 2f, Mathf.Round((bw - 4f) * 0.25f), 6f, Faint, 0.8f);
                    float cx = bx + bw / 2f, vx = cx + ApproachMath.Clamp(dv / ApproachMath.SpeedBarMps, -1f, 1f) * (bw / 2f - 3f);
                    g.Fill(Mathf.Round(Math.Min(cx, vx)), by + 2f, Math.Max(1f, Mathf.Round(Math.Abs(vx - cx))), 6f, tone);
                    g.Line(cx, by - 3f, cx, by + 13f, Hi);
                    g.Text("SLOW", bx, labelY, Dim, 12f, ScopeAlign.Left, false, 0.85f);
                    g.Text("FAST", bx + bw, labelY, Dim, 12f, ScopeAlign.Right, false, 0.85f);
                }
                else g.Text(ScopeStr.Cat("", (int)Math.Round(AvUnitTable.Speed(d.SpeedMps, units)), speedUnit), xr - 4f, 197f, Hi, 12f, ScopeAlign.Right, true);
            }
            else
            {
                g.Text("GND SPD", x0 + 2f, 197f, Dim);
                g.Text(ScopeStr.Cat("", (int)Math.Round(AvUnitTable.Speed(d.GroundMps, units)), speedUnit), xr - 4f, 197f, Hi, 12f, ScopeAlign.Right, true);
                g.Bar(bx, by, bw, 10f, rolloutRef > 1f ? d.GroundMps / rolloutRef : 0f, Ink);
                g.Text("BRAKES", bx, labelY, Dim, 12f, ScopeAlign.Left, false, 0.85f);
                g.Text(d.Autoland ? "AUTO" : d.Airbrake ? "ON" : "OFF", bx + bw, labelY, Hi, 12f, ScopeAlign.Right);
            }

            // Configuration checks: the gear, and the airbrake (the game has no flaps).
            Chip(g, x0, 238f + extra, "GEAR", d.GearDown, true, t);
            Chip(g, x0 + 68f, 238f + extra, "BRK", !(d.Airbrake && d.Stage != ApproachStage.Rollout), false, t);
        }

        private static void Chip(ScopeCanvas g, float x, float y, string label, bool ok, bool flash, float t)
        {
            ScopeTone tone = ok ? Ink : Amber;
            bool lit = !ok && flash && (int)(t * 3f) % 2 == 0;
            g.Box(x, y, 64f, 24f, tone, ok ? 0.85f : 1f, 1f, tone, ok ? 0.1f : lit ? 0.4f : 0.16f);
            if (ok) g.Check(x + 13f, y + 12f, 10f, Hi, 1f, 2f); else g.Cross(x + 13f, y + 12f, 8f, Amber, 1f, 2f);
            g.Text(label, x + 25f, y + 13f, ok ? Hi : Amber, 12f, ScopeAlign.Left, !ok);
        }

        // ------------------------------------------------------------------------------------------------ strip: the autoland chain

        public override void PaintStrip(ScopeCanvas g, in PagePaint p)
        {
            if (!live || !d.Autoland) return;
            string[] names = d.Pad ? PadChain : RunwayChain;
            g.PlateTag("AUTO", 4f, 13f, Ink);
            float x0 = 46f, cw = (p.W - x0 - 4f) / names.Length, prog = Progress();
            for (int i = 0; i < names.Length; i++)
            {
                float x = x0 + i * cw, nl = i > 0 ? 6f : 0f;
                bool act = i == (int)d.Stage, done = i < (int)d.Stage;
                int n = 0;
                strip[n++] = x; strip[n++] = 3f;
                strip[n++] = x + cw - 6f; strip[n++] = 3f;
                strip[n++] = x + cw; strip[n++] = 13f;
                strip[n++] = x + cw - 6f; strip[n++] = 23f;
                strip[n++] = x; strip[n++] = 23f;
                if (nl > 0f) { strip[n++] = x + nl; strip[n++] = 13f; }
                int path = g.Poly(strip, n / 2);
                if (act) g.FillPoly(path, Ink, 0.3f); else if (done) g.FillPoly(path, Ink, 0.08f);
                g.Path(path, act ? Hi : done ? Ink : Faint, act ? 1f : done ? 0.55f : 0.9f);
                float tx = x + nl + (cw - nl - 6f) / 2f + (done ? 6f : 0f);
                if (done) g.Check(x + nl + 9f, 13f, 8f, Ink, 0.8f, 1.5f);
                g.Text(names[i], tx, 14f, act ? Hi : Dim, 12f, ScopeAlign.Center, act, act || done ? 1f : 0.7f);
                if (act) g.Fill(Mathf.Round(x + nl + 2f), 21f, Mathf.Round((cw - nl - 10f) * prog), 2f, Hi);
            }
        }
    }
}
