using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// The real IStrVerbs: the war view, the sortie view and the two wing seams, each resolved when it is used. Explicit presses only (R5): the
    /// gate in StrActions has already refused whatever the model says is not allowed, so a service that is missing here answers false or
    /// says why, and nothing is sent twice. SEND WING is host only; a client has neither wing service.
    /// </summary>
    internal sealed class StrVerbs : IStrVerbs
    {
        private readonly StrViewHub hub;

        public StrVerbs(StrViewHub owner) { hub = owner; }

        public bool Pick(int proposalId, int revision) => ModuleServices.TryGet(out ITheaterWarView war) && war.RequestPick(proposalId, revision);

        public bool Cancel(int operationId, int revision) => ModuleServices.TryGet(out ITheaterWarView war) && war.RequestCancel(operationId, revision);

        public bool Posture(TheaterWarPosture posture) => ModuleServices.TryGet(out ITheaterWarView war) && war.RequestPosture(posture);

        public void Accept(int contractId)
        {
            if (ModuleServices.TryGet(out ISecondaryObjectivesView sorties)) sorties.RequestAccept(contractId);
        }

        public bool SendWing(int contractId, out string answer)
        {
            answer = "";
            if (!GameAccess.IsServer()) { answer = "ONLY THE HOST CAN SEND A WING"; return false; }
            if (!ModuleServices.TryGet(out ISortieWingOrderSource source) || !ModuleServices.TryGet(out IWingSortieOrders orders))
            {
                answer = "THE WING DESK IS NOT AVAILABLE IN THIS MISSION";
                return false;
            }
            if (!source.TryGetWingOrder(contractId, out WingSortieOrder order))
            {
                answer = "THIS SORTIE IS NOT ON THE BOARD";
                return false;
            }
            return orders.Send(in order, out answer);
        }

        /// <summary>Recalls the sortie the wing is tasked on, as the last 1 Hz wing read found it; there is only ever one.</summary>
        public bool Recall(out string answer)
        {
            answer = "";
            if (!GameAccess.IsServer()) { answer = "ONLY THE HOST CAN RECALL A WING"; return false; }
            if (hub.TaskedSortieId < 0 || !ModuleServices.TryGet(out IWingSortieOrders orders))
            {
                answer = "NO WING TASKING TO RECALL";
                return false;
            }
            return orders.Recall(hub.TaskedSortieId, out answer);
        }
    }
}
