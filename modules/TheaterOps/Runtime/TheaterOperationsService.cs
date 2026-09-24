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
using Mirage;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Features.TheaterOps.Runtime
{
    /// <summary>
    /// Host-authoritative offensives for the STR console's OPERATIONS page.
    ///
    /// <para>An operation is a staff plan that runs itself. The theater director opens it,
    /// names its objective, sizes and funds its waves from the faction pool; the plan
    /// musters and plans on its own; H-hour then comes on its own and the offensive sets
    /// the faction's main effort to that objective and spends its wave slots on the
    /// mission's own convoy groups as the vanilla supply path lets them go. When the last
    /// funded wave is on the road the push holds, and the director funds more or lets it
    /// report. Every wave is real vanilla supply; GroundFrontService later stages eligible
    /// depot ground AI through vanilla's destination query. This service owns the escrow and
    /// the board; the director owns every
    /// decision, and players conduct through standing orders.</para>
    ///
    /// <para>Only one push may hold the faction's effort: a plan due at H-hour while
    /// another objective holds the order waits, visibly, and launches itself when the
    /// effort frees. A concluded offensive releases an effort it still owns, so the AI is
    /// never left pushing a dead objective.</para>
    ///
    /// <para>Clients receive the host's board read-only over <see cref="TheaterOpsNet"/> and
    /// never tick or mutate it.</para>
    /// </summary>
    internal sealed class TheaterOperationsService : MonoBehaviour, ISceneService, ITheaterOperationsView
    {
        private const float AuthorityInterval = 1f;
        private const float SyncInterval = 1f;
        private const float ViewInterval = 0.5f;

        /// <summary>How long a concluded plan keeps reporting its outcome before its slot frees.</summary>
        private const float ResolvedHoldSeconds = 24f;

        /// <summary>One bounded pass over the mission's own convoy groups per wave attempt.</summary>
        private const int MaximumConvoyScan = 8;

        private sealed class RemoteBoard
        {
            internal readonly TheaterOperationView[] Plans =
                new TheaterOperationView[OffensiveTable.MaximumOperations];
            internal int Count;

            internal void Reset()
            {
                Array.Clear(Plans, 0, Plans.Length);
                Count = 0;
            }
        }

        private sealed class RemoteDirection
        {
            internal TheaterDirectionView Direction;
            internal TheaterInfluenceView Influence;
            internal readonly List<string> Log = new List<string>(Domain.StaffLog.MaximumEntries);
        }

        private readonly OffensiveTable table = new OffensiveTable();
        private readonly List<TheaterOperationView> views =
            new List<TheaterOperationView>(OffensiveTable.MaximumOperations);
        private readonly Dictionary<string, RemoteBoard> remote =
            new Dictionary<string, RemoteBoard>(OffensiveTable.MaximumFactions, StringComparer.Ordinal);
        private readonly Dictionary<string, RemoteDirection> remoteDirection =
            new Dictionary<string, RemoteDirection>(OffensiveTable.MaximumFactions, StringComparer.Ordinal);
        private readonly List<string> logView = new List<string>(Domain.StaffLog.MaximumEntries);

        private TheaterOpsSettings settings;
        private TheaterOpsNet network;
        private TheaterPriorityService priority;
        private TheaterDirectorService director;
        private ManualLogSource logger;
        private IHighCommandView highCommand;

        private bool authoritative;
        private bool broadcastHadOperations;
        private float planScale = 1f;
        private float nextAuthority;
        private float nextSync;
        private float nextView;

        public bool Available => settings != null && settings.Enabled.Value;

        /// <summary>Any faction member conducts; the host validates every intent.</summary>
        public bool CanCommand => Available;
        public string Status => authoritative
            ? "Host authority. The staff fights the faction's war."
            : "The host's staff fights the faction's war.";
        public float OverheadCost => settings != null
            ? Mathf.Max(0f, settings.OperationOverheadCost.Value)
            : 0f;
        public float WaveBudget => settings != null
            ? Mathf.Max(0f, settings.OperationWaveBudget.Value)
            : 0f;
        public int MaximumWaves => OffensivePlan.MaximumWaves;
        public IReadOnlyList<TheaterOperationView> Operations => views;

        public void Configure(
            TheaterOpsSettings config, TheaterOpsNet net, TheaterPriorityService owner,
            TheaterDirectorService directorService, ManualLogSource log)
        {
            settings = config;
            network = net;
            priority = owner;
            director = directorService;
            logger = log;
        }

        public void ResetForScene()
        {
            table.Clear();
            views.Clear();
            remote.Clear();
            remoteDirection.Clear();
            logView.Clear();
            broadcastHadOperations = false;
            planScale = 1f;
            nextAuthority = 0f;
            nextSync = 0f;
            nextView = 0f;
            authoritative = false;
        }

        private void Update()
        {
            if (settings == null) return;

            float now = Time.unscaledTime;
            if (now >= nextAuthority)
            {
                nextAuthority = now + AuthorityInterval;
                authoritative = settings.Enabled.Value && GameAccess.IsServer();
                RefreshPlanScale();
            }
            if (!settings.Enabled.Value || !authoritative) return;

            TickHost(Time.deltaTime);

            if (now < nextSync) return;
            nextSync = now + SyncInterval;
            SyncHost();
        }

        // ---- Host simulation ---------------------------------------------------------------

        private void TickHost(float delta)
        {
            if (delta <= 0f) return;
            if (delta > 1f) delta = 1f;
            if (!TryGetLocalFaction(out FactionHQ hq)) return;
            if (!table.TryGet(hq.faction.factionName, out FactionOffensives faction)) return;

            OffensiveTiming timing = Timing();
            for (int i = 0; i < faction.Count; i++)
            {
                OffensivePlan plan = faction[i];
                if (plan == null) continue;

                // One push owns the faction's effort at a time. A plan due at H-hour while
                // another objective holds the order waits here rather than stealing it; it
                // launches by itself the moment the effort frees.
                if (plan.IsAtHHour && !EffortFree(plan)) continue;

                OffensiveTick tick = plan.Tick(delta, timing);
                if (tick.LaunchDue) Launch(hq, plan);
                if (tick.WaveDue) DeliverWave(hq, plan);
                if (tick.AssaultExpired)
                    Conclude(hq, plan,
                        plan.Launched ? TheaterOperationOutcome.CommitmentSpent : TheaterOperationOutcome.Stalled,
                        plan.Launched ? "waves spent" : "no wave found the road");
            }
        }

        private void SyncHost()
        {
            if (!TryGetLocalFaction(out FactionHQ hq)) return;
            string factionName = hq.faction.factionName;

            if (!table.TryGet(factionName, out FactionOffensives faction))
            {
                BroadcastEmpty(factionName);
                return;
            }

            CheckObjectives(hq, faction);

            for (int i = faction.Count - 1; i >= 0; i--)
            {
                OffensivePlan plan = faction[i];
                if (plan != null && plan.ConcludedExpired(ResolvedHoldSeconds)) faction.Remove(plan.Id);
            }

            if (faction.Count == 0)
            {
                table.Prune(factionName);
                BroadcastEmpty(factionName);
                return;
            }

            network?.BroadcastOperations(factionName, faction);
            broadcastHadOperations = true;
            nextView = 0f;
        }

        private void BroadcastEmpty(string factionName)
        {
            if (!broadcastHadOperations) return;
            network?.BroadcastOperations(factionName, null);
            broadcastHadOperations = false;
            nextView = 0f;
        }

        /// <summary>
        /// At H-hour the push starts with one supply wave and the staff hands the objective to
        /// the AI as the faction's main effort, so every unit with no better order follows it.
        /// </summary>
        private void Launch(FactionHQ hq, OffensivePlan plan)
        {
            // Make the effort visible before a depot can release an immediately ready unit.
            if (priority != null && plan.TargetKey != null) priority.SetDirective(plan.TargetKey);
            int firstGroup = DeliverWave(hq, plan);
            // A cohesive staff can release two distinct funded groups at H-hour. The
            // ordinary wave schedule resumes afterward; no extra escrow or unit order.
            if (firstGroup >= 0 && plan.WavesPlanned >= 3 &&
                highCommand != null && highCommand.Available &&
                highCommand.FriendlyCohesion >= .65f &&
                DeliverWave(hq, plan, firstGroup) >= 0)
                director?.ReportBattle(hq.faction.factionName,
                    plan.Name.ToUpperInvariant() + " SHOCK PUSH — TWO GROUPS RELEASED");
            director?.ReportBattle(hq.faction.factionName,
                "H-HOUR " + plan.Name.ToUpperInvariant() + " — " +
                (plan.TargetLabel ?? plan.TargetKey ?? "TARGET").ToUpperInvariant());
            logger?.LogInfo("Offensive \"" + plan.Name + "\" launched at " +
                            (plan.TargetLabel ?? plan.TargetKey) + " for " + hq.faction.factionName + ".");
        }

        /// <summary>
        /// Funds one wave from the escrow. The heaviest group whose cost the remaining budget
        /// covers and whose own vanilla cooldown has elapsed goes onto the road; a wave that
        /// finds nothing ready stays pending and is retried, never silently dropped.
        /// </summary>
        private int DeliverWave(FactionHQ hq, OffensivePlan plan, int excludedGroup = -1)
        {
            if (hq.preventDonation) return -1;
            List<Faction.ConvoyGroup> groups = hq.faction.GetConvoyGroups();
            if (groups == null || groups.Count == 0 || plan.AllWavesDelivered) return -1;

            int count = Mathf.Min(groups.Count, MaximumConvoyScan);
            int best = -1;
            float bestCost = -1f;
            for (int i = 0; i < count; i++)
            {
                if (i == excludedGroup) continue;
                Faction.ConvoyGroup group = groups[i];
                if (group == null) continue;

                float cost = group.GetCost();
                if (float.IsNaN(cost) || float.IsInfinity(cost) || cost < 0f ||
                    cost > plan.Budget) continue;
                if (hq.CmdGetDelaySpawnConvoy((byte)i) > 0f) continue;
                if (cost > bestCost)
                {
                    bestCost = cost;
                    best = i;
                }
            }
            if (best < 0) return -1;

            Faction.ConvoyGroup chosen = groups[best];
            hq.AddConvoy(chosen);
            plan.WaveDelivered(bestCost);
            director?.ReportBattle(hq.faction.factionName,
                plan.Name.ToUpperInvariant() + " WAVE " + plan.WavesLaunched + "/" +
                plan.WavesPlanned + " ON THE ROAD");
            logger?.LogInfo("Offensive \"" + plan.Name + "\" delivered " + chosen.Name +
                            " (" + bestCost.ToString("F0") + ") for " + hq.faction.factionName + ".");
            return best;
        }

        private void CheckObjectives(FactionHQ hq, FactionOffensives faction)
        {
            if (!MissionPosition.TryGetActiveObjectives(hq, out List<Objective> active) || active == null)
                return;

            for (int i = 0; i < faction.Count; i++)
            {
                OffensivePlan plan = faction[i];
                if (plan == null || plan.TargetKey == null) continue;
                if (plan.Phase != TheaterOperationPhase.Launching &&
                    plan.Phase != TheaterOperationPhase.Assault &&
                    plan.Phase != TheaterOperationPhase.Holding) continue;
                if (ContainsObjective(active, plan.TargetKey)) continue;

                // The objective left the active board. If its own status says complete it is
                // ours — whether our waves took it or the line moved under us; anything else
                // means the offensive has nothing left to take.
                Objective resolved = MissionManager.Objectives != null
                    ? MissionManager.Objectives.GetObjective(plan.TargetKey)
                    : null;
                bool secured = resolved != null && resolved.Status == ObjectiveStatus.Complete;

                Conclude(hq, plan,
                    secured ? TheaterOperationOutcome.ObjectiveSecured : TheaterOperationOutcome.ObjectiveLost,
                    secured ? "objective secured" : "objective closed");
            }
        }

        private void Conclude(
            FactionHQ hq, OffensivePlan plan,
            TheaterOperationOutcome outcome, string reason)
        {
            bool launched = plan.Launched;
            float refund = plan.Conclude(outcome);
            if (refund > 0f) hq.AddFunds(refund);
            director?.ReportConclusion(hq.faction.factionName, plan);

            // A push that reached H-hour owns the faction's effort. When it ends, the order
            // ends with it: leaving it set would point the AI at a dead objective and would
            // block the next offensive from ever taking the effort.
            if (launched && priority != null && plan.TargetKey != null &&
                priority.TryGetLocalDirective(out PriorityDirective directive) &&
                string.Equals(directive.Key, plan.TargetKey, StringComparison.Ordinal))
                priority.ClearDirective();

            logger?.LogInfo("Offensive \"" + plan.Name + "\" concluded (" + outcome + "): " +
                            reason + " (" + refund.ToString("F0") + " refunded).");
        }

        /// <summary>Whether the faction's effort already points where this plan is going.</summary>
        private bool EffortFree(OffensivePlan plan)
        {
            if (priority == null || plan.TargetKey == null) return true;
            if (!priority.TryGetLocalDirective(out PriorityDirective directive)) return true;
            return string.Equals(directive.Key, plan.TargetKey, StringComparison.Ordinal);
        }

        /// <summary>
        /// What is holding the effort when a due plan is not free to launch: the other
        /// offensive that owns it, or the staff's defense when it holds the order.
        /// </summary>
        private string EffortHolder(FactionOffensives faction, OffensivePlan plan)
        {
            if (priority == null || plan.TargetKey == null) return null;
            if (!priority.TryGetLocalDirective(out PriorityDirective directive)) return null;
            if (string.Equals(directive.Key, plan.TargetKey, StringComparison.Ordinal)) return null;

            for (int i = 0; i < faction.Count; i++)
            {
                OffensivePlan other = faction[i];
                if (other == null || ReferenceEquals(other, plan) || !other.Launched) continue;
                if (string.Equals(other.TargetKey, directive.Key, StringComparison.Ordinal))
                    return other.Name;
            }

            return string.IsNullOrEmpty(directive.Label)
                ? "THE MAIN EFFORT"
                : directive.Label.ToUpperInvariant();
        }

        private static bool ContainsObjective(List<Objective> objectives, string key)
        {
            for (int i = 0; i < objectives.Count; i++)
            {
                Objective objective = objectives[i];
                if (objective != null && objective.SavedObjective != null &&
                    string.Equals(objective.SavedObjective.UniqueName, key, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        // ---- ITheaterOperationsView -------------------------------------------------------

        public void Refresh()
        {
            float now = Time.unscaledTime;
            if (now < nextView) return;
            nextView = now + ViewInterval;

            views.Clear();
            if (!TryGetLocalFaction(out FactionHQ hq) || hq.faction == null) return;
            string factionName = hq.faction.factionName;

            if (authoritative)
            {
                if (!table.TryGet(factionName, out FactionOffensives faction)) return;
                for (int i = 0; i < faction.Count; i++)
                {
                    OffensivePlan plan = faction[i];
                    if (plan != null) views.Add(ViewOf(faction, plan));
                }
                return;
            }

            if (!remote.TryGetValue(factionName, out RemoteBoard board)) return;
            for (int i = 0; i < board.Count; i++)
                if (board.Plans[i] != null) views.Add(board.Plans[i]);
        }

        // ---- Direction, influence and staff log -------------------------------------------

        public TheaterDirectionView Direction
        {
            get
            {
                if (!TryGetLocalFaction(out FactionHQ hq) || hq.faction == null) return null;
                if (authoritative) return director?.DirectionOf(hq.faction.factionName);
                return remoteDirection.TryGetValue(hq.faction.factionName, out RemoteDirection mirror)
                    ? mirror.Direction : null;
            }
        }

        public TheaterInfluenceView Influence
        {
            get
            {
                if (!TryGetLocalFaction(out FactionHQ hq) || hq.faction == null) return null;
                if (authoritative) return director?.InfluenceOf(hq.faction.factionName);
                return remoteDirection.TryGetValue(hq.faction.factionName, out RemoteDirection mirror)
                    ? mirror.Influence : null;
            }
        }

        public IReadOnlyList<string> StaffLog
        {
            get
            {
                logView.Clear();
                if (!TryGetLocalFaction(out FactionHQ hq) || hq.faction == null) return logView;
                if (authoritative)
                {
                    director?.CopyStaffLog(hq.faction.factionName, logView);
                    return logView;
                }
                if (remoteDirection.TryGetValue(hq.faction.factionName, out RemoteDirection mirror))
                    for (int i = 0; i < mirror.Log.Count; i++) logView.Add(mirror.Log[i]);
                return logView;
            }
        }

        public bool RequestStance(float stance) =>
            Conduct(TheaterOpsNet.InfluenceStance, stance, 0f, null);

        public bool RequestHold(bool hold) =>
            Conduct(TheaterOpsNet.InfluenceHold, hold ? 1f : 0f, 0f, null);

        public bool RequestChest(float maxEscrowPerPlan, float reserveFloor) =>
            Conduct(TheaterOpsNet.InfluenceChest, maxEscrowPerPlan, reserveFloor, null);

        public bool RequestAxis(string objectiveKey, float weight) =>
            string.IsNullOrEmpty(objectiveKey)
                ? false : Conduct(TheaterOpsNet.InfluenceAxis, weight, 0f, objectiveKey);

        /// <summary>
        /// One funnel for every influence intent. The host applies it to its own faction;
        /// a client sends it and the host derives the faction and the setter from the
        /// sender, so a wire intent can neither forge a faction nor sign another name.
        /// </summary>
        private bool Conduct(byte kind, float value, float value2, string key)
        {
            if (!Available || director == null) return false;
            if (authoritative)
            {
                if (!TryGetLocalFaction(out FactionHQ hq) || hq.faction == null) return false;
                return director.ApplyInfluence(
                    hq.faction.factionName, kind, value, value2, key, LocalSetter());
            }
            if (network == null) return false;
            network.SendInfluenceIntent(kind, value, value2, key);
            return true;
        }

        private static string LocalSetter()
        {
            try
            {
                if (GameManager.GetLocalPlayer<Player>(out Player player) && player != null)
                {
                    string name = player.GetDisplayName(PlayerNameContext.ChatOrLeaderboard);
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
            catch (Exception)
            {
                // The setter is attribution, never authority: fall through to HOST.
            }
            return "HOST";
        }

        // ---- Director drive (host only, no player intent reaches here) ---------------------

        internal bool PrepareOperation(out int id)
        {
            id = -1;
            if (!authoritative || !TryGetLocalFaction(out FactionHQ hq)) return false;
            if (hq.preventDonation) return false;
            if (!table.TryCreate(hq.faction.factionName, out FactionOffensives faction)) return false;

            // Preparing escrows the staff overhead and the first wave slot in one charge.
            float cost = OverheadCost + WaveBudget;
            if (!Affordable(hq, cost)) return false;
            if (!faction.TryPrepare(cost, out OffensivePlan plan)) return false;

            hq.AddFunds(-cost);
            logger?.LogInfo("Offensive \"" + plan.Name + "\" prepared for " +
                            hq.faction.factionName + " (" + cost.ToString("F0") + ").");
            broadcastHadOperations = true;
            nextView = 0f;
            nextSync = 0f;
            id = plan.Id;
            return true;
        }

        internal bool CommitWave(int id)
        {
            if (!authoritative || !TryGetLocalFaction(out FactionHQ hq)) return false;
            if (hq.preventDonation) return false;
            if (!table.TryGet(hq.faction.factionName, out FactionOffensives faction)) return false;

            OffensivePlan plan = faction.Find(id);
            if (plan == null || !plan.CanCommit) return false;

            float cost = WaveBudget;
            if (!Affordable(hq, cost) || !plan.Commit(cost)) return false;

            hq.AddFunds(-cost);
            logger?.LogInfo("Offensive \"" + plan.Name + "\" committed wave " +
                            plan.WavesPlanned + " (" + cost.ToString("F0") + ").");
            nextView = 0f;
            nextSync = 0f;
            return true;
        }

        internal bool TargetOperation(int id, string objectiveKey)
        {
            if (!authoritative || string.IsNullOrEmpty(objectiveKey)) return false;
            if (!TryGetLocalFaction(out FactionHQ hq)) return false;
            if (!table.TryGet(hq.faction.factionName, out FactionOffensives faction)) return false;

            OffensivePlan plan = faction.Find(id);
            if (plan == null || !plan.CanTarget) return false;
            if (!TheaterPriorityService.TryResolveObjective(
                    hq, objectiveKey, out string label, out Vector3 position))
                return false;

            if (!plan.SetTarget(objectiveKey, label, position.x, position.y, position.z,
                    Mathf.Max(0f, settings.OperationLaunchDelaySeconds.Value)))
                return false;

            logger?.LogInfo("Offensive \"" + plan.Name + "\" targeted at " + label + ".");
            nextView = 0f;
            nextSync = 0f;
            return true;
        }

        internal bool AbortOperation(int id, string reason)
        {
            if (!authoritative || !TryGetLocalFaction(out FactionHQ hq)) return false;
            if (!table.TryGet(hq.faction.factionName, out FactionOffensives faction)) return false;

            OffensivePlan plan = faction.Find(id);
            if (plan == null || !plan.CanAbort) return false;

            Conclude(hq, plan, TheaterOperationOutcome.Cancelled,
                string.IsNullOrEmpty(reason) ? "stood down by the staff" : reason);
            nextView = 0f;
            nextSync = 0f;
            return true;
        }

        /// <summary>The director's read of its own plans: the local faction's board, if any.</summary>
        internal bool TryGetFactionPlans(out FactionOffensives faction)
        {
            faction = null;
            if (!authoritative || !TryGetLocalFaction(out FactionHQ hq)) return false;
            return table.TryGet(hq.faction.factionName, out faction);
        }

        internal bool IsLaunchedTarget(string key)
        {
            if (string.IsNullOrEmpty(key) || !TryGetFactionPlans(out FactionOffensives faction))
                return false;
            for (int i = 0; i < faction.Count; i++)
            {
                OffensivePlan plan = faction[i];
                if (plan != null && plan.IsActive && plan.Launched && plan.TargetKey == key)
                    return true;
            }
            return false;
        }

        // ---- Client mirror -----------------------------------------------------------------

        /// <summary>Applies a host broadcast on a client. Never overwrites host truth.</summary>
        internal void ApplyRemoteOperation(string faction, byte count, byte index, TheaterOperationState state)
        {
            if (authoritative || string.IsNullOrEmpty(faction) ||
                faction.Length > OffensiveTable.MaximumFactionLength)
                return;

            if (count == 0)
            {
                remote.Remove(faction);
                return;
            }
            if (count > OffensiveTable.MaximumOperations || index >= count) return;

            if (!remote.TryGetValue(faction, out RemoteBoard board))
            {
                if (remote.Count >= OffensiveTable.MaximumFactions) return;
                board = new RemoteBoard();
                remote.Add(faction, board);
            }
            if (index == 0) board.Reset();

            board.Plans[index] = new TheaterOperationView(
                TheaterOpsNet.Text(state.Name, OffensivePlan.MaximumNameLength),
                (TheaterOperationPhase)state.Phase,
                (TheaterOperationOutcome)state.Outcome,
                TheaterOpsNet.Text(state.Target, OffensivePlan.MaximumTargetLabelLength),
                Mathf.Clamp01(state.Progress), state.Budget, state.Committed, Mathf.Max(0f, state.Spent),
                Mathf.Max(0f, state.Duration), -1f,
                state.WavesPlanned, state.WavesLaunched, state.Countdown,
                TheaterOpsNet.Text(state.Holder, OffensivePlan.MaximumNameLength));
            board.Count = count;
            nextView = 0f;
        }

        /// <summary>Applies a host director snapshot on a client. Never overwrites host truth.</summary>
        internal void ApplyRemoteDirector(string faction, TheaterDirectorState state)
        {
            if (authoritative || string.IsNullOrEmpty(faction) ||
                faction.Length > OffensiveTable.MaximumFactionLength)
                return;
            RemoteDirection mirror = MirrorOf(faction);
            if (mirror == null) return;

            var axes = new List<TheaterAxisView>(InfluenceState.MaximumAxes);
            AddAxis(axes, state.AxisKey0, state.AxisWeight0);
            AddAxis(axes, state.AxisKey1, state.AxisWeight1);
            AddAxis(axes, state.AxisKey2, state.AxisWeight2);
            AddAxis(axes, state.AxisKey3, state.AxisWeight3);
            mirror.Influence = new TheaterInfluenceView(
                Mathf.Clamp01(state.Stance), state.Hold != 0,
                Mathf.Max(0f, state.MaxEscrow), Mathf.Max(0f, state.Reserve),
                TheaterOpsNet.Text(state.Setter, InfluenceState.MaximumSetterLength), axes);
            TheaterDirectorPosture posture = state.Posture <= (byte)TheaterDirectorPosture.Holding
                ? (TheaterDirectorPosture)state.Posture : TheaterDirectorPosture.Idle;
            mirror.Direction = new TheaterDirectionView(
                posture, state.EffortDefense != 0,
                TheaterOpsNet.Text(state.DefenseLabel, PriorityDirective.MaximumLabelLength),
                Mathf.Clamp(state.ActivePlans, 0, OffensiveTable.MaximumOperations));
            nextView = 0f;
        }

        /// <summary>Applies a host staff log on a client. Newest first, like the host's ring.</summary>
        internal void ApplyRemoteDirectorLog(string faction, string[] lines)
        {
            if (authoritative || string.IsNullOrEmpty(faction) ||
                faction.Length > OffensiveTable.MaximumFactionLength || lines == null)
                return;
            RemoteDirection mirror = MirrorOf(faction);
            if (mirror == null) return;
            mirror.Log.Clear();
            for (int i = 0; i < lines.Length && mirror.Log.Count < Domain.StaffLog.MaximumEntries; i++)
            {
                if (string.IsNullOrEmpty(lines[i])) continue;
                mirror.Log.Add(TheaterOpsNet.Text(lines[i], Domain.StaffLog.MaximumTextLength));
            }
            nextView = 0f;
        }

        private RemoteDirection MirrorOf(string faction)
        {
            if (remoteDirection.TryGetValue(faction, out RemoteDirection mirror)) return mirror;
            if (remoteDirection.Count >= OffensiveTable.MaximumFactions) return null;
            mirror = new RemoteDirection();
            remoteDirection.Add(faction, mirror);
            return mirror;
        }

        private static void AddAxis(List<TheaterAxisView> into, string key, float weight)
        {
            if (string.IsNullOrEmpty(key)) return;
            float clamped = float.IsNaN(weight) || float.IsInfinity(weight) ? 0f : weight;
            if (clamped < -1f) clamped = -1f;
            if (clamped > 1f) clamped = 1f;
            into.Add(new TheaterAxisView(
                TheaterOpsNet.Text(key, PriorityDirective.MaximumKeyLength), clamped));
        }

        // ---- Internals --------------------------------------------------------------------

        /// <summary>Snapshot for a querying client, bounded by the board's own ceiling.</summary>
        internal int CopyOperations(string faction, List<OffensivePlan> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(faction) || !table.TryGet(faction, out FactionOffensives offensives))
                return 0;

            for (int i = 0; i < offensives.Count; i++)
                if (offensives[i] != null) into.Add(offensives[i]);
            return into.Count;
        }

        /// <summary>Who holds the effort against this plan, for the wire; null when free to launch.</summary>
        internal string OperationHolder(string factionName, OffensivePlan plan)
        {
            if (plan == null || !plan.IsAtHHour) return null;
            if (string.IsNullOrEmpty(factionName) ||
                !table.TryGet(factionName, out FactionOffensives faction))
                return null;
            return EffortHolder(faction, plan);
        }

        private TheaterOperationView ViewOf(FactionOffensives faction, OffensivePlan plan) => new TheaterOperationView(
            plan.Name, plan.Phase, plan.Outcome, plan.TargetLabel,
            plan.Progress, plan.Budget, plan.Committed, plan.Spent, plan.ElapsedSeconds, plan.HoldRemaining,
            plan.WavesPlanned, plan.WavesLaunched, plan.Countdown,
            plan.IsAtHHour ? EffortHolder(faction, plan) : null);

        private OffensiveTiming Timing() => new OffensiveTiming(
            settings.OperationMusterSeconds.Value,
            settings.OperationPlanSeconds.Value / planScale,
            settings.OperationLaunchDelaySeconds.Value,
            settings.OperationWaveSeconds.Value,
            settings.OperationWaveRetrySeconds.Value,
            settings.OperationHoldSeconds.Value,
            settings.OperationAssaultSeconds.Value);

        private void RefreshPlanScale()
        {
            if (highCommand == null) ModServices.TryGet(out highCommand);
            planScale = highCommand != null && highCommand.Available
                ? 0.75f + Mathf.Clamp01(highCommand.FriendlyCohesion) * 0.75f
                : 1f;
        }

        private static bool Affordable(FactionHQ hq, float cost)
        {
            float funds = hq.factionFunds;
            return !float.IsNaN(funds) && !float.IsInfinity(funds) && funds >= cost;
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
