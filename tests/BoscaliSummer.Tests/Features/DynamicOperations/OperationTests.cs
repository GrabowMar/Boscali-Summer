using System;
using BoscaliSummer.Features.DynamicOperations.Domain;

namespace BoscaliSummer.Tests.Features.DynamicOperations
{
    internal static class OperationTests
    {
        public static void Run()
        {
            CaptureAndInterdictionAwardOnce();
            DefenseRequiresObservedOwnership();
            TerminalStatesCannotPay();
            BoardRetainsHistoryAndBounds();
            InvalidTimeCannotChangeState();
            InterdictionDistinguishesDespawnFromCombat();
        }

        private static void InterdictionDistinguishesDespawnFromCombat()
        {
            var combat = new InterdictionState();
            combat.ObserveDisable(true, true);
            combat.ObserveDisable(true, true);
            TestAssert.That(combat.Neutralized && !combat.Despawned,
                "A real disable remains credited across native wreck destruction");
            var scriptedRemoval = new InterdictionState();
            scriptedRemoval.ObserveDisable(false, true);
            scriptedRemoval.ObserveDisable(true, true);
            TestAssert.That(scriptedRemoval.Despawned && !scriptedRemoval.Neutralized,
                "Listen-host OnDestroy's second disable event must not turn scripted removal into a kill");
            var changedOwner = new InterdictionState();
            changedOwner.ObserveDisable(true, false);
            TestAssert.That(!changedOwner.Neutralized,
                "Destroying a target after an allegiance change cannot satisfy the original interdiction");
        }

        private static Operation Create(int id, OperationKind kind, int target = 100, float now = 10f) =>
            new Operation(id, target, kind, OperationReward.None, now, 1200, 100);

        private static void CaptureAndInterdictionAwardOnce()
        {
            Operation capture = Create(1, OperationKind.Capture);
            capture.Observe(11f, 1f, valid: true, owned: false, neutralized: false);
            TestAssert.That(capture.State == OperationState.Active && !capture.TryTakeAward(),
                "A capture opportunity cannot pay while the base remains hostile");
            capture.Observe(12f, 1f, valid: true, owned: true, neutralized: false);
            TestAssert.That(capture.State == OperationState.Completed && capture.Progress == 1f &&
                capture.EndedAt == 12f && capture.TryTakeAward(), "Capturing a valid base releases its award");
            capture.Observe(1300f, 1200f, valid: false, owned: false, neutralized: false);
            TestAssert.That(capture.State == OperationState.Completed && capture.EndedAt == 12f &&
                !capture.TryTakeAward(), "Later observations cannot reopen or repay a completed operation");

            Operation interdict = Create(2, OperationKind.Interdict);
            interdict.Observe(11f, 1f, valid: true, owned: true, neutralized: false);
            TestAssert.That(interdict.State == OperationState.Active,
                "Friendly anchor ownership alone never completes interdiction");
            interdict.Observe(12f, 1f, valid: true, owned: false, neutralized: true);
            TestAssert.That(interdict.State == OperationState.Completed && interdict.TryTakeAward() &&
                !interdict.TryTakeAward(), "A neutralized valid target pays once across repeated polls");

            var bounded = new Operation(3, 100, OperationKind.Capture, OperationReward.None, 10f,
                int.MaxValue, int.MaxValue);
            var nonnegative = new Operation(4, 101, OperationKind.Capture, OperationReward.None, 10f, -1, -1);
            TestAssert.That(bounded.Money == 100000 && bounded.Xp == 10000 &&
                nonnegative.Money == 0 && nonnegative.Xp == 0, "Award amounts respect protocol limits");
        }

        private static void DefenseRequiresObservedOwnership()
        {
            Operation defense = Create(1, OperationKind.Defend, now: 0f);
            defense.Observe(600f, 600f, valid: true, owned: true, neutralized: false);
            TestAssert.That(defense.HoldSeconds == 2f && defense.State == OperationState.Active,
                "An unobserved scheduling stall cannot instantly complete defense");
            for (int i = 0; i < 20; i++)
                defense.Observe(600f, 0f, valid: true, owned: true, neutralized: false);
            TestAssert.That(defense.HoldSeconds == 2f, "Paused mission time cannot advance the hold");
            for (int i = 1; i <= 88; i++)
                defense.Observe(600f + i * 2f, 2f, valid: true, owned: true, neutralized: false);
            TestAssert.That(defense.HoldSeconds == 178f && defense.Progress < 1f && !defense.TryTakeAward(),
                "Defense remains active until the full observed three-minute hold");
            defense.Observe(778f, 2f, valid: true, owned: true, neutralized: false);
            TestAssert.That(defense.HoldSeconds == 180f && defense.Progress == 1f && defense.TryTakeAward(),
                "A completed observed hold releases its award");

            Operation lost = Create(2, OperationKind.Defend, now: 0f);
            for (int i = 1; i <= 89; i++)
                lost.Observe(i * 2f, 2f, valid: true, owned: true, neutralized: false);
            lost.Observe(180f, 2f, valid: true, owned: false, neutralized: false);
            TestAssert.That(lost.State == OperationState.Cancelled && lost.HoldSeconds == 178f && !lost.TryTakeAward(),
                "Losing the base on the final hold tick cancels instead of paying");
        }

