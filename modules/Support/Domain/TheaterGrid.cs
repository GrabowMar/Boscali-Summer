using System;
using System.Globalization;

namespace BoscaliSummer.Modules.Support.Domain
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
    }
}
