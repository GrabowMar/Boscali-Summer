using BoscaliSummer.Features.Support.Runtime;

namespace BoscaliSummer.Tests.Features.Support
{
    internal static class SupportTests
    {
        public static void Run()
        {
            var ledger = new SupportRequestLedger(4);

            // Only accepted requests are remembered. A denial must not burn the id, or the
            // client's next legitimate attempt with that id comes back as a duplicate.
            TestAssert.That(!ledger.WasAccepted(10, 1), "an unseen request was marked accepted");
            ledger.Accept(10, 1, 100f);
            TestAssert.That(ledger.WasAccepted(10, 1), "an accepted request replay was not detected");
            TestAssert.That(!ledger.WasAccepted(11, 1), "request ids leaked between players");

            TestAssert.That(ledger.IsCoolingDown(10, 105f, 10f), "an active cooldown was ignored");
            TestAssert.That(!ledger.IsCoolingDown(10, 111f, 10f), "an expired cooldown stayed active");
            TestAssert.That(ledger.CooldownRemaining(10, 105f, 10f) == 5f, "cooldown countdown is wrong");
            TestAssert.That(ledger.CooldownRemaining(10, 130f, 10f) == 0f,
                "an expired cooldown reported time remaining");

            TestAssert.That(!ledger.IsRateLimited(20, 1f, 2, 1f), "the first request was rate limited");
            TestAssert.That(!ledger.IsRateLimited(20, 1.2f, 2, 1f), "the second request was rate limited");
            TestAssert.That(ledger.IsRateLimited(20, 1.4f, 2, 1f), "a request flood was not rate limited");
            TestAssert.That(!ledger.IsRateLimited(20, 2.1f, 2, 1f), "the rate window did not recover");

            for (int i = 2; i <= 6; i++) ledger.Accept(10, i, 100f);
            TestAssert.That(!ledger.WasAccepted(10, 1), "bounded replay history kept an evicted id");

            ledger.Clear();
            TestAssert.That(!ledger.IsCoolingDown(10, 101f, 10f), "a scene reset kept cooldown state");
        }
    }
}
