using System;
using System.Globalization;
using BoscaliSummer.Features.Weather.Domain;

namespace BoscaliSummer.Features.Weather.Presentation
{
    /// <summary>
    /// Every string the weather panel and the debug overlay print. Pure by design: an unknown
    /// reading always renders as a dash rather than a confident zero.
    /// </summary>
    internal static class WeatherReadout
    {
        public const string Unknown = "—";

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

        /// <summary>
        /// Wind as a pilot reads it: the direction it blows FROM, then the speed. Every heading
        /// this module stores points the way the air travels, so the flip happens here, once.
        /// </summary>
        public static string Wind(float speedMetersPerSecond, float headingDegrees)
        {
            if (float.IsNaN(speedMetersPerSecond) || float.IsInfinity(speedMetersPerSecond)) return Unknown;
            return Compass16(WindFrom(headingDegrees)) + " " +
                   speedMetersPerSecond.ToString("0.0", CultureInfo.InvariantCulture) + " M/S";
        }

        /// <summary>The compass point a wind blows from, given the direction it blows toward.</summary>
        public static float WindFrom(float headingDegrees) => WeatherState.WrapHeading(headingDegrees + 180f);

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

        public static string Feet(float meters)
        {
            if (float.IsNaN(meters) || float.IsInfinity(meters) || meters < 0f) return Unknown;
            float feet = meters * FeetPerMetre;
            if (float.IsInfinity(feet)) return Unknown;
            return feet.ToString("0", CultureInfo.InvariantCulture) + " FT";
        }

        public static string MetersAgL(float meters)
        {
            if (float.IsNaN(meters) || float.IsInfinity(meters) || meters < 0f) return Unknown;
            return meters.ToString("0", CultureInfo.InvariantCulture) + " M AGL";
        }

        public static string Kilometres(float meters)
        {
            if (float.IsNaN(meters) || float.IsInfinity(meters) || meters < 0f) return Unknown;
            float kilometers = meters / 1000f;
            return kilometers.ToString(kilometers < 10f ? "0.0" : "0", CultureInfo.InvariantCulture) + " KM";
        }

