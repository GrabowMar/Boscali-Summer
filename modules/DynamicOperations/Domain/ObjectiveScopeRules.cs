using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.DynamicOperations.Domain
{
    /// <summary>
    /// What the OBJECTIVE page of the TAC BOARD asks of DynamicOperations (HUD v8), pure: which phases the on-station gates decide, the four gates themselves (the
    /// host publishes only its blocker text, so the gates are read back from the pilot's own height and range and from the contact, and the sightline is the
    /// one thing left when the other three hold and the host still says he is not on station), which air-defence rings are worth the plot, and a heading.
    /// </summary>
    internal static class ObjectiveScopeRules
    {
        /// <summary>The metres above the contact the host wants a pilot flying to be on station (<c>HasPlayerOnStation</c>).</summary>
        public const float StationHeightM = 50f;
        /// <summary>Rings kept for the plot and how far past its edge a ring still matters.</summary>
        public const int MaxRings = 8;
        public const float RingReachM = 15000f;

        /// <summary>How long a contract's clock runs from its acceptance (<c>Operation.Accept</c>): ten minutes for an intercept, twenty for the rest; the ring's whole.</summary>
        public static float DeadlineSeconds(string title) => title == OperationTitles.Title(OperationKind.Intercept) ? 600f : 1200f;

        /// <summary>The phases the on-station gates decide: join the area, cover or hold it, survey or observe it.</summary>
        public static bool GatedPhase(string title) =>
            title == "JOIN" || title == "COVER" || title == "HOLD" || title == "SURVEY" || title == "OBSERVE";

        /// <summary>The contact is one the host can still point at: fixed, or known fresh (a last-known or lost contact fails tracking).</summary>
        public static bool Tracked(ObjectiveContact contact) => contact == ObjectiveContact.Fixed || contact == ObjectiveContact.Known;

        /// <summary>
        /// The four hold gates. Range: inside the zone. Altitude: 50 m above the ground (the host measures it above the contact; over level ground the same).
        /// Track: the contact is fixed or fresh. Sightline: read back, true unless range, altitude and track all hold and the host still blocks the objective.
        /// </summary>
        public static void Gates(float distanceM, float radiusM, float agl, ObjectiveContact contact, string blocker, out bool range, out bool alt, out bool los, out bool track)
        {
            range = distanceM <= radiusM;
            alt = agl >= StationHeightM;
            track = Tracked(contact);
            los = !(range && alt && track && !string.IsNullOrEmpty(blocker));
        }

        /// <summary>True heading in degrees (0 = north, +z) from a forward vector's x and z.</summary>
        public static float Heading(float forwardX, float forwardZ) => (float)((Math.Atan2(forwardX, forwardZ) * 180.0 / Math.PI % 360.0 + 360.0) % 360.0);

        /// <summary>
        /// The rings worth drawing: those that draw at all (not a sensor, with a range), whose edge is within <see cref="RingReachM"/> of the zone or of the pilot,
        /// the <see cref="MaxRings"/> nearest to either, written to <paramref name="into"/>. Returns how many.
        /// </summary>
        public static int PickRings(AirDefenceRing[] all, int count, float zoneX, float zoneZ, float ownX, float ownZ, AirDefenceRing[] into, float[] rank)
        {
            int n = 0;
            for (int i = 0; i < count && i < all.Length; i++)
            {
                AirDefenceRing ring = all[i];
                if (ring.Kind == AirDefenceKind.Sensor || !(ring.MaxRange > 0f) || float.IsNaN(ring.X) || float.IsNaN(ring.Z)) continue;
                float d = Math.Min(Scalar.Distance2D(ring.X, ring.Z, zoneX, zoneZ), Scalar.Distance2D(ring.X, ring.Z, ownX, ownZ));
                if (d - ring.MaxRange > RingReachM) continue;
                int at = n < into.Length ? n++ : -1;
                if (at < 0)
                {
                    int worst = 0;
                    for (int k = 1; k < into.Length; k++) if (rank[k] > rank[worst]) worst = k;
                    if (d >= rank[worst]) continue;
                    at = worst;
                }
                into[at] = ring;
                rank[at] = d;
            }
            return n;
        }
    }
}
