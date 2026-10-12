using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>The faction bank of finished tied sorties: three buy one wave, at most six are kept (ruling D4, annex 03 C12).</summary>
    internal static class WaveLedgerTests
    {
        public static void Run()
        {
            AFreshLedgerOwesNothing();
            OneAndTwoCreditsAreNotOwed();
            ThreeCreditsAreOwedAndSpendLeavesNone();
            SpendWhenNotOwedDoesNothing();
            TheBankCapsAtSixAndRefusesTheSeventh();
            SpendAtSixLeavesThreeAndStillOwes();
            BoughtOnlyGoesUp();
            ResetEmptiesTheBank();
        }

        private static WaveLedger Filled(int credits)
        {
            var ledger = new WaveLedger();
            for (int i = 0; i < credits; i++) ledger.Credit();
            return ledger;
        }

        private static void AFreshLedgerOwesNothing()
        {
            var ledger = new WaveLedger();
            TestAssert.That(WaveLedger.CreditsPerWave == 3 && WaveLedger.MaximumCredits == 6, "three credits buy a wave and six is the bank");
            TestAssert.That(ledger.Credits == 0 && ledger.Bought == 0 && !ledger.Owed, "a new ledger holds nothing");
        }

        private static void OneAndTwoCreditsAreNotOwed()
        {
            var ledger = new WaveLedger();
            TestAssert.That(ledger.Credit() && ledger.Credits == 1 && !ledger.Owed, "one credit is not a wave");
            TestAssert.That(ledger.Credit() && ledger.Credits == 2 && !ledger.Owed, "two credits are not a wave");
        }

        private static void ThreeCreditsAreOwedAndSpendLeavesNone()
        {
            WaveLedger ledger = Filled(3);
            TestAssert.That(ledger.Credits == 3 && ledger.Owed, "the third credit owes a wave");
            ledger.Spend();
            TestAssert.That(ledger.Credits == 0 && ledger.Bought == 1 && !ledger.Owed, "a bought wave takes all three credits and counts once");
        }

        private static void SpendWhenNotOwedDoesNothing()
        {
            foreach (int credits in new[] { 0, 1, 2 })
            {
                WaveLedger ledger = Filled(credits);
                ledger.Spend();
                TestAssert.That(ledger.Credits == credits && ledger.Bought == 0, "a spend below three credits changes nothing: " + credits);
            }
        }

        private static void TheBankCapsAtSixAndRefusesTheSeventh()
        {
            WaveLedger ledger = Filled(6);
            TestAssert.That(ledger.Credits == 6, "six credits are kept while the wave is held");
            TestAssert.That(!ledger.Credit() && ledger.Credits == 6, "the seventh credit is refused and the bank stays at six");
            TestAssert.That(ledger.Credit() == false && ledger.Bought == 0, "a refused credit buys nothing");
        }

        private static void SpendAtSixLeavesThreeAndStillOwes()
        {
            WaveLedger ledger = Filled(6);
            ledger.Spend();
            TestAssert.That(ledger.Credits == 3 && ledger.Bought == 1 && ledger.Owed, "a spend from a full bank leaves three, which still owe the next wave");
            TestAssert.That(ledger.Credit() && ledger.Credits == 4, "room opens as credits are spent");
            ledger.Spend();
            ledger.Spend();
            TestAssert.That(ledger.Credits == 1 && ledger.Bought == 2, "the second spend leaves one; the third does nothing");
        }

        private static void BoughtOnlyGoesUp()
        {
            WaveLedger ledger = Filled(3);
            ledger.Spend();
            for (int i = 0; i < 3; i++) ledger.Credit();
            ledger.Spend();
            TestAssert.That(ledger.Bought == 2, "bought counts every wave of the mission");
        }

        private static void ResetEmptiesTheBank()
        {
            WaveLedger ledger = Filled(3);
            ledger.Spend();
            ledger.Credit();
            ledger.Reset();
            TestAssert.That(ledger.Credits == 0 && ledger.Bought == 0 && !ledger.Owed, "a scene or mission change empties the ledger and the bought count");
        }
    }
}
