using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// The host seam of the SOF desk: the wallet (a raise and a mission cost HQ FUND through the same ledger CYBER uses), the pilot pay, and the TASKED board
    /// (posting and withdrawing the COVER and LASE requests). A SOF post is a service, not a strike: free to claim, no bird, no CALL cooldown (see SupportManager.SofTasked).
    /// </summary>
    internal sealed partial class SupportManager
    {
        private SofService sof;

        internal SofService Sof => sof;

        internal void AttachSof(SofService service) => sof = service;

        internal SofOutcome SofSpend(FactionHQ owner, ulong op, int cr, out int detail)
        {
            CyberOutcome o = CyberSpend(owner, op, cr, out detail);
            return o == CyberOutcome.None ? SofOutcome.None : o == CyberOutcome.LowCredit ? SofOutcome.LowCredit : o == CyberOutcome.Frozen ? SofOutcome.Frozen : SofOutcome.Unavailable;
        }

        internal void SofRefund(FactionHQ owner, ulong op, int cr) => CyberRefund(owner, op, cr);

        /// <summary>Pilot pay for a lift, an extraction or a cover (core 6.2), through the capped contributor path.</summary>
        internal void SofPay(FactionHQ owner, ulong pilot, int cr)
        {
            // A pilot who has left the faction (or the game) since the claim or the boarding is paid nothing: the wallet check inside CyberAssist would also route it to the HQ fund.
            if (owner == null || pilot == 0 || FindPlayer(owner, pilot) == null) return;
            CyberAssist(owner, pilot, cr);
        }

        /// <summary>Posts a COVER or LASE request for one team on the faction's TASKED board. False when the faction has no SPACE desk or the board has no room.</summary>
        internal bool PostSofPost(FactionHQ owner, SupportActionId action, int slot, float x, float z, ulong maker)
        {
            if (space == null || !space.TryGetDesk(owner, out TaskedDesk desk) || maker == 0) return false;
            TaskedResult r = maker == SpaceContacts.WatchOfficerId ? desk.PostWatchOfficerPackage(action, slot + 1, x, z) : desk.PostPackage(maker, action, slot + 1, x, z, 1f);
            return r.Outcome == TaskedOutcome.Posted;
        }

        internal void WithdrawSofPost(FactionHQ owner, SupportActionId action, int slot)
        {
            if (space != null && space.TryGetDesk(owner, out TaskedDesk desk)) desk.WithdrawPost(action, slot + 1);
        }
    }
}
