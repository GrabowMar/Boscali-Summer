using BoscaliSummer.Features.Support.Runtime;

namespace BoscaliSummer.Tests.Features.Support
{
    internal static class SupportTests
    {
        public static void Run()
        {
            var ledger = new SupportRequestLedger(4);
            TestAssert.That(!ledger.IsDuplicate(10, 1), "first request was marked duplicate");
            TestAssert.That(ledger.IsDuplicate(10, 1), "request replay was not detected");
            TestAssert.That(!ledger.IsDuplicate(11, 1), "request IDs leaked between players");
            ledger.Accept(10, 100f);
            TestAssert.That(ledger.IsCoolingDown(10, 105f, 10f), "active cooldown was ignored");
            TestAssert.That(!ledger.IsCoolingDown(10, 111f, 10f), "expired cooldown remained active");

            TestAssert.That(!ledger.IsRateLimited(20, 1f, 2, 1f), "first request was rate limited");
            TestAssert.That(!ledger.IsRateLimited(20, 1.2f, 2, 1f), "second request was rate limited");
            TestAssert.That(ledger.IsRateLimited(20, 1.4f, 2, 1f), "request flood was not rate limited");
            TestAssert.That(!ledger.IsRateLimited(20, 2.1f, 2, 1f), "rate limit window did not recover");

            for (int i = 2; i <= 6; i++) ledger.IsDuplicate(10, i);
            TestAssert.That(!ledger.IsDuplicate(10, 1), "bounded replay history retained an evicted ID");
            ledger.Clear();
            TestAssert.That(!ledger.IsCoolingDown(10, 101f, 10f), "scene reset retained cooldown state");
        }
    }
}
