using BoscaliSummer.Modules.HighCommand.Domain;

namespace BoscaliSummer.Tests.Features.HighCommand
{
    /// <summary>Annex 04 H9: which confirmed enemy post the next STRIKE COMMAND POST sortie offers.</summary>
    internal static class StrikePostRulesTests
    {
        private static PostCandidate C(int id, int tier, float distance, bool claimed = false) =>
            new PostCandidate { Id = id, Tier = tier, Distance = distance, Claimed = claimed };

        public static void Run()
        {
            NearestWithinRangeWins();
            TheLimitIsInclusiveAndNanZeroNegativeMeanNoLimit();
            SkippedAndClaimedOfficesAreNeverOffered();
            TiesGoToTheHigherOfficeThenTheLowerId();
            NothingToPickIsMinusOne();
            AnUnknownDistanceIsLastNeverNearest();
            OnlyTheFirstCountCandidatesAreRead();
        }

        private static void NearestWithinRangeWins()
        {
            var list = new[] { C(1, CommandTier.Base, 30000f), C(2, CommandTier.Base, 12000f), C(3, CommandTier.Theater, 45000f) };
            TestAssert.That(StrikePostRules.Pick(list, 3, 60000f, null) == 1, "the nearest post within range is picked");
            TestAssert.That(StrikePostRules.Pick(list, 3, 20000f, null) == 1, "a tighter limit still finds the near one");
            TestAssert.That(StrikePostRules.Pick(list, 3, 10000f, null) == -1, "nothing within range is -1, never the nearest out of range");
        }

        private static void TheLimitIsInclusiveAndNanZeroNegativeMeanNoLimit()
        {
            var list = new[] { C(4, CommandTier.Base, 80000f) };
            TestAssert.That(StrikePostRules.Pick(list, 1, 80000f, null) == 0, "a post exactly at the limit is in range");
            TestAssert.That(StrikePostRules.Pick(list, 1, 79999f, null) == -1, "a post just beyond it is not");
            TestAssert.That(StrikePostRules.Pick(list, 1, float.NaN, null) == 0, "a NaN limit means no limit");
            TestAssert.That(StrikePostRules.Pick(list, 1, 0f, null) == 0, "a zero limit means no limit");
            TestAssert.That(StrikePostRules.Pick(list, 1, -5f, null) == 0, "a negative limit means no limit");
        }

        private static void SkippedAndClaimedOfficesAreNeverOffered()
        {
            var list = new[] { C(1, CommandTier.Base, 5000f, claimed: true), C(2, CommandTier.Base, 9000f), C(3, CommandTier.Base, 15000f) };
            TestAssert.That(StrikePostRules.Pick(list, 3, 60000f, null) == 1, "a claimed office is skipped even when nearest");
            TestAssert.That(StrikePostRules.Pick(list, 3, 60000f, id => id == 2) == 2, "an office the board already issued is skipped");
            TestAssert.That(StrikePostRules.Pick(list, 3, 60000f, id => id >= 2) == -1, "all skipped or claimed is -1");
            int calls = 0;
            StrikePostRules.Pick(list, 3, 60000f, id => { calls++; return false; });
            TestAssert.That(calls >= 1 && calls <= 3, "the skip predicate is asked about candidates only");
        }

        private static void TiesGoToTheHigherOfficeThenTheLowerId()
        {
            var tied = new[] { C(5, CommandTier.Base, 10000f), C(2, CommandTier.Component, 10000f), C(7, CommandTier.Theater, 10000f), C(6, CommandTier.Theater, 10000f) };
            TestAssert.That(tied[StrikePostRules.Pick(tied, 4, 60000f, null)].Id == 6, "a tie goes to the higher office, then the lower id");
            var tiers = new[] { C(1, CommandTier.Base, 10000f), C(9, CommandTier.Component, 10000f) };
            TestAssert.That(tiers[StrikePostRules.Pick(tiers, 2, 60000f, null)].Id == 9, "the component post outranks a base post at equal distance");
            var ids = new[] { C(8, CommandTier.Base, 10000f), C(3, CommandTier.Base, 10000f) };
            TestAssert.That(ids[StrikePostRules.Pick(ids, 2, 60000f, null)].Id == 3, "equal office and distance: the lower id");
            var nearer = new[] { C(1, CommandTier.Theater, 10001f), C(2, CommandTier.Base, 10000f) };
            TestAssert.That(nearer[StrikePostRules.Pick(nearer, 2, 60000f, null)].Id == 2, "distance outranks office");
        }

        private static void NothingToPickIsMinusOne()
        {
            TestAssert.That(StrikePostRules.Pick(new PostCandidate[0], 0, 60000f, null) == -1, "an empty list is -1");
            TestAssert.That(StrikePostRules.Pick(null, 3, 60000f, null) == -1, "no list is -1");
            TestAssert.That(StrikePostRules.Pick(new[] { C(1, 2, 1f) }, -1, 60000f, null) == -1, "a negative count is -1");
        }

        private static void AnUnknownDistanceIsLastNeverNearest()
        {
            var list = new[] { C(1, CommandTier.Theater, float.NaN), C(2, CommandTier.Base, 40000f) };
            TestAssert.That(StrikePostRules.Pick(list, 2, 0f, null) == 1, "with no limit an unknown distance loses to a known one");
            TestAssert.That(StrikePostRules.Pick(list, 2, 50000f, null) == 1, "and is out of any finite limit");
            TestAssert.That(StrikePostRules.Pick(new[] { C(1, 0, float.NaN) }, 1, 0f, null) == 0, "alone, with no limit, it can still be the offer");
            TestAssert.That(StrikePostRules.Pick(new[] { C(1, 0, float.NaN) }, 1, 50000f, null) == -1, "alone, under a limit, it is not");
        }

        private static void OnlyTheFirstCountCandidatesAreRead()
        {
            var list = new[] { C(1, CommandTier.Base, 30000f), C(2, CommandTier.Base, 100f) };
            TestAssert.That(StrikePostRules.Pick(list, 1, 60000f, null) == 0, "the reusable array's stale tail is never read");
            TestAssert.That(StrikePostRules.Pick(list, 99, 60000f, null) == 1, "a count above the array length is clamped");
        }
    }
}
