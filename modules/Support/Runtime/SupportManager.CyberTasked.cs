using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>The TASKED claim of a CYBER package: authority, the (empty) resource slot, and the launch that starts the effect.</summary>
    internal sealed partial class SupportManager
    {
        /// <summary>
        /// Authority for a CYBER package: a connected member of the owning faction, CYBER enabled and standing, a cooldown that is not running.
        /// There is no bird, no uplink and no tier floor: the package was earned by the operator's work (spec §0). A claim prices from the tier.
        /// </summary>
        internal TaskedOutcome AuthorizeCyberTasked(FactionHQ owner, ulong playerId, SupportActionId action, bool claiming,
            out int baselinePrice, out bool charge, out int detail)
        {
            baselinePrice = 0; charge = !BypassRequirements; detail = 0;
            if (!GameAccess.IsServer() || credits == null || cyber == null || settings == null || !settings.CyberEnabled.Value || !cyber.HasCyber(owner) ||
                (playerId == SpaceContacts.WatchOfficerId && claiming) || !TaskedKinds.TryGet(action, out TaskedKind kind)) return TaskedOutcome.Unavailable;
            Player player = playerId == SpaceContacts.WatchOfficerId ? null : FindPlayer(owner, playerId);
            if (playerId != SpaceContacts.WatchOfficerId && (player == null || player.HQ != owner)) return TaskedOutcome.Unavailable;
            if (!claiming) return TaskedOutcome.None;
            float now = MissionNow();
            if (!DisableCooldowns && ledger.IsCoolingDown(playerId, now, CooldownFor(player)))
            {
                detail = Mathf.CeilToInt(ledger.CooldownRemaining(playerId, now, CooldownFor(player)));
                return TaskedOutcome.Cooldown;
            }
            baselinePrice = CallSheet.BasePrice(kind.Tier);
            return TaskedOutcome.None;
        }

        /// <summary>A package needs no physical resource: the slot only checks the faction's CYBER still stands.</summary>
        internal ITaskedSlot ReserveCyberTasked(FactionHQ owner, out TaskedOutcome refusal)
        {
            refusal = cyber != null && cyber.HasCyber(owner) ? TaskedOutcome.None : TaskedOutcome.Unavailable;
            return refusal == TaskedOutcome.None ? new CyberSlot(cyber, owner) : null;
        }

        /// <summary>Starts the package effect and reports the launch receipt to the job; no effect, no receipt.</summary>
        internal void LaunchCyberTasked(FactionHQ owner, TaskedLaunchJob job)
        {
            bool started;
            try { started = cyber != null && cyber.FirePackage(owner, job); }
            catch (Exception e) { logger.LogError(e); started = false; }
            if (!started) { job.ReportFailure(); return; }
            if (!job.ReportSpawn()) logger.LogWarning("[Support.Cyber] A package launch receipt was refused after its effect started.");
        }

        /// <summary>A small operator assist (an enemy killed while its node is held), through the capped contributor path.</summary>
        internal void CyberAssist(FactionHQ owner, ulong op, float amount)
        {
            if (credits == null || settings == null || op == SpaceContacts.WatchOfficerId) return; // OVERLORD earns nothing
            ContributorCreditReceipt r = credits.Tasked.EarnContributor(op, credits.FactionKey(owner), amount * settings.EarnKnob.Value, MissionNow());
            if (r.Unapplied > 0f) credits.Tasked.AddHq(credits.FactionKey(owner), r.Unapplied);
        }

        private sealed class CyberSlot : ITaskedSlot
        {
            private readonly CyberService service;
            private readonly FactionHQ owner;
            public CyberSlot(CyberService service, FactionHQ owner) { this.service = service; this.owner = owner; }
            public bool CanCommit() => service != null && service.HasCyber(owner);
            public bool Commit() => true;
            public void Cancel() { }
        }
    }
}
