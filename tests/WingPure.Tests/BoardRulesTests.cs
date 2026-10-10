using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Tests;

namespace WingPure.Tests
{
    /// <summary>The WING page's state words (HUD v8) from a snapshot entry's facts.</summary>
    internal static class BoardRulesTests
    {
        private static WingState S(bool defending = false, bool damaged = false, bool recovering = false, bool refit = false, bool engaged = false, bool dry = false,
            bool grounded = false, bool settled = false, bool behind = false, float err = 5f) =>
            BoardRules.State(defending, damaged, recovering, refit, engaged, dry, grounded, settled, behind, err);

        public static void Run()
        {
            TestAssert.That(S() == WingState.Form, "A member on station reads FORM");
            TestAssert.That(S(err: 61f) == WingState.Join && S(behind: true) == WingState.Join, "Out of its slot or falling behind reads JOIN");
            TestAssert.That(S(err: 60f) == WingState.Form, "Sixty metres is still in the slot");
            TestAssert.That(S(engaged: true, err: 900f) == WingState.Engage, "A fighting member is ENGAGE however far it is");
            TestAssert.That(S(dry: true) == WingState.Winch, "Out of weapons in formation reads WINCH");
            TestAssert.That(S(recovering: true, dry: true) == WingState.Rtb, "A dry member going home reads RTB");
            TestAssert.That(S(recovering: true, refit: true) == WingState.Refit, "Going home to rearm reads REFIT");
            TestAssert.That(S(damaged: true, engaged: true) == WingState.Damaged, "A hit outranks a fight");
            TestAssert.That(S(defending: true, damaged: true) == WingState.Defend, "A missile on it outranks a hit");
            TestAssert.That(S(grounded: true) == WingState.Ground && S(settled: true) == WingState.Land, "Ground and landing");
            TestAssert.That(S(grounded: true, err: 5000f) == WingState.Ground, "A member on the ground is never out of slot");

            TestAssert.Eq(BoardRules.AckText(true, "rejoin", null), "REJOIN - WILCO", "A WILCO ack");
            TestAssert.Eq(BoardRules.AckText(true, null, null), "ORDER - WILCO", "A WILCO ack with no words");
            TestAssert.Eq(BoardRules.AckText(false, null, "no fuel"), "NO FUEL - UNABLE", "A refusal says why");
            TestAssert.Eq(BoardRules.AckText(false, "x", null), "REFUSED - UNABLE", "A refusal with no reason");
            TestAssert.Near(BoardRules.Fraction(255), 1f, "A full byte is 1");
            TestAssert.Near(BoardRules.Fraction(0), 0f, "A zero byte is 0");
        }
    }
}
