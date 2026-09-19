using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;

namespace BoscaliSummer.Tests.Features.Support
{
    internal static class SupportTests
    {
        private static void TestMapGesture()
        {
            MapPicker.Reset();
            var gesture = new SupportMapGesture();
            TestAssert.That(!gesture.Armed, "a fresh gesture must not be armed");

            TestAssert.That(MapPicker.TryArm(MapPicker.WingPoint, MapPicker.GestureLeft, "WING"),
                "test setup could not arm the wing picker");
            TestAssert.That(!gesture.TryArm("CALL IN"),
                "support armed while Wing Command owned the shared map picker");
            MapPicker.Disarm(MapPicker.WingPoint);

            TestAssert.That(gesture.TryArm("CALL IN"), "arming must succeed");
            TestAssert.That(gesture.Armed, "TryArm must arm the gesture");
            TestAssert.That(gesture.Prompt == "CALL IN", "TryArm must keep the supplied prompt");

            gesture.TryArm(null);
            TestAssert.That(!string.IsNullOrEmpty(gesture.Prompt), "an empty prompt must fall back to a default");

            // The click is consumed in Update; the gesture must still read as armed for the
            // rest of that frame, no matter where the plugin's Update falls in frame order.
            gesture.Complete(10);
            gesture.Advance(10);
            TestAssert.That(gesture.Armed, "the consuming frame must keep the gesture armed");
            gesture.Advance(11);
            TestAssert.That(!gesture.Armed, "the gesture must release on the following frame");
            TestAssert.That(gesture.Prompt == null, "release must clear the prompt");

            // Re-arming before the release frame cancels the pending release.
            gesture.TryArm("FIRST");
            gesture.Complete(20);
            gesture.TryArm("SECOND");
            gesture.Advance(21);
            TestAssert.That(gesture.Armed && gesture.Prompt == "SECOND", "re-arming must cancel a pending release");

            gesture.Reset();
            TestAssert.That(!gesture.Armed, "Reset must disarm the gesture");
        }

        private static void TestClickVsDrag()
        {
            var gesture = new SupportMapGesture();

            // With no press tracked, a release must not read as a click.
            TestAssert.That(!gesture.ReleasedAsClick(100f, 100f, 8f),
                "a release with no tracked press was read as a click");

            gesture.NotePointerDown(100f, 100f);
            TestAssert.That(gesture.ReleasedAsClick(104f, 103f, 8f),
                "a barely-moved release was not read as a click");
            TestAssert.That(gesture.ReleasedAsClick(108f, 100f, 8f),
                "a release on the slop boundary was rejected");
            TestAssert.That(!gesture.ReleasedAsClick(120f, 100f, 8f),
                "a dragged release was still read as a click");

            // Arming drops any pending press so it cannot leak into the next gesture.
            gesture.NotePointerDown(100f, 100f);
            gesture.TryArm("SUPPORT");
            TestAssert.That(!gesture.ReleasedAsClick(100f, 100f, 8f), "arming kept a stale press");

            gesture.NotePointerDown(100f, 100f);
            gesture.Reset();
            TestAssert.That(!gesture.ReleasedAsClick(100f, 100f, 8f), "reset kept a stale press");
        }

        public static void Run()
        {
            InfoNetworkTests.Run();
            OpsDomainTests.Run();
            CyberNetworkTests.Run();
            OrbitalTests.Run();
            TestAssert.That(SupportEffectPolicy.EmpDuration == 30f,
                "EMP disruption must retain its 30-second operational duration");
            TestAssert.That(SupportEffectPolicy.EmpBurstAltitude >= 20000f &&
                SupportEffectPolicy.EmpBurstAltitude <= 40000f,
                "EMP burst must sit in the 20-40 km gamma-deposition band");
            TestAssert.That(SupportEffectPolicy.RodDamage(0f) == 12000f &&
                SupportEffectPolicy.RodDamage(150f) == 12000f, "rod core must retain its compact lethal plateau");
            TestAssert.That(SupportEffectPolicy.RodDamage(200f) > SupportEffectPolicy.RodDamage(300f) &&
                SupportEffectPolicy.RodDamage(300f) > SupportEffectPolicy.RodDamage(419f), "rod blast must fall off with distance");
            TestAssert.That(SupportEffectPolicy.RodDamage(420f) == 0f &&
                SupportEffectPolicy.RodDamage(421f) == 0f && SupportEffectPolicy.RodDamage(float.NaN) == 0f,
                "rod blast must not damage outside the displayed radius");
            string emp = SupportEffectPolicy.EmpName("BoscaliSummer:Support:Emp:42", 37500.5f);
            TestAssert.That(SupportEffectPolicy.EmpRadius(emp) == 37500.5f, "EMP radius must survive native name replication");
            TestAssert.That(SupportEffectPolicy.EmpRadius("bad:r=NaN") == 12000f &&
                SupportEffectPolicy.EmpRadius("bad:r=Infinity") == 12000f &&
                SupportEffectPolicy.EmpRadius("bad:r=999999") == 12000f, "invalid EMP metadata must use bounded fallback");
            TestMapGesture();
            TestClickVsDrag();

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
