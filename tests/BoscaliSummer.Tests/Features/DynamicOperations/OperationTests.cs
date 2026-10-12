using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.DynamicOperations.Domain;

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
            WithdrawFreesOfferedCardsOnly();
            PickDisplacedChoosesWhatATiedSortieMayReplace();
            InvalidTimeCannotChangeState();
            InterdictionDistinguishesDespawnFromCombat();
            AcceptanceAndContinuousTasks();
            ExtendedMissionsRequireTheirOwnEvidence();
            ReturnMissionsRetainStagesAndFailClosed();
        }

        private static void ExtendedMissionsRequireTheirOwnEvidence()
        {
            foreach (OperationKind kind in new[] { OperationKind.SupplyInterdict, OperationKind.ElectronicWarfare })
            {
                Operation op = Create(1, kind, now: 0f);
                op.Observe(1f, 1f, true, true, false, true, true, true, true);
                TestAssert.That(op.State == OperationState.Active, "Unrelated service/return evidence cannot finish " + kind);
                op.Observe(2f, 1f, true, true, true);
                TestAssert.That(op.TryTakeAward() && !op.TryTakeAward(), "Native neutralization pays once for " + kind);
            }
            foreach (OperationKind kind in new[] { OperationKind.Recon, OperationKind.BattlefieldSurvey, OperationKind.DamageAssessment })
            {
                Operation op = Create(2, kind, now: 0f);
                bool killed = kind == OperationKind.DamageAssessment;
                if (killed)
                {
                    op.Observe(1f, 2f, true, true, false, true);
                    TestAssert.That(op.HoldSeconds == 0f, "BDA cannot survey before the actual strike");
                }
                op.Observe(2f, 1f, true, true, killed, true);
                op.Observe(3f, 1f, true, true, killed, false);
                TestAssert.That(op.HoldSeconds == 0f, "Lost observation resets " + kind);
                for (int i = 1; i < op.HoldRequired; i++) op.Observe(3f + i, 1f, true, true, killed, true);
                TestAssert.That(!op.TryTakeAward(), "Partial survey never pays for " + kind);
                op.Observe(3f + op.HoldRequired, 1f, true, true, killed, true);
                TestAssert.That(op.TryTakeAward() && !op.TryTakeAward(), "Full verified survey pays once for " + kind);
            }
            foreach (OperationKind kind in new[] { OperationKind.SupplyEscort, OperationKind.RepairCover })
            {
                Operation op = Create(3, kind, now: 0f);
                op.Observe(1f, 1f, true, true, false, true, serviced: true);
                TestAssert.That(!op.TryTakeAward(), "Service without sufficient cover cannot pay for " + kind);
                for (int i = 2; i <= op.HoldRequired; i++) op.Observe(i, 1f, true, true, false, true);
                TestAssert.That(op.State == OperationState.Active && op.Progress < 1f, "Cover alone cannot fabricate service for " + kind);
                op.Observe(100f, 1f, true, true, false, false, serviced: true);
                TestAssert.That(op.HoldSeconds == 0f && !op.TryTakeAward(), "No present cover means no service reward for " + kind);
                for (int i = 1; i <= op.HoldRequired; i++) op.Observe(100f + i, 1f, true, true, false, true);
                op.Observe(200f, 1f, true, true, false, true, serviced: true);
                TestAssert.That(op.TryTakeAward() && !op.TryTakeAward(), "Cover plus native completion pays once for " + kind);
            }
            Operation recon = Create(4, OperationKind.Recon);
            recon.Observe(11f, 1f, true, true, true, true);
            TestAssert.That(recon.State == OperationState.Cancelled, "Destroying the reconnaissance subject cancels it");
            var offer = new Operation(5, 1, OperationKind.DamageAssessment, OperationReward.None, 0f, 1, 1);
            offer.Observe(1f, 1f, true, true, true);
            TestAssert.That(offer.State == OperationState.Cancelled, "A strike before accepting BDA cannot be cashed in later");
        }

        private static void ReturnMissionsRetainStagesAndFailClosed()
        {
            foreach (OperationKind kind in new[] { OperationKind.Rescue, OperationKind.SortieReport })
            {
                var offer = new Operation(1, 1, kind, OperationReward.None, 0f, 1, 1);
                TestAssert.That(!offer.BeginReturn(1f), "Unaccepted return missions cannot capture events");
                Operation op = Create(2, kind, now: 0f);
                op.Observe(1f, 1f, true, true, false, returned: true);
                TestAssert.That(op.State == OperationState.Active, "Landing before acquisition does not complete " + kind);
                if (kind == OperationKind.SortieReport)
                {
                    TestAssert.That(!op.BeginReturn(2f), "Sortie reports require the observation stage");
                    for (int i = 1; i <= op.HoldRequired; i++) op.Observe(1f + i, 1f, true, true, false, true);
                    TestAssert.That(!op.TryTakeAward(), "An acquired report still needs delivery");
                }
                TestAssert.That(op.BeginReturn(100f) && !op.BeginReturn(101f), "Return stage is latched exactly once for " + kind);
                op.Observe(101f, 1f, true, true, true, false);
                TestAssert.That(op.Returning && op.State == OperationState.Active && op.Progress == 0.75f,
                    "Losing the original subject or observation does not erase acquired evidence for " + kind);
                op.Observe(102f, 1f, true, true, false, false, returned: true);
                TestAssert.That(op.TryTakeAward() && !op.TryTakeAward() && !op.BeginReturn(103f), "Returning pays only once for " + kind);
            }
            Operation lost = Create(3, OperationKind.Rescue, now: 0f);
            lost.BeginReturn(1f);
            lost.Observe(2f, 1f, false, true, false, returned: true);
            TestAssert.That(lost.State == OperationState.Cancelled && !lost.TryTakeAward(), "Lost carrier/base beats return completion");
            Operation late = Create(4, OperationKind.Rescue, now: 0f);
            TestAssert.That(!late.BeginReturn(float.NaN) && !late.BeginReturn(late.Deadline), "Invalid or expired pickup cannot begin return");
            late.BeginReturn(1f);
            late.Observe(late.Deadline, 1f, true, true, false, returned: true);
            TestAssert.That(late.State == OperationState.Expired && !late.TryTakeAward(), "Expired return cannot pay");
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

        private static Operation Create(int id, OperationKind kind, int target = 100, float now = 10f)
        {
            var operation = new Operation(id, target, kind, OperationReward.None, now, 1200, 100);
            operation.Accept(now);
            return operation;
        }

        private static void AcceptanceAndContinuousTasks()
        {
            var board = new OperationBoard();
            for (int i = 1; i <= 3; i++) board.TryAdd(new Operation(i, i, OperationKind.Patrol, OperationReward.None, 0f, 700, 60));
            Operation offer = board.Operations[0];
            offer.Observe(10f, 2f, true, true, true, true, true);
            TestAssert.That(offer.State == OperationState.Offered && offer.Progress == 0f && !offer.TryTakeAward(), "Offers cannot progress or pay before acceptance");
            board.Prune(70f);
            TestAssert.That(board.Operations.Count == 3, "Live offers survive result pruning");
            TestAssert.That(!board.TryAccept(99, 100f) && board.TryAccept(1, 100f) && board.TryAccept(2, 100f) &&
                !board.TryAccept(3, 100f) && !board.TryAccept(1, 101f), "Acceptance validates identity, duplicate transitions and two-active limit");
            TestAssert.That(offer.Deadline == 1300f, "Acceptance starts a fresh execution timer");
            TestAssert.That(offer.AcceptedBy.Length == 0, "Unattributed test acceptance remains faction-wide");
            TestAssert.That(board.Operations[1].AcceptedBy.Length == 0 &&
                !board.TryAccept(3, 101f, "LATE PILOT") && board.Operations[2].AcceptedBy.Length == 0,
                "A rejected claim must not assign an accepting pilot");
            offer.Cancel(101f);
            TestAssert.That(!offer.TryTakeAward() && board.TryAccept(3, 102f), "Aborting releases capacity without an award");
            var attributed = new Operation(40, 40, OperationKind.Patrol, OperationReward.None, 0f, 1, 1);
            TestAssert.That(attributed.Accept(1f, "PILOT ONE") && attributed.AcceptedBy == "PILOT ONE" &&
                !attributed.Accept(2f, "PILOT TWO") && attributed.AcceptedBy == "PILOT ONE",
                "Only successful acceptance records the pilot; later faction actions cannot replace it");
            attributed.Cancel(3f);
            TestAssert.That(attributed.AcceptedBy == "PILOT ONE", "Result cards retain the accepting pilot");
            var identified = new Operation(41, 41, OperationKind.Patrol, OperationReward.None, 0f, 1, 1);
            TestAssert.That(identified.AcceptorId == 0 && identified.Accept(1f, "PILOT ONE", 7) && identified.AcceptorId == 7,
                "Acceptance records the acceptor identity the host pays and guards by");
            var expired = new Operation(4, 4, OperationKind.Capture, OperationReward.None, 0f, 1, 1);
            TestAssert.That(!expired.Accept(300f) && !expired.Accept(float.NaN), "Expired and invalid-time offers cannot be accepted");
            expired.Observe(300f, 1f, true, true, true);
            TestAssert.That(expired.State == OperationState.Expired && !expired.TryTakeAward(), "Offer expiry beats automatic completion");
            foreach (OperationKind kind in new[] { OperationKind.Patrol, OperationKind.Jam, OperationKind.Defend })
            {
                Operation task = Create(8, kind, now: 0f);
                task.Observe(1f, 1f, true, true, false, true);
                task.Observe(2f, 1f, true, true, false, false);
                TestAssert.That(task.HoldSeconds == 0f, "Interrupted holds reset for " + kind);
                for (int second = 1; second <= task.HoldRequired; second++) task.Observe(2f + second, 1f, true, true, false, true);
                TestAssert.That(task.State == OperationState.Completed && task.TryTakeAward() && !task.TryTakeAward(), "Full continuous hold pays once for " + kind);
            }
            foreach (OperationKind kind in new[] { OperationKind.Rappel, OperationKind.Rooftop })
            {
                Operation task = Create(9, kind);
                task.Observe(11f, 1f, true, true, false, true, false);
                TestAssert.That(task.State == OperationState.Active, "Hovering cannot complete insertion");
                task.Observe(12f, 1f, true, true, false, true, true);
                TestAssert.That(task.TryTakeAward(), "Verified landing completes insertion");
            }
            Operation jam = Create(10, OperationKind.Jam);
            jam.Observe(11f, 1f, true, true, true, true);
            TestAssert.That(jam.State == OperationState.Cancelled && !jam.TryTakeAward(), "Destroying an emitter cannot complete a jamming contract");
        }

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
            TestAssert.That(session.Operations.Count == 0 && session.HasCapacity &&
                session.WasIssued(OperationKind.Capture, 0) && session.WasIssued(OperationKind.Capture, OperationBoard.MaximumIssued - 1),
                "Issued history is a memory, not a ceiling: the board still takes cards after 128 issues and remembers all 128");
            TestAssert.That(session.TryAdd(Create(129, OperationKind.Capture, target: 128, now: 9000f)),
                "The 129th unique issue is accepted");
            TestAssert.That(!session.WasIssued(OperationKind.Capture, 0) && session.WasIssued(OperationKind.Capture, 1) &&
                session.WasIssued(OperationKind.Capture, 128),
                "The oldest key is forgotten, the second is still remembered, the newest is remembered");
            TestAssert.That(session.TryAdd(Create(130, OperationKind.Capture, target: 0, now: 9000f)) &&
                !session.WasIssued(OperationKind.Capture, 1),
                "A forgotten opportunity can be issued again, and each new issue forgets the next oldest");
            TestAssert.That(!session.TryAdd(Create(131, OperationKind.Capture, target: 128, now: 9000f)),
                "A remembered opportunity is still refused");
        }

        private static Operation Offer(int id, OperationKind kind, int target, int focusId = 0, string anchor = "")
        {
            return new Operation(id, target, kind, OperationReward.None, 10f, 1200, 100, 0, focusId, anchor);
        }

        private static void WithdrawFreesOfferedCardsOnly()
        {
            var board = new OperationBoard();
            Operation offered = Offer(1, OperationKind.Capture, 10);
            Operation active = Create(2, OperationKind.Defend, target: 11);
            Operation done = Create(3, OperationKind.Capture, target: 12);
            TestAssert.That(board.TryAdd(offered) && board.TryAdd(active) && board.TryAdd(done), "Three cards fit");
            done.Observe(12f, 1f, valid: true, owned: true, neutralized: false);
            TestAssert.That(done.State == OperationState.Completed, "The third card is terminal");
            TestAssert.That(!board.Withdraw(2) && !board.Withdraw(3) && !board.Withdraw(99) && board.Operations.Count == 3,
                "Only an Offered card can be withdrawn: not an Active, a terminal or an unknown id");
            TestAssert.That(board.WasIssued(OperationKind.Capture, 12) && board.WasIssued(OperationKind.Defend, 11),
                "A refused withdraw keeps the key");
            TestAssert.That(board.Withdraw(1) && board.Operations.Count == 2 && !board.WasIssued(OperationKind.Capture, 10),
                "Withdraw removes an Offered card and frees its key");
            TestAssert.That(!board.Withdraw(1), "A withdrawn card cannot be withdrawn twice");
            TestAssert.That(board.TryAdd(Offer(4, OperationKind.Capture, 10)), "A freed opportunity can be offered again");

            var ring = new OperationBoard();
            for (int i = 0; i < OperationBoard.MaximumIssued - 1; i++)
            {
                float now = i * 70f;
                Operation op = Create(i + 1, OperationKind.Capture, target: i, now: now);
                TestAssert.That(ring.TryAdd(op), "Ring fill " + i);
                op.Observe(now + 1f, 1f, valid: true, owned: true, neutralized: false);
                ring.Prune(now + 61f);
            }
            TestAssert.That(ring.TryAdd(Offer(500, OperationKind.Patrol, 1)) && ring.Withdraw(500),
                "A card withdrawn from a nearly full history leaves no ghost in it");
            TestAssert.That(ring.TryAdd(Offer(501, OperationKind.Capture, 1000)) && ring.WasIssued(OperationKind.Capture, 0),
                "One new issue after the withdraw still fits the 128 memory");
            TestAssert.That(ring.Withdraw(501) && ring.TryAdd(Offer(502, OperationKind.Capture, 1001)) &&
                ring.TryAdd(Offer(503, OperationKind.Capture, 1002)) && !ring.WasIssued(OperationKind.Capture, 0) &&
                ring.WasIssued(OperationKind.Capture, 1),
                "The history evicts only when it really holds more than 128 keys");
        }

        private static void PickDisplacedChoosesWhatATiedSortieMayReplace()
        {
            var focus = new OperationFocus(7, FocusRole.Assault, "PORT", "PORT AURELIA", 0f, 0f, 8000f);
            var board = new OperationBoard();
            Operation tied = Offer(1, OperationKind.Capture, 10, focusId: 7, anchor: "PORT");
            Operation general = Offer(2, OperationKind.Defend, 11);
            Operation patrol = Offer(3, OperationKind.Patrol, 12);
            TestAssert.That(board.TryAdd(tied) && board.TryAdd(general) && board.TryAdd(patrol), "A full board of Offered cards");
            TestAssert.That(board.PickDisplaced(OperationKind.Interdict, focus) == general,
                "A full board gives up the lowest-id card that is not tied to the live focus");
            TestAssert.That(board.PickDisplaced(OperationKind.Patrol, focus) == patrol,
                "A card of the same kind is preferred over a lower id");
            TestAssert.That(board.PickDisplaced(OperationKind.Capture, focus) == general,
                "The covered card of the same kind is never given up; the board gives up another");
            TestAssert.That(board.PickDisplaced(OperationKind.Interdict, default) == null &&
                board.PickDisplaced(OperationKind.Patrol, default) == null,
                "A focus that is not live displaces nothing");

            TestAssert.That(board.Withdraw(3) && board.PickDisplaced(OperationKind.Interdict, focus) == null,
                "A board with room and no card of the kind displaces nothing");
            TestAssert.That(board.PickDisplaced(OperationKind.Defend, focus) == general,
                "A board with room still replaces a card of the same kind (one live card per kind)");

            TestAssert.That(board.TryAccept(2, 11f), "The general card is accepted");
            TestAssert.That(board.PickDisplaced(OperationKind.Defend, focus) == null,
                "An Active card is never displaced");
            TestAssert.That(board.TryAdd(Offer(4, OperationKind.Jam, 13, focusId: 3, anchor: "PORT")) &&
                board.PickDisplaced(OperationKind.Interdict, focus) == null,
                "A REPLAN to the same anchor (older id, same anchor) keeps its tied card covered");

            var second = new OperationBoard();
            Operation stale = Offer(5, OperationKind.Jam, 20, focusId: 3, anchor: "OLD ANCHOR");
            Operation patrolA = Offer(6, OperationKind.Patrol, 21);
            Operation patrolB = Offer(7, OperationKind.Patrol, 22);
            TestAssert.That(second.TryAdd(stale) && second.TryAdd(patrolA) && second.TryAdd(patrolB), "A second full board");
            TestAssert.That(second.PickDisplaced(OperationKind.Patrol, focus) == patrolA,
                "Two cards of the same kind: the lower id goes");
            TestAssert.That(second.PickDisplaced(OperationKind.Interdict, focus) == stale,
                "A card tied to a closed operation with another anchor is replaceable, lowest id first");

            Operation noAnchor = Offer(8, OperationKind.Recon, 23, focusId: 1, anchor: null);
            Operation untied = Offer(9, OperationKind.Recon, 24);
            TestAssert.That(noAnchor.FocusId == 1 && noAnchor.FocusAnchor == "" && untied.FocusId == 0 && untied.FocusAnchor == "",
                "A missing anchor reads empty and a card made without a tie reads focus 0");
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
