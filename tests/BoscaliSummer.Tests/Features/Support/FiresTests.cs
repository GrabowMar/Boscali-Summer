using System.Collections.Generic;
using BoscaliSummer.Features.Support.Runtime;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>
    /// FIRES (§6.1) wire pins and the pure ballistics helpers. TTI is always a
    /// live quotient (polyline length over the missile's real top speed), never
    /// a stored estimate; unknown inputs quote -1 so the MFD renders nothing.
    /// Leg validation mirrors the native seeker refusal (inside half terminal
    /// range of the missile or the target); the seeker stays authoritative.
    /// </summary>
    internal static class FiresTests
    {
        public static void Run()
        {
            CheckWireIds();
            CheckTimeOfFlight();
            CheckMultiLeg();
            CheckLegRefusal();
            CheckIntelGate();
            CheckWindows();
        }

        private static void CheckWireIds()
        {
            TestAssert.That((byte)SupportActionId.Prsm == 30, "Prsm rides the wire as 30");
            TestAssert.That((byte)SupportActionId.Cruise == 31, "Cruise rides the wire as 31");
            TestAssert.That((byte)SupportResult.StaleIntel == 46, "StaleIntel rides the wire as 46");
            TestAssert.That((byte)SupportResult.WindowClosed == 47, "WindowClosed rides the wire as 47");
        }

        private static void CheckTimeOfFlight()
        {
            TestAssert.That(StrikeBallistics.TimeOfFlight(10000f, 500f) == 20f, "ten kay at five hundred is twenty seconds");
            TestAssert.That(StrikeBallistics.TimeOfFlight(0f, 500f) < 0f, "no distance quotes nothing");
            TestAssert.That(StrikeBallistics.TimeOfFlight(-5f, 500f) < 0f, "negative distance quotes nothing");
            TestAssert.That(StrikeBallistics.TimeOfFlight(10000f, 0f) < 0f, "no speed quotes nothing");
            TestAssert.That(StrikeBallistics.TimeOfFlight(10000f, -500f) < 0f, "negative speed quotes nothing");
            TestAssert.That(StrikeBallistics.TimeOfFlight(float.NaN, 500f) < 0f, "NaN distance quotes nothing");
            TestAssert.That(StrikeBallistics.TimeOfFlight(10000f, float.PositiveInfinity) < 0f, "infinite speed quotes nothing");
        }

        private static void CheckMultiLeg()
        {
            var legs = new List<StrikeWaypoint>
            {
                new StrikeWaypoint(0f, 0f),
                new StrikeWaypoint(3000f, 0f),
                new StrikeWaypoint(3000f, 4000f)
            };
            TestAssert.That(StrikeBallistics.MultiLegTimeOfFlight(legs, 350f) == 20f, "seven-kay dogleg at three-fifty is twenty seconds");
            TestAssert.That(StrikeBallistics.MultiLegTimeOfFlight(null, 350f) < 0f, "no legs quote nothing");
            TestAssert.That(StrikeBallistics.MultiLegTimeOfFlight(new List<StrikeWaypoint> { new StrikeWaypoint(0f, 0f) }, 350f) < 0f, "one point quotes nothing");
            TestAssert.That(StrikeBallistics.MultiLegTimeOfFlight(legs, 0f) < 0f, "no speed quotes nothing on a dogleg");

            var tooMany = new List<StrikeWaypoint>();
            for (int i = 0; i <= StrikeBallistics.MaxWaypoints; i++) tooMany.Add(new StrikeWaypoint(i * 1000f, 0f));
            TestAssert.That(StrikeBallistics.MultiLegTimeOfFlight(tooMany, 500f) < 0f, "over-bound legs quote nothing");
        }

        private static void CheckLegRefusal()
        {
            TestAssert.That(!StrikeBallistics.LegRefusedBySeeker(5000f, 9000f, 2000f), "long legs pass");
            TestAssert.That(StrikeBallistics.LegRefusedBySeeker(1000f, 9000f, 2000f), "the waypoint rim is refused");
            TestAssert.That(StrikeBallistics.LegRefusedBySeeker(999f, 9000f, 2000f), "inside the waypoint rim is refused");
            TestAssert.That(StrikeBallistics.LegRefusedBySeeker(5000f, 1000f, 2000f), "the target rim is refused");
            TestAssert.That(StrikeBallistics.LegRefusedBySeeker(5000f, 9000f, 0f), "unknown terminal range fails closed");
            TestAssert.That(StrikeBallistics.LegRefusedBySeeker(5000f, 9000f, -2000f), "negative terminal range fails closed");
        }

        private static void CheckIntelGate()
        {
            var empty = new List<IntelCandidate>();
            TestAssert.That(!IntelGate.AnyFresh(empty, 120f, 1000f), "no tracks means no intel");
            TestAssert.That(!IntelGate.AnyFresh(null, 120f, 1000f), "null means no intel");
            TestAssert.That(!IntelGate.AnyFresh(empty, 0f, 1000f), "a dead window means no intel");
            TestAssert.That(!IntelGate.AnyFresh(empty, 120f, 0f), "a dead radius means no intel");

            var fresh = new List<IntelCandidate> { new IntelCandidate(30f, 500f) };
            TestAssert.That(IntelGate.AnyFresh(fresh, 120f, 1000f), "a fresh near track opens the gate");
            TestAssert.That(IntelGate.AnyFresh(new List<IntelCandidate> { new IntelCandidate(120f, 1000f) }, 120f, 1000f), "the rims are inside");
            TestAssert.That(!IntelGate.AnyFresh(new List<IntelCandidate> { new IntelCandidate(121f, 500f) }, 120f, 1000f), "a stale track keeps the gate shut");
            TestAssert.That(!IntelGate.AnyFresh(new List<IntelCandidate> { new IntelCandidate(30f, 1001f) }, 120f, 1000f), "a far track keeps the gate shut");
            TestAssert.That(IntelGate.AnyFresh(new List<IntelCandidate> { new IntelCandidate(-5f, 500f) }, 120f, 1000f), "clock skew reads as fresh");
            TestAssert.That(!IntelGate.AnyFresh(new List<IntelCandidate> { new IntelCandidate(float.NaN, 500f) }, 120f, 1000f), "garbage age fails closed");
            TestAssert.That(!IntelGate.AnyFresh(new List<IntelCandidate> { new IntelCandidate(30f, float.NaN) }, 120f, 1000f), "garbage distance fails closed");

            var crowd = new List<IntelCandidate>();
            for (int i = 0; i < IntelGate.MaxTracks + 10; i++) crowd.Add(new IntelCandidate(9999f, 99999f));
            crowd.Add(new IntelCandidate(1f, 1f));
            TestAssert.That(!IntelGate.AnyFresh(crowd, 120f, 1000f), "the scan stays bounded past the cap");
            TestAssert.That(StrikeBallistics.MaxLegs + 2 <= StrikeBallistics.MaxWaypoints, "missile, legs and target fit the route");
        }

        private static void CheckWindows()
        {
            TestAssert.That(WindowMath.Open(0.0, 0.0, 180f, 90f, out float changeIn) && changeIn == 180f, "the cycle opens at the anchor");
            TestAssert.That(WindowMath.Open(0.0, 179.9, 180f, 90f, out changeIn) && changeIn > 0f && changeIn < 1f, "open until the rim");
            TestAssert.That(!WindowMath.Open(0.0, 180.0, 180f, 90f, out changeIn) && changeIn == 90f, "the rim is closed");
            TestAssert.That(WindowMath.Open(0.0, 270.0, 180f, 90f, out changeIn) && changeIn == 180f, "the cycle repeats");
            TestAssert.That(!WindowMath.Open(100.0, 50.0, 180f, 90f, out changeIn) && changeIn == 50f, "before the anchor is closed");
            TestAssert.That(WindowMath.Open(0.0, 10.0, 180f, 0f, out changeIn), "a zero closed span stays open");
            TestAssert.That(!WindowMath.Open(0.0, 10.0, 0f, 90f, out changeIn), "a zero open span stays closed");
            TestAssert.That(WindowMath.Open(0.0, 10.0, 0f, 0f, out changeIn), "a degenerate cycle fails open");
            TestAssert.That(WindowMath.Open(0.0, 10.0, float.NaN, 90f, out changeIn), "garbage fails open");

            double a = WindowMath.Stagger("BOSCALI", 270f);
            TestAssert.That(a >= 0.0 && a < 270f, "the stagger lands inside the cycle");
            TestAssert.That(WindowMath.Stagger("BOSCALI", 270f) == a, "the stagger is deterministic");
            TestAssert.That(WindowMath.Stagger(null, 270f) == 0.0, "no key means no stagger");
            TestAssert.That(WindowMath.Stagger("", 270f) == 0.0, "an empty key means no stagger");
            TestAssert.That(WindowMath.Stagger("BOSCALI", 0f) == 0.0, "no cycle means no stagger");
        }
    }
}
