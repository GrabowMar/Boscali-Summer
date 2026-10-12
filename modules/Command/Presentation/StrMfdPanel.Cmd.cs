using System;
using NOAvionics;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>
    /// The OPERATIONS page while the Station Board is built (throw-away, STR repair S2): one note that says what the staff is doing
    /// and why there is nothing to press. Picking, calling off and the posture come back with the new panel; until then the sim
    /// drives them through StrAutomation.Verb.
    /// </summary>
    internal sealed partial class StrMfdPanel
    {
        private StrNote staffState;

        private void ResetCmd() => staffState = null;

        private void BuildCmdPage(AvFlow p) => staffState = p.Add(new StrNote(p.Content, AvIcon.Radio));

        private void RefreshCmd() => FillStaffState(staffState, theaterWar);

        internal static void FillStaffState(StrNote note, ITheaterWarView war)
        {
            if (war == null) { note.Set("THEATER OPS OFF", "Enable TheaterOps in F1 module settings and restart."); return; }
            string words = TheaterWords.ViewState(war.State, war.SnapshotAgeSeconds);
            if (words.Length > 0)
            {
                int cut = words.IndexOf(" - ", StringComparison.Ordinal);
                note.Set(cut > 0 ? words.Substring(0, cut) : words, cut > 0 ? words.Substring(cut + 3) : "",
                    war.State == TheaterViewState.Stale ? AvState.Caution : AvState.Info);
                return;
            }
            TheaterOperationView operation = war.ActiveOperation;
            if (operation != null)
                note.Set(TheaterWords.Kind(operation.Kind) + " · " + operation.Name,
                    TheaterWords.Phase(operation.Phase, operation.PhaseNote),
                    operation.Phase == TheaterPhase.InContact ? AvState.Caution : AvState.Info);
            else if (war.Proposals.Count > 0)
                note.Set("STAFF OPTIONS READY", war.Proposals.Count + " choices; the staff selects one by itself.", AvState.Info);
            else note.Set("STAFF OBSERVING FRONTS", "No operation yet; the staff keeps reviewing every front.", AvState.Info);
        }
    }
}
