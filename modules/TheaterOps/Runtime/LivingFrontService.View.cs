using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.TheaterOps.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Math;
using UnityEngine;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    // What the staff says: the immutable snapshot, the view every peer reads, and the small contracts other modules call.
    internal sealed partial class LivingFrontService
    {
        private static readonly Comparison<TheaterFrontView> ByPressure = (a, b) =>
            a.Pressure != b.Pressure ? b.Pressure.CompareTo(a.Pressure) : string.CompareOrdinal(a.Key, b.Key);

        private static byte Tally(int count) => (byte)Math.Min(254, Math.Max(0, count));

        /// <summary>
        /// Builds the fronts the pilot sees from the reads and the trend against the last review, logs a status flip, and stamps the data's age. Reviews only: a
        /// command would reset the trends and the status-change log (the study's livingfront-11), so TrendWrites counts any write that did not come from one.
        /// </summary>
        private void PublishFronts(FactionWar war, float now)
        {
            if (!war.InReview) war.TrendWrites++;
            war.Fronts.Clear();
            for (int i = 0; i < war.Reads.Count && war.Fronts.Count < LivingWarRules.MaximumFronts; i++)
            {
                WarFrontRead read = war.Reads[i];
                if (!read.Live) continue;
                bool known = war.Baseline.TryGetValue(read.Key, out FrontMemo before);
                TheaterFrontStatus status = read.NearFriendly > 0 && read.NearHostile > 0 ? TheaterFrontStatus.InContact
                    : read.NearHostile > read.NearFriendly ? TheaterFrontStatus.UnderPressure
                    : read.NearFriendly > 0 ? TheaterFrontStatus.Holding : TheaterFrontStatus.Unconfirmed;
                // A front seen for the first time has no trend; an unobserved one is published as zero and printed as a dash.
                float trend = known ? Mathf.Clamp(read.Pressure - before.Pressure, -1f, 1f) : 0f;
                if (known && before.Status != status) war.Log.Add(read.Name + " · " + TheaterWords.FrontStatus(status));
                war.Fronts.Add(new TheaterFrontView
                {
                    Key = read.Key, Name = read.Name, X = read.X, Z = read.Z, Status = status,
                    Pressure = read.Observed ? read.Pressure : 0f, Trend = read.Observed ? trend : 0f,
                    ObservedAgeSeconds = war.ObservedAt.TryGetValue(read.Key, out float seen) ? Mathf.Clamp(now - seen, 0f, 86400f) : -1f,
                    Observed = read.Observed, Objective = read.Objective, Held = read.Held,
                    NearFriendly = Tally(read.NearFriendly), NearHostile = read.Observed ? Tally(read.NearHostile) : (byte)255, Carriers = Tally(read.Carriers),
                });
                if (war.Baseline.Count < MaximumRemembered || known) war.Baseline[read.Key] = new FrontMemo { Pressure = read.Pressure, Status = status };
            }
            war.Fronts.Sort(ByPressure);
            war.SensedAt = now;
        }

        /// <summary>Rebuilds the picture from the staff's own state and sends it to the faction's clients.</summary>
        private void Publish(FactionWar war, float now)
        {
            war.Published = BuildSnapshot(war, now);
            war.PublishedAt = now;
            network?.Broadcast(war.HQ.faction.factionName);
        }

        private TheaterWarSnapshot BuildSnapshot(FactionWar war, float now)
        {
            bool human = HasHuman(war.HQ);
            var snapshot = new TheaterWarSnapshot
            {
                Faction = war.HQ.faction.factionName, Posture = war.Posture, GroundReason = war.GroundShown,
                SensedAgeSeconds = Mathf.Clamp(SnapshotAge.Host(now, war.SensedAt), 0f, 3600f),
                StaffPickSeconds = war.DeadlineArmed && war.Active == null ? Mathf.Clamp(war.OfferDeadline - now, 0f, 120f) : -1f,
                Census = war.Census,
            };

            TheaterCaps caps = TheaterCaps.None;
            if (war.Faults.Healthy) caps |= TheaterCaps.StaffRunning;
            if (SteeringAvailable) caps |= TheaterCaps.GroundSteering;
            for (int i = 0; i < war.Reads.Count; i++) if (war.Reads[i].Live && war.Reads[i].Carriers > 0) { caps |= TheaterCaps.GroundCarriers; break; }
            if (war.GroundSeen) caps |= TheaterCaps.GroundWar;
            if (human) caps |= TheaterCaps.HumanPilot;
            if (territory != null) caps |= TheaterCaps.TerritoryField;
            if (war.HasObjectives) caps |= TheaterCaps.Objectives;
            if (picture != null) caps |= TheaterCaps.Intel;
            snapshot.Caps = caps;

            var inputs = new EmptyInputs
            {
                Healthy = war.Faults.Healthy, HasTerritory = territory != null, OperationLive = war.Active != null, MissionHasGroundWar = war.GroundSeen,
                SecondsToFirstDeal = Math.Max(0f, war.FirstDealAt - now), GroundReason = war.GroundShown, DeckCount = war.Deck.Count, Tally = war.Tally,
            };
            snapshot.Reason = EmptyReasonRules.Decide(inputs, Math.Max(0f, war.LastVetoUntil - now), out float reasonSeconds);
            snapshot.ReasonSeconds = Mathf.Clamp(reasonSeconds, 0f, 600f);

            snapshot.Fronts = war.Fronts.ToArray();
            int cards = Math.Min(war.Deck.Count, LivingWarRules.MaximumOffers);
            snapshot.Proposals = new TheaterProposalView[cards];
            for (int i = 0; i < cards; i++) snapshot.Proposals[i] = ProposalOf(war, war.Deck[i]);

            Operation op = war.Active;
            if (op != null)
                snapshot.Operation = new TheaterOperationView
                {
                    Id = op.Id, Revision = op.Revision, Kind = op.Kind, TargetKey = op.Key, Name = op.Name, X = op.X, Z = op.Z,
                    OriginX = op.OriginX, OriginZ = op.OriginZ, Radius = LivingWarRules.RadiusOf(op.Kind), Phase = op.Phase, PhaseNote = op.PhaseNote,
                    Forces = op.Forces,
                    ElapsedSeconds = Mathf.Clamp(now - op.Started, 0f, 3600f), LimitSeconds = LivingWarRules.OperationLimitSeconds, Progress = op.Progress,
                    PickedBy = op.PickedBy, PickedById = op.PickedById, PickedByStaff = op.PickedByStaff,
                    LockSeconds = op.PickedByStaff ? 0f : Mathf.Clamp(op.LockUntil - now, 0f, LivingWarRules.PickLockSeconds),
                };
            TheaterResultView last = war.LastResult;
            if (last != null)
                snapshot.LastResult = new TheaterResultView
                {
                    OperationId = last.OperationId, Kind = last.Kind, Name = last.Name, Outcome = last.Outcome, Why = last.Why,
                    AgeSeconds = Mathf.Clamp(now - war.LastResultAt, 0f, 86400f), Funds = last.Funds, Morale = last.Morale,
                };
            snapshot.Wave = desk.View(war.HQ, now);
            snapshot.LastOutcome = desk.LastOutcome(war.HQ);
            snapshot.Log = new string[war.Log.Count];
            for (int i = 0; i < snapshot.Log.Length; i++) snapshot.Log[i] = war.Log.Entries[i];
            return snapshot;
        }

        private TheaterProposalView ProposalOf(FactionWar war, DeckCard card)
        {
            WarFrontRead read = default;
            for (int i = 0; i < war.Reads.Count; i++) if (war.Reads[i].Key == card.Key) { read = war.Reads[i]; break; }
            int hostile = card.Kind == TheaterOpKind.Defend ? read.HostileNear : read.ReachHostile;
            return new TheaterProposalView
            {
                Id = card.Id, Revision = card.Revision, Kind = card.Kind, Name = read.Name ?? "", TargetKey = card.Key, X = read.X, Z = read.Z,
                Radius = LivingWarRules.RadiusOf(card.Kind), Pressure = read.Observed ? read.Pressure : 0f,
                Risk = !read.Observed ? TheaterRisk.Unknown : hostile > read.Friendly ? TheaterRisk.High : TheaterRisk.Moderate,
                Carrier = CarrierOf(read),
                FriendlyGround = Tally(read.ReachFriendlyGround), FriendlyOther = Tally(read.ReachFriendlyOther),
                HostileTracked = read.Observed ? Tally(hostile) : (byte)255, Recommended = card.Recommended,
            };
        }

        // ---- ITheaterAirStationView, IOperationFocusSource ----------------------------------------------------------------

        public bool TryGetStation(string faction, out float x, out float z, out float radiusMeters)
        {
            x = z = radiusMeters = 0f;
            if (!Authoritative || string.IsNullOrEmpty(faction) ||
                !wars.TryGetValue(faction, out FactionWar war) || war.Active == null) return false;
            x = war.Active.X; z = war.Active.Z; radiusMeters = 4000f;
            return true;
        }

        /// <summary>Reads the live war, not the published picture, so a pick is visible to the sortie director within one tick.</summary>
        public bool TryGetFocus(int factionInstanceId, out OperationFocus focus)
        {
            focus = default;
            if (!Authoritative || MissionOver) return false;
            foreach (FactionWar war in wars.Values)
            {
                if (war.HQ == null || war.HQ.GetInstanceID() != factionInstanceId) continue;
                focus = FocusOf(war);
                return focus.IsLive;
            }
            return false;
        }

        // ---- ITheaterWarView: always scoped to the local player's faction; clients only read host snapshots ------------------

        public bool Available => settings != null && settings.Enabled.Value;
        public bool IsHost => GameAccess.IsServer();
        public TheaterWarSnapshot Snapshot => Available ? LocalState()?.Published : null;
        public bool HasSnapshot => Snapshot != null;

        /// <summary>The host's age is on the mission clock (it stands still when nothing newer exists and grows if the staff stalls); a client adds the unscaled time since receipt.</summary>
        public float SnapshotAgeSeconds
        {
            get
            {
                FactionWar war = Available ? LocalState() : null;
                TheaterWarSnapshot snapshot = war?.Published;
                if (snapshot == null) return -1f;
                return IsHost ? SnapshotAge.Host(Time.timeSinceLevelLoad, war.SensedAt)
                    : SnapshotAge.Client(snapshot.SensedAgeSeconds, Time.unscaledTime, war.PublishedAt);
            }
        }

        public float SecondsSincePublished
        {
            get
            {
                FactionWar war = Available ? LocalState() : null;
                if (war?.Published == null) return -1f;
                return Mathf.Max(0f, IsHost ? Time.timeSinceLevelLoad - war.PublishedAt : Time.unscaledTime - war.PublishedAt);
            }
        }

        public bool IsFresh { get { float age = SnapshotAgeSeconds; return age >= 0f && age <= LivingWarRules.SnapshotFreshSeconds; } }

        public TheaterViewState State => ViewStateRules.Decide(Available, GameAccess.TryGetLocalFaction(out _), IsHost, HasSnapshot, SnapshotAgeSeconds,
            network != null ? network.SecondsSinceFirstQuery : 0f, network != null ? network.UnansweredQueries : 0);

        public bool CommandPending => network?.CommandPending == true;

        public TheaterCommandResult LastCommand
        {
            get
            {
                if (!Authoritative) return network?.LastCommand;
                if (hostCommand == null || !ViewStateRules.CommandFresh(Time.unscaledTime, hostCommandAt)) return hostCommand = null;
                hostCommand.SecondsAgo = Time.unscaledTime - hostCommandAt;
                return hostCommand;
            }
        }

        /// <summary>The host is the source and gates on its own health, never on age; a client needs a fresh picture.</summary>
        public bool CanCommand
        {
            get
            {
                if (!HasSnapshot || CommandPending || !GameAccess.TryGetLocalFaction(out _)) return false;
                return IsHost ? LocalState().Faults.Healthy : IsFresh;
            }
        }

        public TheaterWarPosture Posture => LocalState()?.Posture ?? TheaterWarPosture.Steady;
        public IReadOnlyList<TheaterFrontView> Fronts => Snapshot?.Fronts ?? Array.Empty<TheaterFrontView>();
        public IReadOnlyList<TheaterProposalView> Proposals => Snapshot?.Proposals ?? Array.Empty<TheaterProposalView>();
        public TheaterOperationView ActiveOperation => Snapshot?.Operation;
        public IReadOnlyList<string> StaffLog => Snapshot?.Log ?? Array.Empty<string>();

        public void Refresh()
        {
            GameAccess.TryGetLocalFaction(out FactionHQ hq);
            if (!ReferenceEquals(hq, viewHq))
            {
                viewHq = hq;
                if (!GameAccess.IsServer()) { ClearRemoteState(); network?.ClearClientState(); }
                hostCommand = null;
            }
        }

        public bool RequestPick(int proposalId, int revision) => Request(0, proposalId, revision, 0);
        public bool RequestCancel(int operationId, int revision) => Request(1, operationId, revision, 0);
        public bool RequestPosture(TheaterWarPosture posture) => Request(2, 0, 0, (byte)posture);

        private bool Request(byte kind, int id, int revision, byte posture)
        {
            if (!CanCommand || !GameAccess.TryGetLocalFaction(out FactionHQ hq)) return false;
            if (!Authoritative) return network != null && network.SendIntent(kind, id, revision, posture);
            IntentActor actor = GameManager.GetLocalPlayer(out NuclearOption.Networking.Player local) ? ActorOf(local) : default;
            IntentReply reply = ApplyIntent(hq, kind, id, revision, posture, actor);
            hostCommand = new TheaterCommandResult { Code = reply.Code, Detail = reply.Detail, LockSeconds = reply.LockSeconds };
            hostCommandAt = Time.unscaledTime;
            return reply.Code == TheaterCommandCode.Accepted;
        }

        /// <summary>The picture the transport sends for a faction; null before the host's first review.</summary>
        internal TheaterWarSnapshot PublishedFor(string faction) =>
            wars.TryGetValue(faction, out FactionWar war) ? war.Published : null;

        internal void ApplyRemote(TheaterWarSnapshot body)
        {
            if (GameAccess.IsServer() || body == null || string.IsNullOrEmpty(body.Faction) ||
                !GameAccess.TryGetLocalFaction(out FactionHQ hq) || hq?.faction?.factionName != body.Faction) return;
            viewHq = hq;
            FactionWar war = StateOf(body.Faction);
            if (war == null) return;
            war.Published = body;
            war.PublishedAt = Time.unscaledTime;
            war.Posture = body.Posture;
        }

        // ---- IGroundWarSource: what the ground service reads of the war -------------------------------------------------------------

        bool IGroundWarSource.TryGetOperation(FactionHQ hq, out GroundOperation operation)
        {
            operation = default;
            if (!Authoritative || MissionOver || hq?.faction == null || !wars.TryGetValue(hq.faction.factionName, out FactionWar war) || war.Active == null) return false;
            Operation op = war.Active;
            operation = new GroundOperation(op.Id, op.Key, op.Name, op.Kind, op.X, op.Z, op.Started);
            return true;
        }

        int IGroundWarSource.CopyFronts(FactionHQ hq, List<GroundFront> into)
        {
            into.Clear();
            if (!Authoritative || hq?.faction == null || !wars.TryGetValue(hq.faction.factionName, out FactionWar war)) return 0;
            for (int i = 0; i < war.Fronts.Count && into.Count < 8; i++)
                into.Add(new GroundFront(war.Fronts[i].Key, war.Fronts[i].Name, war.Fronts[i].X, war.Fronts[i].Z));
            return into.Count;
        }
    }
}
