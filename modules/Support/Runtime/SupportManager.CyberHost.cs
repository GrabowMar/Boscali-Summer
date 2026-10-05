using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// The host seam of the CYBER desk: the wallet (an intrusion start and its upkeep), the restore fund (HQ FUND) and the TASKED board
    /// (BURN packages). Intrusion CR goes to HQ FUND, which pays for the EW truck and data center restore bars; the dev bypass charges nothing.
    /// </summary>
    internal sealed partial class SupportManager
    {
        private CyberService cyber;

        internal CyberService Cyber => cyber;

        internal void AttachCyber(CyberService service) => cyber = service;

        internal CyberOutcome CyberSpend(FactionHQ owner, ulong op, int cr, out int detail)
        {
            detail = 0;
            if (cr <= 0 || BypassRequirements) return CyberOutcome.None;
            if (credits == null) return CyberOutcome.Unavailable;
            float now = MissionNow();
            int key = credits.FactionKey(owner);
            if (!credits.Tasked.IsActive(op, key, now))
            {
                float frozen = credits.Tasked.FrozenRemaining(op, now);
                detail = (int)Math.Ceiling(frozen);
                return frozen > 0f ? CyberOutcome.Frozen : CyberOutcome.Unavailable;
            }
            if (credits.Tasked.Balance(op) + 0.001f < cr) { detail = cr; return CyberOutcome.LowCredit; }
            if (!credits.Tasked.TrySpend(op, cr, now)) { detail = cr; return CyberOutcome.LowCredit; }
            credits.Tasked.AddHq(key, cr);
            return CyberOutcome.None;
        }

        internal void CyberRefund(FactionHQ owner, ulong op, int cr)
        {
            if (cr <= 0 || BypassRequirements || credits == null) return;
            // The refund comes out of HQ FUND (where the start cost went): if the fund no longer holds it, nothing is minted.
            if (!credits.Fund.TrySpend(credits.FactionKey(owner), cr)) { Plugin.Logger?.LogWarning("[Support.Cyber] Refund of " + cr + " CR skipped: HQ FUND is short."); return; }
            credits.Tasked.Refund(op, cr);
        }

        internal float CyberTreasury(FactionHQ owner) => credits != null ? credits.Fund.Balance(credits.FactionKey(owner)) : 0f;

        internal void CyberTreasurySpend(FactionHQ owner, float amount)
        {
            if (credits != null) credits.Fund.TrySpend(credits.FactionKey(owner), amount);
        }

        /// <summary>Posts a BURN package on the faction's TASKED board. False when the faction has no SPACE desk or the board has no room.</summary>
        internal CyberOutcome PostCyberPackage(FactionHQ owner, ulong op, in CyberNode node, in PackageDef def, bool exploit, float effort)
        {
            if (space == null || !space.TryGetDesk(owner, out TaskedDesk desk)) return CyberOutcome.NoBoard;
            TaskedResult r = desk.PostPackage(op, def.Action, node.Id, node.X, node.Z, effort);
            if (r.Outcome == TaskedOutcome.Posted) return CyberOutcome.None;
            if (r.Outcome == TaskedOutcome.NotPosted)
                // Every board holds at least six posts, so a refusal on a smaller board means the same package is already posted on that node.
                return desk.Board.Count < 6 ? CyberOutcome.AlreadyPosted : CyberOutcome.BoardFull;
            return CyberOutcome.Unavailable;
        }
    }
}
