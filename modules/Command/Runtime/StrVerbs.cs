using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Command.Domain;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// The real IStrVerbs: the war view and the sortie view, each resolved when it is used. Explicit presses only (R5): the
    /// gate in StrActions has already refused whatever the model says is not allowed, so a service that is missing here answers false or
    /// says why, and nothing is sent twice.
    /// </summary>
    internal sealed class StrVerbs : IStrVerbs
    {
        private readonly StrViewHub hub;

        public StrVerbs(StrViewHub owner) { hub = owner; }

        public bool Pick(int proposalId, int revision) => ModuleServices.TryGet(out ITheaterWarView war) && war.RequestPick(proposalId, revision);

        public bool Cancel(int operationId, int revision) => ModuleServices.TryGet(out ITheaterWarView war) && war.RequestCancel(operationId, revision);
        public bool Draw(float startX, float startZ, float fixX, float fixZ) => ModuleServices.TryGet(out ITheaterWarView war) && war.RequestDraw(startX, startZ, fixX, fixZ);

        public bool Posture(TheaterWarPosture posture) => ModuleServices.TryGet(out ITheaterWarView war) && war.RequestPosture(posture);

        public void Accept(int contractId)
        {
            if (ModuleServices.TryGet(out ISecondaryObjectivesView sorties)) sorties.RequestAccept(contractId);
        }

        // The game's own two commands, as its contribute menu sends them: the host checks the pilot's funds, queues the group at a depot or credits the
        // pool, and tells the faction. The cooldown is noted here at once, so a second press cannot land before the host's next answer.
        public bool BuyConvoy(int index)
        {
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player == null || player.HQ == null || player.HQ.faction == null ||
                !player.HQ.faction.TryGetConvoyGroup(index, out Faction.ConvoyGroup group)) return false;
            player.CmdPurchaseConvoy(index);
            hub.NoteBought(index, group.coolDown);
            return true;
        }

        public bool Donate(float amount)
        {
            if (!(amount > 0f) || float.IsInfinity(amount) || !GameManager.GetLocalPlayer<Player>(out Player player) || player == null || player.HQ == null) return false;
            player.CmdDonateFactionFunds(Math.Min(amount, player.Allocation));
            hub.NoteDonated();
            return true;
        }
    }
}
