using System;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Space;
using UnityEngine;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// OPERATIONS on the manager. Host side: allocation (an operation is a sink: FUND leaves the economy, a cancel or a BROKEN refund gives back only what the member put in).
    /// Client side: the mirror of the local player's own faction and the verbs the OPERATION box sends. A press is a request, never an effect; the verdict arrives through
    /// <see cref="SpaceReplied"/> like every SPACE reply.
    /// </summary>
    internal sealed partial class SupportManager
    {
        private OpsService ops;
        private readonly OpsMirror opsMirror = new OpsMirror();
        private MirrorFeed<OpsStateData> opsFeed;

        internal OpsService Ops => ops;
        internal void AttachOps(OpsService service) => ops = service;

        /// <summary>The local player's faction OPERATIONS view as the host last told it (own faction only; flights are everyone's, pings the enemies').</summary>
        internal OpsMirror OpsMirror => opsMirror;

        /// <summary>FUND taps cost the member's vanilla allocation 1:1 (an operation is a sink); an AI faction's OVERLORD spends the faction's vanilla funds instead.</summary>
        internal OpOutcome OpsSpend(FactionHQ owner, ulong op, int cr, out int detail)
        {
            detail = 0;
            if (cr <= 0 || BypassRequirements) return OpOutcome.None;
            if (op == SpaceContacts.WatchOfficerId)
            {
                if (owner == null || CyberTreasury(owner) + 0.001f < cr) { detail = cr; return OpOutcome.LowCredit; }
                owner.AddFunds(-cr);
                return OpOutcome.None;
            }
            Player player = FindPlayer(owner, op);
            if (player == null) return OpOutcome.Unavailable;
            if (TrySpendAllocation(player, cr)) return OpOutcome.None;
            detail = cr;
            return OpOutcome.LowCredit;
        }

        /// <summary>The player (by identity) is still in the faction (an operation whose owner left can be cancelled by any member).</summary>
        internal bool InFaction(FactionHQ owner, ulong op) => FindPlayer(owner, op) != null;

        /// <summary>The faction's share of the map objectives it holds (held plus half the contested, over every ground airbase); the ASAT victim is the other faction with the chosen bird and the highest share.</summary>
        internal float ObjectiveShare(FactionHQ hq)
        {
            if (hq == null) return 0f;
            ObjectiveCount c = Census(hq);
            if (c.n <= 0) return float.NaN;
            return Mathf.Clamp01((Mathf.Max(0, c.held) + 0.5f * Mathf.Max(0, c.contested)) / c.n);
        }

        /// <summary>A cancelled or broken operation gives back what was put in: allocation to the member, funds to an AI faction.</summary>
        internal void OpsRefund(FactionHQ owner, ulong op, int cr)
        {
            if (cr <= 0 || BypassRequirements) return;
            if (op == SpaceContacts.WatchOfficerId) { owner?.AddFunds(cr); return; } // back into the treasury it came from
            RefundAllocation(FindPlayer(owner, op), cr);
        }

        /// <summary>One FUND, PLAN or CANCEL for a transport-authenticated player (host only; the desk judges it).</summary>
        internal OpResult RunOpsVerb(Player player, in SpaceCommand command) =>
            ops != null ? ops.Verb(player, command) : new OpResult(OpOutcome.Unavailable);

        /// <summary>The NET or SOF page is on screen: while it is, the feed asks the host for a state.</summary>
        internal MirrorFeed<OpsStateData> OpsFeed => opsFeed ?? (opsFeed = new MirrorFeed<OpsStateData>(opsMirror, SpaceCommandKind.OpSync));

        /// <summary>FUND tap: 25 allocation (<paramref name="large"/> false) or 50. Returns the request id (0 when not sent).</summary>
        internal int OpsFund(OpDomain domain, bool large) => SendSpace(SpaceCommandKind.OpFund, (int)domain | (large ? 2 : 0), null);

        /// <summary>Start (or retarget) an operation. The target is a bird 0..2 (ASAT), a SAM C2 node id (ZERO-DAY) or a held building id (FOB).</summary>
        internal int OpsPlan(OpKind kind, int target) =>
            OpsRules.Valid(kind) && target >= 0 ? SendSpace(SpaceCommandKind.OpPlan, 0, new[] { (int)kind, target }) : 0;

        internal int OpsCancel(OpDomain domain) => SendSpace(SpaceCommandKind.OpCancel, (int)domain, null);
    }
}
