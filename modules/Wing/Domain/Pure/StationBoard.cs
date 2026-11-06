using System;
using System.Globalization;

namespace BoscaliSummer.Modules.Wing.Domain.Pure
{
    /// <summary>FORMATION's Station Board words (spec 2026-10-04 §4.2) from what a snapshot carries.</summary>
    internal static class StationBoard
    {
        /// <summary>Slot error as words: "4 m", "310 m", "1.2 km" past a kilometre.</summary>
        public static string ErrorText(float metres) => metres >= 1000f
            ? WmcText.Km(metres)
            : ((int)Math.Round(metres)).ToString(CultureInfo.InvariantCulture) + " m";
    }
}
