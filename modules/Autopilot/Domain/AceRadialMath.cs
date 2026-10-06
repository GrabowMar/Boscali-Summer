using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Autopilot.Domain
{
    /// <summary>Pure 2D vector for deterministic radial layout math without UnityEngine.</summary>
    internal readonly struct AceVec2
    {
        public float X { get; }
        public float Y { get; }

        public AceVec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static AceVec2 operator +(AceVec2 a, AceVec2 b) => new AceVec2(a.X + b.X, a.Y + b.Y);
        public static AceVec2 operator *(AceVec2 a, float scalar) => new AceVec2(a.X * scalar, a.Y * scalar);

        public static float Distance(AceVec2 a, AceVec2 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return (float)Math.Sqrt((dx * dx) + (dy * dy));
        }

        public static AceVec2 FromAngle(float degrees)
        {
            double rad = degrees * Math.PI / 180.0;
            return new AceVec2((float)Math.Cos(rad), (float)Math.Sin(rad));
        }

        public override string ToString() => $"({X:F1}, {Y:F1})";
    }

    /// <summary>
    /// Layout constants and geometry from ACE3 interact_menu (fnc_renderMenu / fnc_render):
    /// children fan out along their parent's direction in 55 degree steps, a level that would
    /// span 305 degrees or more becomes a full ring, sub-levels are squeezed into 150 degrees,
    /// and the radius widens as the step narrows so labels never collide. Angles are
    /// counter-clockwise from screen right with y up (Unity screen space).
    /// </summary>
    internal static class AceRadialMath
    {
        public const float StepDeg = 55f;
        public const float RootSpanDeg = 360f;
        public const float SubLevelSpanDeg = 150f;
        public const float RingThresholdDeg = 305f;

        /// <summary>ACE base radius (0.17 UI units) expressed in 1080p pixels.</summary>
        public const float BaseRadiusPx = 190f;
        /// <summary>ACE hover pick distance (0.1118 UI units) in the same 1080p pixels.</summary>
        public const float HoverRadiusPx = 125f;

        /// <summary>Child angles for one level, centred on <paramref name="centerDeg"/>.</summary>
        public static float[] FanAngles(int count, float centerDeg, float maxSpanDeg, out float intervalDeg)
        {
            if (count <= 0)
            {
                intervalDeg = StepDeg;
                return Array.Empty<float>();
            }

            float span = Math.Min(maxSpanDeg, StepDeg * (count - 1));
            if (span >= RingThresholdDeg) span = 360f;
            intervalDeg = count == 1 ? StepDeg : span >= 360f ? 360f / count : span / (count - 1);

            var angles = new float[count];
            float start = span >= 360f ? centerDeg : centerDeg - (span * 0.5f);
            for (int i = 0; i < count; i++) angles[i] = start + (intervalDeg * i);
            return angles;
        }

        /// <summary>ACE radius factor: clamp(0.8 * 0.46 / sin(interval / 2), 0.5, 1.1).</summary>
        public static float RadiusFactor(float intervalDeg)
        {
            double half = Math.Max(1.0, intervalDeg) * Math.PI / 360.0;
            double factor = 0.8 * 0.46 / Math.Sin(half);
            return (float)Math.Max(0.5, Math.Min(1.1, factor));
        }

        /// <summary>Newest level pops out from 30% to full size (0.3 + 0.7 * progress).</summary>
        public static float ExpandScale(float progress) =>
            0.3f + (0.7f * Math.Max(0f, Math.Min(1f, progress)));

        /// <summary>Nearest point to the cursor within <paramref name="threshold"/>, or -1.</summary>
        public static int FindClosest(AceVec2 cursor, IReadOnlyList<AceVec2> points, float threshold)
        {
            int closest = -1;
            float best = threshold;
            if (points == null) return closest;
            for (int i = 0; i < points.Count; i++)
            {
                float distance = AceVec2.Distance(cursor, points[i]);
                if (distance < best)
                {
                    best = distance;
                    closest = i;
                }
            }
            return closest;
        }

    }
}
