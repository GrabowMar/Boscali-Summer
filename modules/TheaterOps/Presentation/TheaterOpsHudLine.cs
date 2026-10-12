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

        internal void Configure(ITheaterWarView view)
        {
            war = view;
        }

        public void ResetForScene()
        {
            sentText = null;
            nextRefresh = 0f;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            if (!ModuleServices.TryGet(out board)) return;
            if (!Read()) { sentText = null; return; }
            if (text == sentText && tone == sentTone) return;
            sentText = text;
            sentTone = tone;
            board.Notice("theater", tone, text, detail);
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
