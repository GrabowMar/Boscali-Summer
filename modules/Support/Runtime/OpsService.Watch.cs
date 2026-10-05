using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// The host path of an AI-controlled faction's operations (the AI doctrine, spec section 4): the same PLAN and FUND a member sends, as the reserved WATCH OFFICER identity. A target is
    /// resolved against the faction's own fog exactly as a client's id is, and the cost comes out of the treasury (HQ FUND) through <see cref="SupportManager.OpsSpend"/>.
    /// </summary>
    internal sealed partial class OpsService
    {
        internal OpResult WatchPlan(FactionHQ owner, OpKind kind, int target)
        {
            if (!GameAccess.IsServer() || !Enabled || owner == null || !factions.TryGetValue(owner, out FactionOps f)) return new OpResult(OpOutcome.Offline);
            if (!OpsRules.Valid(kind)) return new OpResult(OpOutcome.NoOperation);
            if (!DomainOnline(f, OpsRules.DomainOf(kind))) return new OpResult(OpOutcome.Offline, kind);
            if (!TryTarget(f, kind, target, out OpTarget resolved)) return new OpResult(OpOutcome.NoTarget, kind);
            return f.Desk.Plan(SpaceContacts.WatchOfficerId, kind, resolved);
        }

        internal OpResult WatchFund(FactionHQ owner, OpDomain domain, bool large)
        {
            if (!GameAccess.IsServer() || !Enabled || owner == null || !factions.TryGetValue(owner, out FactionOps f)) return new OpResult(OpOutcome.Offline);
            return f.Desk.Fund(SpaceContacts.WatchOfficerId, domain, large ? OpsRules.FundLarge : OpsRules.FundSmall);
        }
    }
}
