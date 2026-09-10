using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.DynamicOperations.Configuration;
using BoscaliSummer.Features.DynamicOperations.Domain;
using BoscaliSummer.Features.DynamicOperations.Networking;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.DynamicOperations.Runtime
{
    internal sealed class OperationsManager : MonoBehaviour, ISceneService, ISecondaryObjectivesView
    {
        private sealed class Target
        {
            public Operation Mission;
            public Airbase Base;
            public Unit Unit;
            public FactionHQ OriginalOwner;
            public string Name, Outcome;
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

        public IReadOnlyList<SecondaryObjectiveView> Objectives { get; private set; } = Array.Empty<SecondaryObjectiveView>();
        public string Status { get; private set; } = "Waiting for a running mission.";

        public void Configure(DynamicOperationsSettings configuration, OperationsNet transport, ManualLogSource log)
        {
            settings = configuration; network = transport; logger = log;
            rewards.Configure(log);
            wasEnabled = settings.Enabled.Value;
        }

        public void ResetForScene()
        {
            foreach (FactionBoard board in boards.Values)
                for (int i = 0; i < board.Targets.Count; i++) board.Targets[i].Unwatch();
            rewards.ResetForScene(); boards.Clear(); bases.Clear(); paidPlayers.Clear();
            missionIdentity = null; previousTime = 0f; nextTick = 0f; nextId = 0;
            viewHq = null;
            network?.ResetScene();
            SetLocalStatus("Waiting for a running mission.");
        }

        private void OnDestroy() => ResetForScene();

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
                if (op.State != OperationState.Active && now - op.EndedAt >= 60f)
                { target.Unwatch(); board.Targets.RemoveAt(i); continue; }
                bool interdict = op.Kind == OperationKind.Interdict;
                bool valid = interdict ? !target.Life.Despawned &&
                    (target.Life.Neutralized || target.Unit != null && target.Unit.NetworkHQ == target.OriginalOwner) :
                    Usable(target.Base) && (target.Base.CurrentHQ == target.OriginalOwner || target.Base.CurrentHQ == hq);
                bool owned = target.Base != null && target.Base.CurrentHQ == hq;
                bool neutralized = target.Life.Neutralized || target.Unit != null && target.Unit.disabled;
                op.Observe(now, elapsed, valid, owned, neutralized);
                if (op.State != OperationState.Active) target.Unwatch();
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
            if (defend != null && !HasKind(board, OperationKind.Defend))
                Add(board, now, OperationKind.Defend, defend, null, hq,
                    rewards.CanOffer(OperationReward.Convoy, hq) ? OperationReward.Convoy : OperationReward.None, 1200, 100);
            if (capture != null && !HasKind(board, OperationKind.Capture))
                Add(board, now, OperationKind.Capture, capture, null, hq,
                    rewards.CanOffer(OperationReward.Fortification, hq) ? OperationReward.Fortification : OperationReward.None, 1800, 150);
            if (strike != null && !HasKind(board, OperationKind.Interdict))
                Add(board, now, OperationKind.Interdict, strikeBase, strike, hq, OperationReward.None, 800, 75);
        }

        private void Add(FactionBoard board, float now, OperationKind kind, Airbase airbase, Unit unit,
            FactionHQ hq, OperationReward reward, int money, int xp)
        {
            int targetId = unit != null ? unit.GetInstanceID() : airbase.GetInstanceID();
            float multiplier = settings.RewardMultiplier.Value;
            if (!Operation.Finite(multiplier)) multiplier = 1f;
            multiplier = Mathf.Clamp(multiplier, 0.25f, 4f);
            var op = new Operation(++nextId, targetId, kind, reward, now,
                Mathf.RoundToInt(money * multiplier), Mathf.RoundToInt(xp * multiplier));
            if (!board.Rules.TryAdd(op)) return;
            var target = new Target
            {
                Mission = op, Base = airbase, Unit = unit,
                OriginalOwner = unit != null ? unit.NetworkHQ : airbase.CurrentHQ,
                Name = OperationsNet.Text(unit != null ? unit.unitName : airbase.name),
                Outcome = reward == OperationReward.Convoy ? "SPECIAL: 6-vehicle reinforcement convoy (space/route permitting)." :
                    reward == OperationReward.Fortification ? "SPECIAL: defensive positions at the captured base (space permitting)." : "Money and XP; no special deployment."
            };
            target.Watch();
            board.Targets.Add(target);
        }

        private void Pay(FactionHQ hq, Target target, Player participant)
        {
            // Marked taken before any side effects: neither another poll nor a callback may pay twice.
            Operation op = target.Mission;
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
                if (board.Targets[i].Mission.Kind == kind && board.Targets[i].Mission.State == OperationState.Active) return true;
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
            snapshot.Status = "Faction objectives. Connected faction players earn money (before tax) + XP; specials deploy once.";
            snapshot.Cards = new SecondaryObjectiveView[board.Targets.Count];
            for (int i = 0; i < board.Targets.Count; i++)
            {
                Target target = board.Targets[i]; Operation op = target.Mission;
                bool active = op.State == OperationState.Active;
                snapshot.Cards[i] = new SecondaryObjectiveView(op.Id,
                    op.Kind == OperationKind.Capture ? "SECURE THE FRONT" : op.Kind == OperationKind.Defend ? "HOLD THE LINE" : "BREAK ENEMY PRESSURE",
                    op.Kind == OperationKind.Capture ? "Capture this forward base to expand friendly territory." :
                    op.Kind == OperationKind.Defend ? "Keep this threatened base under faction control for 3 minutes." : "Neutralize this known ground threat near friendly territory.",
                    target.Name, op.State.ToString().ToUpperInvariant(), active || op.State == OperationState.Completed ? target.Outcome : "No reward: " + op.State.ToString().ToLowerInvariant() + ".",
                    op.Progress, active ? Mathf.Clamp(op.Deadline - now, 0f, 1200f) : 0f, op.Money, op.Xp,
                    op.State == OperationState.Completed);
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
    }
}
