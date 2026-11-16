using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The two segmented arcs round the reticle (2026-11-16, the approved mockup's <c>arcs(d)</c>), pure: fuel on the left, throttle on the right, each 22 segments over 29 degrees. Angles are
    /// degrees counter-clockwise from the arc's own axis (the direction from the reticle to the middle of the game's arc: left for fuel, right for throttle), so the same numbers serve both
    /// arcs and a rolled or moved HUD only turns the frame. Fuel runs the other way round (full at the clockwise end, the top of the left arc); throttle fills counter-clockwise (up the right arc).
    /// </summary>
    internal static class FlightArcs
    {
        public const int Segments = 22;
        public const float SpanDeg = 29f, HalfSpanDeg = SpanDeg * 0.5f, GapFraction = 0.006f;
        /// <summary>The game's own arcs sweep their pointer 28 (fuel) and 26 (throttle) degrees: half of it is what the middle of an arc's rectangle sits off the circle (<see cref="RadiusFromRect"/>).</summary>
        public const float FuelNativeHalfDeg = 14f, ThrottleNativeHalfDeg = 13f;
        /// <summary>The afterburner detent tick of the throttle arc is the game's own (its boundary pivot turns <c>(start + 0.01) * 26 - 13</c> degrees).</summary>
        public const float ThrottleNativeSweepDeg = 26f;
        /// <summary>The segment width lit and unlit (px at 1080 high), and the unlit alpha.</summary>
        public const float LitWidth = 8f, UnlitWidth = 5f, UnlitAlpha = 0.4f;

        /// <summary>How many segments are lit at <paramref name="fraction"/> (0..1): a segment is lit when its middle is at or below the fraction, so 0 is none and 1 is all.</summary>
        public static int Lit(float fraction)
        {
            if (!(fraction > 0f)) return 0;
            if (fraction >= 1f) return Segments;
            int n = (int)Math.Floor(fraction * Segments - 0.5f) + 1;
            return n < 0 ? 0 : n > Segments ? Segments : n;
        }

        /// <summary>Where segment <paramref name="i"/> (0 = empty end) starts and ends, in the arc's own angle; <paramref name="reversed"/> for fuel (the empty end is the counter-clockwise one).</summary>
        public static void Segment(int i, bool reversed, out float a0, out float a1)
        {
            float t0 = (float)i / Segments + GapFraction, t1 = (float)(i + 1) / Segments - GapFraction;
            a0 = At(t0, reversed);
            a1 = At(t1, reversed);
        }

        /// <summary>The angle of fill fraction <paramref name="fraction"/> (clamped to 0..1) along the arc.</summary>
        public static float At(float fraction, bool reversed)
        {
            float f = fraction < 0f ? 0f : fraction > 1f ? 1f : fraction;
            float a = -HalfSpanDeg + SpanDeg * f;
            return reversed ? -a : a;
        }

        /// <summary>The circle's radius from the distance between the arc's centre and the middle of its rectangle: a circular arc of half-span h has a rectangle whose middle sits at R (1 + cos h) / 2.</summary>
        public static float RadiusFromRect(float distance, float nativeHalfDeg) =>
            distance / ((1f + (float)Math.Cos(nativeHalfDeg * Math.PI / 180.0)) * 0.5f);

        /// <summary>The throttle fraction of the game's detent tick from the boundary pivot's rotation (degrees, any wrap); NaN when it is not on the arc.</summary>
        public static float DetentFraction(float pivotDeg)
        {
            float z = ((pivotDeg % 360f) + 540f) % 360f - 180f;
            float f = (z + ThrottleNativeSweepDeg * 0.5f) / ThrottleNativeSweepDeg;
            return f > 0.01f && f < 0.995f ? f : float.NaN;
        }

        /// <summary>A tick position as a small int for a change signature (NaN is -1); the half-segment is the finest the picture can show.</summary>
        public static int Quant(float fraction) => float.IsNaN(fraction) ? -1 : (int)Math.Round(Math.Max(0f, Math.Min(1f, fraction)) * Segments * 2f);

        /// <summary>The whole percent the label prints (0..100, rounded), clamped.</summary>
        public static int Percent(float fraction) => float.IsNaN(fraction) ? 0 : (int)Math.Round(Math.Max(0f, Math.Min(1f, fraction)) * 100f);

        /// <summary>Writes "NN%" into <paramref name="to"/> (prefix first when not empty) and returns its length.</summary>
        public static int PercentText(char[] to, int start, int percent)
        {
            int n = start;
            if (percent >= 100) { to[n++] = '1'; to[n++] = '0'; to[n++] = '0'; }
            else if (percent >= 10) { to[n++] = (char)('0' + percent / 10); to[n++] = (char)('0' + percent % 10); }
            else to[n++] = (char)('0' + Math.Max(0, percent));
            to[n++] = '%';
            return n;
        }
    }
}
