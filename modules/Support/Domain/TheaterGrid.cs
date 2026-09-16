using System;
using System.Globalization;

namespace BoscaliSummer.Features.Support.Domain
{
    /// <summary>Operator-facing formatting for theatre coordinates, ranges and clocks.</summary>
    internal static class TheaterGrid
    {
        /// <summary>Ground position in kilometres east / north of the map centre, e.g. "12.4 / -3.1 KM".</summary>
        public static string Kilometres(double x, double z) => Km(x) + " / " + Km(z) + " KM";

        public static string Km(double metres)
        {
            if (double.IsNaN(metres) || double.IsInfinity(metres)) return "—";
            double km = metres / 1000.0;
            return Math.Abs(km) >= 100.0
                ? Math.Round(km).ToString("0", CultureInfo.InvariantCulture)
                : km.ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>Mission-control clock: "MM:SS" under an hour, "H:MM:SS" beyond.</summary>
        public static string Clock(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0) return "--:--";
            long total = (long)Math.Floor(seconds);
            long hours = total / 3600;
            long minutes = total / 60 % 60;
            long secs = total % 60;
            return hours > 0
                ? hours.ToString(CultureInfo.InvariantCulture) + ":" + minutes.ToString("00", CultureInfo.InvariantCulture) +
                  ":" + secs.ToString("00", CultureInfo.InvariantCulture)
                : minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + secs.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>Ground elapsed time in the Apollo style, "HHH:MM:SS".</summary>
        public static string Elapsed(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0) return "---:--:--";
            long total = (long)Math.Floor(seconds);
            return (total / 3600).ToString("000", CultureInfo.InvariantCulture) + ":" +
                   (total / 60 % 60).ToString("00", CultureInfo.InvariantCulture) + ":" +
                   (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
