using System;

namespace NOAvionics
{
    /// <summary>Metric vs imperial unit choice. Ordinals match the game's PlayerSettings.UnitSystem.</summary>
    public enum AvUnits
    {
        Metric = 0,
        Imperial = 1,
    }

    /// <summary>
    /// Allocation-free number formatting into caller-owned char buffers. HUD readouts run every
    /// frame, so this never builds a string or touches LINQ; it only ever writes into
    /// <paramref name="buf"/> and returns the index after the last char it wrote.
    /// </summary>
    public static class AvNumFormat
    {
        /// <summary>
        /// Copies <paramref name="s"/> into <paramref name="buf"/> starting at
        /// <paramref name="at"/>, stopping at the buffer's end. A null <paramref name="s"/>
        /// leaves <paramref name="at"/> unchanged.
        /// </summary>
        public static int Append(char[] buf, int at, string s)
        {
            if (buf == null || s == null) return at;
            int i = 0;
            while (i < s.Length && at < buf.Length)
            {
                buf[at] = s[i];
                at++;
                i++;
            }
            return at;
        }

        /// <summary>Writes the two-character "no data" marker.</summary>
        public static int Unknown(char[] buf, int at) => Append(buf, at, "--");

        /// <summary>
        /// Writes <paramref name="value"/> with <paramref name="decimals"/> fractional digits
        /// (clamped to 0..3), rounding the magnitude away from zero. NaN and infinities print
        /// the no-data marker instead. A sign is written only when the rounded magnitude is
        /// non-zero: a negative value keeps its '-', and <paramref name="plusSign"/> adds a '+'
        /// to a positive one. Never writes past <paramref name="buf"/>'s end.
        /// </summary>
        public static int Write(char[] buf, int at, double value, int decimals, bool plusSign = false)
        {
            if (buf == null) return at;
            if (double.IsNaN(value) || double.IsInfinity(value)) return Unknown(buf, at);

            int d = decimals < 0 ? 0 : decimals > 3 ? 3 : decimals;

            double scale = 1.0;
            for (int i = 0; i < d; i++) scale *= 10.0;

            double scaled = Math.Round(Math.Abs(value) * scale, MidpointRounding.AwayFromZero);
            if (scaled > 9e14) scaled = 9e14;
            long total = (long)scaled;
            bool nonZero = total != 0;

            if (nonZero && value < 0.0) at = Put(buf, at, '-');
            else if (nonZero && plusSign) at = Put(buf, at, '+');

            long divisor = 1;
            for (int i = 0; i < d; i++) divisor *= 10;
            long whole = total / divisor;
            long frac = total % divisor;

            at = Digits(buf, at, whole, 1);
            if (d > 0)
            {
                at = Put(buf, at, '.');
                at = Digits(buf, at, frac, d);
            }
            return at;
        }

        /// <summary>Writes one char, clamping the returned index to <paramref name="buf"/>'s length rather than ever exceeding it.</summary>
        private static int Put(char[] buf, int at, char c)
        {
            if (at < buf.Length)
            {
                buf[at] = c;
                return at + 1;
            }
            return buf.Length;
        }

        /// <summary>
        /// Writes the non-negative <paramref name="v"/> as decimal digits, zero-padded to at
        /// least <paramref name="minDigits"/>, most significant digit first. No intermediate
        /// string or array: the digit count is measured first, then each digit is pulled out
        /// with a shrinking power-of-ten divisor.
        /// </summary>
        private static int Digits(char[] buf, int at, long v, int minDigits)
        {
            int count = 1;
            long t = v;
            while (t >= 10) { t /= 10; count++; }
            if (count < minDigits) count = minDigits;

            long divisor = 1;
            for (int i = 1; i < count; i++) divisor *= 10;

            for (int i = 0; i < count; i++)
            {
                long digit = (v / divisor) % 10;
                at = Put(buf, at, (char)('0' + digit));
                divisor /= 10;
            }
            return at;
        }
    }

    /// <summary>
    /// Unit conversion and ready-made HUD readings, mirroring the game's own
    /// <c>UnitConverter</c> factors and breakpoints exactly.
    /// </summary>
    public static class AvUnitTable
    {
        public static float Speed(float mps, AvUnits u) => u == AvUnits.Imperial ? mps * 1.94384f : mps * 3.6f;
        public static string SpeedUnit(AvUnits u) => u == AvUnits.Imperial ? "kt" : "km/h";

        public static float Altitude(float m, AvUnits u) => u == AvUnits.Imperial ? m * 3.28084f : m;
        public static string AltitudeUnit(AvUnits u) => u == AvUnits.Imperial ? "ft" : "m";

        public static float Climb(float mps, AvUnits u) => u == AvUnits.Imperial ? mps * 60f * 3.28084f : mps;
        public static string ClimbUnit(AvUnits u) => u == AvUnits.Imperial ? "fpm" : "m/s";

        public static int SpeedReading(char[] b, int at, float mps, AvUnits u)
        {
            at = AvNumFormat.Write(b, at, Speed(mps, u), 0);
            return AvNumFormat.Append(b, at, SpeedUnit(u));
        }

        public static int AltitudeReading(char[] b, int at, float m, AvUnits u)
        {
            int decimals = u == AvUnits.Metric && Math.Abs(m) < 10f ? 1 : 0;
            at = AvNumFormat.Write(b, at, Altitude(m, u), decimals);
            return AvNumFormat.Append(b, at, AltitudeUnit(u));
        }

        public static int DistanceReading(char[] b, int at, float metres, AvUnits u)
        {
            double value;
            int decimals;
            string unit;

            if (u == AvUnits.Metric)
            {
                if (Math.Abs(metres) > 10000f) { value = metres * 0.001; decimals = 0; unit = "km"; }
                else if (Math.Abs(metres) > 1000f) { value = metres * 0.001; decimals = 1; unit = "km"; }
                else { value = metres; decimals = 0; unit = "m"; }
            }
            else
            {
                float yd = metres * 1.09361f;
                if (Math.Abs(yd) < 1000f) { value = yd; decimals = 0; unit = "yd"; }
                else { value = metres * 0.000539957; decimals = 1; unit = "nm"; }
            }

            at = AvNumFormat.Write(b, at, value, decimals);
            return AvNumFormat.Append(b, at, unit);
        }

        public static int ClimbRateReading(char[] b, int at, float mps, AvUnits u)
        {
            bool plus = mps > 0.5f;
            int decimals = u == AvUnits.Metric && Math.Abs(mps) < 10f ? 1 : 0;
            at = AvNumFormat.Write(b, at, Climb(mps, u), decimals, plus);
            return AvNumFormat.Append(b, at, ClimbUnit(u));
        }
    }
}
