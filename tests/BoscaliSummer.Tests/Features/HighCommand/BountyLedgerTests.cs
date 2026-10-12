using BoscaliSummer.Modules.HighCommand.Domain;

namespace BoscaliSummer.Tests.Features.HighCommand
{
    /// <summary>Annex 04 H2 and D5: a command-post bounty is paid once per office per killer faction per mission, for a real death, to a human faction.</summary>
    internal static class BountyLedgerTests
    {
        public static void Run()
        {
            FirstClaimWinsPerKillerAndOffice();
            OfficeBoundsAndNegativeKillerIds();
            AFullLedgerPaysNothing();
            ClearForgetsEverything();
            SettlePaysOncePerOfficePerKiller();
            SettleRulesOutrankTheLedgerInOrder();
            AnAiKillerOrAnAbsentCommanderConsumesNoClaim();
        }

        private static void FirstClaimWinsPerKillerAndOffice()
        {
            var ledger = new BountyLedger();
            TestAssert.That(!ledger.Claimed(7, 3), "nothing is claimed before the first kill");
            TestAssert.That(ledger.TryClaim(7, 3), "the first claim of (killer, office) is paid");
            TestAssert.That(ledger.Claimed(7, 3), "the claim is remembered");
            TestAssert.That(!ledger.TryClaim(7, 3), "the same killer and office is paid once");
            TestAssert.That(ledger.TryClaim(8, 3), "another killer faction may claim the same office");
            TestAssert.That(ledger.TryClaim(7, 4), "the same killer may claim another office");
            TestAssert.That(!ledger.Claimed(9, 3) && !ledger.Claimed(7, 5), "untouched pairs stay unclaimed");
        }

        private static void OfficeBoundsAndNegativeKillerIds()
        {
            var ledger = new BountyLedger();
            TestAssert.That(!ledger.TryClaim(1, -1) && !ledger.TryClaim(1, 64) && !ledger.TryClaim(1, 255) && !ledger.TryClaim(1, 256),
                "an office outside 0..63 is never paid (256 must not wrap onto office 0)");
            TestAssert.That(!ledger.Claimed(1, -1) && !ledger.Claimed(1, 64), "a bad office is never claimed");
            TestAssert.That(ledger.TryClaim(1, 0) && ledger.TryClaim(1, 63), "offices 0 and 63 are the edges of the range");

            TestAssert.That(ledger.TryClaim(-5, 2), "a negative killer id (an instance id) works");
            TestAssert.That(!ledger.TryClaim(-5, 2), "and is remembered");
            TestAssert.That(ledger.TryClaim(5, 2) && ledger.TryClaim(-1, 2) && ledger.TryClaim(0, 2),
                "-5, 5, -1 and 0 are four different killers");
            TestAssert.That(ledger.TryClaim(int.MinValue, 2) && ledger.TryClaim(int.MaxValue, 2), "the id extremes work");
            TestAssert.That(!ledger.TryClaim(int.MinValue, 2), "and are remembered");
        }

        private static void AFullLedgerPaysNothing()
        {
            var ledger = new BountyLedger();
            for (int killer = 0; killer < 8; killer++)
                for (int office = 0; office < 64; office++)
                    TestAssert.That(ledger.TryClaim(killer, office), "claim " + (killer * 64 + office + 1) + " of " + BountyLedger.MaxClaims + " is accepted");
            TestAssert.That(!ledger.TryClaim(8, 0), "claim 513 is refused: a full ledger fails closed");
            TestAssert.That(!ledger.Claimed(8, 0), "a refused claim is not remembered");
            TestAssert.That(!ledger.TryClaim(0, 0) && ledger.Claimed(0, 0) && ledger.Claimed(7, 63), "earlier claims stay claimed");
            ledger.Clear();
            TestAssert.That(ledger.TryClaim(8, 0), "a cleared ledger accepts claims again");
        }

        private static void ClearForgetsEverything()
        {
            var ledger = new BountyLedger();
            ledger.TryClaim(3, 3);
            ledger.Clear();
            TestAssert.That(!ledger.Claimed(3, 3), "Clear forgets the claim");
            TestAssert.That(ledger.TryClaim(3, 3), "and the office can be claimed again (a new mission)");
        }

        private static BountyVerdict Settle(BountyLedger ledger, int killer = 11, int office = 4, bool economyOn = true,
            bool humanKiller = true, bool postFlipped = false, bool commanderAway = false) =>
            ledger.Settle(killer, office, economyOn, humanKiller, postFlipped, commanderAway);

        private static void SettlePaysOncePerOfficePerKiller()
        {
            var ledger = new BountyLedger();
            TestAssert.That(Settle(ledger) == BountyVerdict.Pay, "the first human kill of an office pays");
            TestAssert.That(Settle(ledger) == BountyVerdict.Claimed, "the rebuilt post killed again by the same faction pays nothing");
            TestAssert.That(Settle(ledger, killer: 12) == BountyVerdict.Pay, "another human faction is paid for the same office");
            TestAssert.That(Settle(ledger, office: 5) == BountyVerdict.Pay, "the same faction is paid for another office");
            TestAssert.That(Settle(ledger, office: 99) != BountyVerdict.Pay, "an office outside the tree never pays");
        }

        private static void SettleRulesOutrankTheLedgerInOrder()
        {
            var ledger = new BountyLedger();
            ledger.TryClaim(11, 4);
            // Every rule below outranks "already claimed": the staff must say the true reason.
            TestAssert.That(Settle(ledger, economyOn: false) == BountyVerdict.Off, "the economy switch outranks everything");
            TestAssert.That(Settle(ledger, economyOn: false, humanKiller: false) == BountyVerdict.Off, "off outranks a non-human killer");
            TestAssert.That(Settle(ledger, humanKiller: false) == BountyVerdict.NotHuman, "a non-human killer outranks the ledger");
            TestAssert.That(Settle(ledger, humanKiller: false, commanderAway: true) == BountyVerdict.NotHuman, "a non-human killer outranks an absent commander");
            TestAssert.That(Settle(ledger, commanderAway: true) == BountyVerdict.Afield, "an absent commander outranks the ledger");
            TestAssert.That(Settle(ledger, commanderAway: true, postFlipped: true) == BountyVerdict.Afield, "afield outranks flipped (the line still tells the killer)");
            TestAssert.That(Settle(ledger, postFlipped: true) == BountyVerdict.Flipped, "a flipped post outranks the ledger");
            TestAssert.That(Settle(ledger) == BountyVerdict.Claimed, "with every other rule passing, the ledger decides");
        }

        private static void AnAiKillerOrAnAbsentCommanderConsumesNoClaim()
        {
            var ledger = new BountyLedger();
            Settle(ledger, humanKiller: false);
            TestAssert.That(!ledger.Claimed(11, 4), "an AI killer consumes no ledger entry");
            Settle(ledger, commanderAway: true);
            TestAssert.That(!ledger.Claimed(11, 4), "an absent commander consumes no ledger entry");
            Settle(ledger, postFlipped: true);
            TestAssert.That(!ledger.Claimed(11, 4), "a flipped post consumes no ledger entry");
            Settle(ledger, economyOn: false);
            TestAssert.That(!ledger.Claimed(11, 4), "a switched-off economy consumes no ledger entry");
            TestAssert.That(Settle(ledger) == BountyVerdict.Pay, "so a human faction killing the commander later is still paid once");
            TestAssert.That(ledger.Claimed(11, 4), "and that payment is the one claim");
        }
    }
}
