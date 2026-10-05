using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>The TASKED claim of a SOF post (COVER TEAM, LASE TARGET): authority, the (empty) resource slot, and the effect that runs when a pilot claims it.</summary>
    internal sealed partial class SupportManager
    {
        /// <summary>Authority for a SOF post: a connected member of the owning faction with SOF standing. Free: no tier floor, no cooldown, no price.</summary>
        internal TaskedOutcome AuthorizeSofTasked(FactionHQ owner, ulong playerId, SupportActionId action, bool claiming,
            out int baselinePrice, out bool charge, out int detail)
        {
            baselinePrice = 0; charge = !BypassRequirements; detail = 0;
            if (!GameAccess.IsServer() || credits == null || sof == null || settings == null || !settings.SofEnabled.Value || !sof.HasSof(owner) ||
                (playerId == SpaceContacts.WatchOfficerId && claiming) || !SofPosts.IsPost(action)) return TaskedOutcome.Unavailable;
            Player player = playerId == SpaceContacts.WatchOfficerId ? null : FindPlayer(owner, playerId);
            if (playerId != SpaceContacts.WatchOfficerId && (player == null || player.HQ != owner)) return TaskedOutcome.Unavailable;
            if (claiming) baselinePrice = 1; // the fee table prices a SOF post at 0; a positive baseline only says "priced by the host"
            return TaskedOutcome.None;
        }

        internal ITaskedSlot ReserveSofTasked(FactionHQ owner, out TaskedOutcome refusal)
        {
            refusal = sof != null && sof.HasSof(owner) ? TaskedOutcome.None : TaskedOutcome.Unavailable;
            return refusal == TaskedOutcome.None ? new SofSlot(sof, owner) : null;
        }

        /// <summary>Applies the SOF post and reports the launch receipt to the job; a team that no longer needs it means no receipt (the post reopens and withdraws).</summary>
        internal void LaunchSofTasked(FactionHQ owner, TaskedLaunchJob job)
        {
            bool done;
            try { done = sof != null && sof.FirePost(owner, job); }
            catch (Exception e) { logger.LogError(e); done = false; }
            if (!done) { job.ReportFailure(); WithdrawSofPost(owner, job.Action, job.Call.MarkCount > 0 ? job.Call.MarkAt(0).Id - 1 : 0); return; }
            if (!job.ReportSpawn()) logger.LogWarning("[Support.Sof] A post launch receipt was refused after its effect started.");
        }

        private sealed class SofSlot : ITaskedSlot
        {
            private readonly SofService service;
            private readonly FactionHQ owner;
            public SofSlot(SofService service, FactionHQ owner) { this.service = service; this.owner = owner; }
            public bool CanCommit() => service != null && service.HasSof(owner);
            public bool Commit() => true;
            public void Cancel() { }
        }
    }
}
