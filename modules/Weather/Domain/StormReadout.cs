using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// The storm half of the readout vocabulary: kind names, warning tiers and the ring
    /// distances the panel and the HUD print. Pure, so the warning ladder is testable without a
    /// single metre of rendered cloud.
    /// </summary>
    internal static class StormReadout
    {
        private static readonly string[] KindLabels = { "CUMULUS", "TOWERING CUMULUS", "SUPERCELL" };
        private static readonly string[] WarningLabels = { "CLEAR", "ADVISORY", "WATCH", "WARNING" };
        private static readonly string[] WarningShort = { "—", "ADV", "WATCH", "WARN" };

        /// <summary>Metres in one nautical mile, for range readings a pilot already thinks in.</summary>
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

        /// <summary>Radio-stack class for the tier: nothing, information, caution, danger.</summary>
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

        /// <summary>Range to a cell in nautical miles, which is how a cockpit reads distance.</summary>
        public static string NauticalMiles(float metres)
        {
            if (float.IsNaN(metres) || float.IsInfinity(metres) || metres < 0f) return WeatherReadout.Unknown;
            return (metres / MetresPerNauticalMile).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " NM";
        }

        /// <summary>Bearing from a point to a cell, as a compass point and its degrees.</summary>
        public static string BearingTo(float fromX, float fromZ, float toX, float toZ)
        {
            if (float.IsNaN(fromX) || float.IsNaN(fromZ) || float.IsNaN(toX) || float.IsNaN(toZ))
                return WeatherReadout.Unknown;
            float degrees = (float)(Math.Atan2(toX - fromX, toZ - fromZ) * 180.0 / Math.PI);
            float wrapped = WeatherState.WrapHeading(degrees);
            return WeatherReadout.Compass16(wrapped) + " " +
                   Math.Round(wrapped).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "°";
        }

        /// <summary>
        /// The one line a pilot needs: how bad, how far, which way. Unknown inputs degrade to a
        /// dash rather than a confident zero, as everywhere else in this module.
        /// </summary>
        public static string HazardLine(StormWarning warning, string kind, float distanceMetres, string bearing)
        {
            if (warning == StormWarning.None) return "NO STORM IN RANGE";
            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(bearing)) return WeatherReadout.Unknown;
            return Warning(warning) + " — " + kind + " " + NauticalMiles(distanceMetres) + " " + bearing;
        }
    }
}
