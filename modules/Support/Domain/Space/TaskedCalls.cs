using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    internal readonly struct EffortShare
    {
        public readonly ulong Player;
        public readonly float Effort;
        public EffortShare(ulong player, float effort) { Player = player; Effort = effort; }
    }

    /// <summary>Immutable host-created call. Human SEND uses TryPostVerified, never caller-supplied weights.</summary>
    internal sealed class TaskedCall
    {
        public readonly int Id, HumanProfile;
        public readonly SupportActionId Action;
        /// <summary>The OPS domain that made this post (SPACE rod, CYBER package, later SOF); derived from the action, never from a client.</summary>
        public readonly TaskedDomain Domain;
        public readonly ulong Maker;
        public readonly bool WatchOfficer;
        public readonly float CreatedAt, ExpiresAt;
        private readonly SpaceMark[] marks;
        private readonly EffortShare[] shares;

        public TaskedCall(int id, SupportActionId action, ulong maker, bool watchOfficer, int humanProfile,
            float createdAt, SpaceMark[] marks, EffortShare[] shares)
        {
            Id = id; Action = action; Domain = TaskedKinds.DomainOf(action); Maker = maker; WatchOfficer = watchOfficer; HumanProfile = humanProfile;
            CreatedAt = createdAt; ExpiresAt = createdAt + TaskedBoard.CallSeconds;
            // Refuse oversized input without allocating from an untrusted length.
            this.marks = marks != null && marks.Length <= TaskedBoard.MaxMarks ? (SpaceMark[])marks.Clone() : null;
            this.shares = shares != null && shares.Length <= TaskedBoard.MaxMarks ? (EffortShare[])shares.Clone() : null;
        }

        public int MarkCount => marks?.Length ?? 0;
        public int ShareCount => shares?.Length ?? 0;
        public SpaceMark MarkAt(int index) => marks[index];

        public EffortShare ShareAt(int index) => shares[index];
        public EffortShare[] CopyShares() => shares == null ? Array.Empty<EffortShare>() : (EffortShare[])shares.Clone();

        internal bool Valid(float now)
        {
            if (Id <= 0 || (!WatchOfficer && Maker == 0) || !TaskedFees.ValidProfile(HumanProfile) ||
                !TaskedKinds.TryGet(Action, out _) || !SpaceRules.MissionTime(CreatedAt) ||
                !SpaceRules.MissionTime(now) || now < CreatedAt || !SpaceRules.Finite(ExpiresAt) ||
                ExpiresAt <= CreatedAt || now >= ExpiresAt || marks == null || marks.Length == 0 || shares == null) return false;
            for (int i = 0; i < marks.Length; i++)
            {
                SpaceMark mark = marks[i];
                if (mark.Id <= 0 || !Coordinate(mark.X) || !Coordinate(mark.Z) ||
                    !SpaceRules.Finite(mark.ExpiresAt) || CreatedAt >= mark.ExpiresAt ||
                    (mark.Source != BirdKind.Optical && mark.Source != BirdKind.Radar)) return false;
                for (int j = 0; j < i; j++) if (marks[j].Id == mark.Id) return false;
            }
            for (int i = 0; i < shares.Length; i++)
                if (shares[i].Player == 0 || !SpaceRules.Finite(shares[i].Effort) || shares[i].Effort < 0) return false;
            return true;
        }

        internal bool ValidAtSend(float now)
        {
            if (!Valid(now)) return false;
            for (int i = 0; i < marks.Length; i++) if (now >= marks[i].ExpiresAt) return false;
            return true;
        }

        private static bool Coordinate(float value) => SpaceRules.Finite(value) && Math.Abs(value) <= 10000000f;
    }

    internal readonly struct TaskedPostInfo
    {
        public readonly TaskedCall Call;
        /// <summary>A pilot holds the post: reserved for 2 s, or physically launching.</summary>
        public readonly bool Held;
        /// <summary>Physically launching only (not the 2 s reservation).</summary>
        public readonly bool Launching;
        public readonly ulong Holder;
        public TaskedPostInfo(TaskedCall call, bool held, bool launching, ulong holder) { Call = call; Held = held; Launching = launching; Holder = holder; }
    }

    internal readonly struct TaskedClaim
    {
        public readonly int CallId, RequestId, SceneGeneration, CallGeneration;
        public readonly ulong Pilot;
        public readonly float ReservationExpiresAt;
        internal readonly TaskedBoard Owner;
        internal readonly int Token;

        internal TaskedClaim(TaskedBoard owner, int token, int callId, int requestId, ulong pilot,
            float expiresAt, int sceneGeneration, int callGeneration)
        {
            Owner = owner; Token = token; CallId = callId; RequestId = requestId; Pilot = pilot;
            ReservationExpiresAt = expiresAt; SceneGeneration = sceneGeneration; CallGeneration = callGeneration;
        }
    }

    /// <summary>One faction's bounded SEND work and physical-launch claim ledger.</summary>
    internal sealed class TaskedBoard
    {
        public const int MaxCalls = 12, MaxMarks = 6, MaxClaimants = 128;
        public const int MaxConsumedTokens = MaxCalls * MaxMarks + SpaceContacts.MaxMarks;
        public const float CallSeconds = 600f, ArbitrationSeconds = .2f, ReservationSeconds = 2f, LaunchSeconds = 30f;
        private enum Status : byte { Available, Reserved, Launching }

        private readonly struct Contender
        {
            public readonly int RequestId;
            public readonly ulong Pilot;
            public readonly bool Favorite;
            public Contender(int requestId, ulong pilot, bool favorite) { RequestId = requestId; Pilot = pilot; Favorite = favorite; }
        }

        private sealed class Entry
        {
            public readonly TaskedCall Call;
            public readonly int Generation;
            public readonly bool VerifiedWork;
            public readonly List<Contender> Claims = new List<Contender>();
            public Status Status;
            public float FirstClaimAt, ReservedAt, LaunchAt;
            public TaskedClaim Claim;
            public Entry(TaskedCall call, int generation, bool verifiedWork) { Call = call; Generation = generation; VerifiedWork = verifiedWork; }
        }

        private readonly struct WorkKey : IEquatable<WorkKey>
        {
            public readonly int Id, Generation;
            public WorkKey(int id, int generation) { Id = id; Generation = generation; }
            public bool Equals(WorkKey other) => Id == other.Id && Generation == other.Generation;
            public override bool Equals(object obj) => obj is WorkKey key && Equals(key);
            public override int GetHashCode() => unchecked(Id * 397 ^ Generation);
        }

        private readonly struct UsedWork
        {
            public readonly float SpentAt, ExpiresAt;
            public UsedWork(float spentAt, float expiresAt) { SpentAt = spentAt; ExpiresAt = expiresAt; }
        }

        private readonly Dictionary<int, Entry> calls = new Dictionary<int, Entry>();
        private readonly Dictionary<WorkKey, UsedWork> consumed = new Dictionary<WorkKey, UsedWork>();
        private readonly Dictionary<ulong, float> effortAdjustments = new Dictionary<ulong, float>();
        private readonly List<int> expiredCalls = new List<int>(MaxCalls);
        private readonly List<WorkKey> expiredWork = new List<WorkKey>(MaxConsumedTokens);
        private SpaceContacts workOwner;
        private int contactsGeneration, sceneGeneration = 1, nextCallGeneration, nextClaimToken, posted, fired;
        private bool retired;

        public int Count { get { SyncContacts(); return calls.Count; } }
        public int Posted { get { SyncContacts(); return posted; } }
        public int Fired { get { SyncContacts(); return fired; } }
        public int Generation { get { SyncContacts(); return retired ? 0 : sceneGeneration; } }

        /// <summary>Live OVERLORD posts of one domain: SPACE, CYBER and SOF each keep their own allowance, so one domain's posts never use up another's.</summary>
        public int CountWatchOfficer(float now, TaskedDomain domain)
        {
            SyncContacts();
            int n = 0;
            foreach (Entry entry in calls.Values) if (entry.Call.WatchOfficer && entry.Call.Domain == domain && entry.Call.Valid(now)) n++;
            return n;
        }

        public static int CapacityFor(int humanProfile) => !TaskedFees.ValidProfile(humanProfile) ? 0 :
            humanProfile <= 4 ? 6 : humanProfile <= 16 ? 6 + humanProfile / 4 : MaxCalls;

        /// <summary>For host-owned prebuilt/AI data only. It does not consume or refund caller-supplied effort.</summary>
        public bool TryPost(TaskedCall call, float now)
        {
            SyncContacts();
            if (!SpaceRules.MissionTime(now)) return false;
            Prune(now);
            if (!CanPost(call, now)) return false;
            Add(call, false);
            return true;
        }

        public bool TryPostVerified(int id, SupportActionId action, ulong maker, bool watchOfficer,
            int humanProfile, int[] markIds, SpaceContacts contacts, float now, out TaskedCall call)
        {
            call = null;
            SyncContacts();
            if (contacts == null || contacts.Generation <= 0 || (workOwner != null && !ReferenceEquals(workOwner, contacts)) ||
                !SpaceRules.MissionTime(now) || markIds == null || markIds.Length == 0 || markIds.Length > MaxMarks) return false;
            Prune(now);
            var marks = new SpaceMark[markIds.Length];
            var provenance = new SpaceMarkProvenance[markIds.Length];
            var shares = new List<EffortShare>(MaxMarks);
            var types = new Dictionary<int, int>();
            int newPlayers = 0;
            for (int i = 0; i < markIds.Length; i++)
            {
                int markId = markIds[i];
                for (int j = 0; j < i; j++) if (markIds[j] == markId) return false;
                if (!contacts.TryMark(markId, now, out marks[i]) || !contacts.TryProvenance(markId, now, out provenance[i]) ||
                    provenance[i].ObservationGeneration <= 0 || provenance[i].Player == 0 || provenance[i].TypeId < 0 ||
                    provenance[i].EarnedEffort < 0 || provenance[i].EarnedEffort > 1 ||
                    consumed.ContainsKey(new WorkKey(markId, provenance[i].ObservationGeneration))) return false;
                if (watchOfficer || provenance[i].EarnedEffort == 0) continue;
                types.TryGetValue(provenance[i].TypeId, out int sameType);
                if (sameType >= 2) continue; // Additional legal targets do not mint a third paid token of this unit type.
                types[provenance[i].TypeId] = sameType + 1;
                ulong player = provenance[i].Player;
                int share = -1;
                for (int j = 0; j < shares.Count; j++) if (shares[j].Player == player) { share = j; break; }
                if (share >= 0) shares[share] = new EffortShare(player, shares[share].Effort + 1);
                else { shares.Add(new EffortShare(player, 1)); if (!effortAdjustments.ContainsKey(player)) newPlayers++; }
            }
            var candidate = new TaskedCall(id, action, maker, watchOfficer, humanProfile, now, marks, shares.ToArray());
            if (!CanPost(candidate, now) || consumed.Count + marks.Length > MaxConsumedTokens ||
                effortAdjustments.Count + newPlayers > SpaceContacts.MaxPlayers) return false;
            if (workOwner == null) { workOwner = contacts; contactsGeneration = contacts.Generation; }
            for (int i = 0; i < marks.Length; i++)
                consumed.Add(new WorkKey(marks[i].Id, provenance[i].ObservationGeneration), new UsedWork(now, marks[i].ExpiresAt));
            for (int i = 0; i < shares.Count; i++) Adjust(shares[i].Player, -shares[i].Effort);
            Add(candidate, true);
            call = candidate;
            return true;
        }

        public bool TryGet(int callId, out TaskedCall call)
        {
            SyncContacts();
            if (calls.TryGetValue(callId, out Entry entry)) { call = entry.Call; return true; }
            call = null; return false;
        }

        /// <summary>
        /// The board as a viewer may see it: every post inside its own 600 s life (a post is a fixed snapshot of ground points,
        /// so a lapsed MARK never hides it), whether a pilot holds it, and who.
        /// </summary>
        public int Snapshot(float now, List<TaskedPostInfo> into)
        {
            into.Clear();
            SyncContacts();
            if (!SpaceRules.MissionTime(now)) return 0;
            foreach (Entry entry in calls.Values)
            {
                if (!entry.Call.Valid(now)) continue;
                bool held = entry.Status != Status.Available;
                into.Add(new TaskedPostInfo(entry.Call, held, entry.Status == Status.Launching, held ? entry.Claim.Pilot : 0UL));
            }
            return into.Count;
        }

        public bool EnqueueClaim(int callId, int requestId, ulong pilot, bool favorite, float now)
        {
            SyncContacts();
            if (retired || requestId <= 0 || pilot == 0 || !SpaceRules.MissionTime(now)) return false;
            Prune(now);
            if (!calls.TryGetValue(callId, out Entry entry) || entry.Status != Status.Available || !entry.Call.Valid(now)) return false;
            for (int i = 0; i < entry.Claims.Count; i++)
                if (entry.Claims[i].RequestId == requestId && entry.Claims[i].Pilot == pilot)
                    return entry.Claims[i].Favorite == favorite;
            if (entry.Claims.Count >= MaxClaimants) return false;
            if (entry.Claims.Count == 0)
            {
                if (!Deadline(now, ArbitrationSeconds, out _)) return false;
                entry.FirstClaimAt = now;
            }
            else if (now < entry.FirstClaimAt || now >= entry.FirstClaimAt + ArbitrationSeconds) return false;
            entry.Claims.Add(new Contender(requestId, pilot, favorite));
            return true;
        }

        public bool TryReserve(int callId, float now, out TaskedClaim claim)
        {
            claim = default;
            SyncContacts();
            if (retired || !SpaceRules.MissionTime(now) || nextClaimToken == int.MaxValue) return false;
            Prune(now);
            if (!calls.TryGetValue(callId, out Entry entry) || entry.Status != Status.Available ||
                entry.Claims.Count == 0 || now < entry.FirstClaimAt + ArbitrationSeconds || !entry.Call.Valid(now) ||
                !Deadline(now, ReservationSeconds, out float expiresAt)) return false;
            Contender winner = entry.Claims[0];
            for (int i = 0; i < entry.Claims.Count; i++)
                if (entry.Claims[i].Favorite) { winner = entry.Claims[i]; break; }
            claim = new TaskedClaim(this, ++nextClaimToken, callId, winner.RequestId, winner.Pilot,
                expiresAt, sceneGeneration, entry.Generation);
            entry.Claim = claim; entry.ReservedAt = now; entry.Status = Status.Reserved;
            return true;
        }

        /// <summary>Revalidate immediately before native spawn, including a delayed LAUNCHING job.</summary>
        public bool CanLaunch(in TaskedClaim claim, float now)
        {
            SyncContacts();
            if (!SpaceRules.MissionTime(now) || !TryClaim(claim, out Entry entry) || !entry.Call.Valid(now)) return false;
            return entry.Status == Status.Reserved ? now >= entry.ReservedAt && now < claim.ReservationExpiresAt :
                entry.Status == Status.Launching && now >= entry.LaunchAt && now < entry.LaunchAt + LaunchSeconds;
        }

        public bool BeginLaunch(in TaskedClaim claim, float now)
        {
            if (!CanLaunch(claim, now) || !TryClaim(claim, out Entry entry) || entry.Status != Status.Reserved) return false;
            entry.Status = Status.Launching; entry.LaunchAt = now;
            return true;
        }

        /// <summary>Called only for an actual native launch receipt; a queued action acceptance is insufficient.</summary>
        public bool Commit(in TaskedClaim claim, float now)
        {
            SyncContacts();
            if (!SpaceRules.MissionTime(now) || !TryClaim(claim, out Entry entry) ||
                entry.Status != Status.Launching || now < entry.LaunchAt || now >= entry.LaunchAt + LaunchSeconds) return false;
            calls.Remove(claim.CallId);
            if (fired < int.MaxValue) fired++;
            return true;
        }

        public void Release(in TaskedClaim claim)
        {
            SyncContacts();
            if (TryClaim(claim, out Entry entry)) ResetClaim(entry);
        }

        /// <summary>Takes an unclaimed post off the board (a SOF post whose team is no longer in need). A held or launching post stays.</summary>
        public bool Withdraw(int callId)
        {
            SyncContacts();
            if (!calls.TryGetValue(callId, out Entry entry) || entry.Status != Status.Available || entry.Claims.Count > 0) return false;
            calls.Remove(callId);
            return true;
        }

        public void Prune(float now)
        {
            SyncContacts();
            if (!SpaceRules.MissionTime(now)) return;
            expiredCalls.Clear();
            foreach (var pair in calls)
            {
                Entry entry = pair.Value;
                // LAUNCHING outlives the 2 s reservation but not LaunchSeconds: a launch that never reports a receipt
                // is released (escrow handled by the caller) and the post returns to Available or expires normally.
                if (entry.Status == Status.Launching)
                {
                    if (now < entry.LaunchAt + LaunchSeconds) continue;
                    ResetClaim(entry);
                }
                if (now >= entry.Call.ExpiresAt) expiredCalls.Add(pair.Key);
                else if (entry.Status == Status.Reserved && now >= entry.Claim.ReservationExpiresAt) ResetClaim(entry);
            }
            for (int i = 0; i < expiredCalls.Count; i++)
            {
                Entry entry = calls[expiredCalls[i]];
                if (entry.VerifiedWork)
                    for (int j = 0; j < entry.Call.ShareCount; j++)
                    { EffortShare share = entry.Call.ShareAt(j); Adjust(share.Player, share.Effort * .5f); }
                calls.Remove(expiredCalls[i]);
            }
            expiredCalls.Clear();
            if (workOwner == null) return;
            expiredWork.Clear();
            foreach (var pair in consumed)
                if (now >= pair.Value.ExpiresAt || (now >= pair.Value.SpentAt &&
                    (!workOwner.TryProvenance(pair.Key.Id, now, out SpaceMarkProvenance current) ||
                     current.ObservationGeneration != pair.Key.Generation))) expiredWork.Add(pair.Key);
            for (int i = 0; i < expiredWork.Count; i++) consumed.Remove(expiredWork[i]);
            expiredWork.Clear();
        }

        public void Clear()
        {
            calls.Clear(); expiredCalls.Clear(); posted = fired = 0;
            if (sceneGeneration == int.MaxValue) retired = true;
            else sceneGeneration++;
            // Board-only reset does not make still-live Contacts work spendable a second time.
        }

        private void SyncContacts()
        {
            if (workOwner == null || workOwner.Generation == contactsGeneration) return;
            Clear(); consumed.Clear(); effortAdjustments.Clear(); expiredWork.Clear();
            contactsGeneration = workOwner.Generation;
        }

        private bool CanPost(TaskedCall call, float now)
        {
            if (retired || nextCallGeneration == int.MaxValue || call == null || !call.ValidAtSend(now) ||
                calls.ContainsKey(call.Id) || calls.Count >= CapacityFor(call.HumanProfile)) return false;
            foreach (Entry existing in calls.Values)
                if (existing.Call.Action == call.Action)
                    for (int i = 0; i < existing.Call.MarkCount; i++)
                        for (int j = 0; j < call.MarkCount; j++)
                            if (existing.Call.MarkAt(i).Id == call.MarkAt(j).Id) return false;
            return true;
        }

        private void Add(TaskedCall call, bool verifiedWork)
        {
            calls.Add(call.Id, new Entry(call, ++nextCallGeneration, verifiedWork));
            if (posted < int.MaxValue) posted++;
        }

        private bool TryClaim(in TaskedClaim claim, out Entry entry)
        {
            entry = null;
            return !retired && ReferenceEquals(claim.Owner, this) && claim.SceneGeneration == sceneGeneration &&
                claim.Token > 0 && calls.TryGetValue(claim.CallId, out entry) && entry.Generation == claim.CallGeneration &&
                entry.Claim.Token == claim.Token && entry.Claim.RequestId == claim.RequestId && entry.Claim.Pilot == claim.Pilot &&
                entry.Claim.ReservationExpiresAt == claim.ReservationExpiresAt;
        }

        private static void ResetClaim(Entry entry) { entry.Status = Status.Available; entry.Claim = default; entry.Claims.Clear(); }
        private void Adjust(ulong player, float amount) { effortAdjustments.TryGetValue(player, out float existing); effortAdjustments[player] = existing + amount; }
        private static bool Deadline(float now, float seconds, out float deadline) { deadline = now + seconds; return SpaceRules.Finite(deadline) && deadline > now; }
    }

    internal readonly struct ContributorPayout
    {
        public readonly ulong Player;
        public readonly float Amount;
        public ContributorPayout(ulong player, float amount) { Player = player; Amount = amount; }
    }

    /// <summary>Adjacent census boundaries (1/2, 4/5, 16/17) must stay crossed for sixty mission seconds.</summary>
    internal sealed class TaskedHumanProfile
    {
        public const float HoldSeconds = 60f;
        public int Humans { get; private set; }
        private int pendingCategory = -1;
        private float pendingAt, lastObservedAt;

        public TaskedHumanProfile(int initialHumans, float now)
        {
            Humans = initialHumans >= 0 && initialHumans <= SpaceContacts.MaxPlayers ? Math.Max(1, initialHumans) : 1;
            lastObservedAt = SpaceRules.MissionTime(now) ? now : 0;
        }

        public int Observe(int humans, float now)
        {
            if (humans < 0 || humans > SpaceContacts.MaxPlayers || !SpaceRules.MissionTime(now) || now < lastObservedAt) return Humans;
            humans = Math.Max(1, humans);
            int category = Category(humans);
            lastObservedAt = now;
            if (category == Category(Humans)) { pendingCategory = -1; Humans = humans; }
            else if (pendingCategory != category) { pendingCategory = category; pendingAt = now; }
            else if (now - pendingAt >= HoldSeconds) { Humans = humans; pendingCategory = -1; }
            return Humans;
        }

        private static int Category(int humans) => humans == 1 ? 0 : humans <= 4 ? 1 : humans <= 16 ? 2 : 3;
    }

    internal readonly struct FeeSettlement
    {
        public readonly float Hq;
        public readonly ContributorPayout[] Payouts;
        public FeeSettlement(float hq, ContributorPayout[] payouts) { Hq = hq; Payouts = payouts; }
    }

    internal static class TaskedFees
    {
        public static int Quote(SupportActionId action, int hostBaselinePrice, int humanProfile, bool ownCall)
        {
            if (SofPosts.IsPost(action)) return ValidProfile(humanProfile) ? 0 : -1; // a SOF post is a service: free to claim
            if (hostBaselinePrice <= 0 || !ValidProfile(humanProfile) || !TaskedKinds.TryGet(action, out TaskedKind row)) return -1;
            if ((humanProfile == 1 && ownCall) || (humanProfile >= 2 && humanProfile <= 4)) return 0;
            return action == SupportActionId.Artillery ? Math.Max(1, (int)Math.Round(hostBaselinePrice * .25d, MidpointRounding.AwayFromZero)) :
                row.Tier == CallTier.Light ? 10 : row.Tier == CallTier.Heavy ? 25 : 120;
        }

        public static FeeSettlement Split(int chargedFee, bool watchOfficer, EffortShare[] verifiedShares, int humanProfile = 5)
        {
            if (chargedFee <= 0) return new FeeSettlement(0, Array.Empty<ContributorPayout>());
            if (watchOfficer || !ValidProfile(humanProfile) || humanProfile <= 4 || verifiedShares == null ||
                verifiedShares.Length == 0 || verifiedShares.Length > TaskedBoard.MaxMarks)
                return new FeeSettlement(chargedFee, Array.Empty<ContributorPayout>());
            var players = new List<ulong>(TaskedBoard.MaxMarks);
            var weights = new List<double>(TaskedBoard.MaxMarks);
            double total = 0;
            foreach (EffortShare share in verifiedShares)
            {
                if (share.Player == 0 || !SpaceRules.Finite(share.Effort) || share.Effort < 0)
                    return new FeeSettlement(chargedFee, Array.Empty<ContributorPayout>());
                if (share.Effort == 0) continue;
                int index = players.IndexOf(share.Player);
                if (index < 0) { players.Add(share.Player); weights.Add(share.Effort); }
                else weights[index] += share.Effort;
                total += share.Effort;
            }
            if (total <= 0) return new FeeSettlement(chargedFee, Array.Empty<ContributorPayout>());
            var payouts = new ContributorPayout[players.Count];
            float distributed = 0;
            for (int i = 0; i < payouts.Length; i++)
            {
                float amount = (float)(chargedFee * .7d * weights[i] / total);
                payouts[i] = new ContributorPayout(players[i], amount); distributed += amount;
            }
            return new FeeSettlement(chargedFee - distributed, payouts);
        }

        internal static bool ValidProfile(int humanProfile) => humanProfile > 0 && humanProfile <= SpaceContacts.MaxPlayers;
    }
}
