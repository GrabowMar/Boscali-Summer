using BoscaliSummer.Modules.TheaterOps.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using UnityEngine;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    // The staff's side of the reinforcement desk: the sortie-finished handler and the review hook. The desk owns the money and the bank.
    internal sealed partial class LivingFrontService
    {
        private readonly ReinforcementDesk desk = new ReinforcementDesk();
        private readonly FaultLedger deskFaults = new FaultLedger();
        private ISortieOutcomeSource sorties;

        internal ReinforcementDesk Desk => desk;

        private static OperationFocus FocusOf(FactionWar war)
        {
            Operation op = war.Active;
            return op == null ? default : new OperationFocus(op.Id, LivingWarRules.RoleOf(op.Kind), op.Key, op.Name, op.X, op.Z, LivingWarRules.RadiusOf(op.Kind));
        }

        /// <summary>Late, never at install: the sortie source registers after this module. Unsubscribe before subscribe, so a repeat never doubles.</summary>
        private void BindSorties()
        {
            if (sorties != null || !ModuleServices.TryGet(out ISortieOutcomeSource source)) return;
            sorties = source;
            source.Finished -= OnSortieFinished;
            source.Finished += OnSortieFinished;
            desk.SortiesAvailable = true;
        }

        private void UnbindSorties()
        {
            if (sorties != null) sorties.Finished -= OnSortieFinished;
            sorties = null;
            desk.ResetForScene();
        }

        private void OnSortieFinished(SortieOutcome outcome)
        {
            if (!Authoritative || MissionOver) return;
            FactionWar war = null;
            foreach (FactionWar candidate in wars.Values)
                if (candidate.HQ != null && candidate.HQ.GetInstanceID() == outcome.FactionId) { war = candidate; break; }
            if (war == null) return;
            float now = Time.timeSinceLevelLoad;
            OperationFocus live = FocusOf(war);
            Guard.Run(deskFaults, now, () =>
            {
                if (desk.OnSortieFinished(war.HQ, outcome, live, war.Log, now) && war.Published != null) Publish(war, now);
            }, LogFault, "sortie credit");
        }

        private void ReviewDesk(FactionWar war, float now)
        {
            OperationFocus live = FocusOf(war);
            Guard.Run(deskFaults, now, () => desk.Review(war.HQ, live, war.Log, now), LogFault, "reinforcement desk");
        }
    }
}