        public static string Celsius(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees)) return Unknown;
            return degrees.ToString("0", CultureInfo.InvariantCulture) + "°C";
        }

        public static string Speed(float metersPerSecond)
        {
            if (float.IsNaN(metersPerSecond) || float.IsInfinity(metersPerSecond) || metersPerSecond < 0f)
                return Unknown;
            return metersPerSecond.ToString("0.0", CultureInfo.InvariantCulture) + " M/S";
        }

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

        public static string SignedPercent(float delta)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta)) return Unknown;
            int percent = (int)Math.Round(delta * 100.0);
            if (percent > 0) return "+" + percent.ToString(CultureInfo.InvariantCulture) + "%";
            if (percent < 0) return percent.ToString(CultureInfo.InvariantCulture) + "%";
            return "0%";
        }

        public static string TrendMark(float delta, float deadband)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta)) return Unknown;
            if (deadband < 0f) deadband = 0f;
            if (delta > deadband) return "▲";
            if (delta < -deadband) return "▼";
            return "=";
        }

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

        public static string ShearLabel(float shear)
        {
            if (float.IsNaN(shear) || float.IsInfinity(shear)) return Unknown;
            float clamped = WeatherRegimes.Clamp01(shear);
            if (clamped < 0.25f) return "LIGHT";
            if (clamped < 0.55f) return "MODERATE";
            return "STRONG";
        }

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

        public static string ChipClass(WeatherRegime regime)
        {
            switch (Severity(regime))
            {
                case 3: return "chip danger";
                case 2: return "chip warn";
                default: return "chip live";
            }
        }

        public static int FlightSeverity(FlightCategory category) => Atmospheres.Rank(category);

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

    /// <summary>
    /// The storm half of the readout vocabulary: kind names, warning tiers and the ring
    /// distances the panel and the HUD print.
    /// </summary>
    internal static class StormReadout
    {
        private static readonly string[] KindLabels = { "CUMULUS", "TOWERING CUMULUS", "SUPERCELL" };
        private static readonly string[] WarningLabels = { "CLEAR", "ADVISORY", "WATCH", "WARNING" };
        private static readonly string[] WarningShort = { "—", "ADV", "WATCH", "WARN" };

        public const float MetresPerNauticalMile = 1852f;

        public static string Kind(StormKind kind)
        {
            int index = (int)kind;
            if (index < 0) index = 0;
            if (index >= KindLabels.Length) index = KindLabels.Length - 1;
            return KindLabels[index];
        }

        public static string Warning(StormWarning warning)
        {
            int index = (int)warning;
            if (index < 0) index = 0;
            if (index >= WarningLabels.Length) index = WarningLabels.Length - 1;
            return WarningLabels[index];
        }

        public static string WarningShortCode(StormWarning warning)
        {
            int index = (int)warning;
            if (index < 0) index = 0;
            if (index >= WarningShort.Length) index = WarningShort.Length - 1;
            return WarningShort[index];
        }

        public static string RailClass(StormWarning warning)
        {
            switch (warning)
            {
                case StormWarning.Warning: return "rail danger";
                case StormWarning.Watch: return "rail contested";
                case StormWarning.Advisory: return "rail info";
                default: return "rail inert";
            }
        }

        public static string ChipClass(StormWarning warning)
        {
            switch (warning)
            {
                case StormWarning.Warning: return "danger";
                case StormWarning.Watch: return "warn";
                case StormWarning.Advisory: return "info";
                default: return "inert";
            }
        }

        public static string NauticalMiles(float metres)
        {
            if (float.IsNaN(metres) || float.IsInfinity(metres) || metres < 0f) return WeatherReadout.Unknown;
            return (metres / MetresPerNauticalMile).ToString("0.0", CultureInfo.InvariantCulture) + " NM";
        }

        public static float BearingDegrees(float fromX, float fromZ, float toX, float toZ)
        {
            if (float.IsNaN(fromX) || float.IsNaN(fromZ) || float.IsNaN(toX) || float.IsNaN(toZ)) return 0f;
            return WeatherState.WrapHeading((float)(Math.Atan2(toX - fromX, toZ - fromZ) * 180.0 / Math.PI));
        }

        public static string BearingTo(float fromX, float fromZ, float toX, float toZ)
        {
            if (float.IsNaN(fromX) || float.IsNaN(fromZ) || float.IsNaN(toX) || float.IsNaN(toZ))
                return WeatherReadout.Unknown;
            float wrapped = BearingDegrees(fromX, fromZ, toX, toZ);
            return WeatherReadout.Compass16(wrapped) + " " +
                   Math.Round(wrapped).ToString("0", CultureInfo.InvariantCulture) + "°";
        }

        public static string HazardLine(StormWarning warning, string kind, float distanceMetres, string bearing)
        {
            if (warning == StormWarning.None) return "NO STORM IN RANGE";
            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(bearing)) return WeatherReadout.Unknown;
            return Warning(warning) + " — " + kind + " " + NauticalMiles(distanceMetres) + " " + bearing;
        }

        /// <summary>
        /// The cell the cockpit banner names: the nearest one already imposing a warning, or
        /// failing that the nearest supercell. Reads the caller's bounded buffer in one pass.
        /// </summary>
        public static bool Nearest(
            StormCell[] cells, int count, float fromX, float fromZ, out StormCell nearest, out float distance)
        {
            nearest = default;
            distance = 0f;
            if (cells == null) return false;
            if (count < 0) count = 0;
            if (count > cells.Length) count = cells.Length;

            bool foundWarned = false;
            bool foundSupercell = false;
            float warnedDistance = float.MaxValue;
            float supercellDistance = float.MaxValue;
            StormCell warned = default;
            StormCell supercell = default;

            for (int i = 0; i < count; i++)
            {
                StormCell cell = cells[i];
                float metres = cell.DistanceTo(fromX, fromZ);
                if (float.IsNaN(metres)) continue;

                if (metres < warnedDistance && cell.WarningAt(fromX, fromZ) != StormWarning.None)
                {
                    warnedDistance = metres;
                    warned = cell;
                    foundWarned = true;
                }
                if (metres < supercellDistance && cell.IsSupercell)
                {
                    supercellDistance = metres;
                    supercell = cell;
                    foundSupercell = true;
                }
            }

            if (foundWarned)
            {
                nearest = warned;
                distance = warnedDistance;
                return true;
            }
            if (foundSupercell)
            {
                nearest = supercell;
                distance = supercellDistance;
                return true;
            }
            return false;
        }
    }
}
