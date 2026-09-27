using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Orbital;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>The multiplayer rules for what a faction shares; each one gives way for a faction of one.</summary>
    internal static class TeamRulesTests
    {
        public static void Run()
        {
            TestPriceScale();
            TestTeamCooldowns();
            TestJobPools();
            TestGuard();
            TestRefundShares();
            TestRefundRecipient();
            TestDeorbitGuard();
            TestStationPayers();
        }

        private static void TestPriceScale()
        {
            TestAssert.That(TeamRules.SizeScale(1, 0.1f, 1.75f) == 1f, "a lone pilot pays the base price");
            TestAssert.That(TeamRules.SizeScale(0, 0.1f, 1.75f) == 1f, "an empty faction count must not discount");
            TestAssert.That(Near(TeamRules.SizeScale(4, 0.1f, 1.75f), 1.3f), "four pilots pay 1 + 3 x 0.1");
            TestAssert.That(Near(TeamRules.SizeScale(8, 0.1f, 1.75f), 1.7f), "eight pilots pay 1 + 7 x 0.1");
            TestAssert.That(Near(TeamRules.SizeScale(16, 0.1f, 1.75f), 1.75f), "the scale stops at its cap");
            TestAssert.That(TeamRules.SizeScale(8, 0f, 1.75f) == 1f, "a zero step turns scaling off");
            TestAssert.That(TeamRules.SizeScale(8, float.NaN, 1.75f) == 1f, "a bad step must not price anything");
            TestAssert.That(TeamRules.SizeScale(8, 0.1f, float.NaN) == 1f && TeamRules.SizeScale(8, 0.1f, 0.5f) == 1f,
                "a cap below 1 must never discount");
        }

        private static void TestTeamCooldowns()
        {
            var ledger = new TeamLedger();
            TestAssert.That(ledger.Remaining(TeamGate.FlareBarrage, 100f, 20f) == 0f, "an unused gate is open");
            ledger.Accept(TeamGate.FlareBarrage, 100f);
            TestAssert.That(Near(ledger.Remaining(TeamGate.FlareBarrage, 108f, 20f), 12f), "the gate counts down from the accept");
            TestAssert.That(ledger.Remaining(TeamGate.Fortify, 108f, 20f) == 0f, "gates are independent");
            TestAssert.That(ledger.Remaining(TeamGate.FlareBarrage, 121f, 20f) == 0f, "an expired gate reopens");
            TestAssert.That(ledger.Remaining(TeamGate.FlareBarrage, 108f, 0f) == 0f, "zero seconds turns the gate off");
            TestAssert.That(Near(ledger.Remaining(TeamGate.FlareBarrage, 108f, 60f), 52f),
                "a changed setting applies to a running gate");
            ledger.Accept(TeamGate.Relocate, 10f);
            ledger.Clear();
            TestAssert.That(ledger.Remaining(TeamGate.Relocate, 11f, 60f) == 0f, "a scene reset opens every gate");
            TestAssert.That(TeamGates.Count == 4, "the snapshot carries four gates");
        }

        private static void TestJobPools()
        {
            var blue = new TeamLedger();
            var red = new TeamLedger();
            TestAssert.That(blue.TryReserve(0, 2) && blue.TryReserve(0, 2), "a faction holds two strike jobs");
            TestAssert.That(!blue.TryReserve(0, 2), "a third strike job waits");
            TestAssert.That(red.TryReserve(0, 2), "one faction's jobs never starve the other");
            TestAssert.That(blue.TryReserve(1, 2), "the CYBER pool is separate from the strike pool");
            blue.Release(0);
            TestAssert.That(blue.TryReserve(0, 2), "a released job frees its slot");
            blue.Release(1);
            blue.Release(1);
            TestAssert.That(blue.TryReserve(1, 1), "a release never drives the pool negative");
            TestAssert.That(!blue.TryReserve(7, 2), "an unknown pool reserves nothing");
        }

        private static void TestGuard()
        {
            TestAssert.That(TeamRules.MayTouch(1, 1, true, 4, true), "the owner may touch its own asset");
            TestAssert.That(!TeamRules.MayTouch(2, 1, true, 4, true), "a teammate may not touch a present owner's asset");
            TestAssert.That(TeamRules.MayTouch(2, 1, false, 4, true), "an asset whose owner left is anyone's");
            TestAssert.That(TeamRules.MayTouch(2, 1, true, 1, true), "a lone pilot is never guarded out");
            TestAssert.That(TeamRules.MayTouch(2, 0, true, 4, true), "an asset nobody owns is anyone's");
            TestAssert.That(TeamRules.MayTouch(2, 1, true, 4, false), "the guard can be switched off");
        }

        private static void TestRefundShares()
        {
            ulong[] payers = { 7, 0, 9, 7, 7 };
            float[] paid = { 1000f, 500f, 400f, 250f, -20f };
            var into = new ulong[5];
            var amounts = new float[5];
            int count = TeamRules.Shares(payers, paid, payers.Length, into, amounts);
            TestAssert.That(count == 2, "each payer appears once; an unknown payer holds no share");
            TestAssert.That(into[0] == 7 && Near(amounts[0], 1250f), "one payer's cells add up");
            TestAssert.That(into[1] == 9 && Near(amounts[1], 400f), "another payer keeps its own share");
            TestAssert.That(TeamRules.Shares(payers, paid, 3, new ulong[1], new float[1]) == 1,
                "a full buffer drops further payers rather than overrun");
        }

        private static void TestRefundRecipient()
        {
            TestAssert.That(TeamRules.RefundRecipient(2, 7, true) == 7, "a present payer is refunded");
            TestAssert.That(TeamRules.RefundRecipient(7, 7, true) == 7, "the requester keeps its own refund");
            TestAssert.That(TeamRules.RefundRecipient(2, 7, false) == 2, "an absent payer's share falls to the requester");
            TestAssert.That(TeamRules.RefundRecipient(2, 0, false) == 2, "an unknown payer refunds the requester");
        }

        private static void TestDeorbitGuard()
        {
            ulong[] payers = { 7, 9, 11 };
            float[] amounts = { 1200f, 1200f, 300f };
            bool[] present = { true, true, true };
            TestAssert.That(TeamRules.MayDeorbit(7, payers, amounts, present, 3, 4, true), "a largest payer may deorbit");
            TestAssert.That(TeamRules.MayDeorbit(9, payers, amounts, present, 3, 4, true), "a tie shares the largest share");
            TestAssert.That(!TeamRules.MayDeorbit(11, payers, amounts, present, 3, 4, true), "a small payer may not deorbit");
            TestAssert.That(!TeamRules.MayDeorbit(42, payers, amounts, present, 3, 4, true), "a pilot who paid nothing may not deorbit");
            bool[] gone = { false, false, true };
            TestAssert.That(TeamRules.MayDeorbit(42, payers, amounts, gone, 3, 4, true),
                "once every largest payer has left anyone may deorbit");
            TestAssert.That(TeamRules.MayDeorbit(42, payers, amounts, present, 3, 1, true), "a lone pilot may deorbit");
            TestAssert.That(TeamRules.MayDeorbit(42, payers, amounts, present, 0, 4, true), "a station nobody paid for is anyone's");
            TestAssert.That(TeamRules.MayDeorbit(42, payers, amounts, present, 3, 4, false), "the guard can be switched off");
        }

        /// <summary>The station remembers who paid for each docked module, so a refund can go back to them.</summary>
        private static void TestStationPayers()
        {
            var platform = new OrbitalPlatform();
            platform.TryLaunch(ModuleKind.Core, OrbitalPlatform.CoreCell, OrbitRegimes.Standard, 1, 0.0, 1300f, 45.0, 20.0, 7);
            TestAssert.That(platform.Payer(OrbitalPlatform.CoreCell) == 7 && platform.Paid(OrbitalPlatform.CoreCell) == 1300f,
                "the core remembers its payer and price");
            double now = 100.0;
            platform.TryLaunch(ModuleKind.Solar, 6, 0, 0, now, 250f, 45.0, 20.0, 9);
            TestAssert.That(platform.Payer(6) == 0, "a module in flight is not docked yet");
            platform.Tick(now + 20.0, 0.01f, true);
            TestAssert.That(platform.Payer(6) == 9 && platform.Paid(6) == 250f, "a docked module keeps the payer of its launch");
            platform.TryResupply(now + 21.0, 20.0, 350f, 7UL);
            platform.Tick(now + 41.0, 0.01f, true);
            TestAssert.That(platform.Payer(OrbitalPlatform.CoreCell) == 7, "a cargo docking never changes a payer");
            TestAssert.That(platform.TryJettison(6, out _, out float refund) == PlacementFailure.None && refund == 250f &&
                            platform.Payer(6) == 0, "a jettisoned module forgets its payer");
            platform.TryJettison(OrbitalPlatform.CoreCell, out _, out _);
            TestAssert.That(platform.Payer(OrbitalPlatform.CoreCell) == 0 && platform.Payer(-1) == 0,
                "a deorbit forgets every payer");
        }

        private static bool Near(float a, float b) => a > b - 0.001f && a < b + 0.001f;
    }
}
