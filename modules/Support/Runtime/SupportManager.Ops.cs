using System;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// OPERATIONS on the manager. Host side: the wallet (an operation is a sink: FUND CR leaves the economy, a cancel or a BROKEN refund mints back only what the member put in).
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

        internal OpOutcome OpsSpend(FactionHQ owner, ulong op, int cr, out int detail)
        {
            detail = 0;
            if (cr <= 0 || BypassRequirements) return OpOutcome.None;
            if (credits == null) return OpOutcome.Unavailable;
            float now = MissionNow();
            int key = credits.FactionKey(owner);
            // An AI faction's OVERLORD has no wallet: its FUND taps come out of the treasury (HQ FUND), the same sink a member's CR is.
            if (op == SpaceContacts.WatchOfficerId)
            {
                if (!credits.Fund.TrySpend(key, cr)) { detail = cr; return OpOutcome.LowCredit; }
                return OpOutcome.None;
            }
            if (!credits.Tasked.IsActive(op, key, now))
            {
                float frozen = credits.Tasked.FrozenRemaining(op, now);
                detail = (int)Math.Ceiling(frozen);
                return frozen > 0f ? OpOutcome.Frozen : OpOutcome.Unavailable;
            }
            if (credits.Tasked.Balance(op) + 0.001f < cr || !credits.Tasked.TrySpend(op, cr, now)) { detail = cr; return OpOutcome.LowCredit; }
            return OpOutcome.None; // the CR is gone from the economy: an operation is a sink, it does not feed HQ FUND
        }

        /// <summary>The player (by identity) is still in the faction (an operation whose owner left can be cancelled by any member).</summary>
        internal bool InFaction(FactionHQ owner, ulong op) => FindPlayer(owner, op) != null;

        /// <summary>The faction's share of the map objectives it holds (the ASAT victim is the other faction with the chosen bird and the highest share).</summary>
        internal float ObjectiveShare(FactionHQ hq)
        {
            if (hq == null || credits == null) return 0f;
            ObjectiveCount c = credits.Census(hq);
            return Domain.Calls.CallFloors.Share(c.held, c.contested, c.n);
        }

        internal void OpsRefund(FactionHQ owner, ulong op, int cr)
        {
            if (cr <= 0 || BypassRequirements || credits == null) return;
            if (op == SpaceContacts.WatchOfficerId) { credits.Fund.Add(credits.FactionKey(owner), cr); return; } // back into the treasury it came from
            credits.Tasked.Refund(op, cr);
        }

        /// <summary>One FUND, PLAN or CANCEL for a transport-authenticated player (host only; the desk judges it).</summary>
        internal OpResult RunOpsVerb(Player player, in SpaceCommand command) =>
            ops != null ? ops.Verb(player, command) : new OpResult(OpOutcome.Unavailable);

        /// <summary>The NET or SOF page is on screen: while it is, the feed asks the host for a state.</summary>
        internal MirrorFeed<OpsStateData> OpsFeed => opsFeed ?? (opsFeed = new MirrorFeed<OpsStateData>(opsMirror, SpaceCommandKind.OpSync));

        /// <summary>FUND tap: 25 CR (<paramref name="large"/> false) or 50 CR. Returns the request id (0 when not sent).</summary>
        internal int OpsFund(OpDomain domain, bool large) => SendSpace(SpaceCommandKind.OpFund, (int)domain | (large ? 2 : 0), null);

        /// <summary>Start (or retarget) an operation. The target is a bird 0..2 (ASAT), a SAM C2 node id (ZERO-DAY) or a held building id (FOB).</summary>
        internal int OpsPlan(OpKind kind, int target) =>
            OpsRules.Valid(kind) && target >= 0 ? SendSpace(SpaceCommandKind.OpPlan, 0, new[] { (int)kind, target }) : 0;

        internal int OpsCancel(OpDomain domain) => SendSpace(SpaceCommandKind.OpCancel, (int)domain, null);
    }
}
