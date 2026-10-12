using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>A throwing staff is contained: counted, backed off, logged rarely and never propagated (annex 01 E10).</summary>
    internal static class FaultLedgerTests
    {
        public static void Run()
        {
            BackOffScheduleIsTenTwentyThirtyFortyForty();
            HealthFlipsAtTheThirdFaultAndRecovers();
            TheLogGateAllowsOneLinePerSixtySeconds();
            GuardSwallowsAThrowAndReturnsFalse();
            GuardLogsOncePerWindowAndCountsEveryFault();
            AThrowingLogDoesNotPropagate();
            ACommandPathAnswersInvalidWhenCollectThrows();
        }

        private static void BackOffScheduleIsTenTwentyThirtyFortyForty()
        {
            var ledger = new FaultLedger();
            TestAssert.Near(ledger.NextDelay(10f), 10f, "a healthy ledger reviews every 10 s");
            float[] want = { 10f, 20f, 30f, 40f, 40f, 40f };
            for (int n = 0; n < want.Length; n++)
            {
                ledger.Fail(n * 100f, out _);
                TestAssert.Near(ledger.NextDelay(10f), want[n], "after fault " + (n + 1));
            }
        }

        private static void HealthFlipsAtTheThirdFaultAndRecovers()
        {
            var ledger = new FaultLedger();
            TestAssert.That(ledger.Healthy, "a new ledger is healthy");
            ledger.Fail(0f, out _);
            ledger.Fail(1f, out _);
            TestAssert.That(ledger.Healthy && ledger.Faults == 2, "two faults are survivable");
            ledger.Fail(2f, out _);
            TestAssert.That(!ledger.Healthy && ledger.Faults == FaultLedger.UnhealthyAt, "the third fault makes the staff unhealthy");
            ledger.Ok();
            TestAssert.That(ledger.Healthy && ledger.Faults == 0, "a clean pass restores it");
            TestAssert.Near(ledger.NextDelay(10f), 10f, "and the schedule starts again");
        }

        private static void TheLogGateAllowsOneLinePerSixtySeconds()
        {
            var ledger = new FaultLedger();
            ledger.Fail(0f, out bool first);
            TestAssert.That(first, "the first fault is logged");
            ledger.Fail(10f, out bool second);
            ledger.Fail(59.9f, out bool third);
            TestAssert.That(!second && !third, "nothing more inside 60 s");
            ledger.Fail(60f, out bool fourth);
            TestAssert.That(fourth, "a line is due again at 60 s");
            ledger.Fail(119.9f, out bool fifth);
            TestAssert.That(!fifth, "the window restarts from the last line");
            ledger.Fail(120f, out bool sixth);
            TestAssert.That(sixth, "and opens again 60 s later");
            ledger.Ok();
            ledger.Fail(121f, out bool afterOk);
            TestAssert.That(!afterOk, "a clean pass does not reopen the log gate");
        }

        private static void GuardSwallowsAThrowAndReturnsFalse()
        {
            var ledger = new FaultLedger();
            var lines = new List<string>();
            bool ran = Guard.Run(ledger, 5f, () => throw new InvalidOperationException("boom"), lines.Add, "WEST");
            TestAssert.That(!ran, "a throwing body returns false");
            TestAssert.Eq(ledger.Faults, 1, "and is counted");
            TestAssert.Eq(lines.Count, 1, "and is logged once");
            TestAssert.Eq(lines[0], "[TheaterOps] WEST staff fault: boom (1)", "in the documented form");
            TestAssert.That(Guard.Run(ledger, 6f, () => { }, lines.Add, "WEST"), "a clean body returns true");
            TestAssert.Eq(ledger.Faults, 0, "and resets the count");
            TestAssert.That(Guard.Run(ledger, 7f, null, lines.Add, "WEST"), "a null body is a clean no-op");
            TestAssert.That(!Guard.Run(null, 8f, () => throw new Exception("no ledger"), lines.Add, "WEST"), "no ledger still contains the throw");
        }

        private static void GuardLogsOncePerWindowAndCountsEveryFault()
        {
            var ledger = new FaultLedger();
            var lines = new List<string>();
            for (int n = 0; n < 4; n++) Guard.Run(ledger, 10f * n, () => throw new Exception("again"), lines.Add, "EAST");
            TestAssert.Eq(lines.Count, 1, "four faults in 30 s write one line");
            TestAssert.Eq(ledger.Faults, 4, "but are all counted");
            Guard.Run(ledger, 60f, () => throw new Exception("late"), lines.Add, "EAST");
            TestAssert.Eq(lines.Count, 2, "a second line after the window");
            TestAssert.Eq(lines[1], "[TheaterOps] EAST staff fault: late (5)", "carrying the running count");
        }

        private static void AThrowingLogDoesNotPropagate()
        {
            var ledger = new FaultLedger();
            bool ran = Guard.Run(ledger, 1f, () => throw new Exception("x"), line => throw new InvalidOperationException("log failed"), "WEST");
            TestAssert.That(!ran && ledger.Faults == 1, "a broken log sink changes nothing");
            TestAssert.That(!Guard.Run(ledger, 2f, () => throw new Exception("y"), null, "WEST"), "a missing log sink is fine");
        }

        // The command path of the service, offline: collect, validate, apply, answer. A throw anywhere answers Invalid and propagates nothing.
        private static TheaterCommandCode Apply(FaultLedger ledger, Action collect)
        {
            TheaterCommandCode answer = TheaterCommandCode.Invalid;
            bool ran = Guard.Run(ledger, 1f, () => { collect(); answer = TheaterCommandCode.Accepted; }, null, "WEST");
            return ran ? answer : TheaterCommandCode.Invalid;
        }

        private static void ACommandPathAnswersInvalidWhenCollectThrows()
        {
            var ledger = new FaultLedger();
            TestAssert.Eq(Apply(ledger, () => throw new NullReferenceException("Collect")), TheaterCommandCode.Invalid, "a throwing Collect answers Invalid");
            TestAssert.Eq(Apply(ledger, () => { }), TheaterCommandCode.Accepted, "a clean command is Accepted");
        }
    }
}
