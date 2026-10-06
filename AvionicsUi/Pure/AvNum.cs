using System;
using System.Globalization;

namespace NOAvionics
{
    /// <summary>
    /// The only way a console turns a number into text. Built on <see cref="AvNumFormat"/>, which
    /// writes digits by hand, so the host's culture can never swap '.' for ',' (the pl-PL
    /// "$6,08b" / "0,5 s" bug). Strings are allocated; hot readouts should use AvNumText.
    /// </summary>
    public static class AvNum
    {
        [ThreadStatic] private static char[] buf;
        private static char[] Buf => buf ?? (buf = new char[48]);

        public static string Fixed(double v, int decimals)
        {
            char[] b = Buf;
            int n = AvNumFormat.Write(b, 0, v, decimals);
            return new string(b, 0, n);
        }

        public static string Thousands(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "--";
            long whole = (long)Math.Round(Math.Abs(v), MidpointRounding.AwayFromZero);
            string digits = Fixed(whole, 0);
            var sb = new System.Text.StringBuilder(digits.Length + digits.Length / 3 + 1);
            if (whole != 0 && v < 0) sb.Append('-');
            for (int i = 0; i < digits.Length; i++)
            {
                if (i > 0 && (digits.Length - i) % 3 == 0) sb.Append(',');
                sb.Append(digits[i]);
            }
            return sb.ToString();
        }

        public static string Compact(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "--";
            double a = Math.Abs(v);
            string sign = v < 0 && a >= 0.5 ? "-" : "";
            if (a < 10000) return a < 1000 ? Fixed(v, 0) : Thousands(v);
            if (a < 1e6) return sign + Fixed(a / 1e3, a % 1000 == 0 ? 0 : 1) + "K";
            if (a < 1e9) return sign + Fixed(a / 1e6, 2) + "M";
            return sign + Fixed(a / 1e9, 2) + "B";
        }

        public static string Money(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "--";
            string body = Compact(Math.Abs(v));
            if (Math.Abs(v) >= 1e3 && Math.Abs(v) < 1e4) body = Fixed(Math.Abs(v) / 1e3, 1) + "K";
            return (v < 0 ? "-$" : "$") + body;
        }

        public static string Percent(double fraction, int decimals = 0) =>
            double.IsNaN(fraction) ? "--%" : Fixed(fraction * 100.0, decimals) + "%";

        public static string Signed(double v, int decimals)
        {
            char[] b = Buf;
            int n = AvNumFormat.Write(b, 0, v, decimals, true);
            return new string(b, 0, n);
        }

        public static string Clock(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) return "--:--";
            long s = (long)Math.Floor(seconds);
            long h = s / 3600, m = (s / 60) % 60, sec = s % 60;
            return h > 0
                ? h + ":" + Two(m) + ":" + Two(sec)
                : m + ":" + Two(sec);
        }

        /// <summary>
        /// A ratio clamped to 0..1 as whole percent, halves rounded away from zero; a non-finite ratio reads as
        /// an em dash (the mods' unknown mark, unlike <see cref="Percent"/>'s "--%").
        /// </summary>
        public static string PercentWhole(float ratio)
        {
            if (float.IsNaN(ratio) || float.IsInfinity(ratio)) return "\u2014";
            float clamped = ratio < 0f ? 0f : ratio > 1f ? 1f : ratio;
            return ((int)Math.Round(clamped * 100f, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>
        /// "T-45s" under a minute, "T-5:00" from a minute up, rounded up; empty when no clock is running (negative
        /// or non-finite). Unlike <see cref="Clock"/> it counts down, so it rounds up.
        /// </summary>
        public static string TMinus(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f) return "";
            int total = (int)Math.Ceiling(seconds);
            if (total < 60) return "T-" + total.ToString(CultureInfo.InvariantCulture) + "s";
            return "T-" + (total / 60).ToString(CultureInfo.InvariantCulture) + ":" +
                (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        public static string Seconds(double s, int decimals = 1) => Fixed(s, decimals) + " s";

        private static string Two(long v) => v < 10 ? "0" + v : v.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
