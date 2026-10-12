using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>When a reinforcement wave may be bought and which group it is (rulings R4, D3, D4; annex 03 C12).</summary>
    internal static class WavePolicyTests
    {
        // Every gate passing, price 100, pool 150. A test breaks one thing at a time.
        private struct Spec
        {
            public bool SwitchedOff, Sorties, Mission, Operation, Depot, Group;
            public float Cooldown, Pool, Cost, Factor;

            public static Spec Ready() => new Spec
            {
                Sorties = true, Mission = true, Operation = true, Depot = true, Group = true,
                Cooldown = 0f, Pool = 150f, Cost = 100f, Factor = 1f,
            };

            public WaveInputs Inputs() =>
                new WaveInputs(SwitchedOff, Sorties, Mission, Operation, Depot, Group, Cooldown, Pool, Cost, Factor);

            public WaveBlock Block() => WavePolicy.Evaluate(Inputs());
        }

        public static void Run()
        {
            AReadyWaveHasNoBlock();
            EachGateAloneBlocksWithItsOwnReason();
            EarlierGatesWinOverEveryLaterOne();
            PoolEqualToCostIsReady();
            UnknownNumbersAreNeverReady();
            ANegativeCooldownReadsAsElapsed();
            TheAiFactorDoublesTheRequiredPool();
            ABadFactorNeverMakesTheWaveCheaper();
            ChooseTakesTheFirstReadyGroupFromTheRotationStart();
            ChooseFallsBackToTheFirstEligibleSoThePriceShows();
            ChooseHandlesNothingEligibleAndBadArguments();
            ChooseAndEvaluateAgreeOnReady();
            AiTimerIsDueOnlyWhenSwitchedOnLiveAndPastTheTime();
            TwoCreditsBuyNothing();
            ThreeCreditsBuyWhenThePoolCoversThePrice();
            AShortPoolHoldsTheWaveAndKeepsTheCredits();
            TheBankSurvivesARedirectAndTheEndOfAnOperation();
        }

        private static void AReadyWaveHasNoBlock()
        {
            TestAssert.That(Spec.Ready().Block() == WaveBlock.None, "every gate open gives None");
            TestAssert.That(WavePolicy.Evaluate(default) != WaveBlock.None, "a default input never reads as ready");
        }

        private static void EachGateAloneBlocksWithItsOwnReason()
        {
            Spec s = Spec.Ready(); s.SwitchedOff = true;
            TestAssert.That(s.Block() == WaveBlock.SwitchedOff, "switched off");
            s = Spec.Ready(); s.Sorties = false;
            TestAssert.That(s.Block() == WaveBlock.NoSorties, "sorties unavailable");
            s = Spec.Ready(); s.Mission = false;
            TestAssert.That(s.Block() == WaveBlock.MissionForbids, "mission forbids");
            s = Spec.Ready(); s.Operation = false;
            TestAssert.That(s.Block() == WaveBlock.NoOperation, "no operation");
            s = Spec.Ready(); s.Depot = false;
            TestAssert.That(s.Block() == WaveBlock.NoDepot, "no depot");
            s = Spec.Ready(); s.Group = false;
            TestAssert.That(s.Block() == WaveBlock.NoGroup, "no group");
            s = Spec.Ready(); s.Cooldown = 12.5f;
            TestAssert.That(s.Block() == WaveBlock.Cooling, "cooling");
            s = Spec.Ready(); s.Pool = 99.9f;
            TestAssert.That(s.Block() == WaveBlock.PoolShort, "pool short");
        }

        private static void EarlierGatesWinOverEveryLaterOne()
        {
            Spec s = new Spec { SwitchedOff = true, Sorties = false, Mission = false, Operation = false, Depot = false, Group = false,
                Cooldown = 9f, Pool = 0f, Cost = 100f, Factor = 1f };
            WaveBlock[] order =
            {
                WaveBlock.SwitchedOff, WaveBlock.NoSorties, WaveBlock.MissionForbids, WaveBlock.NoOperation,
                WaveBlock.NoDepot, WaveBlock.NoGroup, WaveBlock.Cooling, WaveBlock.PoolShort, WaveBlock.None,
            };
            TestAssert.That(s.Block() == order[0], "all failing gives the first reason");
            s.SwitchedOff = false; TestAssert.That(s.Block() == order[1], "then no sorties");
            s.Sorties = true; TestAssert.That(s.Block() == order[2], "then the mission");
            s.Mission = true; TestAssert.That(s.Block() == order[3], "then no operation");
            s.Operation = true; TestAssert.That(s.Block() == order[4], "then no depot");
            s.Depot = true; TestAssert.That(s.Block() == order[5], "then no group");
            s.Group = true; TestAssert.That(s.Block() == order[6], "then cooling");
            s.Cooldown = 0f; TestAssert.That(s.Block() == order[7], "then the pool");
            s.Pool = 100f; TestAssert.That(s.Block() == order[8], "then ready");
        }

        private static void PoolEqualToCostIsReady()
        {
            Spec s = Spec.Ready(); s.Pool = 100f;
            TestAssert.That(s.Block() == WaveBlock.None, "a pool equal to the price is enough");
            s.Pool = 99.99f;
            TestAssert.That(s.Block() == WaveBlock.PoolShort, "a hair under the price is short");
            s.Cost = 0f; s.Pool = 0f;
            TestAssert.That(s.Block() == WaveBlock.None, "a free wave on an empty pool is ready");
        }

        private static void UnknownNumbersAreNeverReady()
        {
            Spec s = Spec.Ready(); s.Pool = float.NaN;
            TestAssert.That(s.Block() == WaveBlock.PoolShort, "an unreadable pool is never enough");
            s = Spec.Ready(); s.Cost = float.NaN;
            TestAssert.That(s.Block() == WaveBlock.PoolShort, "an unreadable price is never affordable");
            s = Spec.Ready(); s.Cost = -1f;
            TestAssert.That(s.Block() == WaveBlock.PoolShort, "a negative price (unknown) is never affordable");
            s = Spec.Ready(); s.Cost = float.PositiveInfinity;
            TestAssert.That(s.Block() == WaveBlock.PoolShort, "an infinite price is never affordable");
            s = Spec.Ready(); s.Cooldown = float.NaN;
            TestAssert.That(s.Block() == WaveBlock.Cooling, "an unreadable cooldown counts as cooling");
            s = Spec.Ready(); s.Cooldown = 0.001f;
            TestAssert.That(s.Block() == WaveBlock.Cooling, "any cooldown left blocks");
        }

        private static void ANegativeCooldownReadsAsElapsed()
        {
            Spec s = Spec.Ready(); s.Cooldown = -3f;
            TestAssert.That(s.Block() == WaveBlock.None, "a negative cooldown reads as elapsed");
        }

        private static void TheAiFactorDoublesTheRequiredPool()
        {
            Spec s = Spec.Ready(); s.Factor = 2f; s.Pool = 199.9f;
            TestAssert.That(s.Block() == WaveBlock.PoolShort, "an AI-only faction needs twice the price");
            s.Pool = 200f;
            TestAssert.That(s.Block() == WaveBlock.None, "exactly twice the price is enough");
        }

        private static void ABadFactorNeverMakesTheWaveCheaper()
        {
            foreach (float factor in new[] { 0f, -2f, 0.5f, float.NaN })
            {
                Spec s = Spec.Ready(); s.Factor = factor; s.Pool = 99f;
                TestAssert.That(s.Block() == WaveBlock.PoolShort, "a factor below one is read as one, still short at 99: " + factor);
                s.Pool = 100f;
                TestAssert.That(s.Block() == WaveBlock.None, "and ready at the price: " + factor);
            }
        }

        private static GroupRead[] Groups() => new[]
        {
            new GroupRead(true, 30f, 0f),     // 0 cheap, ready
            new GroupRead(true, 80f, 0f),     // 1 dear, ready
            new GroupRead(false, 10f, 0f),    // 2 not a ground group
            new GroupRead(true, 20f, 10f),    // 3 cooling
        };

        private static int Pick(GroupRead[] groups, int count, int start, float pool, float factor)
        {
            return WavePolicy.Choose(groups, count, start, pool, factor, out int index) ? index : -1;
        }

        private static void ChooseTakesTheFirstReadyGroupFromTheRotationStart()
        {
            GroupRead[] g = Groups();
            TestAssert.Eq(Pick(g, 4, 0, 100f, 1f), 0, "start at the cheap ready group");
            TestAssert.Eq(Pick(g, 4, 1, 100f, 1f), 1, "rotation start at the dear group buys it when the pool covers it");
            TestAssert.Eq(Pick(g, 4, 2, 100f, 1f), 0, "an ineligible and a cooling group are skipped, wrapping to the first");
            TestAssert.Eq(Pick(g, 4, 3, 100f, 1f), 0, "a cooling start is skipped");
            TestAssert.Eq(Pick(g, 4, 1, 50f, 1f), 0, "an unaffordable group is skipped for an affordable one");
            TestAssert.Eq(Pick(g, 4, 1, 100f, 2f), 0, "the AI factor makes the dear group unaffordable at 100");
            TestAssert.Eq(Pick(g, 4, 7, 100f, 1f), 0, "a start past the count wraps (7 mod 4 = 3, cooling, then 0)");
            TestAssert.Eq(Pick(g, 4, -1, 100f, 1f), 0, "a negative start wraps from the end");
        }

        private static void ChooseFallsBackToTheFirstEligibleSoThePriceShows()
        {
            GroupRead[] g = Groups();
            TestAssert.Eq(Pick(g, 4, 1, 10f, 1f), 1, "nothing affordable: the first eligible from the start shows its price");
            TestAssert.Eq(Pick(g, 4, 2, 10f, 1f), 3, "the fallback skips an ineligible group but may show a cooling one");
            var allCooling = new[] { new GroupRead(true, 30f, 5f), new GroupRead(true, 40f, float.NaN) };
            TestAssert.Eq(Pick(allCooling, 2, 1, 1000f, 1f), 1, "everything cooling still shows a price, from the start");
            var unknownPrice = new[] { new GroupRead(true, float.NaN, 0f), new GroupRead(true, 30f, 0f) };
            TestAssert.Eq(Pick(unknownPrice, 2, 0, 1000f, 1f), 1, "an unreadable price is never the affordable pick");
            TestAssert.Eq(Pick(unknownPrice, 2, 0, float.NaN, 1f), 0, "an unreadable pool picks nothing affordable and falls back to the start");
        }

        private static void ChooseHandlesNothingEligibleAndBadArguments()
        {
            var none = new[] { new GroupRead(false, 1f, 0f), new GroupRead(false, 2f, 0f) };
            TestAssert.That(!WavePolicy.Choose(none, 2, 0, 100f, 1f, out int index) && index == -1, "no eligible group gives false and -1");
            TestAssert.That(!WavePolicy.Choose(null, 2, 0, 100f, 1f, out index) && index == -1, "no array gives false");
            TestAssert.That(!WavePolicy.Choose(Groups(), 0, 0, 100f, 1f, out index) && index == -1, "a zero count gives false");
            TestAssert.That(!WavePolicy.Choose(Groups(), -3, 0, 100f, 1f, out index) && index == -1, "a negative count gives false");
            TestAssert.Eq(Pick(Groups(), 99, 0, 100f, 1f), 0, "a count past the array is clamped to it");
            TestAssert.Eq(Pick(Groups(), 1, 0, 100f, 1f), 0, "a count of one looks at one group");
            TestAssert.Eq(Pick(Groups(), 3, 2, 10f, 1f), 0, "a group past the count is never picked (count 4 would show group 3)");
        }

        private static void ChooseAndEvaluateAgreeOnReady()
        {
            GroupRead[] g = Groups();
            foreach (float pool in new[] { 0f, 29.9f, 30f, 79.9f, 80f, 500f, float.NaN })
                foreach (float factor in new[] { 1f, 2f })
                    for (int start = 0; start < 4; start++)
                    {
                        if (!WavePolicy.Choose(g, 4, start, pool, factor, out int index)) continue;
                        GroupRead pick = g[index];
                        var inputs = new WaveInputs(false, true, true, true, true, true, pick.Cooldown, pool, pick.Cost, factor);
                        bool chosenAsReady = pick.Cooldown <= 0f && pool >= pick.Cost * factor;
                        TestAssert.That((WavePolicy.Evaluate(inputs) == WaveBlock.None) == chosenAsReady,
                            "Choose and Evaluate read the same group the same way at pool " + pool + " factor " + factor + " start " + start);
                    }
            // When any group is ready the pick is ready: the panel never shows a price it could have avoided.
            TestAssert.That(WavePolicy.Choose(g, 4, 3, 100f, 1f, out int ready) && g[ready].Cooldown <= 0f, "a ready group exists, so the pick is ready");
        }

        private static void AiTimerIsDueOnlyWhenSwitchedOnLiveAndPastTheTime()
        {
            TestAssert.That(!WavePolicy.AiTimerDue(false, true, 500f, 100f), "the switch off: never due");
            TestAssert.That(!WavePolicy.AiTimerDue(true, false, 500f, 100f), "no live operation: never due");
            TestAssert.That(!WavePolicy.AiTimerDue(true, true, 99.9f, 100f), "before the time: not due");
            TestAssert.That(WavePolicy.AiTimerDue(true, true, 100f, 100f), "exactly at the time: due");
            TestAssert.That(WavePolicy.AiTimerDue(true, true, 250f, 100f), "past the time: due");
            TestAssert.That(!WavePolicy.AiTimerDue(true, true, float.NaN, 100f) && !WavePolicy.AiTimerDue(true, true, 100f, float.NaN),
                "an unreadable clock is never due");
        }

        // The desk's rule in two lines: try only when three credits are banked, spend the credits only after a successful buy.
        private static bool DeskTry(WaveLedger ledger, Spec spec)
        {
            if (!ledger.Owed || spec.Block() != WaveBlock.None) return false;
            ledger.Spend();
            return true;
        }

        private static void TwoCreditsBuyNothing()
        {
            var ledger = new WaveLedger();
            Spec rich = Spec.Ready(); rich.Pool = 1000000f;
            ledger.Credit(); ledger.Credit();
            TestAssert.That(!DeskTry(ledger, rich) && ledger.Credits == 2 && ledger.Bought == 0, "two credits buy nothing, however rich the pool");
        }

        private static void ThreeCreditsBuyWhenThePoolCoversThePrice()
        {
            var ledger = new WaveLedger();
            Spec s = Spec.Ready(); s.Pool = 100f;
            for (int i = 0; i < 3; i++) ledger.Credit();
            TestAssert.That(DeskTry(ledger, s) && ledger.Credits == 0 && ledger.Bought == 1,
                "three credits buy a wave when the pool equals the price, and the credits are spent");
        }

        private static void AShortPoolHoldsTheWaveAndKeepsTheCredits()
        {
            var ledger = new WaveLedger();
            Spec s = Spec.Ready(); s.Pool = 60f;
            for (int i = 0; i < 3; i++) ledger.Credit();
            TestAssert.That(s.Block() == WaveBlock.PoolShort && !DeskTry(ledger, s) && ledger.Credits == 3 && ledger.Bought == 0 && ledger.Owed,
                "a short pool holds the wave: nothing bought, the credits kept, the wave still owed");
            TestAssert.That(!DeskTry(ledger, s) && ledger.Credits == 3, "and it is held again on the next review");
            ledger.Credit();
            TestAssert.That(ledger.Credits == 4, "a finished sortie while held still banks its credit");
            s.Pool = 150f;
            TestAssert.That(DeskTry(ledger, s) && ledger.Credits == 1 && ledger.Bought == 1,
                "when the pool recovers the held wave is bought and the credit beyond it stays");
        }

        private static void TheBankSurvivesARedirectAndTheEndOfAnOperation()
        {
            var ledger = new WaveLedger();
            Spec s = Spec.Ready();
            ledger.Credit(); ledger.Credit();
            // A redirect changes the operation id and the end of an operation drops the live flag; neither touches the bank.
            s.Operation = false;
            TestAssert.That(s.Block() == WaveBlock.NoOperation && ledger.Credits == 2, "with no operation the wave is blocked and the bank stays");
            s.Operation = true;
            ledger.Credit();
            TestAssert.That(ledger.Owed && DeskTry(ledger, s) && ledger.Bought == 1, "the next operation buys with the banked credits");
            for (int i = 0; i < 3; i++) ledger.Credit();
            s.Operation = false;
            TestAssert.That(!DeskTry(ledger, s) && ledger.Credits == 3, "an owed wave waits for the next operation, never dropped");
            for (int i = 0; i < 10; i++) ledger.Credit();
            TestAssert.That(ledger.Credits == WaveLedger.MaximumCredits, "the bank caps at six whatever happens meanwhile");
        }
    }
}
