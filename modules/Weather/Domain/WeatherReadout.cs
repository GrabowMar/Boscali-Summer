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

        /// <summary>
        /// Feet in a metre. One constant so the feet reading, the metres reading and the
        /// tests can never disagree about the conversion.
        /// </summary>
        public const float FeetPerMetre = 3.28084f;

        /// <summary>Above this a trend is rising, below its negative it is falling.</summary>
        public const float TrendDeadband = 0.02f;

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

        /// <summary>
        /// Cloud base in feet, the unit the cockpit altimeter is already in. A reading below
        /// ground is not a reading; it is a dash.
        /// </summary>
        public static string Feet(float meters)
        {
            if (float.IsNaN(meters) || float.IsInfinity(meters) || meters < 0f) return Unknown;
            float feet = meters * FeetPerMetre;
            if (float.IsInfinity(feet)) return Unknown;
            return feet.ToString("0", CultureInfo.InvariantCulture) + " FT";
        }

        /// <summary>Height above ground in metres, spelled so it cannot be read as an altitude.</summary>
        public static string MetersAgL(float meters)
        {
            if (float.IsNaN(meters) || float.IsInfinity(meters) || meters < 0f) return Unknown;
            return meters.ToString("0", CultureInfo.InvariantCulture) + " M AGL";
        }

        /// <summary>Visibility in kilometres: one decimal below ten, whole kilometres above.</summary>
        public static string Kilometres(float meters)
        {
            if (float.IsNaN(meters) || float.IsInfinity(meters) || meters < 0f) return Unknown;
            float kilometers = meters / 1000f;
            return kilometers.ToString(kilometers < 10f ? "0.0" : "0", CultureInfo.InvariantCulture) + " KM";
        }

        /// <summary>Air temperature or dewpoint, degrees Celsius.</summary>
        public static string Celsius(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees)) return Unknown;
            return degrees.ToString("0", CultureInfo.InvariantCulture) + "°C";
        }

        /// <summary>A wind speed on its own, for a gust that has no heading of its own.</summary>
        public static string Speed(float metersPerSecond)
        {
            if (float.IsNaN(metersPerSecond) || float.IsInfinity(metersPerSecond) || metersPerSecond < 0f)
                return Unknown;
            return metersPerSecond.ToString("0.0", CultureInfo.InvariantCulture) + " M/S";
        }

        /// <summary>A change with its direction spelled out: "+5.7", "-2.3", "0.0".</summary>
        public static string SignedDecimal(float value, int places)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return Unknown;
            if (places < 0) places = 0;
            if (places > 3) places = 3;
            float rounded = (float)Math.Round(value, places);
            string magnitude = Math.Abs(rounded).ToString("F" + places, CultureInfo.InvariantCulture);
            if (rounded > 0f) return "+" + magnitude;
            if (rounded < 0f) return "-" + magnitude;
            return magnitude;
        }

        /// <summary>A 0..1 change as a signed percentage: "+18%", "-24%", "0%".</summary>
        public static string SignedPercent(float delta)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta)) return Unknown;
            int percent = (int)Math.Round(delta * 100.0);
            if (percent > 0) return "+" + percent.ToString(CultureInfo.InvariantCulture) + "%";
            if (percent < 0) return percent.ToString(CultureInfo.InvariantCulture) + "%";
            return "0%";
        }

        /// <summary>
        /// The arrow for a signed change, with its own dead band so a level reading renders as
        /// a level mark instead of a direction it does not have.
        /// </summary>
        public static string TrendMark(float delta, float deadband)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta)) return Unknown;
            if (deadband < 0f) deadband = 0f;
            if (delta > deadband) return "▲";
            if (delta < -deadband) return "▼";
            return "=";
        }

        /// <summary>
        /// Shortest-arc change from one heading to another, as "+22°" or "-15°". This is the
        /// veer a frontal passage delivers, so it has to cross north the short way.
        /// </summary>
        public static string Veer(float fromHeading, float toHeading)
        {
            if (float.IsNaN(fromHeading) || float.IsInfinity(fromHeading) ||
                float.IsNaN(toHeading) || float.IsInfinity(toHeading))
                return Unknown;

            float delta = WeatherState.WrapHeading(toHeading - fromHeading);
            if (delta > 180f) delta -= 360f;
            int rounded = (int)Math.Round(delta);
            if (rounded > 0) return "+" + rounded.ToString(CultureInfo.InvariantCulture) + "°";
            if (rounded < 0) return rounded.ToString(CultureInfo.InvariantCulture) + "°";
            return "0°";
        }

        /// <summary>Shear as a word, so a bar or a colour is never the only carrier of "strong".</summary>
        public static string ShearLabel(float shear)
        {
            if (float.IsNaN(shear) || float.IsInfinity(shear)) return Unknown;
            float clamped = WeatherRegimes.Clamp01(shear);
            if (clamped < 0.25f) return "LIGHT";
            if (clamped < 0.55f) return "MODERATE";
            return "STRONG";
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

        /// <summary>The same severity ladder, worn by a forecast chip instead of a rail.</summary>
        public static string ChipClass(WeatherRegime regime)
        {
            switch (Severity(regime))
            {
                case 3: return "chip danger";
                case 2: return "chip warn";
                default: return "chip live";
            }
        }

        /// <summary>
        /// The flight-category rung, 0 best (VFR) to 3 worst (LIFR). The panel paints, bars and
        /// labels all read this one number, so a category can never mean two things on screen.
        /// </summary>
        public static int FlightSeverity(FlightCategory category) => Atmospheres.Rank(category);

        /// <summary>Rail class for a flight category, monotonic in <see cref="FlightSeverity"/>.</summary>
        public static string FlightRailClass(FlightCategory category)
        {
            switch (FlightSeverity(category))
            {
                case 0: return "rail ready";
                case 1: return "rail info";
                case 2: return "rail contested";
                default: return "rail danger";
            }
        }

        /// <summary>Chip class for a flight category, same rungs as the rail.</summary>
        public static string FlightChipClass(FlightCategory category)
        {
            switch (FlightSeverity(category))
            {
                case 0: return "chip live";
                case 1: return "chip info";
                case 2: return "chip warn";
                default: return "chip danger";
            }
        }
    }
}
