using System;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// Pure math and formatting for the in-flight Quick Map (<c>MapHoldKey</c>): zoom stepping, open/close animation easing,
    /// heading-up forward look-ahead, clean round range-ring step sizing, North-marker perimeter projection, and compact telemetry formatting.
    /// No Unity types, compiled directly into <c>BoscaliSummer.Tests</c>.
    /// </summary>
    internal static class QuickMapMath
    {
        public const float MinZoom = 0.50f, MaxZoom = 4.00f, DefaultZoom = 1.15f;
        public const float ZoomFactor = 1.20f;
        public const float OpenSeconds = 0.11f, CloseSeconds = 0.08f;
        public const float DecenterShare = 0.18f;
        public const float CornerLookAheadMetres = 4000f;
        public const float NorthTickPx = 9f, NorthLabelInsetPx = 28f;

        private const float MetresPerNm = 1852f;
        private static readonly float[] MetricSteps = { 1000f, 2000f, 5000f, 10000f, 20000f, 40000f, 80000f };
        private static readonly float[] ImperialNmSteps = { 1f, 2f, 5f, 10f, 20f, 40f, 80f };

        /// <summary>Steps Quick Map zoom by <paramref name="notches"/> (positive = zoom in, negative = zoom out), clamped to [<see cref="MinZoom"/>, <see cref="MaxZoom"/>].</summary>
        public static float StepZoom(float current, float notches)
        {
            float baseZoom = float.IsFinite(current) && current > 0f ? current : DefaultZoom;
            if (!float.IsFinite(notches) || notches == 0f) return Clamp(baseZoom, MinZoom, MaxZoom);
            return Clamp(baseZoom * MathF.Pow(ZoomFactor, notches), MinZoom, MaxZoom);
        }

        /// <summary>Smooth open/close curve over normalized progress <paramref name="t"/> in [0, 1].</summary>
        public static float Ease(float t, bool opening)
        {
            float u = Clamp(t, 0f, 1f);
            if (opening)
            {
                float inv = 1f - u;
                return 1f - inv * inv * inv;
            }
            return u * u;
        }

        /// <summary>Metres ahead of the nose to offset the map center in heading-up mode so ownship sits ~32% from the bottom; 0 in North-Up or on invalid inputs.</summary>
        public static float LookAheadMetres(float sidePx, float mapDisplayFactor, float mapScale, bool northUp)
        {
            if (northUp || !ValidPositive(sidePx) || !ValidPositive(mapDisplayFactor) || !ValidPositive(mapScale)) return 0f;
            return sidePx * DecenterShare / (mapDisplayFactor * mapScale);
        }

        /// <summary>Picks a clean round range-ring step (in metres) yielding 1 to 3 concentric rings ahead of ownship inside the Quick Map.</summary>
        public static float RingStepMetres(float sidePx, float mapDisplayFactor, float mapScale, bool northUp, bool imperial, out int ringCount, out float pxPerRing)
        {
            ringCount = 0;
            pxPerRing = 0f;
            if (!ValidPositive(sidePx) || !ValidPositive(mapDisplayFactor) || !ValidPositive(mapScale)) return 0f;
            float pxPerMetre = mapDisplayFactor * mapScale;
            float forwardPx = sidePx * (northUp ? 0.44f : 0.62f);
            float minRingPx = forwardPx / 3.4f;

            float[] table = imperial ? ImperialNmSteps : MetricSteps;
            float scale = imperial ? MetresPerNm : 1f;
            float chosen = table[table.Length - 1] * scale;
            for (int i = 0; i < table.Length; i++)
            {
                float candidate = table[i] * scale;
                if (candidate * pxPerMetre >= minRingPx)
                {
                    chosen = candidate;
                    break;
                }
            }
            pxPerRing = chosen * pxPerMetre;
            ringCount = Math.Max(1, Math.Min(3, (int)(forwardPx / Math.Max(1f, pxPerRing))));
            return chosen;
        }

        /// <summary>
        /// Intersects a ray toward geographic North from the center of a square centered at (0, 0) (Unity UI Y-up coordinates) with half-extent <paramref name="halfSide"/>.
        /// Positive <paramref name="turnedCcwDeg"/> (clockwise aircraft heading in degrees) rotates North counter-clockwise from up.
        /// </summary>
        public static void NorthEdgePoint(float halfSide, float turnedCcwDeg, float cornerClear,
            out float ex, out float ey, out float inX, out float inY, out float lblX, out float lblY)
        {
            float rad = (float.IsFinite(turnedCcwDeg) ? turnedCcwDeg : 0f) * (MathF.PI / 180f);
            float dx = -MathF.Sin(rad), dy = MathF.Cos(rad);
            float tx = MathF.Abs(dx) > 1e-4f ? halfSide / MathF.Abs(dx) : float.MaxValue;
            float ty = MathF.Abs(dy) > 1e-4f ? halfSide / MathF.Abs(dy) : float.MaxValue;
            float limit = Math.Max(0f, halfSide - Math.Max(0f, cornerClear));
            if (tx < ty)
            {
                float sx = dx >= 0f ? 1f : -1f;
                ex = sx * halfSide;
                ey = Clamp(dy * tx, -limit, limit);
                inX = ex - sx * NorthTickPx; inY = ey;
                lblX = ex - sx * NorthLabelInsetPx; lblY = ey;
            }
            else
            {
                float sy = dy >= 0f ? 1f : -1f;
                ex = Clamp(dx * ty, -limit, limit);
                ey = sy * halfSide;
                inX = ex; inY = ey - sy * NorthTickPx;
                lblX = ex; lblY = ey - sy * NorthLabelInsetPx;
            }
        }

        /// <summary>Clockwise compass bearing in [0, 359] for horizontal offset (<paramref name="dx"/> east, <paramref name="dz"/> north).</summary>
        public static int BearingDeg(float dx, float dz)
        {
            if (!float.IsFinite(dx) || !float.IsFinite(dz)) return 0;
            float deg = MathF.Atan2(dx, dz) * (180f / MathF.PI);
            int rounded = (int)MathF.Round(deg);
            return ((rounded % 360) + 360) % 360;
        }

        public static string FormatRingStep(float stepMetres, bool imperial)
        {
            if (!ValidPositive(stepMetres)) return "";
            if (imperial) return ((int)MathF.Round(stepMetres / MetresPerNm)) + " NM";
            return ((int)MathF.Round(stepMetres / 1000f)) + " KM";
        }

        public static string FormatRange(float metres, bool imperial)
        {
            if (!float.IsFinite(metres) || metres < 0f) return "--";
            if (imperial)
            {
                float nm = metres / MetresPerNm;
                return nm >= 10f ? ((int)MathF.Round(nm)) + " NM" : nm.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " NM";
            }
            if (metres < 1000f) return ((int)MathF.Round(metres)) + " M";
            float km = metres / 1000f;
            return km >= 10f ? ((int)MathF.Round(km)) + " KM" : km.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " KM";
        }

        public static string FormatZoom(float zoom) =>
            (float.IsFinite(zoom) && zoom > 0f ? zoom : DefaultZoom).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "×";

        private static bool ValidPositive(float v) => float.IsFinite(v) && v > 0f;
        private static float Clamp(float v, float lo, float hi) => !float.IsFinite(v) ? lo : v < lo ? lo : v > hi ? hi : v;
    }
}
