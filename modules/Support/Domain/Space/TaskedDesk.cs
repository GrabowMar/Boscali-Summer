using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    /// <summary>Typed host verdict for SEND and CLAIM. Every value has words; clients never infer a reason.</summary>
    internal enum TaskedOutcome : byte
    {
        None = 0,
        Posted, Queued, Fired,
        Unavailable, NoCall, NotPosted, ClaimedByOther, Busy,
        LowCredit, Frozen, Locked, Cooldown, BirdBusy, UplinkDown,
        DeliveryFailed, TimedOut, SceneEnded
    }

    internal static class TaskedWords
    {
        public static string Of(TaskedOutcome outcome, int detail = 0)
        {
            switch (outcome)
            {
                case TaskedOutcome.None: return "";
                case TaskedOutcome.Posted: return "TASKED CALL POSTED — FIRST PILOT TO CLAIM IT FIRES";
                case TaskedOutcome.Queued: return "CLAIM SENT — STAND BY";
                case TaskedOutcome.Fired: return "TASKED CALL FIRED";
                case TaskedOutcome.NoCall: return "NEGATIVE: NO SUCH TASKED CALL — IT EXPIRED OR WAS FIRED";
                case TaskedOutcome.NotPosted: return "NEGATIVE: NOT POSTED — MARK AGAIN OR WAIT FOR A FREE SLOT";
                case TaskedOutcome.ClaimedByOther: return "NEGATIVE: ANOTHER PILOT HAS THIS CALL — IT REOPENS IF THEIR LAUNCH FAILS";
                case TaskedOutcome.Busy: return CallWords.Refusal(CallRefusal.Busy);
                case TaskedOutcome.LowCredit: return CallWords.Refusal(CallRefusal.LowCredit, need: detail);
                case TaskedOutcome.Frozen: return CallWords.Refusal(CallRefusal.Frozen, seconds: detail);
                case TaskedOutcome.Locked: return CallWords.Refusal(CallRefusal.Locked);
                case TaskedOutcome.Cooldown: return CallWords.Refusal(CallRefusal.Cooldown, seconds: detail);
                case TaskedOutcome.BirdBusy: return "NEGATIVE: BIRD BUSY — WAIT FOR THE NEXT TASK";
                case TaskedOutcome.UplinkDown: return "NEGATIVE: UPLINK DOWN — RESTORE THE SITE";
                case TaskedOutcome.DeliveryFailed: return "NEGATIVE: DELIVERY FAILED — CR RETURNED, CLAIM AGAIN";
                case TaskedOutcome.TimedOut: return "NEGATIVE: LAUNCH TIMED OUT — CR RETURNED, CLAIM AGAIN";
                case TaskedOutcome.SceneEnded: return "NEGATIVE: MISSION ENDED";
                default: return CallWords.Refusal(CallRefusal.Unavailable);
            }
        }
    }

    internal readonly struct TaskedResult
    {
        public readonly TaskedOutcome Outcome;
        public readonly int CallId, RequestId, Charged, Detail;
        public readonly bool Replayed;

        public TaskedResult(TaskedOutcome outcome, int callId, int requestId, int charged = 0, int detail = 0, bool replayed = false)
        {
            Outcome = outcome; CallId = callId; RequestId = requestId; Charged = charged; Detail = detail; Replayed = replayed;
        }

        public bool Final => Outcome != TaskedOutcome.Queued;
        public string Words => TaskedWords.Of(Outcome, Detail);
        internal TaskedResult AsReplay() => new TaskedResult(Outcome, CallId, RequestId, Charged, Detail, true);
    }

    /// <summary>
    /// The fixed ground point of a post and the host's confirmed-MARK provenance. A fire-control selector
    /// (<see cref="SpaceFireControl.TrySample"/>) consumes exactly these fields; M1 launches with the STANDARD behaviour.
    /// </summary>
    internal readonly struct TaskedAim
    {
        public readonly float X, Z;
        public readonly bool ConfirmedMark, SarOnly;
        public TaskedAim(float x, float z, bool confirmedMark, bool sarOnly) { X = x; Z = z; ConfirmedMark = confirmedMark; SarOnly = sarOnly; }
    }

    /// <summary>The exclusive physical resources reserved for one launch (the KINETIC bird). Host runtime implements it.</summary>
    internal interface ITaskedSlot
    {
        /// <summary>Non-mutating: the resources would still accept a physical launch right now.</summary>
        bool CanCommit();
        /// <summary>Records the physical launch (bird busy and cooldown). Called once, only after the board accepted the receipt.</summary>
        bool Commit();
        void Cancel();
    }

    /// <summary>Everything the desk needs from the live game: clock, census, contacts and non-money host authority.</summary>
    internal interface ITaskedPorts
    {
        float Now { get; }
        /// <summary>Connected humans of this faction (host census; the desk holds the 60 s profile hysteresis).</summary>
        int Humans { get; }
        SpaceContacts Contacts { get; }
        /// <summary>
        /// Host authority for one player and action: identity, authorisation, tier floor, request cooldown and live uplinks.
        /// <paramref name="charge"/> is false only when the host waives all charging (the dev bypass).
        /// <paramref name="baselinePrice"/> is the host-priced STANDARD call for this player.
        /// </summary>
        TaskedOutcome Authorize(ulong player, SupportActionId action, bool claiming,
            out int baselinePrice, out bool charge, out int detail);
        /// <summary>Reserves the physical resources, or returns null with the typed reason.</summary>
        ITaskedSlot Reserve(ulong player, in TaskedCall call, out TaskedOutcome refusal);
        /// <summary>A physical launch was accepted and settled.</summary>
        void Fired(TaskedLaunchJob job);
        void Warn(string message);
    }

    internal interface ITaskedLauncher
    {
        /// <summary>
        /// Starts the physical launch. Immediately after a real native spawn the launcher must call
        /// <see cref="TaskedLaunchJob.ReportSpawn"/> and destroy that spawn when it returns false; with no spawn it must call
        /// <see cref="TaskedLaunchJob.ReportFailure"/>. It may do either later (delayed launches).
        /// </summary>
        void Launch(TaskedLaunchJob job);
    }

    /// <summary>One reserved claim's immutable launch work. Only the final launch receipt settles it.</summary>
    internal sealed class TaskedLaunchJob
    {
        public readonly int CallId, RequestId, Escrow;
        public readonly ulong Pilot;
        public readonly SupportActionId Action;
        public readonly TaskedAim Aim;
        public readonly float LaunchedAt;
        internal readonly TaskedDesk Desk;
        internal readonly ITaskedSlot Slot;
        internal readonly TaskedClaim Claim;
        internal readonly TaskedCall Call;

        internal TaskedLaunchJob(TaskedDesk desk, ITaskedSlot slot, in TaskedClaim claim, TaskedCall call,
            ulong pilot, int requestId, int escrow, float launchedAt)
        {
            Desk = desk; Slot = slot; Claim = claim; Call = call; Pilot = pilot; RequestId = requestId; Escrow = escrow;
            LaunchedAt = launchedAt; CallId = call.Id; Action = call.Action;
            SpaceMark mark = call.MarkAt(0);
            Aim = new TaskedAim(mark.X, mark.Z, true, mark.Source == BirdKind.Radar);
        }

        public bool Final { get; internal set; }
        public bool Spawned { get; internal set; }

        /// <summary>Non-mutating preflight to run immediately before the native spawn.</summary>
        public bool CanLaunch => Desk.CanLaunch(this);
        /// <summary>The native launch happened. False means the receipt was refused: delete that spawn.</summary>
        public bool ReportSpawn() => Desk.ReportSpawn(this);
        /// <summary>No native launch happened: the escrow returns and the call reopens.</summary>
        public void ReportFailure(TaskedOutcome reason = TaskedOutcome.DeliveryFailed) => Desk.ReportFailure(this, reason);
    }

    /// <summary>
    /// One faction's host-authoritative TASKED path: SEND, atomic CLAIM, escrow, launch receipt and fee settlement.
    /// Every request id replays its exact receipt; no client supplies a price, weight or outcome.
    /// </summary>
    internal sealed class TaskedDesk
    {
        public const int MaxReceipts = 256;
        private const float HousekeepingSeconds = .5f;

        private enum Kind : byte { Send, Claim }
        private readonly struct Key : IEquatable<Key>
        {
            public readonly ulong Player;
            public readonly int Request;
            public readonly Kind Kind;
            public Key(ulong player, int request, Kind kind) { Player = player; Request = request; Kind = kind; }
            public bool Equals(Key other) => Player == other.Player && Request == other.Request && Kind == other.Kind;
            public override bool Equals(object obj) => obj is Key key && Equals(key);
            public override int GetHashCode() => unchecked((int)Player * 397 ^ Request * 31 ^ (int)Kind);
        }
        private sealed class Receipt { public TaskedResult Result; public bool Pending; }
        private sealed class Contender { public ulong Pilot; public int RequestId; }
        private sealed class Waiting { public int CallId; public float FirstAt; public readonly List<Contender> Contenders = new List<Contender>(); }

        private readonly int faction;
        private readonly ITaskedPorts ports;
        private readonly ITaskedLauncher launcher;
        private readonly TaskedWallets wallets;
        private readonly TaskedBoard board = new TaskedBoard();
        private readonly Dictionary<Key, Receipt> receipts = new Dictionary<Key, Receipt>();
        private readonly Queue<Key> order = new Queue<Key>();
        private readonly List<Waiting> waiting = new List<Waiting>(TaskedBoard.MaxCalls);
        private readonly List<TaskedLaunchJob> inflight = new List<TaskedLaunchJob>(4);
        private TaskedHumanProfile profile;
        private float nextHousekeeping;
        private int nextCallId;
        private bool retired, advancing;

        public TaskedDesk(int faction, ITaskedPorts ports, ITaskedLauncher launcher, TaskedWallets wallets)
        {
            this.faction = faction; this.ports = ports; this.launcher = launcher; this.wallets = wallets;
        }

        public TaskedBoard Board => board;
        public int ReceiptCount => receipts.Count;
        public int InFlight => inflight.Count;
        public bool Retired => retired;
        /// <summary>Raised once when a queued claim reaches its final verdict (winner, loser, failure or scene end).</summary>
        public event Action<ulong, int, TaskedResult> Resolved;

        // ---- SEND ------------------------------------------------------------------------------

        public TaskedResult Send(ulong player, int requestId, int[] markIds)
        {
            if (player == 0 || requestId <= 0) return new TaskedResult(TaskedOutcome.Unavailable, 0, requestId);
            var key = new Key(player, requestId, Kind.Send);
            if (receipts.TryGetValue(key, out Receipt known)) return known.Result.AsReplay();
            if (retired) return new TaskedResult(TaskedOutcome.Unavailable, 0, requestId);
            Advance();
            float now = ports.Now;
            if (!SpaceRules.MissionTime(now)) return new TaskedResult(TaskedOutcome.Unavailable, 0, requestId);
            TaskedOutcome refusal = ports.Authorize(player, SupportActionId.Artillery, false, out _, out _, out int detail);
            if (refusal != TaskedOutcome.None) return Remember(key, refusal, 0, 0, detail);
            SpaceContacts contacts = ports.Contacts;
            if (contacts == null) return Remember(key, TaskedOutcome.Unavailable, 0, 0, 0);
            if (profile == null) profile = new TaskedHumanProfile(ports.Humans, now);
            int humans = profile.Observe(ports.Humans, now);
            int id = nextCallId + 1;
            if (!MakeRoom()) return new TaskedResult(TaskedOutcome.Busy, 0, requestId);
            if (id <= 0 || !board.TryPostVerified(id, SupportActionId.Artillery, player, false, humans, markIds, contacts, now, out _))
                return Remember(key, TaskedOutcome.NotPosted, 0, 0, 0);
            nextCallId = id;
            return Remember(key, TaskedOutcome.Posted, id, 0, 0);
        }

        // ---- CLAIM -----------------------------------------------------------------------------

        public TaskedResult Claim(ulong player, int requestId, int callId, bool favorite = false)
        {
            if (player == 0 || requestId <= 0) return new TaskedResult(TaskedOutcome.Unavailable, callId, requestId);
            var key = new Key(player, requestId, Kind.Claim);
            if (receipts.TryGetValue(key, out Receipt known)) return known.Result.AsReplay();
            if (retired) return new TaskedResult(TaskedOutcome.Unavailable, callId, requestId);
            Advance(); // an overdue arbitration window resolves before this claim is judged
            if (receipts.TryGetValue(key, out known)) return known.Result.AsReplay();
            float now = ports.Now;
            if (!SpaceRules.MissionTime(now)) return new TaskedResult(TaskedOutcome.Unavailable, callId, requestId);
            if (!board.TryGet(callId, out TaskedCall call)) return Remember(key, TaskedOutcome.NoCall, callId, 0, 0);
            TaskedOutcome refusal = Judge(player, call, now, out _, out int detail);
            if (refusal != TaskedOutcome.None) return Remember(key, refusal, callId, 0, detail);
            Waiting queue = FindWaiting(callId);
            if (queue != null)
                for (int i = 0; i < queue.Contenders.Count; i++)
                    if (queue.Contenders[i].Pilot == player) return new TaskedResult(TaskedOutcome.Busy, callId, requestId);
            if (!MakeRoom()) return new TaskedResult(TaskedOutcome.Busy, callId, requestId);
            if (!board.EnqueueClaim(callId, requestId, player, favorite, now))
                return Remember(key, board.TryGet(callId, out _) ? TaskedOutcome.ClaimedByOther : TaskedOutcome.NoCall, callId, 0, 0);
            if (queue == null) { queue = new Waiting { CallId = callId, FirstAt = now }; waiting.Add(queue); }
            queue.Contenders.Add(new Contender { Pilot = player, RequestId = requestId });
            var queued = new TaskedResult(TaskedOutcome.Queued, callId, requestId);
            Store(key, queued, true);
            return queued;
        }

        public bool TryResult(ulong player, int requestId, out TaskedResult result)
        {
            if (receipts.TryGetValue(new Key(player, requestId, Kind.Claim), out Receipt claim)) { result = claim.Result; return true; }
            if (receipts.TryGetValue(new Key(player, requestId, Kind.Send), out Receipt send)) { result = send.Result; return true; }
            result = default;
            return false;
        }

        // ---- Time ------------------------------------------------------------------------------

        /// <summary>Call every frame: resolves arbitration windows, starts launches and enforces the LAUNCHING deadline.</summary>
        public void Tick() => Advance();

        private void Advance()
        {
            if (retired || advancing) return;
            float now = ports.Now;
            if (!SpaceRules.MissionTime(now)) return;
            advancing = true;
            try
            {
                for (int i = inflight.Count - 1; i >= 0; i--)
                    if (i < inflight.Count && now >= inflight[i].LaunchedAt + TaskedBoard.LaunchSeconds) Fail(inflight[i], TaskedOutcome.TimedOut);
                if (waiting.Count > 0 || now >= nextHousekeeping)
                {
                    nextHousekeeping = now + HousekeepingSeconds;
                    profile?.Observe(ports.Humans, now);
                    board.Prune(now);
                }
                for (int i = waiting.Count - 1; i >= 0; i--)
                {
                    if (i >= waiting.Count) continue;
                    Waiting queue = waiting[i];
                    if (!board.TryGet(queue.CallId, out TaskedCall call))
                    {
                        waiting.RemoveAt(i);
                        FinishAll(queue, TaskedOutcome.NoCall);
                        continue;
                    }
                    if (now < queue.FirstAt + TaskedBoard.ArbitrationSeconds) continue;
                    waiting.RemoveAt(i);
                    if (!board.TryReserve(queue.CallId, now, out TaskedClaim claim)) { FinishAll(queue, TaskedOutcome.NoCall); continue; }
                    Contender winner = null;
                    for (int c = 0; c < queue.Contenders.Count; c++)
                    {
                        Contender contender = queue.Contenders[c];
                        if (contender.Pilot == claim.Pilot && contender.RequestId == claim.RequestId) { winner = contender; continue; }
                        Finish(new Key(contender.Pilot, contender.RequestId, Kind.Claim), TaskedOutcome.ClaimedByOther, queue.CallId, 0, 0);
                    }
                    if (winner == null) { board.Release(claim); continue; }
                    Start(winner, claim, call, now);
                }
            }
            finally { advancing = false; }
        }

        // ---- Launch ----------------------------------------------------------------------------

        private void Start(Contender winner, in TaskedClaim claim, TaskedCall call, float now)
        {
            var key = new Key(winner.Pilot, winner.RequestId, Kind.Claim);
            // Revalidate everything the host decides, then take the money only after the bird is ours.
            TaskedOutcome refusal = Judge(winner.Pilot, call, now, out int fee, out int detail);
            if (refusal != TaskedOutcome.None) { board.Release(claim); Finish(key, refusal, call.Id, 0, detail); return; }
            ITaskedSlot slot = ports.Reserve(winner.Pilot, call, out refusal);
            if (slot == null) { board.Release(claim); Finish(key, refusal == TaskedOutcome.None ? TaskedOutcome.BirdBusy : refusal, call.Id, 0, 0); return; }
            if (fee > 0 && !wallets.TrySpend(winner.Pilot, fee, now))
            {
                slot.Cancel(); board.Release(claim);
                Finish(key, TaskedOutcome.LowCredit, call.Id, 0, fee);
                return;
            }
            if (!board.BeginLaunch(claim, now))
            {
                if (fee > 0) wallets.Refund(winner.Pilot, fee);
                slot.Cancel(); board.Release(claim);
                Finish(key, TaskedOutcome.DeliveryFailed, call.Id, 0, 0);
                return;
            }
            var job = new TaskedLaunchJob(this, slot, claim, call, winner.Pilot, winner.RequestId, fee, now);
            inflight.Add(job);
            try { launcher.Launch(job); }
            catch (Exception e)
            {
                ports.Warn("[Support.Tasked] Launch threw: " + e.Message);
                if (!job.Spawned) Fail(job, TaskedOutcome.DeliveryFailed);
            }
        }

        internal bool CanLaunch(TaskedLaunchJob job)
        {
            if (job == null || job.Final || retired || !ReferenceEquals(job.Desk, this)) return false;
            float now = ports.Now;
            return SpaceRules.MissionTime(now) && board.CanLaunch(job.Claim, now) && job.Slot.CanCommit();
        }

        internal bool ReportSpawn(TaskedLaunchJob job)
        {
            if (job == null || job.Final || !ReferenceEquals(job.Desk, this)) return false; // a replayed or stale receipt never counts
            float now = ports.Now;
            if (retired || !SpaceRules.MissionTime(now) || !board.CanLaunch(job.Claim, now) || !job.Slot.CanCommit())
            {
                Fail(job, !retired && SpaceRules.MissionTime(now) && now >= job.LaunchedAt + TaskedBoard.LaunchSeconds
                    ? TaskedOutcome.TimedOut : TaskedOutcome.DeliveryFailed);
                return false;
            }
            if (!board.Commit(job.Claim, now)) { Fail(job, TaskedOutcome.DeliveryFailed); return false; }
            job.Final = true; job.Spawned = true;
            inflight.Remove(job);
            bool committed = false;
            try { committed = job.Slot.Commit(); } catch (Exception e) { ports.Warn("[Support.Tasked] Bird commit threw: " + e.Message); }
            if (!committed) ports.Warn("[Support.Tasked] Physical launch accepted but the bird commit was refused.");
            Settle(job, now);
            try { ports.Fired(job); } catch (Exception e) { ports.Warn("[Support.Tasked] Fired callback threw: " + e.Message); }
            Finish(new Key(job.Pilot, job.RequestId, Kind.Claim), TaskedOutcome.Fired, job.CallId, job.Escrow, 0);
            return true;
        }

        internal void ReportFailure(TaskedLaunchJob job, TaskedOutcome reason)
        {
            if (job == null || job.Final || !ReferenceEquals(job.Desk, this)) return;
            Fail(job, reason == TaskedOutcome.None || reason == TaskedOutcome.Fired ? TaskedOutcome.DeliveryFailed : reason);
        }

        /// <summary>No physical launch: the escrow returns exactly once, the claim releases and nobody is paid.</summary>
        private void Fail(TaskedLaunchJob job, TaskedOutcome outcome)
        {
            if (job.Final) return;
            job.Final = true;
            inflight.Remove(job);
            if (job.Escrow > 0) wallets.Refund(job.Pilot, job.Escrow);
            try { job.Slot.Cancel(); } catch (Exception e) { ports.Warn("[Support.Tasked] Bird release threw: " + e.Message); }
            board.Release(job.Claim);
            Finish(new Key(job.Pilot, job.RequestId, Kind.Claim), outcome, job.CallId, 0, 0);
        }

        /// <summary>
        /// Exactly-once fee settlement on the physical receipt: original weighted shares first, then the firer, switched,
        /// frozen and capped portions go to HQ FUND without being redistributed. Total in equals total out.
        /// </summary>
        private void Settle(TaskedLaunchJob job, float now)
        {
            if (job.Escrow <= 0) return;
            TaskedCall call = job.Call;
            FeeSettlement split = TaskedFees.Split(job.Escrow, call.WatchOfficer, call.CopyShares(), call.HumanProfile);
            float hq = split.Hq;
            foreach (ContributorPayout payout in split.Payouts)
            {
                if (payout.Player == job.Pilot) { hq += payout.Amount; continue; } // no firer share
                try { hq += wallets.EarnContributor(payout.Player, faction, payout.Amount, now).Unapplied; }
                catch (Exception e)
                {
                    ports.Warn("[Support.Tasked] Contributor payout threw: " + e.Message);
                    hq += payout.Amount;
                }
            }
            wallets.AddHq(faction, hq);
        }

        // ---- Judgement -------------------------------------------------------------------------

        /// <summary>Host authority plus the wallet rule. A free call still needs a real, correct-faction, unfrozen wallet.</summary>
        private TaskedOutcome Judge(ulong player, TaskedCall call, float now, out int fee, out int detail)
        {
            fee = 0;
            TaskedOutcome refusal = ports.Authorize(player, call.Action, true, out int baseline, out bool charge, out detail);
            if (refusal != TaskedOutcome.None || !charge) return refusal;
            bool own = !call.WatchOfficer && call.Maker == player;
            int quote = TaskedFees.Quote(call.Action, baseline, call.HumanProfile, own);
            if (quote < 0) return TaskedOutcome.Unavailable;
            if (!wallets.IsActive(player, faction, now))
            {
                float frozen = wallets.FrozenRemaining(player, now);
                detail = (int)Math.Ceiling(frozen);
                return frozen > 0f ? TaskedOutcome.Frozen : TaskedOutcome.Unavailable;
            }
            if (quote > 0 && wallets.Balance(player) + .001f < quote) { detail = quote; return TaskedOutcome.LowCredit; }
            fee = quote;
            return TaskedOutcome.None;
        }

        // ---- Lifecycle -------------------------------------------------------------------------

        /// <summary>Scene or space teardown: in-flight escrow returns once, queued claims end, every old callback is inert.</summary>
        public void Retire()
        {
            if (retired) return;
            retired = true;
            var jobs = inflight.ToArray();
            for (int i = 0; i < jobs.Length; i++) Fail(jobs[i], TaskedOutcome.SceneEnded);
            for (int i = 0; i < waiting.Count; i++) FinishAll(waiting[i], TaskedOutcome.SceneEnded);
            waiting.Clear();
            board.Clear();
        }

        // ---- Receipts --------------------------------------------------------------------------

        private Waiting FindWaiting(int callId)
        {
            for (int i = 0; i < waiting.Count; i++) if (waiting[i].CallId == callId) return waiting[i];
            return null;
        }

        private TaskedResult Remember(in Key key, TaskedOutcome outcome, int callId, int charged, int detail)
        {
            var result = new TaskedResult(outcome, callId, key.Request, charged, detail);
            if (MakeRoom()) Store(key, result, false);
            return result;
        }

        private void Store(in Key key, in TaskedResult result, bool pending)
        {
            receipts[key] = new Receipt { Result = result, Pending = pending };
            order.Enqueue(key);
        }

        /// <summary>Bounded table: evict the oldest finished receipt; pending claims are never dropped.</summary>
        private bool MakeRoom()
        {
            if (receipts.Count < MaxReceipts) return true;
            for (int n = order.Count; n > 0; n--)
            {
                Key oldest = order.Dequeue();
                if (!receipts.TryGetValue(oldest, out Receipt receipt)) continue;
                if (receipt.Pending) { order.Enqueue(oldest); continue; }
                receipts.Remove(oldest);
                return true;
            }
            return false;
        }

        private void FinishAll(Waiting queue, TaskedOutcome outcome)
        {
            for (int i = 0; i < queue.Contenders.Count; i++)
                Finish(new Key(queue.Contenders[i].Pilot, queue.Contenders[i].RequestId, Kind.Claim), outcome, queue.CallId, 0, 0);
        }

        private void Finish(in Key key, TaskedOutcome outcome, int callId, int charged, int detail)
        {
            var result = new TaskedResult(outcome, callId, key.Request, charged, detail);
            if (!receipts.TryGetValue(key, out Receipt receipt))
            {
                if (MakeRoom()) Store(key, result, false);
            }
            else if (receipt.Pending)
            {
                receipt.Result = result; receipt.Pending = false;
            }
            else return;
            try { Resolved?.Invoke(key.Player, key.Request, result); }
            catch (Exception e) { ports.Warn("[Support.Tasked] Resolved handler threw: " + e.Message); }
        }
    }
}
