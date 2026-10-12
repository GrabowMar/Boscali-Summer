using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The runway funnel's camera (HUD v8 APPROACH, prototype <c>makeCam</c>), pure: a pinhole at the aircraft, aligned with the runway and pitched to
    /// the flight path, in the prototype's anamorphic scale (2000 px per unit lateral angle, 3000 vertical: one LOC dot of 1.25 degrees is 44 px, one GS dot
    /// of 0.35 degrees 18 px, so the picture and the dot scales agree). World: x across (+ right), y up, z along the runway from the threshold, metres.
    /// Points behind <see cref="Near"/> are dropped; polygons and segments are cut at the near plane.
    /// </summary>
    internal sealed class ApproachCam
    {
        public const float Kx = 2000f, Ky = 3000f, Near = 8f, EyeAbove = 2.5f;

        private float cx, cy, ax, ay, az, sp, cp;

        /// <summary>Screen y of the horizon (it leaves the panel upward while the flight path is steeper than about ten degrees).</summary>
        public float Horizon { get; private set; }

        /// <param name="centreX">Screen position of the flight-path reference.</param>
        /// <param name="lateral">Aircraft x (+ right of the centre line); <paramref name="height"/> the wheels' height above the runway; <paramref name="along"/> its z.</param>
        public void Set(float centreX, float centreY, float lateral, float height, float along, float pitchDeg)
        {
            cx = centreX; cy = centreY;
            ax = lateral; ay = height + EyeAbove; az = along;
            double p = pitchDeg * Math.PI / 180.0;
            sp = (float)Math.Sin(p); cp = (float)Math.Cos(p);
            Horizon = centreY - Ky * (float)Math.Tan(p);
        }

        private void View(float x, float y, float z, out float vx, out float vy, out float vz)
        {
            float rx = x - ax, ry = y - ay, rz = z - az;
            vx = rx; vy = ry * cp + rz * sp; vz = -ry * sp + rz * cp;
        }

        private void Screen(float vx, float vy, float vz, out float sx, out float sy)
        {
            sx = cx + Kx * vx / vz;
            sy = cy - Ky * vy / vz;
        }

        /// <summary>Distance in front of the pinhole (the prototype's <c>zc</c>); at or below <see cref="Near"/> the point is behind the picture.</summary>
        public float Depth(float x, float y, float z) { View(x, y, z, out _, out _, out float vz); return vz; }

        public bool Point(float x, float y, float z, out float sx, out float sy)
        {
            View(x, y, z, out float vx, out float vy, out float vz);
            sx = sy = 0f;
            if (vz <= Near) return false;
            Screen(vx, vy, vz, out sx, out sy);
            return true;
        }

        /// <summary>A polygon of world points (x, y, z triplets) cut at the near plane into screen points (x, y pairs) in <paramref name="dst"/>
        /// (room for <c>4 * n</c> floats). Returns the screen point count.</summary>
        public int Polygon(float[] xyz, int n, float[] dst)
        {
            int m = 0;
            float px, py;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                View(xyz[3 * i], xyz[3 * i + 1], xyz[3 * i + 2], out float ax0, out float ay0, out float az0);
                View(xyz[3 * j], xyz[3 * j + 1], xyz[3 * j + 2], out float bx0, out float by0, out float bz0);
                bool ain = az0 > Near, bin = bz0 > Near;
                if (ain) { Screen(ax0, ay0, az0, out px, out py); dst[2 * m] = px; dst[2 * m + 1] = py; m++; }
                if (ain != bin)
                {
                    float u = (Near - az0) / (bz0 - az0);
                    Screen(ax0 + (bx0 - ax0) * u, ay0 + (by0 - ay0) * u, Near, out px, out py);
                    dst[2 * m] = px; dst[2 * m + 1] = py; m++;
                }
            }
            return m;
        }

        /// <summary>A world segment cut at the near plane, as screen endpoints; false when it is wholly behind.</summary>
        public bool Segment(float x1, float y1, float z1, float x2, float y2, float z2, out float sx1, out float sy1, out float sx2, out float sy2)
        {
            sx1 = sy1 = sx2 = sy2 = 0f;
            View(x1, y1, z1, out float ax0, out float ay0, out float az0);
            View(x2, y2, z2, out float bx0, out float by0, out float bz0);
            if (az0 <= Near && bz0 <= Near) return false;
            if (az0 <= Near) { float u = (Near - az0) / (bz0 - az0); ax0 += (bx0 - ax0) * u; ay0 += (by0 - ay0) * u; az0 = Near; }
            if (bz0 <= Near) { float u = (Near - bz0) / (az0 - bz0); bx0 += (ax0 - bx0) * u; by0 += (ay0 - by0) * u; bz0 = Near; }
            Screen(ax0, ay0, az0, out sx1, out sy1);
            Screen(bx0, by0, bz0, out sx2, out sy2);
            return true;
        }
    }

    /// <summary>Clipping to a page's window (the funnel, the OBJECTIVE plot, the pennants, the DOCK bullseye): the prototype clips the canvas, the scope canvas has no clip stack. Liang-Barsky for a segment, Sutherland-Hodgman for a polygon.</summary>
    internal static class ScreenClip
    {
        /// <summary>Clip the segment to the rectangle in place; false when nothing of it is inside.</summary>
        public static bool Line(ref float x1, ref float y1, ref float x2, ref float y2, float left, float top, float right, float bottom)
        {
            float dx = x2 - x1, dy = y2 - y1, u0 = 0f, u1 = 1f;
            for (int k = 0; k < 4; k++)
            {
                float p = k == 0 ? -dx : k == 1 ? dx : k == 2 ? -dy : dy;
                float q = k == 0 ? x1 - left : k == 1 ? right - x1 : k == 2 ? y1 - top : bottom - y1;
                if (p == 0f) { if (q < 0f) return false; continue; }
                float r = q / p;
                if (p < 0f) { if (r > u1) return false; if (r > u0) u0 = r; }
                else { if (r < u0) return false; if (r < u1) u1 = r; }
            }
            float nx1 = x1 + u0 * dx, ny1 = y1 + u0 * dy, nx2 = x1 + u1 * dx, ny2 = y1 + u1 * dy;
            x1 = nx1; y1 = ny1; x2 = nx2; y2 = ny2;
            return true;
        }

        /// <summary>Sutherland-Hodgman against one half-plane: edge 0 keeps x &gt;= v, 1 keeps x &lt;= v, 2 keeps y &gt;= v, 3 keeps y &lt;= v. Writes the cut polygon to
        /// <paramref name="dst"/> (room for <c>2 * (n + 1)</c> floats) and returns its point count.</summary>
        public static int Half(float[] src, int n, float[] dst, int edge, float v)
        {
            int m = 0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float ax = src[2 * i], ay = src[2 * i + 1], bx = src[2 * j], by = src[2 * j + 1];
                float da = Dist(edge, ax, ay, v), db = Dist(edge, bx, by, v);
                if (da >= 0f && m < dst.Length / 2) { dst[2 * m] = ax; dst[2 * m + 1] = ay; m++; }
                if ((da >= 0f) != (db >= 0f) && m < dst.Length / 2)
                {
                    float u = da / (da - db);
                    dst[2 * m] = ax + (bx - ax) * u;
                    dst[2 * m + 1] = ay + (by - ay) * u;
                    m++;
                }
            }
            return m;
        }

        /// <summary>Clip the polygon <paramref name="xy"/> (<paramref name="n"/> points) to the rectangle into <paramref name="dst"/> (room for <c>2 * (n + 4)</c> floats)
        /// using <paramref name="tmp"/> (the same size) as scratch. Returns the point count (0 when outside).</summary>
        public static int Polygon(float[] xy, int n, float[] dst, float[] tmp, float left, float top, float right, float bottom)
        {
            int m = Half(xy, n, tmp, 0, left);
            m = Half(tmp, m, dst, 1, right);
            m = Half(dst, m, tmp, 2, top);
            return Half(tmp, m, dst, 3, bottom);
        }

        /// <summary>Clip the polygon <paramref name="xy"/> to a convex clip polygon <paramref name="clip"/> (<paramref name="m"/> points, clockwise on screen, y down) into
        /// <paramref name="dst"/> (room for <c>2 * (n + m)</c> floats) using <paramref name="tmp"/> as scratch. Returns the point count (0 when outside). The DOCK bullseye clips its
        /// glider silhouette to a 24-gon circle with this.</summary>
        public static int Convex(float[] xy, int n, float[] clip, int m, float[] dst, float[] tmp)
        {
            float[] a = xy, to = dst;
            int count = n;
            for (int e = 0; e < m && count > 0; e++)
            {
                int f = (e + 1) % m;
                float ex = clip[2 * e], ey = clip[2 * e + 1], dx = clip[2 * f] - ex, dy = clip[2 * f + 1] - ey;
                to = (e & 1) == 0 ? dst : tmp;
                int k = 0;
                for (int i = 0; i < count; i++)
                {
                    int j = (i + 1) % count;
                    float x0 = a[2 * i], y0 = a[2 * i + 1], x1 = a[2 * j], y1 = a[2 * j + 1];
                    float s0 = dx * (y0 - ey) - dy * (x0 - ex), s1 = dx * (y1 - ey) - dy * (x1 - ex);     // >= 0: on the inner side of a clockwise (y down) polygon
                    bool in0 = s0 >= 0f, in1 = s1 >= 0f;
                    if (in0) { to[2 * k] = x0; to[2 * k + 1] = y0; k++; }
                    if (in0 != in1)
                    {
                        float u = s0 / (s0 - s1);
                        to[2 * k] = x0 + (x1 - x0) * u; to[2 * k + 1] = y0 + (y1 - y0) * u; k++;
                    }
                }
                a = to;
                count = k;
            }
            if (count > 0 && !ReferenceEquals(a, dst)) Array.Copy(a, dst, count * 2);
            return count;
        }

        private static float Dist(int edge, float x, float y, float v) => edge == 0 ? x - v : edge == 1 ? v - x : edge == 2 ? y - v : v - y;
    }

    /// <summary>The APPROACH page's rules (HUD v8), pure: the dot scales, the cautions, the speed words, the chain progress, the roll-out numbers.</summary>
    internal static class ApproachMath
    {
        /// <summary>Degrees per dot of the localizer (2.5 full scale) and glideslope (0.7) scales; pixels per dot on the page.</summary>
        public const float LocPerDot = 1.25f, GsPerDot = 0.35f, LocDotPx = 44f, GsDotPx = 18f;
        /// <summary>Cautions: a localizer 2.0 degrees off, a glideslope 0.6 degrees off, a speed 10 km/h off.</summary>
        public const float CautionLocDeg = 2.0f, CautionGsDeg = 0.6f, CautionSpeedMps = 10f / 3.6f;
        /// <summary>The speed word reads ON SPD inside 5 km/h; the ruler spans +-20 km/h.</summary>
        public const float OnSpeedMps = 5f / 3.6f, SpeedBarMps = 20f / 3.6f;
        /// <summary>Assumed deceleration for the predicted stopping point (m/s2), the prototype's v^2 / 8.</summary>
        public const float RolloutDecel = 4f;
        /// <summary>The sink rate the gauge and the page call high (m/s).</summary>
        public const float SinkCautionMps = 8f;
        /// <summary>The flare lasts the last five seconds to touchdown (the autopilot's own rule): its progress bar runs over them.</summary>
        public const float FlareSeconds = 5f;

        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        public static float LocDots(float locDeg) => Clamp(locDeg / LocPerDot, -2.6f, 2.6f);

        public static float GsDots(float gsDeg) => Clamp(gsDeg / GsPerDot, -2.6f, 2.6f);

        public static bool LocCaution(float locDeg) => Math.Abs(locDeg) >= CautionLocDeg;

        public static bool GsCaution(float gsDeg) => Math.Abs(gsDeg) >= CautionGsDeg;

        public static bool SpeedCaution(float deltaMps) => Math.Abs(deltaMps) >= CautionSpeedMps;

        public static bool OnSpeed(float deltaMps) => Math.Abs(deltaMps) < OnSpeedMps;

        /// <summary>Where the SPD ruler's index mark sits, 0..1 across it: the middle at the speed the approach wants, an end at +-<see cref="SpeedBarMps"/> (20 km/h) off it.</summary>
        public static float RulerFrac(float deltaMps) => 0.5f + 0.5f * Clamp(deltaMps / SpeedBarMps, -1f, 1f);

        /// <summary>0..1 through the current stage: distance to the field for INTERCEPT, distance to the touchdown point for GLIDE (from 6 km; over a pad the height left of a 60 m descent),
        /// touchdown time for FLARE, speed bled off against <paramref name="rolloutRef"/> for ROLLOUT.</summary>
        public static float StageProgress(ApproachStage stage, bool pad, float distanceM, float toTouchdownM, float touchdownS, float groundMps, float rolloutRef)
        {
            switch (stage)
            {
                case ApproachStage.Intercept: return 1f - Clamp(distanceM / (pad ? 2000f : 10000f), 0f, 1f);
                case ApproachStage.Glide: return 1f - Clamp(toTouchdownM / (pad ? 60f : 6000f), 0f, 1f);
                case ApproachStage.Flare: return 1f - Clamp(touchdownS / FlareSeconds, 0f, 1f);
                default: return rolloutRef > 1f ? 1f - Clamp(groundMps / rolloutRef, 0f, 1f) : 0f;
            }
        }

        /// <summary>Where the aircraft will stop if it brakes at <see cref="RolloutDecel"/>, measured from the threshold, capped at the runway's end.</summary>
        public static float StopM(float alongM, float groundMps, float runwayLength) =>
            Math.Min(runwayLength, alongM + groundMps * groundMps / (2f * RolloutDecel));

        /// <summary>Runway left in front of the aircraft.</summary>
        public static float RemainM(float alongM, float runwayLength) => Math.Max(0f, runwayLength - alongM);

        /// <summary>The distance ladder's marker height in kilometres, clamped a little past each end.</summary>
        public static float LadderKm(float kmToThreshold) => Clamp(kmToThreshold, -0.45f, 5.45f);

        /// <summary>Round to the nearest ten (the roll-out readouts do not flicker with single metres).</summary>
        public static int Round10(float v) => (int)(Math.Round(v / 10f) * 10f);
    }
}
