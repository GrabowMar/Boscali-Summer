using BoscaliSummer.Features.Support.Runtime;

namespace BoscaliSummer.Tests.Features.Support
{
    internal static class SupportTests
    {
        private static void TestMapGesture()
        {
            var gesture = new SupportMapGesture();
            NOAvionics.MapPicker.Reset();
            try
            {
                TestAssert.That(gesture.TryArm("SUPPORT"), "Support must arm without Wing Command");
                TestAssert.That(NOAvionics.MapPicker.IsBusy, "WC running before support must see the reservation");
                gesture.Complete(10);
                gesture.Advance(10);
                TestAssert.That(NOAvionics.MapPicker.IsOwner(NOAvionics.MapPicker.Support),
                    "WC running after support must not reuse the consumed right-click");
                TestAssert.That(!NOAvionics.MapPicker.TryArm(NOAvionics.MapPicker.WingPoint, 1, "WING"),
                    "The consuming frame must remain exclusive");
                gesture.Advance(11);
                TestAssert.That(!NOAvionics.MapPicker.IsBusy, "Support must release on the following frame");
                NOAvionics.MapPicker.TryArm(NOAvionics.MapPicker.WingPoint, 1, "WING");
                TestAssert.That(!gesture.TryArm("SUPPORT"), "Support must respect an armed wing order");
                gesture.Reset();
                TestAssert.That(NOAvionics.MapPicker.IsOwner(NOAvionics.MapPicker.WingPoint),
                    "Support cleanup must not clear Wing Command's gesture");
                NOAvionics.MapPicker.Disarm(NOAvionics.MapPicker.WingPoint);
                gesture.TryArm("SUPPORT");
                gesture.Complete(12);
                gesture.TryArm("NEW SUPPORT");
                gesture.Advance(13);
                TestAssert.That(NOAvionics.MapPicker.IsOwner(NOAvionics.MapPicker.Support),
                    "Rearming must cancel a pending release");
                gesture.Reset();
                TestAssert.That(!NOAvionics.MapPicker.IsBusy, "Scene teardown must release support ownership");
            }
            finally { NOAvionics.MapPicker.Reset(); }
        }

        public static void Run()
        {

            TestMapGesture();
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
            TestAssert.That(!ledger.IsCoolingDown(10, 105f, 0f), "zero cooldown was marked cooling down");
            TestAssert.That(ledger.CooldownRemaining(10, 105f, 0f) == 0f, "zero cooldown reported remaining time");

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
