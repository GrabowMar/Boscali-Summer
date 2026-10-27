using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.TheaterOps.Domain
{
    /// <summary>
    /// An offensive a pilot draws on the map (HOI4's battle plan): press on the front, release on the objective. The live front nearest the
    /// press carries it; the objective decides its kind and, once it is our ground, its success.
    /// </summary>
    internal static class DrawnPlan
    {
        /// <summary>Shortest and longest arrow, how far from a front the press may land, and the hold that makes the objective already ours.</summary>
        public const float MinimumMetres = TheaterDrawLimits.MinimumMetres, MaximumMetres = TheaterDrawLimits.MaximumMetres,
            SnapMetres = TheaterDrawLimits.SnapMetres, OwnHold = 0.25f;
        /// <summary>No theatre is larger: anything beyond is a corrupt or hostile packet.</summary>
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
