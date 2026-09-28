using System;

namespace NOAvionics
{
    /// <summary>One tick mark on a linear or wrapping HUD tape.</summary>
    public struct AvTick
    {
        public float Offset;
        public bool Major;
        public float Value;
    }

    /// <summary>
    /// Allocation-free tick generation for HUD tapes (speed, altitude, heading). Callers own the
    /// output buffer; every method fills it in place and returns how much of it it used.
    /// </summary>
    public static class AvTapeMath
    {
        /// <summary>
        /// Fills <paramref name="into"/> with ticks at every multiple of <paramref name="minorStep"/>
        /// inside <paramref name="center"/> ± <paramref name="halfExtent"/>/<paramref name="pixelsPerUnit"/>.
        /// A tick is major when its step index is a multiple of <paramref name="majorEvery"/>.
        /// When <paramref name="wrap"/> is positive, <see cref="AvTick.Value"/> wraps into
        /// [0, wrap); otherwise it is the raw tape value. Returns the number of ticks written,
        /// capped at <c>into.Length</c>. Invalid input (NaN/Infinity center, a non-positive
        /// <paramref name="pixelsPerUnit"/>/<paramref name="halfExtent"/>/<paramref name="minorStep"/>,
        /// or a null/empty buffer) writes nothing and returns 0.
        /// </summary>
        public static int Ticks(AvTick[] into, float center, float pixelsPerUnit, float halfExtent, float minorStep, int majorEvery, float wrap)
        {
            if (into == null || into.Length == 0) return 0;
            if (float.IsNaN(center) || float.IsInfinity(center)) return 0;
            if (float.IsNaN(pixelsPerUnit) || pixelsPerUnit <= 0f) return 0;
            if (float.IsNaN(halfExtent) || halfExtent <= 0f) return 0;
            if (float.IsNaN(minorStep) || minorStep <= 0f) return 0;

            int every = majorEvery < 1 ? 1 : majorEvery;
            bool wrapping = !float.IsNaN(wrap) && wrap > 0f;

            double step = minorStep;
            double span = (double)halfExtent / pixelsPerUnit;
            double c = center;

            long first = (long)Math.Ceiling((c - span) / step);
            long last = (long)Math.Floor((c + span) / step);

            int count = 0;
            for (long k = first; k <= last; k++)
            {
                if (count >= into.Length) break;

                double v = k * step;
                double offset = (v - c) * pixelsPerUnit;
                double value = wrapping ? PositiveMod(v, wrap) : v;
                bool major = PositiveMod(k, every) == 0;

                into[count].Offset = (float)offset;
                into[count].Major = major;
                into[count].Value = (float)value;
                count++;
            }
            return count;
        }

        /// <summary>
        /// 1 inside <c>(1 − fadeFraction) · halfExtent</c> of the centre, falling off linearly to
        /// 0 at <paramref name="halfExtent"/>, and 0 beyond it.
        /// </summary>
        public static float Fade(float offset, float halfExtent, float fadeFraction)
        {
            if (float.IsNaN(offset) || float.IsNaN(halfExtent) || float.IsNaN(fadeFraction)) return 0f;
            if (halfExtent <= 0f) return 0f;

            float frac = fadeFraction < 0f ? 0f : fadeFraction > 1f ? 1f : fadeFraction;
            float abs = Math.Abs(offset);
            float inner = halfExtent * (1f - frac);

            if (abs <= inner) return 1f;
            if (abs >= halfExtent) return 0f;

            float denom = halfExtent - inner;
            if (denom <= 0f) return 0f;
            return (halfExtent - abs) / denom;
        }

        private static double PositiveMod(double v, double m)
        {
            double r = v % m;
            return r < 0 ? r + m : r;
        }

        private static long PositiveMod(long v, long m)
        {
            long r = v % m;
            return r < 0 ? r + m : r;
        }
    }

    /// <summary>Allocation-free helpers for segmented bar-style HUD readouts (fuel gauges, meters).</summary>
    public static class AvSegments
    {
        /// <summary>
        /// Fills <paramref name="into"/>[0 .. cells) with each cell's fill amount for
        /// <paramref name="fraction"/> of a bar split into <paramref name="cells"/> equal cells:
        /// <c>into[i] = clamp01(clamp01(fraction) · cells − i)</c>. A NaN fraction fills every
        /// cell with 0. Returns the number of cells written, capped at <c>into.Length</c>; a
        /// null buffer or a non-positive <paramref name="cells"/> writes nothing and returns 0.
        /// </summary>
        public static int Fill(float fraction, float[] into, int cells)
        {
            if (into == null) return 0;
            int n = cells < into.Length ? cells : into.Length;
            if (n <= 0) return 0;

            bool nan = float.IsNaN(fraction);
            float f = nan ? 0f : Clamp01(fraction);

            for (int i = 0; i < n; i++)
                into[i] = nan ? 0f : Clamp01(f * cells - i);
            return n;
        }

        /// <summary>The width of one cell in a <paramref name="total"/>-wide bar of <paramref name="cells"/> cells separated by <paramref name="gap"/>.</summary>
        public static float CellWidth(float total, int cells, float gap)
        {
            if (cells <= 0) return 0f;
            float w = (total - gap * (cells - 1)) / cells;
            return w < 0f ? 0f : w;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
