using BoscaliSummer.Core.Contracts;
using NOAvionics;

namespace BoscaliSummer.Modules.QoL.Domain
{
    /// <summary>
    /// The fuel/divert widget's copy and tone, pure so the test project links it directly. It
    /// states the fuel state and where the nearest friendly field is; it never invents a range
    /// or an endurance figure the game does not expose.
    /// </summary>
    internal static class FuelHudCopy
    {
        /// <summary>At or below this the fuel line cautions.</summary>
        public const float CautionLevel = 0.25f;

        /// <summary>At or below this the fuel line warns.</summary>
        public const float WarningLevel = 0.10f;

        public static string Fuel(float level) => "FUEL " + AvNum.PercentWhole(level);

        /// <summary>"RTB ALPHA · 34km", or the honest unknown when no field resolved.</summary>
        public static string Divert(string fieldName, string distanceText)
        {
            if (string.IsNullOrEmpty(fieldName)) return "RTB UNKNOWN";
            return string.IsNullOrEmpty(distanceText) ? "RTB " + fieldName : "RTB " + fieldName + " · " + distanceText;
        }

        public static HudTone Tone(float level)
        {
            if (!Finite(level)) return HudTone.Info;
            if (level <= WarningLevel) return HudTone.Warning;
            return level <= CautionLevel ? HudTone.Caution : HudTone.Info;
        }

        public static float Bar(float level)
        {
            if (!Finite(level)) return 0f;
            return level < 0f ? 0f : level > 1f ? 1f : level;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
