using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>
    /// Surviving FIRES wire pins and pure ballistics helpers. A helper TTI is a
    /// quotient (polyline length over missile speed); unknown inputs quote -1.
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
        }

        private static void CheckWireIds()
        {
            TestAssert.That((byte)SupportActionId.Prsm == 30, "Prsm rides the wire as 30");
            TestAssert.That((byte)SupportActionId.Cruise == 31, "Cruise rides the wire as 31");
            TestAssert.That((byte)SupportResult.StaleIntel == 46, "StaleIntel rides the wire as 46");
            TestAssert.That((byte)SupportResult.WindowClosed == 47, "retired WindowClosed keeps reserved wire id 47");
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
            TestAssert.That(StrikeBallistics.MultiLegTimeOfFlight(new[] { new StrikeWaypoint(0f, 0f),
                new StrikeWaypoint(float.NaN, 0f) }, 500f) < 0f, "unknown waypoint quotes no route time");
            TestAssert.That(StrikeBallistics.MultiLegTimeOfFlight(new[] { new StrikeWaypoint(0f, 0f),
                new StrikeWaypoint(float.PositiveInfinity, 0f) }, 500f) < 0f, "infinite route quotes no time");
            TestAssert.That(StrikeBallistics.MultiLegTimeOfFlight(new[] { new StrikeWaypoint(0f, 0f),
                new StrikeWaypoint(0f, 0f) }, 500f) < 0f, "zero-length route quotes no time");

            var tooMany = new List<StrikeWaypoint>();
            for (int i = 0; i <= StrikeBallistics.MaxWaypoints; i++) tooMany.Add(new StrikeWaypoint(i * 1000f, 0f));
            TestAssert.That(StrikeBallistics.MultiLegTimeOfFlight(tooMany, 500f) < 0f, "over-bound legs quote nothing");
            TestAssert.That(StrikeBallistics.MaxLegs + 2 <= StrikeBallistics.MaxWaypoints, "missile, legs and target fit the route");
        }

        private static void CheckLegRefusal()
        {
            TestAssert.That(!StrikeBallistics.LegRefusedBySeeker(5000f, 9000f, 2000f), "long legs pass");
            TestAssert.That(StrikeBallistics.LegRefusedBySeeker(1000f, 9000f, 2000f), "the waypoint rim is refused");
            TestAssert.That(StrikeBallistics.LegRefusedBySeeker(999f, 9000f, 2000f), "inside the waypoint rim is refused");
            TestAssert.That(StrikeBallistics.LegRefusedBySeeker(5000f, 1000f, 2000f), "the target rim is refused");
            TestAssert.That(StrikeBallistics.LegRefusedBySeeker(5000f, 9000f, 0f), "unknown terminal range fails closed");
            TestAssert.That(StrikeBallistics.LegRefusedBySeeker(5000f, 9000f, -2000f), "negative terminal range fails closed");
            TestAssert.That(StrikeBallistics.LegRefusedBySeeker(5000f, 9000f, float.NaN) &&
                StrikeBallistics.LegRefusedBySeeker(5000f, 9000f, float.PositiveInfinity), "unknown terminal range fails closed");
        }
    }
}
