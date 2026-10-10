using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Autopilot.Domain;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The flight pages of the TAC BOARD (APPROACH, WING, DOCK): the funnel camera and its clipping, the dot scales and cautions, the stage rules, the dock bullseye, the wing plot.</summary>
    internal static class FlightScopeTests
    {
        public static void Run()
        {
            Camera();
            Clipping();
            Approach();
            Dock();
            Wing();
        }

        private static void Camera()
        {
            var cam = new ApproachCam();
            // On the glide path: 3300 m from a touchdown 300 m past the threshold, on a five degree path with the camera pitched to it: the aim point sits on the reference.
            const float d = 3300f;
            float h = d * (float)Math.Tan(5.0 * Math.PI / 180.0) - ApproachCam.EyeAbove;
            cam.Set(137f, 156f, 0f, h, -3000f, 5f);
            TestAssert.That(cam.Point(0f, 0f, 300f, out float sx, out float sy), "The aim point is in front of the camera");
            TestAssert.That(Math.Abs(sx - 137f) < 0.1f && Math.Abs(sy - 156f) < 1.5f, "Flying the path puts the aim point on the reference (got " + sx + ", " + sy + ")");

            // Left of the centre line the runway appears to the right, as the localizer needle does: 100 m left at 3300 m is 2000 * 100 / 3300 px.
            cam.Set(137f, 156f, -100f, h, -3000f, 5f);
            cam.Point(0f, 0f, 300f, out sx, out sy);
            TestAssert.That(sx > 137f + 50f && sx < 137f + 70f, "Left of the line the runway sits right of the reference (got " + sx + ")");
            // High above the path the aim point drops below the reference.
            cam.Set(137f, 156f, 0f, h + 30f, -3000f, 5f);
            cam.Point(0f, 0f, 300f, out sx, out sy);
            TestAssert.That(sy > 156f + 20f, "High above the path the aim point sits below the reference (got " + sy + ")");

            TestAssert.That(!cam.Point(0f, 0f, -3500f, out _, out _), "A point behind the camera is dropped");
            TestAssert.That(!cam.Segment(0f, 0f, -4000f, 0f, 0f, -3500f, out _, out _, out _, out _), "A segment wholly behind is dropped");
            TestAssert.That(cam.Segment(0f, 0f, -4000f, 0f, 0f, 500f, out float ax, out float ay, out float bx, out float by) && float.IsFinite(ax + ay + bx + by), "A segment crossing the near plane is cut and finite");

            // A runway quad half behind the camera is cut at the near plane: only points in front remain.
            cam.Set(137f, 156f, 0f, 20f, 100f, 0f);
            float[] quad = { -30f, 0f, 0f, 30f, 0f, 0f, 30f, 0f, 1800f, -30f, 0f, 1800f };
            float[] dst = new float[32];
            int n = cam.Polygon(quad, 4, dst);
            TestAssert.Eq(n, 4, "A quad straddling the near plane keeps four corners");
            for (int i = 0; i < n; i++) TestAssert.That(float.IsFinite(dst[2 * i]) && float.IsFinite(dst[2 * i + 1]), "The cut corners are finite");

            cam.Set(137f, 156f, 0f, 50f, 0f, 20f);
            TestAssert.That(cam.Horizon < 156f - 900f, "A steep path pushes the horizon far above the panel");
            cam.Set(137f, 156f, 0f, 50f, 0f, 0f);
            TestAssert.Near(cam.Horizon, 156f, "A level camera has the horizon on the reference");
        }

        private static void Clipping()
        {
            float x1 = -50f, y1 = 10f, x2 = 150f, y2 = 10f;
            TestAssert.That(ScreenClip.Line(ref x1, ref y1, ref x2, ref y2, 0f, 0f, 100f, 100f) && Math.Abs(x1) < 0.01f && Math.Abs(x2 - 100f) < 0.01f, "A line across the box is cut at both sides");
            x1 = 200f; y1 = 200f; x2 = 300f; y2 = 250f;
            TestAssert.That(!ScreenClip.Line(ref x1, ref y1, ref x2, ref y2, 0f, 0f, 100f, 100f), "A line outside the box is dropped");
            x1 = 10f; y1 = 10f; x2 = 20f; y2 = 20f;
            TestAssert.That(ScreenClip.Line(ref x1, ref y1, ref x2, ref y2, 0f, 0f, 100f, 100f) && x1 == 10f && y2 == 20f, "A line inside is untouched");

            // A 200 x 200 square centred on a 100 x 100 box clips to the box.
            float[] sq = { -50f, -50f, 150f, -50f, 150f, 150f, -50f, 150f };
            float[] dst = new float[24], tmp = new float[24];
            int n = ScreenClip.Polygon(sq, 4, dst, tmp, 0f, 0f, 100f, 100f);
            TestAssert.That(n >= 4, "The clipped square has its corners");
            float minX = 1e9f, maxX = -1e9f, minY = 1e9f, maxY = -1e9f;
            for (int i = 0; i < n; i++) { minX = Math.Min(minX, dst[2 * i]); maxX = Math.Max(maxX, dst[2 * i]); minY = Math.Min(minY, dst[2 * i + 1]); maxY = Math.Max(maxY, dst[2 * i + 1]); }
            TestAssert.That(minX >= -0.01f && maxX <= 100.01f && minY >= -0.01f && maxY <= 100.01f && maxX - minX > 99f, "The polygon lies inside the box and fills it");
            float[] away = { 200f, 200f, 300f, 200f, 300f, 300f };
            TestAssert.Eq(ScreenClip.Polygon(away, 3, dst, tmp, 0f, 0f, 100f, 100f), 0, "A polygon outside the box vanishes");

            // The bullseye clip: a 24-gon circle of radius 50 around (100, 100); a huge square becomes (about) the circle, a far one vanishes.
            float[] circle = new float[48];
            for (int i = 0; i < 24; i++) { double a = i * Math.PI / 12.0; circle[2 * i] = 100f + 50f * (float)Math.Cos(a); circle[2 * i + 1] = 100f + 50f * (float)Math.Sin(a); }
            float[] big = { -500f, -500f, 900f, -500f, 900f, 900f, -500f, 900f };
            float[] cd = new float[2 * 40], ct = new float[2 * 40];
            int m = ScreenClip.Convex(big, 4, circle, 24, cd, ct);
            TestAssert.Eq(m, 24, "A square swallowing the circle clips to the circle's 24 corners");
            for (int i = 0; i < m; i++) TestAssert.That(Math.Abs(Math.Sqrt((cd[2 * i] - 100.0) * (cd[2 * i] - 100.0) + (cd[2 * i + 1] - 100.0) * (cd[2 * i + 1] - 100.0)) - 50.0) < 0.1, "Each corner lies on the circle");
            float[] wing = { 40f, 95f, 400f, 95f, 400f, 105f, 40f, 105f };      // a bar through the circle, far past its right edge
            m = ScreenClip.Convex(wing, 4, circle, 24, cd, ct);
            float maxR = 0f;
            for (int i = 0; i < m; i++) maxR = Math.Max(maxR, (float)Math.Sqrt((cd[2 * i] - 100f) * (cd[2 * i] - 100f) + (cd[2 * i + 1] - 100f) * (cd[2 * i + 1] - 100f)));
            TestAssert.That(m >= 4 && maxR <= 50.1f, "A bar through the circle is cut at the circle");
            float[] outside = { 300f, 300f, 400f, 300f, 400f, 400f };
            TestAssert.Eq(ScreenClip.Convex(outside, 3, circle, 24, cd, ct), 0, "A polygon outside the circle vanishes");
        }

        private static void Approach()
        {
            TestAssert.Near(ApproachMath.LocDots(1.25f), 1f, "One localizer dot is 1.25 degrees");
            TestAssert.Near(ApproachMath.LocDots(-9f), -2.6f, "The localizer needle stops past the last dot");
            TestAssert.Near(ApproachMath.GsDots(0.35f), 1f, "One glideslope dot is 0.35 degrees");
            TestAssert.That(ApproachMath.LocCaution(-2.0f) && !ApproachMath.LocCaution(1.9f), "The localizer cautions at 2.0 degrees");
            TestAssert.That(ApproachMath.GsCaution(0.6f) && !ApproachMath.GsCaution(-0.55f), "The glideslope cautions at 0.6 degrees");
            TestAssert.That(ApproachMath.SpeedCaution(11f / 3.6f) && !ApproachMath.SpeedCaution(9f / 3.6f), "Speed cautions at 10 km/h off");
            TestAssert.That(ApproachMath.OnSpeed(4f / 3.6f) && !ApproachMath.OnSpeed(6f / 3.6f), "ON SPD inside 5 km/h");

            TestAssert.Near(IlsGuidance.ManualTarget(70f, 55f, 400f), 70f, "High up the manual target is the approach speed");
            TestAssert.Near(IlsGuidance.ManualTarget(70f, 55f, 10f), 55f, "Over the runway it is the landing speed");
            TestAssert.Near(IlsGuidance.ManualTarget(70f, 55f, 85f), 62.5f, "In between it blends");

            TestAssert.Eq(IlsGuidance.ManualStage(true, 0f, 0f, 1f), ApproachStage.Rollout, "On the ground is the roll-out");
            TestAssert.Eq(IlsGuidance.ManualStage(false, 0f, 800f, 3f), ApproachStage.Flare, "Inside five seconds of touchdown is the flare");
            TestAssert.Eq(IlsGuidance.ManualStage(false, 3.2f, 5000f, 40f), ApproachStage.Intercept, "Off the localizer is still the intercept");
            TestAssert.Eq(IlsGuidance.ManualStage(false, 0.2f, 12000f, 120f), ApproachStage.Intercept, "Far out is the intercept");
            TestAssert.Eq(IlsGuidance.ManualStage(false, 0.2f, 5000f, 70f), ApproachStage.Glide, "On the line and close is the glide");
            TestAssert.That(Math.Abs(IlsGuidance.AutolandSlopeDegrees - 3.43f) < 0.01f, "The autopilot flies a 0.06 slope, 3.43 degrees");

            TestAssert.Near(ApproachMath.StageProgress(ApproachStage.Intercept, false, 5000f, 0f, 0f, 0f, 0f), 0.5f, "Intercept runs from ten kilometres");
            TestAssert.Near(ApproachMath.StageProgress(ApproachStage.Glide, false, 0f, 1500f, 0f, 0f, 0f), 0.75f, "Glide runs from six kilometres to touchdown");
            TestAssert.Near(ApproachMath.StageProgress(ApproachStage.Glide, true, 0f, 30f, 0f, 0f, 0f), 0.5f, "Over a pad the descent runs the last 60 m of height");
            TestAssert.Near(ApproachMath.StageProgress(ApproachStage.Intercept, true, 1000f, 0f, 0f, 0f, 0f), 0.5f, "A pad approach runs from two kilometres");
            TestAssert.Near(ApproachMath.StageProgress(ApproachStage.Flare, false, 0f, 0f, 2.5f, 0f, 0f), 0.5f, "Flare runs the last five seconds");
            TestAssert.Near(ApproachMath.StageProgress(ApproachStage.Rollout, false, 0f, 0f, 0f, 25f, 100f), 0.75f, "Roll-out is the speed bled off");
            TestAssert.Near(ApproachMath.StageProgress(ApproachStage.Rollout, false, 0f, 0f, 0f, 25f, 0f), 0f, "No reference speed, no progress");

            // 40 m/s at 4 m/s2 stops in 200 m; the stop point never leaves the runway.
            TestAssert.Near(ApproachMath.StopM(500f, 40f, 1800f), 700f, "The stop point is the speed squared over twice the deceleration");
            TestAssert.Near(ApproachMath.StopM(1700f, 60f, 1800f), 1800f, "The stop point stops at the runway's end");
            TestAssert.Near(ApproachMath.RemainM(1500f, 1800f), 300f, "Runway remaining");
            TestAssert.Near(ApproachMath.RemainM(1900f, 1800f), 0f, "Nothing remains past the end");
            TestAssert.Near(ApproachMath.LadderKm(-3f), -0.45f, "The ladder marker stops below the threshold");
            TestAssert.Near(ApproachMath.LadderKm(9f), 5.45f, "The ladder marker stops above the top rung");
            TestAssert.Eq(ApproachMath.Round10(684f), 680, "Roll-out readouts go in tens");
        }

        private static void Dock()
        {
            TestAssert.Near(DockMath.ConeM(1000f), 90f, "Far out the funnel is the full capture radius");
            TestAssert.Near(DockMath.ConeM(100f), 12f, "The funnel tightens as 0.12 of the range");
            TestAssert.Near(DockMath.ConeM(10f), 6f, "At the contact point it is 6 m");
            TestAssert.Near(DockMath.ConePx(90f), 96f, "The ring never leaves the bullseye");
            TestAssert.Near(DockMath.ConePx(6f), 19.2f, "The ring never draws tighter than 19 px");
            TestAssert.That(DockMath.Inside(8f, 17f) && !DockMath.Inside(18f, 17f), "Inside the funnel by the error");
            TestAssert.That(DockMath.NearEdge(14f, 17f, 140f) && !DockMath.NearEdge(14f, 17f, 20f), "Near the edge only while there are metres left to correct");
            TestAssert.Near(DockMath.TtcS(60f, 12f), 5f, "Time to contact is range over closure");
            TestAssert.Near(DockMath.TtcS(60f, 0.5f), 0f, "No time to contact when not closing");

            TestAssert.That(DockMath.TooFast(26f, 30f, 100f) && !DockMath.TooFast(24f, 30f, 100f) && !DockMath.TooFast(30f, 30f, 0f), "Too fast from 25 m/s of a 30 m/s limit, never at contact");
            TestAssert.Near(DockMath.ClosureFill(20f, 30f), 0.5f, "The closure bar is full at 40 m/s");
            TestAssert.Near(DockMath.LimitTick, 0.75f, "The limit tick sits at three quarters");

            DockMath.Directors(-21f, 13f, out float up, out float down, out float left, out float right);
            TestAssert.That(up == 0f && down == 13f && left == 0f && right == 21f, "Left of and above the contact point: move right and down");
            DockMath.Directors(4f, -9f, out up, out down, out left, out right);
            TestAssert.That(up == 9f && down == 0f && left == 4f && right == 0f, "Right of and below: move left and up");

            TestAssert.Near(DockMath.SilhouetteScale(20f), 215f, "The silhouette stops growing at 40 m");
            TestAssert.Near(DockMath.SilhouetteFade(DockMath.SilhouetteScale(400f)), 1f, "The silhouette is solid far out");
            TestAssert.Near(DockMath.SilhouetteFade(260f), 0f, "and gone when it would fill the view");
            TestAssert.Near(DockMath.RibbonAt(240f), 0f, "The ribbon starts at 240 m");
            TestAssert.Near(DockMath.RibbonAt(0f), 1f, "and ends at contact");
            TestAssert.Near(DockMath.RibbonAt(900f), 0f, "and clamps beyond");
        }

        private static void Wing()
        {
            WingBoardMath.Place(0f, 0f, out float px, out float py, out bool clamped);
            TestAssert.That(!clamped && px == WingBoardMath.PlotX && py == WingBoardMath.PlotY, "The player is the plot's centre");
            WingBoardMath.Place(0f, 350f, out px, out py, out clamped);
            TestAssert.Near(py, WingBoardMath.PlotY - WingBoardMath.PlotR / 2f, "350 m ahead is the half ring, up");
            WingBoardMath.Place(300f, 0f, out px, out _, out clamped);
            TestAssert.That(px > WingBoardMath.PlotX && !clamped, "Right of the nose is right on the plot");
            WingBoardMath.Place(0f, -3000f, out px, out py, out clamped);
            TestAssert.That(clamped && Math.Abs(py - (WingBoardMath.PlotY + WingBoardMath.ClampR)) < 0.01f, "A wingman far astern sits on the rim, behind");
            TestAssert.Near(WingBoardMath.BearingDeg(1f, 1f), 45f, "Bearings are clockwise from the nose");
            TestAssert.Near(WingBoardMath.BearingDeg(-1f, 0f), -90f, "Left is minus ninety");

            // A finger four: slots 2 (210, -140), 3 (-210, -140), 4 (-420, -280) hang lead-2, lead-3, 3-4.
            float[] sx = { 210f, -210f, -420f }, sy = { -140f, -140f, -280f };
            int[] parent = new int[3];
            WingBoardMath.Skeleton(sx, sy, 3, parent);
            TestAssert.That(parent[0] == -1 && parent[1] == -1 && parent[2] == 1, "A finger four's skeleton: lead-2, lead-3, 3-4");
            float s = WingBoardMath.GlyphScale(sx, sy, 3);
            TestAssert.That(s > 0.04f && s < 0.045f, "The glyph scale fits the widest slot (got " + s + ")");

            WingBoardMath.Rows(1, out int shown, out float rowH, out float step, out bool task, out bool acks);
            TestAssert.That(shown == 1 && rowH == 62f && step == 66f && task && acks, "One wingman: a full row and the ack log");
            WingBoardMath.Rows(3, out shown, out rowH, out step, out task, out acks);
            TestAssert.That(shown == 3 && task && acks, "Three wingmen still have the ack log");
            WingBoardMath.Rows(4, out shown, out rowH, out step, out task, out acks);
            TestAssert.That(shown == 4 && rowH == 62f && task && !acks, "Four wingmen fill the column at full size and lose the log");
            WingBoardMath.Rows(7, out shown, out rowH, out step, out task, out acks);
            TestAssert.That(shown == 7 && rowH >= 34f && !task && step * 7 - 3f <= 260.5f, "Seven wingmen compact into the column");
            WingBoardMath.Rows(12, out shown, out _, out _, out _, out _);
            TestAssert.Eq(shown, 7, "No more than seven rows");

            var placer = new LabelPlacer();
            placer.Reset();
            float y0 = placer.Place(10f, 50f, 100f), y1 = placer.Place(20f, 60f, 100f), y2 = placer.Place(200f, 240f, 100f);
            TestAssert.That(y0 == 100f && y1 == 115f && y2 == 100f, "A label that overlaps one placed steps down 15; a clear one stays");

            TestAssert.That(WingBoardMath.ToneOf(WingState.Damaged) == ScopeTone.Amber && WingBoardMath.ToneOf(WingState.Defend) == ScopeTone.Amber && WingBoardMath.ToneOf(WingState.Winch) == ScopeTone.Amber, "Hit, defending and out of weapons are cautions");
            TestAssert.That(WingBoardMath.ToneOf(WingState.Rtb) == ScopeTone.Dim && WingBoardMath.ToneOf(WingState.Engage) == ScopeTone.Ink, "Going home is quiet, fighting is plain");
            TestAssert.That(WingBoardMath.Hollow(WingState.Join) && WingBoardMath.Hollow(WingState.Winch) && !WingBoardMath.Hollow(WingState.Engage), "Hollow while joining or dry");
            TestAssert.Eq(WingBoardMath.Word(WingState.Winch), "WINCH", "WINCH reads WINCH");

            var m = new WingBoardMember[3];
            TestAssert.That(!WingBoardMath.NeedsAttention(m, 3) && WingBoardMath.OutCount(m, 3) == 0, "A wing all in slot needs nothing");
            m[1].State = WingState.Join;
            TestAssert.That(WingBoardMath.NeedsAttention(m, 3) && WingBoardMath.OutCount(m, 3) == 1, "One out of slot calls the page");
            m[1].State = WingState.Ground;
            TestAssert.That(!WingBoardMath.NeedsAttention(m, 3), "A member on the ground is not the page's business");
            m[2].State = WingState.Rtb;
            TestAssert.That(!WingBoardMath.NeedsAttention(m, 3) && WingBoardMath.OutCount(m, 3) == 0, "A member going home is an event, not a standing reason to show the page");
            m[2].State = WingState.Damaged;
            TestAssert.That(!WingBoardMath.NeedsAttention(m, 3), "A hit stays on the roster but does not hold the page up for the sortie");
            m[0].State = WingState.Engage;
            TestAssert.That(WingBoardMath.NeedsAttention(m, 3), "A fight calls the page");
            m[0].State = WingState.Defend;
            TestAssert.That(WingBoardMath.NeedsAttention(m, 3), "A missile on a wingman calls the page");
        }
    }
}
