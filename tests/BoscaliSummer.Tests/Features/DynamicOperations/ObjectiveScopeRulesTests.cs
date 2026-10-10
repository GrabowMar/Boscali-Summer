using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.DynamicOperations.Domain;

namespace BoscaliSummer.Tests.Features.DynamicOperations
{
    /// <summary>What the OBJECTIVE page of the TAC BOARD asks of DynamicOperations: the gated phases, the four hold gates, the rings worth the plot, the heading.</summary>
    internal static class ObjectiveScopeRulesTests
    {
        public static void Run()
        {
            Phases();
            Gates();
            Rings();
            Heading();
            Deadline();
        }

        private static void Deadline()
        {
            TestAssert.Near(ObjectiveScopeRules.DeadlineSeconds(OperationTitles.Title(OperationKind.Intercept)), 600f, "An intercept is given ten minutes");
            TestAssert.Near(ObjectiveScopeRules.DeadlineSeconds(OperationTitles.Title(OperationKind.Recon)), 1200f, "Every other contract twenty");
            TestAssert.Near(ObjectiveScopeRules.DeadlineSeconds(null), 1200f, "An unnamed one too");
            foreach (OperationKind kind in new[] { OperationKind.Intercept, OperationKind.Recon, OperationKind.Defend })
            {
                var op = new Operation(1, 1, kind, OperationReward.None, 0f, 100, 20);
                op.Accept(0f, "Pilot", 1);
                TestAssert.Near(op.Deadline, ObjectiveScopeRules.DeadlineSeconds(OperationTitles.Title(kind)), "The domain's deadline is what the page's ring counts against: " + kind);
            }
        }

        private static void Phases()
        {
            foreach (string title in new[] { "JOIN", "COVER", "HOLD", "SURVEY", "OBSERVE" }) TestAssert.That(ObjectiveScopeRules.GatedPhase(title), "The gates decide " + title);
            foreach (string title in new[] { "STRIKE", "INTERCEPT", "RECOVER", "RETURN", "JAM", "INSERT", "SECURE", "TRANSFER", "REPAIR", "" }) TestAssert.That(!ObjectiveScopeRules.GatedPhase(title), "...but not " + title);
            // The titles OperationPresentation really builds: every timed hold and every join of it is a gated phase.
            foreach (OperationKind kind in new[] { OperationKind.Defend, OperationKind.Patrol, OperationKind.Recon, OperationKind.BattlefieldSurvey, OperationKind.SupplyEscort, OperationKind.RepairCover, OperationKind.DamageAssessment, OperationKind.SortieReport })
            {
                var op = new Operation(1, 1, kind, OperationReward.None, 0f, 100, 20);
                op.Accept(0f, "Pilot", 1);
                ObjectiveTasking tasking = OperationPresentation.Build(op, false, true, ObjectiveAsset.Site, ObjectiveContact.Fixed, 0f, "", "");
                bool anyGated = false;
                for (int i = 0; i < tasking.Phases.Length; i++) anyGated |= ObjectiveScopeRules.GatedPhase(tasking.Phases[i].Title);
                TestAssert.That(anyGated, "A " + kind + " contract has a phase the gates decide");
            }
        }

