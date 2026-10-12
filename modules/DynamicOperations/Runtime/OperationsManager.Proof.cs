#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.DynamicOperations.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
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
                    { "tied", tied ? 1 : 0 }, { "operationLive", covered ? 1 : 0 },
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
                        { "focusId", board.Focus.OperationId }, { "focusAnchor", board.Focus.AnchorKey }, { "completedByHook", proofCompleted },
                    }
                },
            };
        }

        /// <summary>
        /// Accepts (through the real Act) and completes one sortie tied to the live operation, by the board's own rules: a strike target is neutralised, a base is owned or a hold is
        /// observed to its required seconds. Pay and the Finished event then run on the next board tick. Does nothing once <paramref name="limit"/> have been completed this way.
        /// </summary>
        internal Dictionary<string, object> ProofComplete(int limit)
        {
            var reply = new Dictionary<string, object> { { "ok", false }, { "completedByHook", proofCompleted } };
            if (proofCompleted >= limit) { reply["error"] = "limit reached"; return reply; }
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player?.HQ == null || !boards.TryGetValue(player.HQ, out FactionBoard board)) { reply["error"] = "no board"; return reply; }
            float now = NetworkSceneSingleton<MissionManager>.i.MissionTime;
            foreach (Target target in board.Targets)
            {
                Operation op = target.Mission;
                bool hold = op.Kind == OperationKind.Defend || op.Kind == OperationKind.Patrol || op.Kind == OperationKind.BattlefieldSurvey || op.Kind == OperationKind.Recon;
                if (op.FocusId <= 0 || !op.IsLive || !board.HasFocus || !board.Focus.Covers(op.FocusId, op.FocusAnchor) ||
                    !(op.IsStrike && target.Unit != null || op.Kind == OperationKind.Capture || hold)) continue;
                if (op.State == OperationState.Offered) Act(player, op.Id, false);
                if (op.State != OperationState.Active) continue;
                if (op.IsStrike) target.Life.ObserveDisable(true, true);
                else
                    for (int i = 0; i < 100 && op.State == OperationState.Active; i++)
                        op.Observe(now, 2f, true, true, false, true);
                if (op.State == OperationState.Active && !op.IsStrike) continue;
                proofCompleted++;
                reply["ok"] = true; reply["id"] = op.Id; reply["kind"] = op.Kind.ToString(); reply["completedByHook"] = proofCompleted;
                return reply;
            }
            reply["error"] = "no tied sortie to complete";
            return reply;
        }
    }
}
#endif
