using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Host authority for TASKED calls. The desk (Domain) owns the accounting; this half supplies the live facts it
    /// cannot know: perks, tier floors, cooldowns, the KINETIC bird, and the native rod launch.
    /// </summary>
    internal sealed partial class SupportManager
    {
        /// <summary>The M1 physical launch is the orbital rod; every other action is refused rather than launched wrongly.</summary>
        private static bool TaskedSupported(SupportActionId action) => action == SupportActionId.Artillery;

        internal TaskedDesk CreateTaskedDesk(SpaceService service, FactionHQ owner, SpaceObservations observations)
        {
            if (credits == null || service == null || owner == null) return null;
            var host = new TaskedFactionHost(this, service, owner, observations);
            return new TaskedDesk(credits.FactionKey(owner), host, host, credits.Tasked);
        }

        internal TaskedResult SendTasked(Player player, int[] markIds, int requestId) =>
            space != null ? space.SendTasked(player, markIds, requestId) : new TaskedResult(TaskedOutcome.Unavailable, 0, requestId);

        internal TaskedResult ClaimTasked(Player player, int postId, int requestId, bool favorite = false) =>
            space != null ? space.ClaimTasked(player, postId, requestId, favorite) : new TaskedResult(TaskedOutcome.Unavailable, postId, requestId);

        internal bool TryTaskedResult(Player player, int requestId, out TaskedResult result)
        {
            result = default;
            return space != null && space.TryTaskedResult(player, requestId, out result);
        }

        internal int HumanCount(FactionHQ owner)
        {
            int humans = 0;
            List<Player> players = owner?.GetPlayers(false);
            if (players == null) return 0;
            for (int i = 0; i < players.Count; i++)
                if (players[i] != null && PlayerIdentity.Of(players[i]) != PlayerIdentity.None) humans++;
            return humans;
        }

        private static Player FindPlayer(FactionHQ owner, ulong id)
        {
            List<Player> players = owner?.GetPlayers(false);
            if (players == null || id == PlayerIdentity.None) return null;
            for (int i = 0; i < players.Count; i++)
                if (players[i] != null && PlayerIdentity.Of(players[i]) == id) return players[i];
            return null;
        }

        /// <summary>
        /// SEND needs a supported call, a live uplink and a connected human of the faction. CLAIM adds everything a normal
        /// request needs: perk, tier floor, cooldown, a ready bird and the host price (never a client number).
        /// </summary>
        internal TaskedOutcome AuthorizeTasked(FactionHQ owner, ulong playerId, SupportActionId action, bool claiming,
            out int baselinePrice, out bool charge, out int detail)
        {
            baselinePrice = 0; charge = !BypassRequirements; detail = 0;
            if (!GameAccess.IsServer() || catalog == null || credits == null || !TaskedSupported(action)) return TaskedOutcome.Unavailable;
            SupportActionDefinition definition = catalog.Find(action);
            Player player = FindPlayer(owner, playerId);
            if (definition == null || !definition.Enabled || player == null || player.HQ != owner) return TaskedOutcome.Unavailable;
            if (!TryGetSpaceState(owner, out SpaceState state) || !HasRequiredBird(state, definition.RequiredBird)) return TaskedOutcome.Unavailable;
            if (state.LiveUplinkCount == 0) return TaskedOutcome.UplinkDown;
            if (!claiming) return TaskedOutcome.None;
            float now = MissionNow();
            if (!BypassRequirements && !HostAuthorised(player, definition)) return TaskedOutcome.Locked;
            if (definition.SpaceTask.HasValue && !state.CanStart(definition.SpaceTask.Value, now)) return TaskedOutcome.BirdBusy;
            if (!DisableCooldowns && ledger.IsCoolingDown(playerId, now, CooldownFor(player)))
            {
                detail = Mathf.CeilToInt(ledger.CooldownRemaining(playerId, now, CooldownFor(player)));
                return TaskedOutcome.Cooldown;
            }
            CallQuote price = QuoteFor(definition, player);
            if (price.Cost <= 0 || !CallSheet.TryGet(action, out CallRow row)) return TaskedOutcome.Unavailable;
            baselinePrice = price.Cost;
            if (!BypassRequirements)
            {
                ObjectiveCount census = credits.Census(owner);
                if (!CallFloors.Unlocked(row.Tier, census.held, census.n, now / 60f, 1f)) return TaskedOutcome.Locked;
            }
            return TaskedOutcome.None;
        }

        internal ITaskedSlot ReserveTasked(SpaceService service, FactionHQ owner, SupportActionId action, out TaskedOutcome refusal)
        {
            refusal = TaskedOutcome.Unavailable;
            SupportActionDefinition definition = catalog?.Find(action);
            if (definition == null || !definition.SpaceTask.HasValue || !TryGetSpaceState(owner, out SpaceState state)) return null;
            if (!state.TryReserve(definition.SpaceTask.Value, MissionNow(), out SpaceTaskReservation receipt))
            {
                refusal = state.LiveUplinkCount == 0 ? TaskedOutcome.UplinkDown : TaskedOutcome.BirdBusy;
                return null;
            }
            refusal = TaskedOutcome.None;
            return new TaskedSlot(new SpaceActionTransaction(service, owner, state, receipt, definition.TaskSeconds));
        }

        /// <summary>Runs the native launch. The action reports its physical receipt to the job; no report means no launch.</summary>
        internal void LaunchTasked(FactionHQ owner, TaskedLaunchJob job)
        {
            SupportActionDefinition definition = catalog?.Find(job.Action);
            Player pilot = FindPlayer(owner, job.Pilot);
            if (definition == null || pilot == null || !(job.Slot is TaskedSlot slot)) { job.ReportFailure(); return; }
            var context = new SupportContext(pilot, new GlobalPosition(job.Aim.X, 0f, job.Aim.Z), job.RequestId, this,
                slot.Transaction, job);
            SupportResult result;
            try { result = definition.Action.Execute(context); }
            catch (Exception e) { logger.LogError(e); result = SupportResult.SpawnFailed; }
            if (result != SupportResult.Accepted) job.ReportFailure(TaskedReason(result, job));
            // A rod waiting out its prelaunch dwell reports its receipt or failure itself; anything else that is accepted
            // without a physical receipt is not a launch.
            else if (!job.Final && !job.Deferred) job.ReportFailure();
        }

        /// <summary>The typed receipt for an action that refused the launch: its own named reason, else why the job cannot go.</summary>
        private static TaskedOutcome TaskedReason(SupportResult result, TaskedLaunchJob job)
        {
            switch (result)
            {
                case SupportResult.FriendlyNear: return TaskedOutcome.FriendlyNear;
                case SupportResult.UplinkDown:
                case SupportResult.InvalidTarget:
                    TaskedOutcome why = job.WhyNot;
                    return why == TaskedOutcome.None ? TaskedOutcome.DeliveryFailed : why;
                default: return TaskedOutcome.DeliveryFailed;
            }
        }

        internal void TaskedFired(FactionHQ owner, TaskedLaunchJob job)
        {
            float now = MissionNow();
            // TASKED receipts live only in the desk; the CALLS ledger gets the cooldown, never this request id.
            ledger.StartCooldown(job.Pilot, now);
            Player pilot = FindPlayer(owner, job.Pilot);
            if (pilot != null) credits?.RecordInput(pilot, now);
            try { credits.Assists.Record(credits.FactionKey(owner), job.Aim.X, job.Aim.Z, GetEffectRadius(job.Action, owner), now); }
            catch (Exception e) { logger.LogError(e); }
            logger.LogInfo("[Support] TASKED call " + job.CallId + " fired by " + job.Pilot + " request " + job.RequestId +
                (job.Escrow > 0 ? " for " + job.Escrow + " CR." : "."));
        }

        private sealed class TaskedSlot : ITaskedSlot
        {
            public readonly SpaceActionTransaction Transaction;
            public TaskedSlot(SpaceActionTransaction transaction) { Transaction = transaction; }
            public bool CanCommit() => Transaction.CanLaunch;
            public bool Commit() => Transaction.ReportPhysicalLaunch();
            public void Cancel() => Transaction.Cancel();
        }
    }

    /// <summary>The live-game side of one faction's <see cref="TaskedDesk"/>.</summary>
    internal sealed class TaskedFactionHost : ITaskedPorts, ITaskedLauncher
    {
        private readonly SupportManager manager;
        private readonly SpaceService service;
        private readonly FactionHQ owner;
        private readonly SpaceObservations observations;

        public TaskedFactionHost(SupportManager manager, SpaceService service, FactionHQ owner, SpaceObservations observations)
        {
            this.manager = manager; this.service = service; this.owner = owner; this.observations = observations;
        }

        public float Now => SupportManager.MissionNow();
        public int Humans => manager.HumanCount(owner);
        public SpaceContacts Contacts => observations?.Contacts;

        public TaskedOutcome Authorize(ulong player, SupportActionId action, bool claiming,
            out int baselinePrice, out bool charge, out int detail) =>
            manager.AuthorizeTasked(owner, player, action, claiming, out baselinePrice, out charge, out detail);

        public ITaskedSlot Reserve(ulong player, in TaskedCall call, out TaskedOutcome refusal) =>
            manager.ReserveTasked(service, owner, call.Action, out refusal);

        public void Fired(TaskedLaunchJob job) => manager.TaskedFired(owner, job);
        public void Warn(string message) => Plugin.Logger?.LogWarning(message);
        public void Launch(TaskedLaunchJob job) => manager.LaunchTasked(owner, job);
    }
}
