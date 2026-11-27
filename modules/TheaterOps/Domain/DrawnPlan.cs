using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.TheaterOps.Domain
{
    /// <summary>
    /// A directed offensive starts near a live front. The objective determines its kind and where to measure control.
    /// </summary>
    internal static class DrawnPlan
    {
        /// <summary>Minimum and maximum range, maximum distance from a front to the origin, and the hold required to control the objective.</summary>
        public const float MinimumMetres = TheaterDrawLimits.MinimumMetres, MaximumMetres = TheaterDrawLimits.MaximumMetres,
            SnapMetres = TheaterDrawLimits.SnapMetres, OwnHold = 0.25f;
        /// <summary>Outer bound for theatre coordinates.</summary>
        public const float WorldLimit = 500000f;

        /// <summary>Finite, inside the world, and between the shortest and the longest arrow.</summary>
        public static bool Valid(float sx, float sz, float ox, float oz)
        {
            if (!Inside(sx) || !Inside(sz) || !Inside(ox) || !Inside(oz)) return false;
            float dx = ox - sx, dz = oz - sz, d2 = dx * dx + dz * dz;
            return d2 >= MinimumMetres * MinimumMetres && d2 <= MaximumMetres * MaximumMetres;
        }

        /// <summary>Our ground at the objective is a defence (move up and hold it); anything else is an assault.</summary>
        public static TheaterOpKind KindAt(bool hasHold, float hold) =>
            hasHold && !float.IsNaN(hold) && hold >= OwnHold ? TheaterOpKind.Defend : TheaterOpKind.Assault;

        private static bool Inside(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && Math.Abs(v) <= WorldLimit;
    }
}
