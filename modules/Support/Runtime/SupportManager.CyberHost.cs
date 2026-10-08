using System;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// The host seam of the CYBER desk: the allocation an intrusion start and its upkeep cost, and the TASKED board (BURN packages).
    /// The dev bypass charges nothing.
    /// </summary>
    internal sealed partial class SupportManager
    {
        private CyberService cyber;

        internal CyberService Cyber => cyber;

        internal void AttachCyber(CyberService service) => cyber = service;

        /// <summary>An operator verb (an intrusion start, a node's upkeep) costs the operator's vanilla allocation 1:1 with the CR figure the desk quotes. OVERLORD pays nothing; the dev bypass charges nothing.</summary>
        internal CyberOutcome CyberSpend(FactionHQ owner, ulong op, int cr, out int detail)
        {
            detail = 0;
            if (cr <= 0 || BypassRequirements) return CyberOutcome.None;
            // WATCH OFFICER OVERLORD has no wallet, exactly like SPACE's: its starts, its upkeep and its raises are free, bounded by the pacing and the caps instead.
            if (op == SpaceContacts.WatchOfficerId) return CyberOutcome.None;
            Player player = FindPlayer(owner, op);
            if (player == null) return CyberOutcome.Unavailable;
            if (TrySpendAllocation(player, cr)) return CyberOutcome.None;
            detail = cr;
            return CyberOutcome.LowCredit;
        }

        internal void CyberRefund(FactionHQ owner, ulong op, int cr)
        {
            if (cr <= 0 || BypassRequirements || op == SpaceContacts.WatchOfficerId) return; // OVERLORD paid nothing, so nothing comes back
            RefundAllocation(FindPlayer(owner, op), cr);
        }

        /// <summary>The faction's vanilla funds: what an AI faction's OVERLORD spends on operations.</summary>
        internal float CyberTreasury(FactionHQ owner) => owner != null && float.IsFinite(owner.factionFunds) ? owner.factionFunds : 0f;

        /// <summary>Posts a BURN package on the faction's TASKED board. False when the faction has no SPACE desk or the board has no room.</summary>
        internal CyberOutcome PostCyberPackage(FactionHQ owner, ulong op, in CyberNode node, in PackageDef def, bool exploit, float effort)
        {
            if (space == null || !space.TryGetDesk(owner, out TaskedDesk desk)) return CyberOutcome.NoBoard;
            // OVERLORD's package is labelled WATCH OFFICER and carries no effort: its own desk path, never a human maker id.
            TaskedResult r = op == SpaceContacts.WatchOfficerId ? desk.PostWatchOfficerPackage(def.Action, node.Id, node.X, node.Z)
                : desk.PostPackage(op, def.Action, node.Id, node.X, node.Z, effort);
            if (r.Outcome == TaskedOutcome.Posted) return CyberOutcome.None;
            if (r.Outcome == TaskedOutcome.NotPosted)
                // Every board holds at least six posts, so a refusal on a smaller board means the same package is already posted on that node.
                return desk.Board.Count < 6 ? CyberOutcome.AlreadyPosted : CyberOutcome.BoardFull;
            return CyberOutcome.Unavailable;
        }
    }
}
