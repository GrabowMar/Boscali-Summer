using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using UnityEngine;

namespace BoscaliSummer.Modules.TheaterOps.Presentation
{
    /// <summary>
    /// A compact view of the same host-authored operation and staff choices shown on STR. The TAC BOARD draws no held lines, so the staff line is a notice on its ticker, pushed once
    /// each time the words (or their tone) change and not again while they hold.
    /// </summary>
    internal sealed class TheaterOpsHudLine : MonoBehaviour, ISceneService
    {
        private const float RefreshSeconds = 1f;

        private ITheaterWarView war;
        private IHudBoard board;
        private float nextRefresh;

        private string text;
        private string detail;
        private HudTone tone;
        private string sentText;
        private HudTone sentTone;
        private int seenResult;
        private bool resultBaselined;
        private int seenOutcome = -1, seenBought = -1;

        internal void Configure(ITheaterWarView view)
        {
            war = view;
        }

        public void ResetForScene()
        {
            sentText = null;
            nextRefresh = 0f;
            seenResult = 0;
            resultBaselined = false;
            seenOutcome = -1;
            seenBought = -1;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            if (!ModuleServices.TryGet(out board)) return;
            AnnounceResult();
            AnnounceSortie();
            if (!Read()) { sentText = null; return; }
            if (text == sentText && tone == sentTone) return;
            sentText = text;
            sentTone = tone;
            board.Notice("theater", tone, text, detail);
        }

        /// <summary>
        /// A newly ended operation says how it ended, once. The first snapshot only sets the baseline, so joining late announces nothing; calling off or
        /// redirecting is the pilot's own act and is not announced.
        /// </summary>
        private void AnnounceResult()
        {
            TheaterWarSnapshot snapshot = war != null && war.Available ? war.Snapshot : null;
            if (snapshot == null) return;
            TheaterResultView result = snapshot.LastResult;
            int id = result != null ? result.OperationId : 0;
            if (!resultBaselined) { resultBaselined = true; seenResult = id; return; }
            if (id == seenResult || result == null) return;
            seenResult = id;
            if (result.Outcome == TheaterOutcome.CalledOff || result.Outcome == TheaterOutcome.Redirected) return;
            board.Notice("theater", result.Outcome == TheaterOutcome.Secured ? HudTone.Info : HudTone.Caution,
                TheaterWords.Result(result.Kind, result.Name, result.Outcome, result.Why, result.Funds, ""), TheaterWords.Why(result.Why));
        }

        /// <summary>
        /// Every faction pilot hears a finished sortie, derived on each peer from the snapshot's newest outcome (channel "sorties", apart from the
        /// staff line, so neither replaces the other). The first snapshot only sets the watermark: a late joiner is not shown history.
        /// </summary>
        private void AnnounceSortie()
        {
            TheaterWarSnapshot snapshot = war != null && war.Available ? war.Snapshot : null;
            if (snapshot == null) return;
            TheaterOutcomeView outcome = snapshot.LastOutcome;
            int seq = outcome != null ? outcome.Seq : 0, bought = snapshot.Wave != null ? snapshot.Wave.Bought : 0;
            if (seenOutcome < 0) { seenOutcome = seq; seenBought = bought; return; }
            if (bought > seenBought && snapshot.Wave != null)
                board.Notice("sorties", HudTone.Info, "WAVE EN ROUTE", snapshot.Wave.Pool >= 0 ? "POOL " + snapshot.Wave.Pool : "POOL -");
            seenBought = bought;
            if (seq == seenOutcome || outcome == null) return;
            seenOutcome = seq;
            string title = outcome.Title.ToUpperInvariant(), pilot = outcome.Pilot.ToUpperInvariant();
            if (!outcome.Counted)
            {
                board.Notice("sorties", HudTone.Info, "SORTIE COMPLETE · " + title, pilot);
                return;
            }
            int perWave = snapshot.Wave != null ? snapshot.Wave.CreditsPerWave : 3;
            string front = snapshot.Operation != null && !string.IsNullOrEmpty(snapshot.Operation.Name) ? snapshot.Operation.Name.ToUpperInvariant() + " " : "";
            board.Notice("sorties", HudTone.Info, front + "SORTIE DONE · WAVE " + System.Math.Min(outcome.Credits, perWave) + "/" + perWave, title + " · " + pilot);
        }

        private bool Read()
        {
            text = null;
            if (war == null || !war.Available) return false;
            war.Refresh();
            TheaterCommandResult last = war.LastCommand;
            if (!war.HasSnapshot)
            {
                bool unconfirmed = last != null && last.Code == TheaterCommandCode.Unconfirmed;
                text = unconfirmed ? "STAFF COMMAND UNCONFIRMED" : "WAITING FOR STAFF";
                detail = unconfirmed ? TheaterWords.Command(last.Code, last.Detail, last.LockSeconds)
                    : TheaterWords.ViewState(war.State, 0f);
                tone = unconfirmed ? HudTone.Caution : HudTone.Info;
                return true;
            }
            if (war.State == TheaterViewState.Stale)
            {
                text = "STALE STAFF REPORT";
                detail = TheaterWords.ViewState(TheaterViewState.Stale, war.SnapshotAgeSeconds);
                tone = HudTone.Caution;
                return true;
            }
            if (war.CommandPending)
            {
                text = "STAFF COMMAND PENDING";
                detail = "AWAITING HOST";
                tone = HudTone.Info;
                return true;
            }
            TheaterOperationView operation = war.ActiveOperation;
            if (operation != null)
            {
                text = TheaterWords.Kind(operation.Kind) + " · " + operation.Name;
                detail = TheaterWords.Phase(operation.Phase, operation.PhaseNote);
                tone = operation.Phase == TheaterPhase.InContact ? HudTone.Caution : HudTone.Info;
            }
            else if (war.Proposals.Count > 0)
            {
                text = "STAFF OPTIONS READY";
                detail = war.Proposals.Count + " choices on STR";
                tone = HudTone.Info;
            }
            else return false;
            return true;
        }
    }
}
