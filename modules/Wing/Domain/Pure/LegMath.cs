using System;
using System.Globalization;

namespace BoscaliSummer.Modules.Wing.Domain.Pure
{
    /// <summary>Flight Plan's leg table numbers (spec 2026-10-04 §4.1 LegMath). World axes: +X east, +Z north.</summary>
    internal static class LegMath
    {
        public static float BearingDeg(float fromX, float fromZ, float toX, float toZ)
        {
            double deg = Math.Atan2(toX - fromX, toZ - fromZ) * 180.0 / Math.PI;
            return (float)((deg % 360.0 + 360.0) % 360.0);
        }

        public static float Distance(float fromX, float fromZ, float toX, float toZ)
        {
            double dx = toX - fromX, dz = toZ - fromZ;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        public static float EtaSeconds(float distanceM, float speedMps) => !(speedMps > 0.1f) ? float.PositiveInfinity : distanceM / speedMps;

        /// <summary>"m:ss"; a dash when there is no time.</summary>
        public static string Clock(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f) return "—";
            int s = (int)Math.Round(seconds);
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