        private static void Gates()
        {
            ObjectiveScopeRules.Gates(900f, 1500f, 400f, ObjectiveContact.Fixed, "", out bool range, out bool alt, out bool los, out bool track);
            TestAssert.That(range && alt && los && track, "In the zone, high, tracked, nothing blocking: every gate holds");
            ObjectiveScopeRules.Gates(2500f, 1500f, 400f, ObjectiveContact.Known, "", out range, out alt, out los, out track);
            TestAssert.That(!range && alt && los && track, "Outside the zone: range fails");
            ObjectiveScopeRules.Gates(900f, 1500f, 30f, ObjectiveContact.Fixed, "No eligible aircraft on station.", out range, out alt, out los, out track);
            TestAssert.That(range && !alt && los && track, "Too low: altitude fails, and the sightline is not blamed for it");
            ObjectiveScopeRules.Gates(900f, 1500f, 400f, ObjectiveContact.LastKnown, "Fresh faction tracking required.", out range, out alt, out los, out track);
            TestAssert.That(range && alt && los && !track, "A last-known contact fails the track gate");
            ObjectiveScopeRules.Gates(900f, 1500f, 400f, ObjectiveContact.Known, "No eligible aircraft on station. Check range, height and sightline.", out range, out alt, out los, out track);
            TestAssert.That(range && alt && track && !los, "In range, high and tracked while the host still says off station: the sightline is what fails");
            ObjectiveScopeRules.Gates(1500f, 1500f, 50f, ObjectiveContact.Fixed, null, out range, out alt, out los, out track);
            TestAssert.That(range && alt && los && track, "The edge of the zone and exactly 50 m count");
            TestAssert.That(ObjectiveScopeRules.Tracked(ObjectiveContact.Fixed) && ObjectiveScopeRules.Tracked(ObjectiveContact.Known) && !ObjectiveScopeRules.Tracked(ObjectiveContact.LastKnown) && !ObjectiveScopeRules.Tracked(ObjectiveContact.Lost) && !ObjectiveScopeRules.Tracked(ObjectiveContact.Unavailable), "Which contacts count as tracked");
        }

        private static AirDefenceRing Ring(float x, float z, float range, AirDefenceKind kind = AirDefenceKind.RadarSam) =>
            new AirDefenceRing(x, z, range, 0f, 12000f, kind, true, true, RingSource.Tracked, true, 0f, false, 1, 1, 0);

        private static void Rings()
        {
            var all = new[]
            {
                Ring(1000f, 0f, 6000f), Ring(50000f, 0f, 6000f), Ring(0f, 0f, 0f), Ring(2000f, 2000f, 5000f, AirDefenceKind.Sensor), Ring(30000f, 0f, 20000f, AirDefenceKind.Gun),
                Ring(float.NaN, 0f, 6000f), Ring(-3000f, 500f, 2500f, AirDefenceKind.Gun),
            };
            var into = new AirDefenceRing[8];
            var rank = new float[8];
            int n = ObjectiveScopeRules.PickRings(all, all.Length, 0f, 0f, 0f, 0f, into, rank);
            TestAssert.Eq(n, 3, "A near ring, a far ring whose range reaches, and a near gun: three. Not the far one, the empty, the sensor or the broken");
            TestAssert.That(into[0].X == 1000f && into[1].X == 30000f && into[2].X == -3000f, "...in the order they come");
            TestAssert.Eq(ObjectiveScopeRules.PickRings(all, 0, 0f, 0f, 0f, 0f, into, rank), 0, "No rings known: none");

            // a full list keeps the nearest
            var many = new AirDefenceRing[12];
            for (int i = 0; i < many.Length; i++) many[i] = Ring(1000f * (i + 1), 0f, 100f);
            var small = new AirDefenceRing[ObjectiveScopeRules.MaxRings];
            n = ObjectiveScopeRules.PickRings(many, many.Length, 0f, 0f, 0f, 0f, small, new float[ObjectiveScopeRules.MaxRings]);
            TestAssert.Eq(n, ObjectiveScopeRules.MaxRings, "Only so many are kept");
            float far = 0f;
            for (int i = 0; i < n; i++) far = System.Math.Max(far, small[i].X);
            TestAssert.Near(far, 8000f, "...the nearest ones: none of the four farthest");
            // a ring near the pilot, far from the zone, still counts
            n = ObjectiveScopeRules.PickRings(new[] { Ring(60000f, 0f, 3000f) }, 1, 0f, 0f, 59000f, 0f, into, rank);
            TestAssert.Eq(n, 1, "A ring near the pilot counts even when it is far from the zone");
        }

        private static void Heading()
        {
            TestAssert.Near(ObjectiveScopeRules.Heading(0f, 1f), 0f, "North");
            TestAssert.Near(ObjectiveScopeRules.Heading(1f, 0f), 90f, "East");
            TestAssert.Near(ObjectiveScopeRules.Heading(0f, -1f), 180f, "South");
            TestAssert.Near(ObjectiveScopeRules.Heading(-1f, 0f), 270f, "West");
        }
    }
}
