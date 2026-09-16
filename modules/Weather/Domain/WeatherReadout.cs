using System;
using System.Globalization;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// Every string the weather panel and the debug overlay print. Pure by design: the tests
    /// exercise it without Unity, and an unknown reading always renders as a dash rather than a
    /// confident zero.
    /// </summary>
    internal static class WeatherReadout
    {
        public const string Unknown = "—";

        private static readonly string[] Compass =
        {
            "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
            "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"
        };

        public static string Regime(WeatherRegime regime) => WeatherRegimes.Label(regime);

        public static string Compass16(float headingDegrees)
        {
            if (float.IsNaN(headingDegrees) || float.IsInfinity(headingDegrees)) return Unknown;
            float wrapped = WeatherState.WrapHeading(headingDegrees);
            int index = (int)Math.Floor((wrapped / 22.5f) + 0.5f) % Compass.Length;
            return Compass[index];
        }

        /// <summary>Wind as a pilot reads it: the direction it blows from, then the speed.</summary>
        public static string Wind(float speedMetersPerSecond, float headingDegrees)
        {
            if (float.IsNaN(speedMetersPerSecond) || float.IsInfinity(speedMetersPerSecond)) return Unknown;
            return Compass16(headingDegrees) + " " +
                   speedMetersPerSecond.ToString("0.0", CultureInfo.InvariantCulture) + " M/S";
        }

        public static string Percent01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return Unknown;
            return (WeatherRegimes.Clamp01(value) * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        public static string Meters(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return Unknown;
            return value.ToString("0", CultureInfo.InvariantCulture) + " M";
        }

        public static string Decimal(float value, int places)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return Unknown;
            return value.ToString("F" + places, CultureInfo.InvariantCulture);
        }

        /// <summary>Mission-clock reading, mm:ss under an hour and h:mm:ss past it.</summary>
        public static string Clock(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return Unknown;
            if (seconds < 0f) seconds = 0f;
            int total = (int)seconds;
            int h = total / 3600;
            int m = total / 60 % 60;
            int s = total % 60;
            return h > 0 ? h + ":" + m.ToString("00") + ":" + s.ToString("00") : m.ToString("00") + ":" + s.ToString("00");
        }

        /// <summary>Seconds from now until a forecast entry, phrased the way a briefing would.</summary>
        public static string InSeconds(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f) return Unknown;
            return "IN " + Clock(seconds);
        }

        public static string Trend(float now, float later)
        {
            if (float.IsNaN(now) || float.IsNaN(later)) return Unknown;
            float delta = later - now;
            if (delta > 0.06f) return "BUILDING";
            if (delta < -0.06f) return "EASING";
            return "STEADY";
        }

        /// <summary>0 inert, 1 normal, 2 caution, 3 danger — the panel's rail vocabulary.</summary>
        public static int Severity(WeatherRegime regime)
        {
            switch (WeatherRegimes.Index(regime))
            {
                case 0:
                case 1:
                    return 1;
                case 2:
                case 3:
                    return 2;
                default:
                    return 3;
            }
        }

        public static string RailClass(WeatherRegime regime)
        {
            switch (Severity(regime))
            {
                case 3: return "rail danger";
                case 2: return "rail contested";
                default: return "rail ready";
            }
        }
    }
}
