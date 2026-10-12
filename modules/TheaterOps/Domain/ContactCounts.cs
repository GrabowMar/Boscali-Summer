using System.Collections.Generic;

namespace BoscaliSummer.Modules.TheaterOps.Domain
{
    internal enum ContactKind : byte { FriendlyGround = 0, FriendlyCarrier = 1, FriendlyOther = 2, HostileSurface = 3, HostileAir = 4 }

    internal readonly struct ContactFix
    {
        internal readonly float X, Z; internal readonly ContactKind Kind;
        internal ContactFix(float x, float z, ContactKind kind) { X = x; Z = z; Kind = kind; }
    }

    internal struct RingCounts { internal int NearFriendly, NearHostile, HostileAirNear, ReachFriendlyGround, ReachFriendlyOther, ReachHostile, Carriers; }

    /// <summary>What stands around a front, counted once in a 3 km presence ring and once in the reach ring (annex 01 E3).</summary>
    internal static class ContactCounts
    {
        internal const float NearRadius = 3000f;

        /// <summary>Counts the fixes around (x, z) in both rings, edges inclusive. Non-finite fixes are skipped. reachRadius is the caller's (LivingWarRules.ReachRadius); a bad one leaves the reach counts at zero. Tracked hostile aircraft count in HostileAirNear only, never in the ground ratio.</summary>
        internal static RingCounts Count(IReadOnlyList<ContactFix> fixes, int count, float x, float z, float reachRadius)
        {
            var rings = new RingCounts();
            if (fixes == null || !IsFinite(x) || !IsFinite(z)) return rings;
            if (count > fixes.Count) count = fixes.Count;
            double near = (double)NearRadius * NearRadius;
            double reach = IsFinite(reachRadius) && reachRadius > 0f ? (double)reachRadius * reachRadius : -1d;
            for (int i = 0; i < count; i++)
            {
                ContactFix f = fixes[i];
                if (!IsFinite(f.X) || !IsFinite(f.Z)) continue;
                double dx = (double)f.X - x, dz = (double)f.Z - z, squared = dx * dx + dz * dz;
                bool inNear = squared <= near, inReach = reach >= 0d && squared <= reach;
                switch (f.Kind)
                {
                    case ContactKind.FriendlyGround:
                    case ContactKind.FriendlyCarrier:
                        if (inNear) rings.NearFriendly++;
                        if (inReach)
                        {
                            rings.ReachFriendlyGround++;
                            if (f.Kind == ContactKind.FriendlyCarrier) rings.Carriers++;
                        }
                        break;
                    case ContactKind.FriendlyOther:
                        if (inNear) rings.NearFriendly++;
                        if (inReach) rings.ReachFriendlyOther++;
                        break;
                    case ContactKind.HostileSurface:
                        if (inNear) rings.NearHostile++;
                        if (inReach) rings.ReachHostile++;
                        break;
                    case ContactKind.HostileAir:
                        if (inNear) rings.HostileAirNear++;
                        break;
                }
            }
            return rings;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
