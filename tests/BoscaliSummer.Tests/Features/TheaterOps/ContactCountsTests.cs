using System.Collections.Generic;
using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>The two rings around a front: 3 km presence and the 20 km reach that carriers and ratios read (annex 01 E3).</summary>
    internal static class ContactCountsTests
    {
        private const float Reach = 20000f;

        public static void Run()
        {
            RingEdgesAreInclusive();
            CarriersCountOnlyInsideReach();
            HostileAirNeverEntersTheGroundRatio();
            FriendlyKindsLandInTheRightBuckets();
            BadInputIsSkipped();
        }

        private static ContactFix East(float metres, ContactKind kind) => new ContactFix(metres, 0f, kind);

        private static RingCounts Count(params ContactFix[] fixes) => ContactCounts.Count(fixes, fixes.Length, 0f, 0f, Reach);

        private static void RingEdgesAreInclusive()
        {
            RingCounts at = Count(East(3000f, ContactKind.FriendlyGround), East(3000f, ContactKind.HostileSurface), East(3000f, ContactKind.HostileAir));
            TestAssert.Eq(at.NearFriendly, 1, "a friendly exactly on the near ring is near");
            TestAssert.Eq(at.NearHostile, 1, "a hostile surface contact exactly on the near ring is near");
            TestAssert.Eq(at.HostileAirNear, 1, "a hostile aircraft exactly on the near ring is near");
            RingCounts past = Count(East(3001f, ContactKind.FriendlyGround), East(3001f, ContactKind.HostileSurface), East(3001f, ContactKind.HostileAir));
            TestAssert.Eq(past.NearFriendly, 0, "a friendly just past the near ring is not near");
            TestAssert.Eq(past.NearHostile, 0, "a hostile just past the near ring is not near");
            TestAssert.Eq(past.HostileAirNear, 0, "a hostile aircraft just past the near ring is not near");
            TestAssert.Eq(past.ReachFriendlyGround, 1, "but it is inside the reach ring");
            TestAssert.Eq(past.ReachHostile, 1, "and the hostile surface contact is inside reach");
            RingCounts edge = Count(East(Reach, ContactKind.FriendlyGround), East(Reach, ContactKind.HostileSurface));
            TestAssert.Eq(edge.ReachFriendlyGround, 1, "a friendly exactly on the reach ring is in reach");
            TestAssert.Eq(edge.ReachHostile, 1, "a hostile exactly on the reach ring is in reach");
            RingCounts beyond = Count(East(Reach + 1f, ContactKind.FriendlyGround), East(Reach + 1f, ContactKind.HostileSurface));
            TestAssert.Eq(beyond.ReachFriendlyGround, 0, "a friendly past reach is out");
            TestAssert.Eq(beyond.ReachHostile, 0, "a hostile past reach is out");
            RingCounts diagonal = ContactCounts.Count(new[] { new ContactFix(2100f, 2100f, ContactKind.FriendlyGround) }, 1, 0f, 0f, Reach);
            TestAssert.Eq(diagonal.NearFriendly, 1, "the ring is a circle: 2.97 km on the diagonal is near");
            RingCounts corner = ContactCounts.Count(new[] { new ContactFix(2200f, 2200f, ContactKind.FriendlyGround) }, 1, 0f, 0f, Reach);
            TestAssert.Eq(corner.NearFriendly, 0, "the ring is a circle: 3.11 km on the diagonal is not near");
        }

        private static void CarriersCountOnlyInsideReach()
        {
            RingCounts counts = Count(East(1000f, ContactKind.FriendlyCarrier), East(Reach, ContactKind.FriendlyCarrier), East(Reach + 1f, ContactKind.FriendlyCarrier));
            TestAssert.Eq(counts.Carriers, 2, "carriers count while inside reach");
            TestAssert.Eq(counts.ReachFriendlyGround, 2, "a carrier is also friendly ground inside reach");
            TestAssert.Eq(counts.NearFriendly, 1, "and near only inside the near ring");
            TestAssert.Eq(Count(East(1000f, ContactKind.FriendlyGround)).Carriers, 0, "a plain ground vehicle is not a carrier");
        }

        private static void HostileAirNeverEntersTheGroundRatio()
        {
            RingCounts counts = Count(East(500f, ContactKind.HostileAir), East(15000f, ContactKind.HostileAir));
            TestAssert.Eq(counts.HostileAirNear, 1, "tracked hostile aircraft near the front are counted for DEFEND");
            TestAssert.Eq(counts.NearHostile, 0, "hostile aircraft are not near surface hostiles");
            TestAssert.Eq(counts.ReachHostile, 0, "hostile aircraft never enter the reach ratio");
        }

        private static void FriendlyKindsLandInTheRightBuckets()
        {
            RingCounts counts = Count(
                East(100f, ContactKind.FriendlyGround), East(200f, ContactKind.FriendlyCarrier), East(300f, ContactKind.FriendlyOther),
                East(10000f, ContactKind.FriendlyOther), East(400f, ContactKind.HostileSurface));
            TestAssert.Eq(counts.NearFriendly, 3, "near friendly is ground, carrier and other together");
            TestAssert.Eq(counts.ReachFriendlyGround, 2, "reach ground is ground plus carrier");
            TestAssert.Eq(counts.ReachFriendlyOther, 2, "reach other is ships and aircraft");
            TestAssert.Eq(counts.Carriers, 1, "one carrier");
            TestAssert.Eq(counts.NearHostile, 1, "one surface hostile near");
            TestAssert.Eq(counts.ReachHostile, 1, "and in reach");
        }

        private static void BadInputIsSkipped()
        {
            var fixes = new List<ContactFix>
            {
                new ContactFix(float.NaN, 0f, ContactKind.FriendlyGround), new ContactFix(0f, float.PositiveInfinity, ContactKind.HostileSurface),
                East(100f, ContactKind.FriendlyGround), East(100f, ContactKind.FriendlyGround), East(100f, ContactKind.FriendlyGround),
            };
            RingCounts counts = ContactCounts.Count(fixes, fixes.Count, 0f, 0f, Reach);
            TestAssert.Eq(counts.NearFriendly, 3, "non-finite fixes are skipped");
            TestAssert.Eq(counts.NearHostile + counts.ReachHostile, 0, "a non-finite hostile fix is skipped");
            TestAssert.Eq(ContactCounts.Count(fixes, 3, 0f, 0f, Reach).NearFriendly, 1, "only the first count fixes are read");
            TestAssert.Eq(ContactCounts.Count(fixes, 99, 0f, 0f, Reach).NearFriendly, 3, "a count past the list is clamped");
            TestAssert.Eq(ContactCounts.Count(fixes, -1, 0f, 0f, Reach).NearFriendly, 0, "a negative count reads nothing");
            TestAssert.Eq(ContactCounts.Count(null, 5, 0f, 0f, Reach).NearFriendly, 0, "no list reads nothing");
            TestAssert.Eq(ContactCounts.Count(fixes, fixes.Count, float.NaN, 0f, Reach).NearFriendly, 0, "a non-finite centre reads nothing");
            RingCounts noReach = ContactCounts.Count(fixes, fixes.Count, 0f, 0f, float.NaN);
            TestAssert.Eq(noReach.NearFriendly, 3, "a bad reach radius leaves the near ring working");
            TestAssert.Eq(noReach.ReachFriendlyGround, 0, "a bad reach radius reads no reach");
            RingCounts offCentre = ContactCounts.Count(fixes, fixes.Count, 100f, 50f, Reach);
            TestAssert.Eq(offCentre.NearFriendly, 3, "rings are centred on the front, not the origin");
        }
    }
}
