using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    internal enum FlightCategory : byte
    {
        Vfr = 0,
        Mvfr = 1,
        Ifr = 2,
        Lifr = 3,
    }

    /// <summary>
    /// Everything the weather says in words. Status always reads in words beside any colour.
    /// <see cref="WindFrom"/> is the one place a downwind heading becomes the bearing a pilot
    /// reads (where the wind comes <i>from</i>); every display goes through it.
    /// </summary>
    internal static class WeatherWords
    {
        private static readonly string[] Cardinals =
        {
            "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
            "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW",
        };

        public static float WindFrom(float downwindHeading) => WeatherMath.WrapHeading(downwindHeading + 180f);

        public static string Cardinal(float bearing)
        {
            int index = (int)Math.Round(WeatherMath.WrapHeading(bearing) / 22.5f) % 16;
            return Cardinals[index];
        }

        public static string Turbulence(float turbulence)
        {
            if (turbulence < 0.15f) return "SMOOTH";
            if (turbulence < 0.35f) return "LIGHT TURB";
            if (turbulence < 0.6f) return "MOD TURB";
            return "SEVERE TURB";
        }

        public static FlightCategory Category(float visibilityKm, float ceilingMetres)
        {
            float ceilingFeet = ceilingMetres * 3.28084f;
            if (visibilityKm < 1.6f || ceilingFeet < 500f) return FlightCategory.Lifr;
            if (visibilityKm < 4.8f || ceilingFeet < 1000f) return FlightCategory.Ifr;
            if (visibilityKm < 8f || ceilingFeet < 3000f) return FlightCategory.Mvfr;
            return FlightCategory.Vfr;
        }

        public static string CategoryName(FlightCategory category)
        {
            switch (category)
            {
                case FlightCategory.Mvfr: return "MVFR";
                case FlightCategory.Ifr: return "IFR";
                case FlightCategory.Lifr: return "LIFR";
                default: return "VFR";
            }
        }

    }
}
