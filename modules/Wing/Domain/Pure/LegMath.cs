using System;
using System.Globalization;

namespace BoscaliSummer.Modules.Wing.Domain.Pure
{
    /// <summary>Clock words for leg and join times (spec 2026-10-04 §4.1 LegMath).</summary>
    internal static class LegMath
    {
        /// <summary>"m:ss"; a dash when there is no time.</summary>
        public static string Clock(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f) return "—";
            int s = (int)Math.Round(seconds);
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
