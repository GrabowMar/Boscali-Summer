#if !PUBLIC_RELEASE
using System.Collections.Generic;
using BoscaliSummer.Modules.DynamicOperations.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.DynamicOperations.Runtime
{
    // Sim readback and levers for SortieAutomation. Nothing here changes a rule; Complete only drives the real chain (accept, the board's own observation, pay, Finished).
    internal sealed partial class OperationsManager
    {
        private int proofCompleted;

        internal Dictionary<string, object> ProofRead()
        {
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player?.HQ == null || !boards.TryGetValue(player.HQ, out FactionBoard board))
                return new Dictionary<string, object> { { "ok", false }, { "error", "no board for the seat" } };
            int tiedOffered = 0, tiedLive = 0, tiedMismatch = 0, matched = 0;
            var cards = new Dictionary<string, object>();
            for (int i = 0; i < board.Targets.Count; i++)
            {
                Operation op = board.Targets[i].Mission;
                bool tied = op.FocusId > 0, covered = tied && board.HasFocus && board.Focus.Covers(op.FocusId, op.FocusAnchor);
                if (tied && op.State == OperationState.Offered) tiedOffered++;
                if (covered && op.IsLive) tiedLive++;
                if (tied && op.IsLive && board.HasFocus && !covered) tiedMismatch++;
                for (int j = 0; j < Objectives.Count; j++) if (Objectives[j].Id == op.Id) { matched++; break; }
                cards["c" + i] = new Dictionary<string, object>
                {
                    { "id", op.Id }, { "kind", op.Kind.ToString() }, { "state", op.State.ToString() }, { "linkOperationId", op.FocusId },
                    { "tied", tied ? 1 : 0 }, { "operationLive", covered ? 1 : 0 }, { "title", OperationTitles.Title(op.Kind) }, { "subject", board.Targets[i].SubjectId },
                    { "x", board.Targets[i].Position.x }, { "z", board.Targets[i].Position.z },
                };
            }
            return new Dictionary<string, object>
            {
                { "ok", true },
                {
                    "sorties", new Dictionary<string, object>
                    {
                        { "cards", cards }, { "cardCount", board.Targets.Count }, { "tiedOffered", tiedOffered }, { "tiedLive", tiedLive }, { "tiedMismatch", tiedMismatch },
                        { "snapshotAge", hasSnapshot ? SnapshotAgeSeconds : -1f }, { "objectivesMatchBoard", matched == board.Targets.Count && Objectives.Count == board.Targets.Count ? 1 : 0 },
                        { "focusId", board.Focus.OperationId }, { "focusAnchor", board.Focus.AnchorKey }, { "focusX", board.Focus.X }, { "focusZ", board.Focus.Z }, { "completedByHook", proofCompleted },
                    }
                },
            };
        }

        private int proofWingCard = -1, proofFinished, proofFinishedByWing;
        private bool proofSubscribed;

        private void ProofSubscribe()
        {
            if (proofSubscribed) return;
            proofSubscribed = true;
            Finished += o => { proofFinished++; proofFinishedByWing = o.ByWing ? 1 : 0; };
        }

        private bool ProofSeat(out Player player, out FactionBoard board)
        {
            board = null;
            return GameManager.GetLocalPlayer<Player>(out player) && player?.HQ != null && boards.TryGetValue(player.HQ, out board);
        }

        /// <summary>
        /// The production SEND WING path for the first tied strike card: accept it if it is offered, take the host's order for it from the source, ask for the refusal, and send.
        /// Reports the refusal text (so a missing wing is read as the pilot would read it) and the answer.
        /// </summary>
        internal Dictionary<string, object> ProofSendWing()
        {
            ProofSubscribe();
            var reply = new Dictionary<string, object> { { "ok", true }, { "found", 0 }, { "sent", 0 }, { "refusal", "" }, { "refusalLen", 0 }, { "answer", "" } };
            if (!ProofSeat(out Player player, out FactionBoard board)) { reply["ok"] = false; reply["error"] = "no board"; return reply; }
            foreach (Target target in board.Targets)
            {
                Operation op = target.Mission;
                if (op.FocusId <= 0 || !op.IsLive || !op.IsStrike || target.Unit == null) continue;
                if (op.State == OperationState.Offered) Act(player, op.Id, false);
                reply["found"] = 1; reply["id"] = op.Id; reply["kind"] = op.Kind.ToString(); reply["accepted"] = op.State == OperationState.Active ? 1 : 0;
                proofWingCard = op.Id;
                if (Wing == null) { reply["noService"] = 1; return reply; }
                if (!TryGetWingOrder(op.Id, out WingSortieOrder order)) { reply["noOrder"] = 1; return reply; }
                reply["intent"] = (int)order.Intent; reply["targetId"] = (int)order.TargetId; reply["hasPosition"] = float.IsFinite(order.X) ? 1 : 0;
                string refusal = Wing.Refusal(in order) ?? "";
                reply["refusal"] = refusal; reply["refusalLen"] = refusal.Length;
                if (refusal.Length > 0) return reply;
                bool sent = Wing.Send(in order, out string answer);
                reply["sent"] = sent ? 1 : 0; reply["answer"] = answer ?? "";
                return reply;
            }
            return reply;
        }

        // The panel presses the wing button, not this class: the card is the first accepted tied strike card the first time it is asked for.
        private void ProofResolveCard(FactionBoard board)
        {
            if (proofWingCard >= 0) return;
            foreach (Target target in board.Targets)
                if (target.Mission.FocusId > 0 && target.Mission.State == OperationState.Active && target.Mission.IsStrike && target.Unit != null) { proofWingCard = target.Mission.Id; return; }
        }

        /// <summary>The card the last SendWing named: its state, whether its target stands, the wing's status for it and what the last paid sortie said about the wing.</summary>
        internal Dictionary<string, object> ProofWingRead()
        {
            ProofSubscribe();
            var reply = new Dictionary<string, object> { { "ok", true }, { "card", proofWingCard }, { "state", -1 }, { "targetUp", -1 }, { "wingState", 0 }, { "finished", proofFinished }, { "finishedByWing", proofFinishedByWing } };
            if (!ProofSeat(out _, out FactionBoard board)) { reply["ok"] = false; return reply; }
            ProofResolveCard(board);
            reply["card"] = proofWingCard;
            foreach (Target target in board.Targets)
            {
                if (target.Mission.Id != proofWingCard) continue;
                reply["state"] = (int)target.Mission.State;
                reply["targetUp"] = target.Unit != null && !target.Unit.disabled ? 1 : 0;
                if (Wing != null && Wing.TryGetStatus(proofWingCard, out WingSortieStatus status)) { reply["wingState"] = (int)status.State; reply["wingmen"] = status.Wingmen; }
            }
            return reply;
        }

        /// <summary>Takes the SendWing card's target down as the seat's aircraft (the fallback when the wing has not killed it by itself). killedBefore: it was already down.</summary>
        internal Dictionary<string, object> ProofKillTarget()
        {
            var reply = new Dictionary<string, object> { { "ok", true }, { "killedBefore", 0 }, { "killed", 0 } };
            if (!ProofSeat(out Player player, out FactionBoard board)) { reply["ok"] = false; reply["error"] = "no board"; return reply; }
            reply["dealer"] = player.Aircraft != null ? 1 : 0;       // 0: the seat's aircraft is gone, the kill has no recorded dealer
            ProofResolveCard(board);
            foreach (Target target in board.Targets)
            {
                if (target.Mission.Id != proofWingCard || target.Unit == null) continue;
                if (target.Unit.disabled) { reply["killedBefore"] = 1; return reply; }
                if (player.Aircraft != null) target.Unit.RecordDamage(player.Aircraft.persistentID, 1f);
                target.Unit.DisableUnit();
                reply["killed"] = target.Unit.disabled ? 1 : 0;
            }
            return reply;
        }

        /// <summary>
        /// Offers the STRIKE COMMAND POST card through the production generator (Generate with that one kind), making room first by withdrawing an offered card of another
        /// kind when the board is full. Reports the card, its subject and whether the operation covers it, and whether the subject is a row of the staff board's feed.
        /// </summary>
        internal Dictionary<string, object> ProofOfferPost()
        {
            ProofSubscribe();
            var reply = new Dictionary<string, object> { { "ok", true }, { "made", 0 } };
            if (!ProofSeat(out Player player, out FactionBoard board)) { reply["ok"] = false; reply["error"] = "no board"; return reply; }
            if (!HasLive(board, OperationKind.StrikePost))
            {
                if (!board.Rules.HasCapacity)
                    for (int i = 0; i < board.Targets.Count; i++)
                    {
                        Operation card = board.Targets[i].Mission;
                        if (card.State != OperationState.Offered || !board.Rules.Withdraw(card.Id)) continue;
                        board.Targets[i].Unwatch(); board.Targets.RemoveAt(i);
                        reply["withdrew"] = card.Kind.ToString();
                        break;
                    }
                Generate(player.HQ, board, NetworkSceneSingleton<MissionManager>.i.MissionTime, OperationKind.StrikePost, 0);
            }
            foreach (Target target in board.Targets)
            {
                Operation op = target.Mission;
                if (op.Kind != OperationKind.StrikePost || !op.IsLive) continue;
                bool inFeed = false;
                if (ModuleServices.TryGet(out IHighCommandView staff))
                    for (int i = 0; i < staff.Commanders.Count; i++) inFeed |= !staff.Commanders[i].IsFriendly && staff.Commanders[i].Id == target.SubjectId;
                proofWingCard = op.Id;
                reply["made"] = 1; reply["id"] = op.Id; reply["subject"] = target.SubjectId; reply["subjectInFeed"] = inFeed ? 1 : 0;
                reply["tied"] = op.FocusId > 0 ? 1 : 0; reply["state"] = (int)op.State; reply["targetUp"] = target.Unit != null && !target.Unit.disabled ? 1 : 0;
                reply["nameLen"] = target.Name.Length; reply["name"] = target.Name; reply["issuedAgain"] = board.Rules.WasIssued(OperationKind.StrikePost, target.SubjectId + 1) ? 1 : 0;
                reply["markerKnown"] = MarkerPosition(player.HQ, target) ? 1 : 0;
                if (op.State == OperationState.Offered) Act(player, op.Id, false);
                reply["acceptedNow"] = op.State == OperationState.Active ? 1 : 0;
                return reply;
            }
            return reply;
        }

        private static bool HasLive(FactionBoard board, OperationKind kind)
        {
            foreach (Target target in board.Targets) if (target.Mission.Kind == kind && target.Mission.IsLive) return true;
            return false;
        }

        /// <summary>
        /// Accepts (through the real Act) and completes one sortie tied to the live operation, by the board's own rules: a strike target is neutralised, a base is owned or a hold is
        /// observed to its required seconds. Pay and the Finished event then run on the next board tick. Does nothing once <paramref name="limit"/> have been completed this way, and
        /// answers ok with done 0 when no tied sortie can be taken now (two accepted ones still open, none offered): the scenario simply asks again.
        /// A card tied to an operation that has since ended still counts as tied; whether it earns a credit is the desk's decision (the 120 s window).
        /// </summary>
        internal Dictionary<string, object> ProofComplete(int limit)
        {
            var reply = new Dictionary<string, object> { { "ok", true }, { "done", 0 }, { "completedByHook", proofCompleted } };
            if (proofCompleted >= limit) { reply["why"] = "limit reached"; return reply; }
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player?.HQ == null || !boards.TryGetValue(player.HQ, out FactionBoard board)) { reply["ok"] = false; reply["error"] = "no board"; return reply; }
            float now = NetworkSceneSingleton<MissionManager>.i.MissionTime;
            foreach (Target target in board.Targets)
            {
                Operation op = target.Mission;
                bool hold = op.Kind == OperationKind.Defend || op.Kind == OperationKind.Patrol || op.Kind == OperationKind.BattlefieldSurvey || op.Kind == OperationKind.Recon || op.Kind == OperationKind.Jam;
                if (op.FocusId <= 0 || !op.IsLive || !(op.IsStrike && target.Unit != null || op.Kind == OperationKind.Capture || hold)) continue;
                if (op.State == OperationState.Offered) Act(player, op.Id, false);
                if (op.State != OperationState.Active) continue;
                if (op.IsStrike) target.Life.ObserveDisable(true, true);
                else
                    for (int i = 0; i < 100 && op.State == OperationState.Active; i++)
                        op.Observe(now, 2f, true, true, false, true);
                if (op.State == OperationState.Active && !op.IsStrike) continue;
                proofCompleted++;
                reply["done"] = 1; reply["id"] = op.Id; reply["kind"] = op.Kind.ToString(); reply["completedByHook"] = proofCompleted;
                return reply;
            }
            reply["why"] = "no tied sortie to complete";
            return reply;
        }
    }
}
#endif