        private static void TerminalStatesCannotPay()
        {
            foreach (OperationKind kind in Enum.GetValues(typeof(OperationKind)))
            {
                Operation expired = Create(1, kind);
                expired.Observe(expired.Deadline, 2f, valid: true, owned: true, neutralized: true);
                TestAssert.That(expired.State == OperationState.Expired && !expired.TryTakeAward(),
                    "The deadline takes precedence over a same-tick completion for " + kind);
                expired.Observe(expired.Deadline + 1f, 2f, valid: true, owned: true, neutralized: true);
                TestAssert.That(expired.State == OperationState.Expired && !expired.TryTakeAward(),
                    "An expired operation never becomes payable for " + kind);

                Operation cancelled = Create(2, kind);
                cancelled.Observe(11f, 1f, valid: false, owned: true, neutralized: true);
                cancelled.Observe(12f, 1f, valid: true, owned: true, neutralized: true);
                TestAssert.That(cancelled.State == OperationState.Cancelled && !cancelled.TryTakeAward(),
                    "Missing or changed target identity cancels permanently for " + kind);
            }
        }

        private static void BoardRetainsHistoryAndBounds()
        {
            var board = new OperationBoard();
            Operation first = Create(1, OperationKind.Capture, target: -1);
            TestAssert.That(!board.TryAdd(null) && board.TryAdd(first) &&
                !board.TryAdd(Create(2, OperationKind.Capture, target: -1)),
                "Null and duplicate missions cannot consume another card");
            TestAssert.That(board.TryAdd(Create(3, OperationKind.Defend, target: -1)) &&
                board.TryAdd(Create(4, OperationKind.Interdict, target: -1)),
                "History distinguishes operation kinds even for negative Unity identities");
            TestAssert.That(!board.HasCapacity && !board.TryAdd(Create(5, OperationKind.Capture, target: 1)) &&
                !board.WasIssued(OperationKind.Capture, 1), "A full three-card board does not burn rejected opportunities");
            first.Observe(12f, 1f, valid: true, owned: true, neutralized: false);
            board.Prune(71.99f);
            TestAssert.That(board.Operations.Count == 3, "Terminal cards remain visible for their full retention period");
            board.Prune(72f);
            TestAssert.That(board.HasCapacity && board.Operations.Count == 2 &&
                board.WasIssued(OperationKind.Capture, -1) &&
                !board.TryAdd(Create(6, OperationKind.Capture, target: -1)),
                "Pruning a card retains the anti-repeat history");

            var session = new OperationBoard();
            for (int i = 0; i < OperationBoard.MaximumIssued; i++)
            {
                float now = i * 70f;
                Operation op = Create(i + 1, OperationKind.Capture, target: i, now: now);
                TestAssert.That(session.TryAdd(op), "A unique operation below the session ceiling is eligible");
                op.Observe(now + 1f, 1f, valid: true, owned: true, neutralized: false);
                session.Prune(now + 61f);
            }
            TestAssert.That(session.Operations.Count == 0 && !session.HasCapacity &&
                !session.TryAdd(Create(129, OperationKind.Capture, target: 129)),
                "The 128-issued ceiling survives all completed-card pruning");
        }

        private static void InvalidTimeCannotChangeState()
        {
            Operation defense = Create(1, OperationKind.Defend);
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                TestAssert.Throws<ArgumentOutOfRangeException>(() => Create(2, OperationKind.Capture, now: invalid),
                    "Nonfinite creation time cannot produce an immortal operation");
                defense.Observe(invalid, 2f, valid: false, owned: false, neutralized: false);
                defense.Observe(11f, invalid, valid: true, owned: true, neutralized: false);
            }
            defense.Observe(11f, -1f, valid: true, owned: true, neutralized: false);
            TestAssert.That(defense.State == OperationState.Active && defense.HoldSeconds == 0f && defense.Progress == 0f,
                "Invalid clock samples neither advance progress nor cancel an operation");

            var board = new OperationBoard();
            Operation capture = Create(3, OperationKind.Capture);
            board.TryAdd(capture);
            capture.Observe(12f, 1f, valid: true, owned: true, neutralized: false);
            board.Prune(float.NaN);
            board.Prune(float.PositiveInfinity);
            board.Prune(float.NegativeInfinity);
            TestAssert.That(board.Operations.Count == 1, "Invalid prune clocks retain the current mission cards");
        }
    }
}
