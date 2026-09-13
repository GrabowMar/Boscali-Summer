using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.DynamicOperations.Configuration;
using BoscaliSummer.Features.DynamicOperations.Domain;
using BoscaliSummer.Features.DynamicOperations.Networking;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.DynamicOperations.Runtime
{
    internal sealed partial class OperationsManager : MonoBehaviour, ISceneService, ISecondaryObjectivesView, IOperationOutcomeSource
    {
        private sealed class Target
        {
            public Operation Mission;
            public Airbase Base;
            public Unit Unit;
            public FactionHQ OriginalOwner;
            public string Name, Outcome;
            public Vector3 Position;
            public float Radius = 1500f, LastJam = -100f;
            public int ShellId;
            public bool Inserted;
            public bool Serviced;
            public Aircraft ReturnAircraft, Observer;
            public readonly InterdictionState Life = new InterdictionState();

            public void Watch()
            {
                if (Unit != null) Unit.onDisableUnit += OnDisabled;
            }

            public void Unwatch()
            {
                if (!ReferenceEquals(Unit, null)) Unit.onDisableUnit -= OnDisabled;
            }

            private void OnDisabled(Unit unit)
            {
                if (unit != null) Life.ObserveDisable(unit.disabled, unit.NetworkHQ == OriginalOwner);
            }
        }

        private sealed class FactionBoard
        {
            public readonly OperationBoard Rules = new OperationBoard();
            public readonly List<Target> Targets = new List<Target>(OperationBoard.MaximumCards);
            public float NextGeneration;
        }

        private readonly Dictionary<FactionHQ, FactionBoard> boards = new Dictionary<FactionHQ, FactionBoard>();
        private readonly List<Airbase> bases = new List<Airbase>(64);
        private readonly HashSet<ulong> paidPlayers = new HashSet<ulong>();
        private readonly OperationRewards rewards = new OperationRewards();
        private DynamicOperationsSettings settings;
        private OperationsNet network;
        private ManualLogSource logger;
        private object missionIdentity;
        private float nextTick, previousTime, lastSnapshot;
        private int nextId;
        private FactionHQ viewHq;
        private bool wasEnabled;
        private bool generatedThisTick;
        private IAirAssaultObservation assault;
        private readonly System.Random random = new System.Random();
        public static OperationsManager Active { get; private set; }
        public event Action<int, float> MoraleAwarded;

        public IReadOnlyList<SecondaryObjectiveView> Objectives { get; private set; } = Array.Empty<SecondaryObjectiveView>();
        public string Status { get; private set; } = "Waiting for a running mission.";

        public void Configure(DynamicOperationsSettings configuration, OperationsNet transport, ManualLogSource log)
        {
            settings = configuration; network = transport; logger = log;
            rewards.Configure(log);
            wasEnabled = settings.Enabled.Value;
            Active = this;
        }

        public void ResetForScene()
        {
            foreach (FactionBoard board in boards.Values)
                for (int i = 0; i < board.Targets.Count; i++) board.Targets[i].Unwatch();
            rewards.ResetForScene(); boards.Clear(); bases.Clear(); paidPlayers.Clear();
            jammers.Clear();
            foreach (Candidate candidate in candidates) { candidate.Unit = null; candidate.Base = null; candidate.Count = 0; }
            missionIdentity = null; previousTime = 0f; nextTick = 0f;
            viewHq = null;
            network?.ResetScene();
            SetLocalStatus("Waiting for a running mission.");
        }

        private void OnDestroy()
        {
            if (assault != null) assault.Landed -= OnLanded;
            if (Active == this) Active = null;
            ResetForScene();
        }

        private void Update()
        {
            if (settings == null) return;
            if (!settings.Enabled.Value)
            {
                if (wasEnabled) ResetForScene();
                wasEnabled = false;
                SetLocalStatus("Dynamic operations disabled on this host.");
                return;
            }
            wasEnabled = true;
            ModServices.TryGet(out IAirAssaultObservation currentAssault);
            if (!ReferenceEquals(currentAssault, assault))
            {
                if (assault != null) assault.Landed -= OnLanded;
                assault = currentAssault;
                if (assault != null) assault.Landed += OnLanded;
            }
            var current = MissionManager.CurrentMission;
            MissionManager mission = NetworkSceneSingleton<MissionManager>.i;
            if (!ReferenceEquals(missionIdentity, current))
            {
                ResetForScene(); missionIdentity = current;
                previousTime = mission == null ? 0f : mission.MissionTime;
            }
            if (current == null || mission == null || !MissionManager.IsRunning)
            {
                if (boards.Count > 0) { ResetForScene(); missionIdentity = current; }
                SetLocalStatus("Waiting for a running mission.");
                return;
            }
            if (!GameAccess.IsServer())
            {
                if (!GameManager.GetLocalPlayer<Player>(out Player local) || local == null || local.HQ != viewHq ||
                    Time.unscaledTime - lastSnapshot > 6f)
                    SetLocalStatus("Refresh SECONDARY to receive current faction objectives.");
                return;
            }
            float now = mission.MissionTime;
            if (!Operation.Finite(now)) return;
            if (now < previousTime) { ResetForScene(); missionIdentity = current; }
            if (now < nextTick) return;
            float elapsed = Math.Max(0f, now - previousTime);
            previousTime = now; nextTick = now + 1f;
            sightlineQueries = 0;
            rewards.Prune();
            GatherBases();
            generatedThisTick = false;
            int inspected = 0;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (++inspected > 8) break;
                if (hq == null) continue;
                Player participant = FirstPlayer(hq);
                if (!boards.TryGetValue(hq, out FactionBoard board))
                {
                    if (participant == null || boards.Count >= 8) continue;
                    board = new FactionBoard(); boards.Add(hq, board);
                }
                TickBoard(hq, board, now, elapsed, participant);
            }
        }

        private void GatherBases()
        {
            bases.Clear();
            int inspected = 0;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (++inspected > 64) break;
                if (Usable(airbase) && !bases.Contains(airbase)) bases.Add(airbase);
            }
        }

        private void TickBoard(FactionHQ hq, FactionBoard board, float now, float elapsed, Player participant)
        {
            for (int i = board.Targets.Count - 1; i >= 0; i--)
            {
                Target target = board.Targets[i];
                Operation op = target.Mission;
                if (!op.IsLive && now - op.EndedAt >= 60f)
                { target.Unwatch(); board.Targets.RemoveAt(i); continue; }
                if (IsExtended(op.Kind))
                {
                    TickExtended(hq, target, now, elapsed);
                    if (!op.IsLive) target.Unwatch();
                    if (op.TryTakeAward()) Pay(hq, target, participant);
                    continue;
                }
                bool interdict = op.IsStrike || op.Kind == OperationKind.Jam;
                bool valid = interdict ? !target.Life.Despawned &&
                    (target.Life.Neutralized || target.Unit != null && target.Unit.NetworkHQ == target.OriginalOwner) :
                    Usable(target.Base) && (target.Base.CurrentHQ == target.OriginalOwner || target.Base.CurrentHQ == hq);
                bool owned = target.Base != null && target.Base.CurrentHQ == hq;
                bool neutralized = target.Life.Neutralized || target.Unit != null && target.Unit.disabled;
                if (op.Kind == OperationKind.Rappel || op.Kind == OperationKind.Rooftop)
                    valid &= assault?.Available == true && (target.Inserted || target.ShellId == 0 || assault.IsRooftopAvailable(target.ShellId));
                bool present = op.Kind == OperationKind.Jam ? now - target.LastJam <= 1.5f : HasPlayerOnStation(hq, target);
                op.Observe(now, elapsed, valid, owned, neutralized, present, target.Inserted);
                if (!op.IsLive) target.Unwatch();
                if (op.TryTakeAward()) Pay(hq, target, participant);
            }
            board.Rules.Prune(now);
            if (participant == null || generatedThisTick || now < board.NextGeneration || !board.Rules.HasCapacity) return;
            generatedThisTick = true;
            board.NextGeneration = now + 30f;
            Generate(hq, board, now);
        }

        private void Generate(FactionHQ hq, FactionBoard board, float now)
        {
            // ponytail: at most 64 bases and 4096 known-unit candidates per faction/30s;
            // use these native registries until profiling justifies a separate spatial index.
            Airbase capture = null, defend = null;
            float nearestFront = 40000f * 40000f;
            for (int i = 0; i < bases.Count; i++)
            {
                Airbase candidate = bases[i];
                if (candidate.CurrentHQ == null || candidate.CurrentHQ == hq ||
                    candidate.SavedAirbase == null || !candidate.SavedAirbase.Capturable ||
                    board.Rules.WasIssued(OperationKind.Capture, candidate.GetInstanceID())) continue;
                float distance = DistanceToOwnedBase(candidate.center.GlobalPosition().AsVector3(), hq, out _);
                if (distance < nearestFront) { nearestFront = distance; capture = candidate; }
            }
            Unit strike = null;
            Airbase strikeBase = null;
            float bestThreat = 0f, closestThreat = 6500f * 6500f;
            List<Unit> units = UnitRegistry.allUnits;
            int count = Math.Min(4096, units?.Count ?? 0);
            for (int i = 0; i < count; i++)
            {
                Unit unit = units[i];
                if (unit == null || unit.disabled || unit.NetworkHQ == null || unit.NetworkHQ == hq ||
                    !(unit is GroundVehicle || unit is Building) || unit.definition == null) continue;
                TrackingInfo tracking = hq.GetTrackingData(unit.persistentID);
                float age = tracking == null ? float.NaN : Time.timeSinceLevelLoad - tracking.lastSpottedTime;
                if (tracking == null || !Operation.Finite(age) || age < 0f || age > 30f ||
                    !hq.IsTargetBeingTracked(unit)) continue;
                Vector3 known = tracking.lastKnownPosition.AsVector3();
                if (!Finite(known)) continue;
                float distance = DistanceToOwnedBase(known, hq, out Airbase anchor);
                if (anchor == null) continue;
                if (unit is GroundVehicle && distance < closestThreat &&
                    !board.Rules.WasIssued(OperationKind.Defend, anchor.GetInstanceID()))
                { closestThreat = distance; defend = anchor; }
                if (distance > 15000f * 15000f || board.Rules.WasIssued(OperationKind.Interdict, unit.GetInstanceID())) continue;
                float role = 1f + Mathf.Clamp(unit.definition.roleIdentity.antiAir, 0f, 3f) * 2f +
                    Mathf.Clamp(unit.definition.roleIdentity.antiSurface, 0f, 3f);
                float score = role / (1000000f + distance);
                if (score > bestThreat) { bestThreat = score; strike = unit; strikeBase = anchor; }
            }
            GatherMissionCandidates(hq, board, units, count, now);
            // Shuffle viable mission families so a capture/defense pair cannot monopolize every board.
            var kinds = (OperationKind[])Enum.GetValues(typeof(OperationKind));
            for (int i = kinds.Length - 1; i > 0; i--)
            { int j = random.Next(i + 1); OperationKind swap = kinds[i]; kinds[i] = kinds[j]; kinds[j] = swap; }
            foreach (OperationKind kind in kinds)
            {
                if (!board.Rules.HasCapacity) break;
                if (HasKind(board, kind)) continue;
                if (kind == OperationKind.Capture && capture != null)
                    Add(board, now, kind, capture, null, hq, rewards.CanOffer(OperationReward.Fortification, hq) ? OperationReward.Fortification : OperationReward.None, 1800, 150);
                else if (kind == OperationKind.Defend && defend != null)
                    Add(board, now, kind, defend, null, hq, rewards.CanOffer(OperationReward.Convoy, hq) ? OperationReward.Convoy : OperationReward.None, 1200, 100);
                else if (kind == OperationKind.Interdict && strike != null)
                    Add(board, now, kind, strikeBase, strike, hq, OperationReward.None, 800, 75);
                else if (kind == OperationKind.Intercept || kind == OperationKind.Jam || IsExtended(kind))
                {
                    AddMissionCandidate(hq, board, kind, now);
                }
                else if (kind == OperationKind.Patrol || kind == OperationKind.Rappel || kind == OperationKind.Rooftop)
                {
                    if (kind != OperationKind.Patrol && assault?.Available != true) continue;
                    int start = random.Next(Math.Max(1, bases.Count));
                    for (int i = 0; i < bases.Count; i++)
                    {
                        Airbase anchor = bases[(start + i) % bases.Count];
                        if (anchor.CurrentHQ != hq || board.Rules.WasIssued(kind, anchor.GetInstanceID())) continue;
                        Vector3 position = anchor.center.GlobalPosition().AsVector3();
                        int shellId = 0;
                        if (kind == OperationKind.Rooftop && (!assault.TryRooftop(position.x, position.z, out shellId, out position.x, out position.z) ||
                            board.Rules.WasIssued(kind, shellId))) continue;
                        if (kind == OperationKind.Rappel)
                        {
                            Vector3 local = anchor.center.position + new Vector3(250f, 0f, 250f);
                            if (!Physics.Raycast(local + Vector3.up * 300f, Vector3.down, out RaycastHit hit, 1000f,
                                PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore) || hit.point.y <= Datum.LocalSeaY + 1f ||
                                hit.normal.y < 0.95f || GameAssets.i?.terrainMaterial == null || hit.collider.sharedMaterial != GameAssets.i.terrainMaterial) continue;
                            position = hit.point.ToGlobalPosition().AsVector3();
                        }
                        Target added = Add(board, now, kind, anchor, null, hq, OperationReward.None,
                            kind == OperationKind.Patrol ? 700 : 1500, kind == OperationKind.Patrol ? 60 : 125, shellId);
                        if (added != null)
                        { added.Position = position; added.ShellId = shellId; added.Radius = kind == OperationKind.Patrol ? 1500f : kind == OperationKind.Rooftop ? 40f : 100f; }
                        break;
                    }
                }
            }
        }

        private Target Add(FactionBoard board, float now, OperationKind kind, Airbase airbase, Unit unit,
            FactionHQ hq, OperationReward reward, int money, int xp, int shellId = 0)
        {
            int targetId = shellId != 0 ? shellId : unit != null ? unit.GetInstanceID() : airbase.GetInstanceID();
            float multiplier = settings.RewardMultiplier.Value;
            if (!Operation.Finite(multiplier)) multiplier = 1f;
            multiplier = Mathf.Clamp(multiplier, 0.25f, 4f);
            var op = new Operation(++nextId, targetId, kind, reward, now,
                Mathf.RoundToInt(money * multiplier), Mathf.RoundToInt(xp * multiplier));
            if (!board.Rules.TryAdd(op)) return null;
            var target = new Target
            {
                Mission = op, Base = airbase, Unit = unit,
                OriginalOwner = unit != null ? unit.NetworkHQ : airbase.CurrentHQ,
                Name = OperationsNet.Text(unit != null ? unit.unitName : airbase.name),
                Outcome = reward == OperationReward.Convoy ? "6-vehicle convoy if route/space permit; faction morale +3." :
                    reward == OperationReward.Fortification ? "3 defenses if space permits; faction morale +3 / enemy -3." :
                    (unit != null && unit.NetworkHQ != hq || airbase.CurrentHQ != hq) ? "Faction morale +3; hostile target faction -3." : "Faction morale +3. XP contributes to score-based perk points."
            };
            target.Position = unit != null && TryKnownPosition(hq, unit, out Vector3 known) ? known : airbase.center.GlobalPosition().AsVector3();
            if (kind == OperationKind.Interdict || kind == OperationKind.Intercept || kind == OperationKind.Jam) target.Radius = 0f;
            target.Watch();
            board.Targets.Add(target);
            return target;
        }

        private void Pay(FactionHQ hq, Target target, Player participant)
        {
            // Marked taken before any side effects: neither another poll nor a callback may pay twice.
            Operation op = target.Mission;
            MoraleAwarded?.Invoke(hq.GetInstanceID(), 3f);
            if (target.OriginalOwner != null && target.OriginalOwner != hq)
                MoraleAwarded?.Invoke(target.OriginalOwner.GetInstanceID(), -3f);
            int credited = 0, failures = 0;
            paidPlayers.Clear();
            for (int i = 0; i < Math.Min(64, hq.factionPlayers.Count); i++)
            {
                Player player = hq.factionPlayers[i].Player;
                if (player == null || player.HQ != hq || !paidPlayers.Add(PlayerIdentity.Of(player))) continue;
                try { hq.RewardPlayer(player, null, op.Money, op.Xp, FactionHQ.RewardType.None); credited++; }
                catch (Exception ex) { failures++; logger.LogWarning("[Operations] Award failed: " + ex.Message); }
            }
            string deployment = "";
            try
            {
                if (op.Reward != OperationReward.None)
                    deployment = participant == null ? "Special unavailable: no faction player." :
                        rewards.Execute(op.Reward, hq, target.Base, participant, op.Id);
            }
            catch (Exception ex)
            {
                deployment = "Special deployment failed.";
                logger.LogWarning("[Operations] " + deployment + " " + ex.Message);
            }
            target.Outcome = (failures > 0 ? "Payment incomplete; see host log. " :
                credited == 0 ? "No connected faction players to credit. " : "Faction players credited (money after tax). ") + deployment;
            target.Outcome = OperationsNet.Text(target.Outcome);
            logger.LogInfo("[Operations] Completed " + op.Kind + " #" + op.Id + ": " + target.Name + ". " + target.Outcome);
        }

        private float DistanceToOwnedBase(Vector3 position, FactionHQ hq, out Airbase nearest)
        {
            nearest = null; float best = float.MaxValue;
            for (int i = 0; i < bases.Count; i++)
            {
                Airbase airbase = bases[i];
                if (airbase.CurrentHQ != hq) continue;
                Vector3 delta = airbase.center.GlobalPosition().AsVector3() - position;
                float distance = delta.x * delta.x + delta.z * delta.z;
                if (distance < best) { best = distance; nearest = airbase; }
            }
            return best;
        }

        private static bool HasKind(FactionBoard board, OperationKind kind)
        {
            for (int i = 0; i < board.Targets.Count; i++)
                if (board.Targets[i].Mission.Kind == kind && board.Targets[i].Mission.IsLive) return true;
            return false;
        }

        private static bool Usable(Airbase airbase) => airbase != null && !airbase.disabled &&
            !airbase.AttachedAirbase && airbase.center != null;
        private static bool Finite(Vector3 position) => Operation.Finite(position.x) && Operation.Finite(position.y) && Operation.Finite(position.z);

        private static Player FirstPlayer(FactionHQ hq)
        {
            for (int i = 0; i < Math.Min(64, hq.factionPlayers.Count); i++)
            {
                Player player = hq.factionPlayers[i].Player;
                if (player != null && player.HQ == hq) return player;
            }
            return null;
        }

        public void Refresh() => network.Request();
        public void RequestAccept(int id) => network.Request(id, false);
        public void RequestCancel(int id) => network.Request(id, true);

        internal string Act(Player player, int id, bool cancel)
        {
            if (!GameAccess.IsServer() || !settings.Enabled.Value || !MissionManager.IsRunning ||
                !ReferenceEquals(missionIdentity, MissionManager.CurrentMission) || player?.HQ == null ||
                !boards.TryGetValue(player.HQ, out FactionBoard board)) return "Contract unavailable.";
            float now = NetworkSceneSingleton<MissionManager>.i.MissionTime;
            foreach (Target target in board.Targets)
            {
                if (target.Mission.Id != id) continue;
                if (cancel && target.Mission.IsLive)
                { target.Mission.Cancel(now); return "Contract dismissed. No reward or penalty."; }
                bool combat = target.Mission.IsStrike || target.Mission.Kind == OperationKind.Jam;
                bool valid = combat ? !target.Life.Despawned && target.Unit != null && target.Unit.NetworkHQ == target.OriginalOwner :
                    Usable(target.Base) && (target.Base.CurrentHQ == target.OriginalOwner || target.Base.CurrentHQ == player.HQ);
                if (IsExtended(target.Mission.Kind)) valid = ExtendedValid(player.HQ, target);
                if (target.ShellId != 0) valid &= assault?.IsRooftopAvailable(target.ShellId) == true;
                if (target.Mission.State == OperationState.Offered)
                    target.Mission.Observe(now, 0f, valid, target.Base != null && target.Base.CurrentHQ == player.HQ,
                        target.Life.Neutralized || target.Unit != null && target.Unit.disabled);
                if (cancel || !board.Rules.TryAccept(id, now)) return "Cannot accept: offer ended, target unavailable or two contracts already active.";
                target.LastJam = -100f; target.Inserted = false; target.Serviced = false; target.Observer = null;
                return "Contract accepted for your faction. Objective marked on map.";
            }
            return "Offer no longer available to your faction.";
        }

        private static bool TryKnownPosition(FactionHQ hq, Unit unit, out Vector3 position)
        {
            position = default;
            if (hq == null || unit == null || !hq.IsTargetBeingTracked(unit)) return false;
            TrackingInfo tracking = hq.GetTrackingData(unit.persistentID);
            if (tracking == null) return false;
            float age = Time.timeSinceLevelLoad - tracking.lastSpottedTime;
            if (!Operation.Finite(age) || age < 0f || age > 30f) return false;
            position = tracking.lastKnownPosition.AsVector3();
            return Finite(position);
        }

        private static bool HasPlayerOnStation(FactionHQ hq, Target target)
        {
            for (int i = 0; i < Math.Min(64, hq.factionPlayers.Count); i++)
            {
                Player player = hq.factionPlayers[i].Player;
                Aircraft aircraft = player != null && player.HQ == hq ? player.Aircraft : null;
                if (aircraft == null || aircraft.disabled || aircraft.NetworkHQ != hq) continue;
                Vector3 position = aircraft.transform.position.ToGlobalPosition().AsVector3();
                Vector3 delta = position - target.Position;
                if (Finite(position) && delta.y >= 50f && delta.sqrMagnitude <= target.Radius * target.Radius) return true;
            }
            return false;
        }

        private void OnLanded(int factionId, float x, float z, int shellId)
        {
            if (!GameAccess.IsServer() || !Operation.Finite(x) || !Operation.Finite(z)) return;
            foreach (var pair in boards)
            {
                if (pair.Key == null || pair.Key.GetInstanceID() != factionId) continue;
                foreach (Target target in pair.Value.Targets)
                {
                    if (target.Mission.State != OperationState.Active ||
                        !(target.Mission.Kind == OperationKind.Rappel || target.Mission.Kind == OperationKind.Rooftop)) continue;
                    float dx = target.Position.x - x, dz = target.Position.z - z;
                    if (dx * dx + dz * dz <= target.Radius * target.Radius &&
                        (target.ShellId == 0 ? shellId == 0 : target.ShellId == shellId)) target.Inserted = true;
                }
            }
        }

        internal void ObserveJam(Unit unit, Unit.JamEventArgs args)
        {
            if (!GameAccess.IsServer() || !MissionManager.IsRunning || unit == null || args.jammingUnit == null ||
                settings?.Enabled.Value != true || !ReferenceEquals(missionIdentity, MissionManager.CurrentMission) ||
                args.jammingUnit.NetworkHQ == null || !Operation.Finite(args.jamAmount) || args.jamAmount <= 0f) return;
            RememberJammer(unit, args.jammingUnit);
            if (!boards.TryGetValue(args.jammingUnit.NetworkHQ, out FactionBoard board)) return;
            foreach (Target target in board.Targets)
                if (target.Unit == unit && target.Mission.Kind == OperationKind.Jam && target.Mission.State == OperationState.Active)
                    target.LastJam = NetworkSceneSingleton<MissionManager>.i.MissionTime;
        }

        public OperationsSnapshot Snapshot(Player player)
        {
            var snapshot = new OperationsSnapshot { Protocol = OperationsNet.ProtocolVersion, Cards = Array.Empty<SecondaryObjectiveView>(),
                Status = "No eligible frontline missions. Capture, defense and known contacts create opportunities." };
            if (!GameAccess.IsServer()) { snapshot.Status = "Waiting for authoritative host state."; return snapshot; }
            if (!settings.Enabled.Value) { snapshot.Status = "Dynamic operations disabled on this host."; return snapshot; }
            if (!MissionManager.IsRunning || !ReferenceEquals(missionIdentity, MissionManager.CurrentMission))
            { snapshot.Status = "Waiting for a running mission."; return snapshot; }
            if (player == null || player.HQ == null)
            { snapshot.Status = "Join a faction to see its secondary objectives."; return snapshot; }
            if (!boards.TryGetValue(player.HQ, out FactionBoard board)) return snapshot;
            float now = NetworkSceneSingleton<MissionManager>.i.MissionTime;
            snapshot.Status = "Accept up to 2 faction contracts. Holds reset when interrupted. Money is before tax; XP is mission score.";
            snapshot.Cards = new SecondaryObjectiveView[board.Targets.Count];
            for (int i = 0; i < board.Targets.Count; i++)
            {
                Target target = board.Targets[i]; Operation op = target.Mission;
                bool active = op.State == OperationState.Active;
                bool marker = active && MarkerPosition(player.HQ, target);
                snapshot.Cards[i] = new SecondaryObjectiveView(op.Id,
                    OperationTitles.Title(op.Kind), op.Returning ? "Return in the same aircraft and land within 1 km of the marked friendly base." : Description(op.Kind), target.Name,
                    active && op.Returning ? "ACTIVE / RETURN TO BASE" : active && !marker ? "ACTIVE / CONTACT LOST" : op.State.ToString().ToUpperInvariant(),
                    op.IsLive || op.State == OperationState.Completed ? target.Outcome : "No reward: " + op.State.ToString().ToLowerInvariant() + ".",
                    op.Progress, op.IsLive ? Mathf.Clamp(op.Deadline - now, 0f, 1200f) : 0f, op.Money, op.Xp,
                    op.State == OperationState.Completed, op.State == OperationState.Offered, active, marker,
                    marker ? target.Position.x : 0f, marker ? target.Position.z : 0f, marker ? target.Radius : 0f);
            }
            return snapshot;
        }

        public void Apply(OperationsSnapshot snapshot)
        {
            Objectives = snapshot.Cards ?? Array.Empty<SecondaryObjectiveView>();
            Status = snapshot.Status;
            lastSnapshot = Time.unscaledTime;
            viewHq = GameManager.GetLocalPlayer<Player>(out Player player) && player != null ? player.HQ : null;
        }

        public void SetLocalStatus(string status) { Objectives = Array.Empty<SecondaryObjectiveView>(); Status = status; }
        internal void ReportStatus(string status) => Status = status;

        private static string Description(OperationKind kind) => kind switch
        {
            OperationKind.Capture => "Capture this forward base. Faction effort counts after acceptance.",
            OperationKind.Defend => "Keep base friendly; fly within 1.5 km, 50+ m above base, for 180 continuous seconds.",
            OperationKind.Interdict => "Neutralize this known vehicle or installation before the deadline.",
            OperationKind.Intercept => "Bring down the marked hostile aircraft within 10 minutes. Keep it tracked.",
            OperationKind.Patrol => "Fly within 1.5 km, 50+ m above this point, for 90 continuous seconds. Parking does not count.",
            OperationKind.Jam => "Jam this emitter for 45 continuous seconds with a directed jammer. Keep target alive.",
            OperationKind.Rappel => "Ibis + 8 troops: hover below 45 m and fast-rope onto ground within 100 m of the mark.",
            OperationKind.Rooftop => "Ibis + 8 troops: hover below 45 m above the marked roof and finish fast-roping onto that building.",
            OperationKind.Rescue => "Recover this friendly pilot using native rescue, then land the rescuing aircraft at the marked friendly base.",
            OperationKind.Recon => "Fly 50+ m above this contact within 1.5 km for 20s, with clear terrain sightline and fresh faction tracking.",
            OperationKind.DamageAssessment => "Neutralize this target, then survey its last known site from above within 1.5 km for 20 continuous seconds.",
            OperationKind.SupplyEscort => "Cover this truck from above within 1.5 km for 60s, then stay until it transfers supplies to a friendly unit.",
            OperationKind.SupplyInterdict => "Destroy this known hostile supply truck. Native resupply ends when the truck is neutralized.",
            OperationKind.RepairCover => "Cover this site from above within 1.5 km for 30s, then stay until native engineers complete repairs.",
            OperationKind.ElectronicWarfare => "Neutralize this tracked enemy that recently jammed your faction. Its native jamming ends with its destruction.",
            OperationKind.SortieReport => "Observe this tracked contact from above within 1.5 km for 30s, then land the same aircraft at the marked friendly base.",
            OperationKind.BattlefieldSurvey => "Survey this friendly wreck or damaged building from above within 1.5 km for 30s with a clear terrain sightline.",
            _ => "Follow the marked objective."
        };
    }
}
