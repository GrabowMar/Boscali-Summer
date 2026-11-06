using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Domain.Fronts
{
    /// <summary>Pure placement helpers for the organic fronts (world metres). The host decides legality with these.</summary>
    internal static class FrontPlacement
    {
        internal readonly struct Base
        {
            public readonly int Id;
            public readonly float X, Z;
            public readonly bool Ours, Carrier, Land;

            public Base(int id, float x, float z, bool ours, bool carrier, bool land) { Id = id; X = x; Z = z; Ours = ours; Carrier = carrier; Land = land; }
        }

        private static float Dist(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Index of the nearest base we hold within <paramref name="within"/> metres (a carrier only when allowed), or -1.</summary>
        public static int NearestHeld(IReadOnlyList<Base> bases, float x, float z, float within, bool allowCarrier)
        {
            int best = -1; float bestD = within;
            for (int i = 0; i < bases.Count; i++)
            {
                Base b = bases[i];
                if (!b.Ours || (b.Carrier && !allowCarrier)) continue;
                float d = Dist(x, z, b.X, b.Z);
                if (d <= bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>The nearest land base is ours (false when there is no land base).</summary>
        public static bool OwnGround(IReadOnlyList<Base> bases, float x, float z)
        {
            bool ours = false; float bestD = float.PositiveInfinity;
            for (int i = 0; i < bases.Count; i++)
            {
                if (!bases[i].Land) continue;
                float d = Dist(x, z, bases[i].X, bases[i].Z);
                if (d < bestD) { bestD = d; ours = bases[i].Ours; }
            }
            return ours;
        }

        /// <summary>Ring radii (metres) a picked camp widens to when its point does not fit (ruling R13), and the bearings per ring.</summary>
        public static readonly float[] CampRings = { 400f, 800f, 1500f };
        public const int CampBearings = 12;

        /// <summary>
        /// Where a picked camp may stand, nearest first: the point itself, then <see cref="CampRings"/> round it at <see cref="CampBearings"/> spread bearings, own ground only
        /// (<see cref="OwnGround"/>). At most 1 + 3 x 12 = 37 spots. <paramref name="into"/> is cleared first.
        /// </summary>
        public static void CampSpots(IReadOnlyList<Base> bases, float x, float z, List<(float X, float Z)> into) =>
            Spots(x, z, into, (cx, cz) => OwnGround(bases, cx, cz));

        /// <summary>
        /// Where a picked EW truck (DEPLOY or RELOCATE) may stand, nearest first: the point, then the camp rings round it (ruling R38), keeping only spots within
        /// <paramref name="reach"/> of one of <paramref name="centers"/> (the 20 km data center rule). At most 37 spots. <paramref name="into"/> is cleared first.
        /// </summary>
        public static void TruckSpots(IReadOnlyList<(float X, float Z)> centers, float x, float z, float reach, List<(float X, float Z)> into) =>
            Spots(x, z, into, (cx, cz) => WithinReach(centers, cx, cz, reach));

        private static void Spots(float x, float z, List<(float X, float Z)> into, Func<float, float, bool> keep)
        {
            into.Clear();
            if (keep(x, z)) into.Add((x, z));
            foreach (float r in CampRings)
                for (int step = 0; step < CampBearings; step++)
                {
                    double angle = step * 5 % CampBearings * 2.0 * Math.PI / CampBearings; // 5 is coprime with 12: neighbours in the list sit far apart on the ring
                    float cx = x + (float)(Math.Sin(angle) * r), cz = z + (float)(Math.Cos(angle) * r);
                    if (keep(cx, cz)) into.Add((cx, cz));
                }
        }

        public static bool WithinReach(IReadOnlyList<(float X, float Z)> centers, float x, float z, float reach)
        {
            for (int i = 0; i < centers.Count; i++) if (Dist(x, z, centers[i].X, centers[i].Z) <= reach) return true;
            return false;
        }
    }
}
