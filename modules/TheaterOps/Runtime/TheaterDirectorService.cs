using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.TheaterOps.Configuration;
using BoscaliSummer.Features.TheaterOps.Domain;
using BoscaliSummer.Features.TheaterOps.Networking;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Features.TheaterOps.Runtime
{
    /// <summary>
    /// The theater staff: a host-only review loop that fights the local faction's war.
    /// Every 30 seconds it senses the theater (objectives, both sides' ground presence,
    /// pool, plans), asks <see cref="DirectorDecision"/> what the judgment is, and spends
    /// from the pool exactly what the judgment ordered — opening offensives, funding waves,
    /// aiming the main effort, standing defenses. Every 10 seconds a cheaper check watches
    /// held ground for raids and rallies the effort the moment one lands, without waiting
    /// for the next review.
    ///
    /// <para>Players conduct through standing orders (stance, hold, chest, axes), applied
    /// here on the host with the faction and the setter derived from the sender. Every
    /// order and every decision lands in the staff log, which replicates with the posture
    /// so all peers read the same war.</para>
    ///
    /// <para>Only the local faction is ticked: every lever the director pulls (pool, main
    /// effort, convoy queue) is the host faction's own. Sensing reads vanilla state alone
    /// — objectives, the unit registry, the tracking database — so no sibling contract
    /// carries the assessment anywhere.</para>
    /// </summary>
    internal sealed class TheaterDirectorService : MonoBehaviour, ISceneService
    {
        private const float AuthorityInterval = 1f;
        private const float ReviewInterval = 30f;
        private const float ThreatInterval = 10f;
        private const float DefenseFundInterval = 60f;

        private const int MaximumObjectives = 12;
        private const int MaximumUnitScan = 4096;
        private const int MaximumThreatScan = 512;
        private const float PresenceRadiusMeters = 3000f;
        private const int MaximumTrackedPlans = 4;
        private const int MaximumConclusionMemory = 32;

        private sealed class ObjectiveFix
        {
            internal string Key;
            internal string Label;
            internal Vector3 Position;
        }

        private sealed class FactionDirection
        {
            internal readonly InfluenceState Influence = new InfluenceState();
            internal readonly StaffLog Log = new StaffLog();
            internal readonly List<ObjectiveFix> Fixes = new List<ObjectiveFix>(MaximumObjectives);
            internal readonly Dictionary<int, string> PendingTargets = new Dictionary<int, string>(MaximumTrackedPlans);
            internal readonly Dictionary<int, int> DesiredWaves = new Dictionary<int, int>(MaximumTrackedPlans);
            internal readonly HashSet<int> LoggedConclusions = new HashSet<int>();
            internal string CommittedTarget;
            internal int ReviewsHeld;
            internal string DefenseKey;
            internal string DefenseLabel;
            internal float LastDefenseFund = -10000f;
            internal float NextReview;
            internal float NextThreatCheck;
        }

        private readonly Dictionary<string, FactionDirection> states =
            new Dictionary<string, FactionDirection>(OffensiveTable.MaximumFactions);
        private readonly List<ObjectiveRead> reads = new List<ObjectiveRead>(MaximumObjectives);
        private readonly List<string> running = new List<string>(OffensiveTable.MaximumOperations);
        private readonly int[] hostileCounts = new int[MaximumObjectives];
        private readonly int[] friendlyCounts = new int[MaximumObjectives];
        private readonly int[] hostileFieldworks = new int[MaximumObjectives];
        private readonly int[] friendlyFieldworks = new int[MaximumObjectives];
        private readonly int[] suppressedFieldworks = new int[MaximumObjectives];
        private IFieldworksReadiness fieldworks;

        private TheaterOpsSettings settings;
        private TheaterPriorityService priority;
        private TheaterLogisticsService logistics;
        private TheaterOperationsService operations;
        private TheaterOpsNet network;
        private ManualLogSource logger;
        private float nextAuthority;
        private bool authoritative;

        public void Configure(
            TheaterOpsSettings config, TheaterPriorityService priorityService,
            TheaterLogisticsService logisticsService, TheaterOperationsService operationsService,
            TheaterOpsNet net, ManualLogSource log)
        {
            settings = config;
            priority = priorityService;
            logistics = logisticsService;
            operations = operationsService;
            network = net;
            logger = log;
        }

        public void ResetForScene()
        {
            states.Clear();
            reads.Clear();
            running.Clear();
            nextAuthority = 0f;
            authoritative = false;
            fieldworks = null;
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now >= nextAuthority)
            {
                nextAuthority = now + AuthorityInterval;
                authoritative = settings != null && settings.Enabled.Value && GameAccess.IsServer();
            }
            if (!authoritative || !TryGetLocalFaction(out FactionHQ hq)) return;

            string faction = hq.faction.factionName;
            FactionDirection state = StateOf(faction);
            if (state == null) return;

            if (now >= state.NextReview)
            {
                state.NextReview = now + ReviewInterval;
                state.NextThreatCheck = now + ThreatInterval;
                StrategicReview(hq, faction, state, now);
            }
            else if (now >= state.NextThreatCheck)
            {
                state.NextThreatCheck = now + ThreatInterval;
                ThreatCheck(hq, faction, state);
            }
        }

        // ---- Reviews ---------------------------------------------------------------------

        private void StrategicReview(FactionHQ hq, string faction, FactionDirection state, float now)
        {
            logistics?.Refresh();
            float funds = logistics != null ? logistics.FactionFunds : float.NaN;
            if (float.IsNaN(funds) || float.IsInfinity(funds)) funds = 0f;

            SenseObjectives(hq, state.Fixes);
            ScanPresence(hq, state.Fixes, MaximumUnitScan);
            reads.Clear();
            for (int i = 0; i < state.Fixes.Count; i++)
            {
                ObjectiveFix fix = state.Fixes[i];
                reads.Add(new ObjectiveRead(fix.Key, fix.Label, hostileCounts[i], friendlyCounts[i],
                    hostileFieldworks[i], friendlyFieldworks[i], suppressedFieldworks[i]));
            }

            running.Clear();
            string launchedTarget = null;
            int active = 0;
            if (operations != null && operations.TryGetFactionPlans(out FactionOffensives plans))
            {
                for (int i = 0; i < plans.Count; i++)
                {
                    OffensivePlan plan = plans[i];
                    if (plan == null || !plan.IsActive) continue;
                    active++;
                    if (!string.IsNullOrEmpty(plan.TargetKey))
                    {
                        running.Add(plan.TargetKey);
                        if (plan.Launched) launchedTarget = plan.TargetKey;
                    }
                }
            }

            string currentEffort = null;
            if (priority != null && priority.TryGetLocalDirective(out PriorityDirective directive))
                currentEffort = directive.Key;

            var assessment = new DirectorAssessment(
                reads, state.Influence, funds, operations != null ? operations.OverheadCost : 0f,
                operations != null ? operations.WaveBudget : 0f,
                active, OffensiveTable.MaximumOperations,
                state.CommittedTarget, state.ReviewsHeld, launchedTarget, currentEffort, running);
            DirectorOrders orders = DirectorDecision.Review(assessment);

            string oldDefense = state.DefenseKey;
            bool changed = ExecuteOrders(hq, faction, state, orders, funds, now);
            // A defense that ends without moving the effort still changes the replicated posture.
            if (!string.Equals(oldDefense, state.DefenseKey, StringComparison.Ordinal)) changed = true;
            for (int i = 0; i < orders.Log.Length; i++)
                if (state.Log.Add(orders.Log[i])) changed = true;
            changed |= WatchConclusions(state);
            if (changed) Broadcast(faction, state);
        }

        private bool ExecuteOrders(
            FactionHQ hq, string faction, FactionDirection state, DirectorOrders orders, float funds, float now)
        {
            bool changed = false;
            float spent = 0f;
            float reserve = state.Influence.ReserveFloor;
            float maxEscrow = state.Influence.MaxEscrowPerPlan;
            float waveBudget = operations != null ? operations.WaveBudget : 0f;

            if (orders.OpenOffensive && operations != null && operations.PrepareOperation(out int opened))
            {
                if (state.PendingTargets.Count < MaximumTrackedPlans)
                    state.PendingTargets[opened] = orders.TargetKey;
                if (state.DesiredWaves.Count < MaximumTrackedPlans)
                    state.DesiredWaves[opened] = orders.Waves;
                spent += operations.OverheadCost + waveBudget;
                changed = true;
            }

            if (!string.IsNullOrEmpty(orders.PreferredKey))
            {
                if (!string.Equals(state.CommittedTarget, orders.PreferredKey, StringComparison.Ordinal))
                {
                    state.CommittedTarget = orders.PreferredKey;
                    state.ReviewsHeld = 0;
                }
                else state.ReviewsHeld++;
            }
            else
            {
                state.CommittedTarget = null;
                state.ReviewsHeld = 0;
            }

            if (operations != null && operations.TryGetFactionPlans(out FactionOffensives plans))
            {
                for (int i = 0; i < plans.Count; i++)
                {
                    OffensivePlan plan = plans[i];
                    if (plan == null || !plan.IsActive) continue;

                    if (plan.CanTarget && state.PendingTargets.TryGetValue(plan.Id, out string pending))
                    {
                        if (operations.TargetOperation(plan.Id, pending))
                        {
                            state.PendingTargets.Remove(plan.Id);
                            changed = true;
                        }
                        else if (!CanResolve(hq, pending))
                        {
                            state.PendingTargets.Remove(plan.Id);
                            operations.AbortOperation(plan.Id, "target left the board");
                            state.Log.Add("STOOD DOWN " + plan.Name.ToUpperInvariant() + " — TARGET GONE");
                            changed = true;
                        }
                    }

                    int desired = state.DesiredWaves.TryGetValue(plan.Id, out int waves) ? waves : 1;
                    while (plan.WavesPlanned < desired && plan.CanCommit && plan.Committed < maxEscrow &&
                           waveBudget > 0f && funds - spent - reserve >= waveBudget)
                    {
                        if (!operations.CommitWave(plan.Id)) break;
                        spent += waveBudget;
                        changed = true;
                    }

                    if (state.Influence.HoldOffense && !plan.Launched)
                    {
                        operations.AbortOperation(plan.Id, "stood down — holding offense");
                        state.Log.Add("STOOD DOWN " + plan.Name.ToUpperInvariant() + " — HOLDING");
                        changed = true;
                    }
                }
                PrunePlanMemory(state, plans);
            }

            if (!string.IsNullOrEmpty(orders.EffortKey) && priority != null)
            {
                string current = null;
                if (priority.TryGetLocalDirective(out PriorityDirective directive)) current = directive.Key;
                if (!string.Equals(current, orders.EffortKey, StringComparison.Ordinal) &&
                    priority.SetDirective(orders.EffortKey))
                    changed = true;
            }
            else if (orders.ClearEffort && priority != null && priority.ClearDirective())
            {
                changed = true;
            }

            state.DefenseKey = orders.DefenseKey;
            state.DefenseLabel = orders.DefenseLabel;
            if (!string.IsNullOrEmpty(orders.DefenseKey) && now - state.LastDefenseFund >= DefenseFundInterval)
                changed |= FundDefenseShield(hq, state, funds - spent - reserve);

            return changed;
        }

        private bool FundDefenseShield(FactionHQ hq, FactionDirection state, float spendable)
        {
            if (logistics == null || operations == null) return false;
            IReadOnlyList<ReinforcementOption> options = logistics.Reinforcements;
            string cheapest = null;
            float cheapestCost = float.MaxValue;
            for (int i = 0; i < options.Count; i++)
            {
                ReinforcementOption option = options[i];
                if (option == null || !option.Ready || option.Cost > spendable) continue;
                if (option.Cost < cheapestCost)
                {
                    cheapestCost = option.Cost;
                    cheapest = option.Key;
                }
            }
            if (string.IsNullOrEmpty(cheapest)) return false;
            if (!logistics.RequestReinforcement(cheapest)) return false;
            state.LastDefenseFund = Time.unscaledTime;
            state.Log.Add("SHIELD CONVOY → " + (state.DefenseLabel ?? state.DefenseKey).ToUpperInvariant());
            logger?.LogInfo("Director shield convoy " + cheapest + " funded for " +
                            hq.faction.factionName + " (" + cheapestCost.ToString("F0") + ").");
            return true;
        }

        private bool WatchConclusions(FactionDirection state)
        {
            if (operations == null || !operations.TryGetFactionPlans(out FactionOffensives plans))
                return false;
            bool reported = false;
            for (int i = 0; i < plans.Count; i++)
            {
                OffensivePlan plan = plans[i];
                if (plan == null) continue;
                if (plan.Phase == TheaterOperationPhase.Concluded &&
                    !state.LoggedConclusions.Contains(plan.Id))
                {
                    state.LoggedConclusions.Add(plan.Id);
                    state.Log.Add(plan.Name.ToUpperInvariant() + " — " +
                                DirectorWords.OutcomeWord(plan.Outcome));
                    reported = true;
                }
            }
            if (state.LoggedConclusions.Count > MaximumConclusionMemory)
            {
                // Thirty-two reported ends is a long war; clearing risks one repeated line
                // for a plan still on its 24-second report, which the bounded ring absorbs.
                state.LoggedConclusions.Clear();
                for (int i = 0; i < plans.Count; i++)
                    if (plans[i] != null && plans[i].Phase == TheaterOperationPhase.Concluded)
                        state.LoggedConclusions.Add(plans[i].Id);
            }
            return reported;
        }

        private void ThreatCheck(FactionHQ hq, string faction, FactionDirection state)
        {
            if (state.Fixes.Count == 0) return;
            ScanPresence(hq, state.Fixes, MaximumThreatScan);

            string worstKey = null;
            string worstLabel = null;
            int worstThreat = 0;
            for (int i = 0; i < state.Fixes.Count; i++)
            {
                // Held ground only: empty or enemy ground waits for the full review.
                if (friendlyCounts[i] + friendlyFieldworks[i] == 0) continue;
                int threat = hostileCounts[i] + Math.Min(3, suppressedFieldworks[i]);
                if (threat > worstThreat)
                {
                    worstThreat = threat;
                    worstKey = state.Fixes[i].Key;
                    worstLabel = state.Fixes[i].Label;
                }
            }

            if (worstThreat < DirectorDecision.DefenseThreatMinimum || string.IsNullOrEmpty(worstKey))
                return;
            if (string.Equals(worstKey, state.DefenseKey, StringComparison.Ordinal)) return;

            state.DefenseKey = worstKey;
            state.DefenseLabel = worstLabel;
            priority?.SetDirective(worstKey);
            state.Log.Add("FRONT UNDER FIRE — " + (worstLabel ?? worstKey).ToUpperInvariant());
            Broadcast(faction, state);
        }

        // ---- Sensing -----------------------------------------------------------------------

        private static void SenseObjectives(FactionHQ hq, List<ObjectiveFix> into)
        {
            into.Clear();
            if (!MissionPosition.TryGetActiveObjectives(hq, out List<Objective> active) || active == null)
                return;
            for (int i = 0; i < active.Count && into.Count < MaximumObjectives; i++)
            {
                Objective objective = active[i];
                if (objective == null || objective.SavedObjective == null) continue;
                if (objective.SavedObjective.Hidden) continue;
                if (!(objective is IObjectiveWithPosition positioned) || positioned.Positions.Count == 0)
                    continue;
                string key = objective.SavedObjective.UniqueName;
                if (string.IsNullOrEmpty(key)) continue;
                into.Add(new ObjectiveFix
                {
                    Key = key,
                    Label = string.IsNullOrEmpty(objective.SavedObjective.DisplayName)
                        ? key : objective.SavedObjective.DisplayName,
                    Position = positioned.Positions[0].Position.AsVector3(),
                });
            }
        }

        private void ScanPresence(FactionHQ hq, List<ObjectiveFix> fixes, int unitCap)
        {
            if (fieldworks == null) ModServices.TryGet(out fieldworks);
            for (int i = 0; i < fixes.Count; i++)
            {
                hostileCounts[i] = 0;
                friendlyCounts[i] = 0;
                hostileFieldworks[i] = friendlyFieldworks[i] = suppressedFieldworks[i] = 0;
                if (fieldworks != null)
                    fieldworks.CountNear(hq, fixes[i].Position.x, fixes[i].Position.z,
                        PresenceRadiusMeters, out friendlyFieldworks[i], out hostileFieldworks[i],
                        out suppressedFieldworks[i]);
            }
            if (fixes.Count == 0) return;
            List<Unit> all = UnitRegistry.allUnits;
            if (all == null) return;

            float radius = PresenceRadiusMeters * PresenceRadiusMeters;
            int scanned = 0;
            for (int i = 0; i < all.Count && scanned < unitCap; i++)
            {
                Unit unit = all[i];
                if (unit == null || unit.disabled || !(unit is GroundVehicle)) continue;
                if (unit.NetworkHQ == null || unit.transform == null) continue;
                scanned++;

                bool friendly = ReferenceEquals(unit.NetworkHQ, hq);
                if (!friendly && !hq.IsTargetBeingTracked(unit)) continue;
                Vector3 position = unit.transform.position;
                for (int f = 0; f < fixes.Count; f++)
                {
                    Vector3 delta = position - fixes[f].Position;
                    if (delta.x * delta.x + delta.y * delta.y + delta.z * delta.z > radius) continue;
                    if (friendly) friendlyCounts[f]++;
                    else hostileCounts[f]++;
                }
            }
        }

        // ---- Influence ---------------------------------------------------------------------

        /// <summary>
        /// Applies one standing order on the host. The faction and setter arrive derived,
        /// never from the wire; an axis key must name a live objective or it is refused.
        /// </summary>
        internal bool ApplyInfluence(
            string faction, byte kind, float value, float value2, string key, string setter)
        {
            if (!authoritative || settings == null || !settings.Enabled.Value) return false;
            if (!TryGetLocalFaction(out FactionHQ hq) || hq.faction == null) return false;
            if (!string.Equals(hq.faction.factionName, faction, StringComparison.Ordinal))
                return false;

            FactionDirection state = StateOf(faction);
            if (state == null) return false;

            bool changed;
            switch (kind)
            {
                case TheaterOpsNet.InfluenceStance:
                    changed = state.Influence.SetStance(value, setter);
                    if (changed) state.Log.Add(StanceLine(state.Influence.Stance, setter));
                    break;
                case TheaterOpsNet.InfluenceHold:
                    changed = state.Influence.SetHold(value >= 0.5f, setter);
                    if (changed) state.Log.Add(state.Influence.HoldOffense
                        ? "HOLDING OFFENSE — " + SetterOf(setter) : "HOLD RELEASED — " + SetterOf(setter));
                    break;
                case TheaterOpsNet.InfluenceChest:
                    changed = state.Influence.SetChest(value, value2, setter);
                    if (changed) state.Log.Add("CHEST ESC ≤" + state.Influence.MaxEscrowPerPlan.ToString("F0") +
                        " RES ≥" + state.Influence.ReserveFloor.ToString("F0") + " — " + SetterOf(setter));
                    break;
                case TheaterOpsNet.InfluenceAxis:
                    if (!CanResolve(hq, key)) return false;
                    changed = state.Influence.SetAxis(key, value, setter);
                    if (changed) state.Log.Add(AxisLine(AxisLabel(hq, key), state.Influence.WeightOf(key), setter));
                    break;
                default:
                    return false;
            }

            if (changed) Broadcast(faction, state);
            return changed;
        }

        // ---- Reads for the board -------------------------------------------------------------

        internal TheaterDirectionView DirectionOf(string faction)
        {
            if (string.IsNullOrEmpty(faction) || !states.TryGetValue(faction, out FactionDirection state))
                return new TheaterDirectionView(TheaterDirectorPosture.Idle, false, null, 0);

            int active = 0;
            bool launched = false;
            if (operations != null && operations.TryGetFactionPlans(out FactionOffensives plans))
            {
                for (int i = 0; i < plans.Count; i++)
                {
                    if (plans[i] == null || !plans[i].IsActive) continue;
                    active++;
                    if (plans[i].Launched) launched = true;
                }
            }

            bool effortDefense = false;
            if (!string.IsNullOrEmpty(state.DefenseKey) && priority != null &&
                priority.TryGetLocalDirective(out PriorityDirective directive))
                effortDefense = string.Equals(directive.Key, state.DefenseKey, StringComparison.Ordinal);

            TheaterDirectorPosture posture = (launched || active > 0) && !state.Influence.HoldOffense
                ? TheaterDirectorPosture.Attacking
                : !string.IsNullOrEmpty(state.DefenseKey) ? TheaterDirectorPosture.Defending
                : state.Influence.HoldOffense ? TheaterDirectorPosture.Holding
                : TheaterDirectorPosture.Idle;
            return new TheaterDirectionView(posture, effortDefense, state.DefenseLabel, active);
        }

        internal TheaterInfluenceView InfluenceOf(string faction)
        {
            if (string.IsNullOrEmpty(faction) || !states.TryGetValue(faction, out FactionDirection state))
            {
                var fresh = new InfluenceState();
                return new TheaterInfluenceView(
                    fresh.Stance, fresh.HoldOffense, fresh.MaxEscrowPerPlan, fresh.ReserveFloor,
                    "", new TheaterAxisView[0]);
            }
            InfluenceState influence = state.Influence;
            var axes = new List<TheaterAxisView>(influence.Axes.Count);
            for (int i = 0; i < influence.Axes.Count; i++)
                axes.Add(new TheaterAxisView(influence.Axes[i].Key, influence.Axes[i].Weight));
            return new TheaterInfluenceView(
                influence.Stance, influence.HoldOffense, influence.MaxEscrowPerPlan,
                influence.ReserveFloor, influence.Setter, axes);
        }

        internal void CopyStaffLog(string faction, List<string> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(faction) || !states.TryGetValue(faction, out FactionDirection state))
                return;
            for (int i = 0; i < state.Log.Entries.Count; i++) into.Add(state.Log.Entries[i]);
        }

        internal void ReportBattle(string faction, string line)
        {
            if (!authoritative) return;
            FactionDirection state = StateOf(faction);
            if (state != null && state.Log.Add(line)) Broadcast(faction, state);
        }

        internal void ReportConclusion(string faction, OffensivePlan plan)
        {
            if (!authoritative || plan == null) return;
            FactionDirection state = StateOf(faction);
            if (state == null || !state.LoggedConclusions.Add(plan.Id)) return;
            if (state.Log.Add(plan.Name.ToUpperInvariant() + " — " +
                              DirectorWords.OutcomeWord(plan.Outcome))) Broadcast(faction, state);
        }

        // ---- Internals ---------------------------------------------------------------------

        private FactionDirection StateOf(string faction)
        {
            if (string.IsNullOrEmpty(faction)) return null;
            if (states.TryGetValue(faction, out FactionDirection state)) return state;
            if (states.Count >= OffensiveTable.MaximumFactions) return null;
            state = new FactionDirection();
            states.Add(faction, state);
            return state;
        }

        private static bool CanResolve(FactionHQ hq, string key) =>
            !string.IsNullOrEmpty(key) &&
            TheaterPriorityService.TryResolveObjective(hq, key, out _, out _);

        private static void PrunePlanMemory(FactionDirection state, FactionOffensives plans)
        {
            var present = new HashSet<int>();
            for (int i = 0; i < plans.Count; i++)
                if (plans[i] != null) present.Add(plans[i].Id);
            PruneIds(state.PendingTargets, present);
            PruneIds(state.DesiredWaves, present);
        }

        private static void PruneIds<T>(Dictionary<int, T> memory, HashSet<int> present)
        {
            var stale = new List<int>(4);
            foreach (int id in memory.Keys)
                if (!present.Contains(id)) stale.Add(id);
            for (int i = 0; i < stale.Count; i++) memory.Remove(stale[i]);
        }

        private void Broadcast(string faction, FactionDirection state)
        {
            if (network == null) return;
            TheaterDirectionView direction = DirectionOf(faction);
            network.BroadcastDirector(
                faction, InfluenceOf(faction), direction.Posture, direction.EffortIsDefense,
                state.DefenseLabel ?? "", direction.ActivePlans);
            network.BroadcastDirectorLog(faction, state.Log);
        }

        private static string SetterOf(string setter) =>
            string.IsNullOrEmpty(setter) ? "STAFF" : setter.ToUpperInvariant();

        private static string StanceLine(float stance, string setter)
        {
            int attack = (int)Math.Round(stance * 100f);
            return "STANCE " + attack + "/" + (100 - attack) + " — " + SetterOf(setter);
        }

        private static string AxisLabel(FactionHQ hq, string key) =>
            TheaterPriorityService.TryResolveObjective(hq, key, out string label, out _) &&
            !string.IsNullOrEmpty(label) ? label : key;

        private static string AxisLine(string key, float weight, string setter)
        {
            string name = key.Length > 24 ? key.Substring(0, 24) : key;
            string lean = weight >= 0f ? "+" + weight.ToString("F1") : weight.ToString("F1");
            return "AXIS " + name.ToUpperInvariant() + " " + lean + " — " + SetterOf(setter);
        }

        private static bool TryGetLocalFaction(out FactionHQ hq)
        {
            hq = null;
            if (!GameManager.GetLocalHQ(out FactionHQ local) || local == null || local.faction == null)
                return false;
            hq = local;
            return true;
        }
    }
}
