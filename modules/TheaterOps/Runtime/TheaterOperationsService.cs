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
    /// the board of every faction; the director owns every decision, and players conduct
    /// through standing orders.</para>
    ///
    /// <para>Only one push may hold a faction's effort: a plan due at H-hour while
    /// another objective holds the order waits, visibly, and launches itself when the
    /// effort frees. A concluded offensive releases an effort it still owns, so the AI is
    /// never left pushing a dead objective.</para>
    ///
    /// <para>Clients receive their own faction's board read-only over
    /// <see cref="TheaterOpsNet"/> when it changes, and never tick or mutate it.</para>
    /// </summary>
    internal sealed class TheaterOperationsService : MonoBehaviour, ISceneService, ITheaterOperationsView
    {
        private const float AuthorityInterval = 1f;
        private const float SyncInterval = 1f;
        private const float ViewInterval = 0.5f;

        /// <summary>An unchanged running board is re-sent this often anyway.</summary>
        private const float HeartbeatSeconds = 10f;

        /// <summary>How long a concluded plan keeps reporting its outcome before its slot frees.</summary>
        private const float ResolvedHoldSeconds = 24f;

        /// <summary>One bounded pass over the mission's own convoy groups per wave attempt.</summary>
        private const int MaximumConvoyScan = 8;

        private sealed class RemoteBoard
        {
            internal readonly TheaterOperationView[] Plans =
                new TheaterOperationView[OffensiveTable.MaximumOperations];
            internal int Count;
            internal float ReceivedAt;

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

        /// <summary>What one faction's players last received, so an unchanged board stays unsent.</summary>
        private sealed class BoardSync
        {
            internal int Signature;
            internal float NextHeartbeat;
        }

        private readonly OffensiveTable table = new OffensiveTable();
        private readonly List<TheaterOperationView> views =
            new List<TheaterOperationView>(OffensiveTable.MaximumOperations);
        private readonly Dictionary<string, RemoteBoard> remote =
            new Dictionary<string, RemoteBoard>(OffensiveTable.MaximumFactions, StringComparer.Ordinal);
        private readonly Dictionary<string, RemoteDirection> remoteDirection =
            new Dictionary<string, RemoteDirection>(OffensiveTable.MaximumFactions, StringComparer.Ordinal);
        private readonly Dictionary<string, BoardSync> synced =
            new Dictionary<string, BoardSync>(OffensiveTable.MaximumFactions, StringComparer.Ordinal);
        private readonly Dictionary<string, float> planScales =
            new Dictionary<string, float>(OffensiveTable.MaximumFactions, StringComparer.Ordinal);
        private readonly List<string> logView = new List<string>(Domain.StaffLog.MaximumEntries);

        private TheaterOpsSettings settings;
        private TheaterOpsNet network;
        private TheaterPriorityService priority;
        private TheaterDirectorService director;
        private ManualLogSource logger;
        private IHighCommandView highCommand;

        private bool authoritative;
        private float nextAuthority;
        private float nextSync;
        private float nextView;

        public bool Available => settings != null && settings.Enabled.Value;

        /// <summary>Any faction member conducts; the host validates every intent.</summary>
        public bool CanCommand => Available;
        public float OverheadCost => settings != null
            ? Mathf.Max(0f, settings.OperationOverheadCost.Value)
            : 0f;
        public float WaveBudget => settings != null
            ? Mathf.Max(0f, settings.OperationWaveBudget.Value)
            : 0f;
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
            synced.Clear();
            planScales.Clear();
            logView.Clear();
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
                if (authoritative) RefreshPlanScales();
            }
            if (!settings.Enabled.Value || !authoritative || table.Count == 0) return;

            TickHost(Time.deltaTime);

            if (now < nextSync) return;
            nextSync = now + SyncInterval;
            SyncHost(now);
        }

        // ---- Host simulation ---------------------------------------------------------------

        private void TickHost(float delta)
        {
            if (delta <= 0f) return;
            if (delta > 1f) delta = 1f;

            int inspected = 0;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (++inspected > OffensiveTable.MaximumFactions) break;
                if (hq == null || hq.faction == null) continue;
                string faction = hq.faction.factionName;
                if (!table.TryGet(faction, out FactionOffensives board) || board.Count == 0) continue;

                OffensiveTiming timing = Timing(faction);
                for (int i = 0; i < board.Count; i++)
                {
                    OffensivePlan plan = board[i];
                    if (plan == null) continue;

                    // One push owns the faction's effort at a time. A plan due at H-hour while
                    // another objective holds the order waits here rather than stealing it; it
                    // launches by itself the moment the effort frees.
                    if (plan.IsAtHHour && !EffortFree(faction, plan)) continue;

                    OffensiveTick tick = plan.Tick(delta, timing);
                    if (tick.LaunchDue) Launch(hq, plan);
                    if (tick.WaveDue) DeliverWave(hq, plan);
                    if (tick.AssaultExpired)
                        Conclude(hq, plan,
                            plan.Launched ? TheaterOperationOutcome.CommitmentSpent : TheaterOperationOutcome.Stalled,
                            plan.Launched ? "waves spent" : "no wave found the road");
                }
            }
        }

        /// <summary>
        /// Once a second: closes plans whose objective left the board, drops reports that
        /// have been read, and sends a faction's board to its own players only when it
        /// changed, plus a slow heartbeat while it runs.
        /// </summary>
        private void SyncHost(float now)
        {
            int inspected = 0;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (++inspected > OffensiveTable.MaximumFactions) break;
                if (hq == null || hq.faction == null) continue;
                string faction = hq.faction.factionName;
                if (!table.TryGet(faction, out FactionOffensives board)) continue;

                CheckObjectives(hq, board);
                for (int i = board.Count - 1; i >= 0; i--)
                {
                    OffensivePlan plan = board[i];
                    if (plan != null && plan.ConcludedExpired(ResolvedHoldSeconds)) board.Remove(plan.Id);
                }

                BoardSync sync = SyncOf(faction);
                if (sync == null) continue;
                int signature = Signature(faction, board);
                bool heartbeat = board.Count > 0 && now >= sync.NextHeartbeat;
                if (signature == sync.Signature && !heartbeat) continue;

                sync.Signature = signature;
                sync.NextHeartbeat = now + HeartbeatSeconds;
                network?.BroadcastOperations(faction, board);
                nextView = 0f;
            }
        }

        /// <summary>
        /// Everything on the board a player can see move, folded into one number. Clocks
        /// are left out: clients run them down from the last message, and the heartbeat
        /// corrects the drift.
        /// </summary>
        private int Signature(string faction, FactionOffensives board)
        {
            if (board.Count == 0) return 0;
            string effort = priority != null && priority.TryGetDirective(faction, out PriorityDirective directive)
                ? directive.Key : null;
            unchecked
            {
                int hash = board.Count;
                for (int i = 0; i < board.Count; i++)
                {
                    OffensivePlan plan = board[i];
                    if (plan == null) continue;
                    hash = hash * 31 + plan.Id;
                    hash = hash * 31 + (int)plan.Phase;
                    hash = hash * 31 + (int)plan.Outcome;
                    hash = hash * 31 + plan.WavesPlanned;
                    hash = hash * 31 + plan.WavesLaunched;
                    hash = hash * 31 + (int)(plan.Progress * 20f);
                    hash = hash * 31 + (int)plan.Budget;
                    hash = hash * 31 + (int)plan.Committed;
                    hash = hash * 31 + (int)plan.Spent;
                    hash = hash * 31 + (plan.TargetKey != null ? plan.TargetKey.GetHashCode() : 0);
                    // A held H-hour names who holds the effort; the effort's key marks a change.
                    if (plan.IsAtHHour && effort != null) hash = hash * 31 + effort.GetHashCode();
                }
                return hash == 0 ? 1 : hash;
            }
        }

        private BoardSync SyncOf(string faction)
        {
            if (synced.TryGetValue(faction, out BoardSync sync)) return sync;
            if (synced.Count >= OffensiveTable.MaximumFactions) return null;
            sync = new BoardSync();
            synced.Add(faction, sync);
            return sync;
        }

        /// <summary>
        /// At H-hour the push starts with one supply wave and the staff hands the objective to
        /// the AI as the faction's main effort, so its battle-group vehicles advance on it.
        /// </summary>
        private void Launch(FactionHQ hq, OffensivePlan plan)
        {
            // Make the effort visible before a depot can release an immediately ready unit.
            if (priority != null && plan.TargetKey != null) priority.SetDirective(hq, plan.TargetKey);
            int firstGroup = DeliverWave(hq, plan);
            // A cohesive staff can release two distinct funded groups at H-hour. The
            // ordinary wave schedule resumes afterward; no extra escrow or unit order.
            if (firstGroup >= 0 && plan.WavesPlanned >= 3 &&
                highCommand != null && highCommand.TryGetCohesion(hq, out float cohesion) &&
                cohesion >= .65f &&
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

        private void CheckObjectives(FactionHQ hq, FactionOffensives board)
        {
            if (board.Count == 0) return;
            if (!MissionPosition.TryGetActiveObjectives(hq, out List<Objective> active) || active == null)
                return;

            for (int i = 0; i < board.Count; i++)
            {
                OffensivePlan plan = board[i];
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
                priority.TryGetDirective(hq.faction.factionName, out PriorityDirective directive) &&
                string.Equals(directive.Key, plan.TargetKey, StringComparison.Ordinal))
                priority.ClearDirective(hq);

            logger?.LogInfo("Offensive \"" + plan.Name + "\" concluded (" + outcome + "): " +
                            reason + " (" + refund.ToString("F0") + " refunded).");
        }

        /// <summary>Whether the faction's effort already points where this plan is going.</summary>
        private bool EffortFree(string faction, OffensivePlan plan)
        {
            if (priority == null || plan.TargetKey == null) return true;
            if (!priority.TryGetDirective(faction, out PriorityDirective directive)) return true;
            return string.Equals(directive.Key, plan.TargetKey, StringComparison.Ordinal);
        }

        /// <summary>
        /// What is holding the effort when a due plan is not free to launch: the other
        /// offensive that owns it, or the staff's defense when it holds the order.
        /// </summary>
        private string EffortHolder(string faction, FactionOffensives board, OffensivePlan plan)
        {
            if (priority == null || plan.TargetKey == null) return null;
            if (!priority.TryGetDirective(faction, out PriorityDirective directive)) return null;
            if (string.Equals(directive.Key, plan.TargetKey, StringComparison.Ordinal)) return null;

            for (int i = 0; i < board.Count; i++)
            {
                OffensivePlan other = board[i];
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
            if (!GameAccess.TryGetLocalFaction(out FactionHQ hq) || hq.faction == null) return;
            string factionName = hq.faction.factionName;

            if (authoritative)
            {
                if (!table.TryGet(factionName, out FactionOffensives board)) return;
                for (int i = 0; i < board.Count; i++)
                {
                    OffensivePlan plan = board[i];
                    if (plan != null) views.Add(ViewOf(factionName, board, plan));
                }
                return;
            }

            if (!remote.TryGetValue(factionName, out RemoteBoard mirror)) return;
            float age = now - mirror.ReceivedAt;
            for (int i = 0; i < mirror.Count; i++)
                if (mirror.Plans[i] != null) views.Add(Aged(mirror.Plans[i], age));
        }

        // ---- Direction, influence and staff log -------------------------------------------

        public TheaterDirectionView Direction
        {
            get
            {
                if (!GameAccess.TryGetLocalFaction(out FactionHQ hq) || hq.faction == null) return null;
                if (authoritative) return director?.DirectionOf(hq.faction.factionName);
                return remoteDirection.TryGetValue(hq.faction.factionName, out RemoteDirection mirror)
                    ? mirror.Direction : null;
            }
        }

        public TheaterInfluenceView Influence
        {
            get
            {
                if (!GameAccess.TryGetLocalFaction(out FactionHQ hq) || hq.faction == null) return null;
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
                if (!GameAccess.TryGetLocalFaction(out FactionHQ hq) || hq.faction == null) return logView;
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
                if (!GameAccess.TryGetLocalFaction(out FactionHQ hq) || hq.faction == null) return false;
                return director.ApplyInfluence(hq, kind, value, value2, key, LocalSetter());
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

        // ---- Director drive (host only, per faction) ----------------------------------------

        internal bool PrepareOperation(FactionHQ hq, out int id)
        {
            id = -1;
            if (!authoritative || hq == null || hq.faction == null || hq.preventDonation) return false;
            if (!table.TryCreate(hq.faction.factionName, out FactionOffensives board)) return false;

            // Preparing escrows the staff overhead and the first wave slot in one charge.
            float cost = OverheadCost + WaveBudget;
            if (!Affordable(hq, cost)) return false;
            if (!board.TryPrepare(cost, out OffensivePlan plan)) return false;

            hq.AddFunds(-cost);
            logger?.LogInfo("Offensive \"" + plan.Name + "\" prepared for " +
                            hq.faction.factionName + " (" + cost.ToString("F0") + ").");
            nextView = 0f;
            nextSync = 0f;
            id = plan.Id;
            return true;
        }

        internal bool CommitWave(FactionHQ hq, int id)
        {
            OffensivePlan plan = Find(hq, id);
            if (plan == null || hq.preventDonation || !plan.CanCommit) return false;

            float cost = WaveBudget;
            if (!Affordable(hq, cost) || !plan.Commit(cost)) return false;

            hq.AddFunds(-cost);
            logger?.LogInfo("Offensive \"" + plan.Name + "\" committed wave " +
                            plan.WavesPlanned + " (" + cost.ToString("F0") + ").");
            nextView = 0f;
            nextSync = 0f;
            return true;
        }

        internal bool TargetOperation(FactionHQ hq, int id, string objectiveKey)
        {
            if (string.IsNullOrEmpty(objectiveKey)) return false;
            OffensivePlan plan = Find(hq, id);
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

        internal bool AbortOperation(FactionHQ hq, int id, string reason)
        {
            OffensivePlan plan = Find(hq, id);
            if (plan == null || !plan.CanAbort) return false;

            Conclude(hq, plan, TheaterOperationOutcome.Cancelled,
                string.IsNullOrEmpty(reason) ? "stood down by the staff" : reason);
            nextView = 0f;
            nextSync = 0f;
            return true;
        }

        /// <summary>The director's read of one faction's plans, if it has a board.</summary>
        internal bool TryGetFactionPlans(string faction, out FactionOffensives board)
        {
            board = null;
            return authoritative && table.TryGet(faction, out board);
        }

        internal bool IsLaunchedTarget(string faction, string key)
        {
            if (string.IsNullOrEmpty(key) || !TryGetFactionPlans(faction, out FactionOffensives board))
                return false;
            for (int i = 0; i < board.Count; i++)
            {
                OffensivePlan plan = board[i];
                if (plan != null && plan.IsActive && plan.Launched && plan.TargetKey == key)
                    return true;
            }
            return false;
        }

        private OffensivePlan Find(FactionHQ hq, int id)
        {
            if (!authoritative || hq == null || hq.faction == null) return null;
            return table.TryGet(hq.faction.factionName, out FactionOffensives board) ? board.Find(id) : null;
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
                nextView = 0f;
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
            board.ReceivedAt = Time.unscaledTime;
            nextView = 0f;
        }

        /// <summary>A mirrored plan with its clocks run down by the time since the host sent it.</summary>
        private static TheaterOperationView Aged(TheaterOperationView plan, float age)
        {
            if (age <= 0.05f || plan.Outcome != TheaterOperationOutcome.None) return plan;
            float countdown = plan.Countdown;
            float elapsed = plan.ElapsedSeconds;
            float hold = plan.HoldRemaining;
            switch (plan.Phase)
            {
                case TheaterOperationPhase.Launching:
                    if (countdown > 0f) countdown = Mathf.Max(0f, countdown - age);
                    break;
                case TheaterOperationPhase.Assault:
                    elapsed += age;
                    break;
                case TheaterOperationPhase.Holding:
                    elapsed += age;
                    if (hold > 0f) hold = Mathf.Max(0f, hold - age);
                    break;
                default:
                    return plan;
            }
            return new TheaterOperationView(
                plan.Name, plan.Phase, plan.Outcome, plan.TargetLabel, plan.Progress,
                plan.Budget, plan.Committed, plan.Spent, elapsed, hold,
                plan.WavesPlanned, plan.WavesLaunched, countdown, plan.Holder, plan.Id);
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
                !table.TryGet(factionName, out FactionOffensives board))
                return null;
            return EffortHolder(factionName, board, plan);
        }

        private TheaterOperationView ViewOf(string faction, FactionOffensives board, OffensivePlan plan) =>
            new TheaterOperationView(
                plan.Name, plan.Phase, plan.Outcome, plan.TargetLabel,
                plan.Progress, plan.Budget, plan.Committed, plan.Spent, plan.ElapsedSeconds, plan.HoldRemaining,
                plan.WavesPlanned, plan.WavesLaunched, plan.Countdown,
                plan.IsAtHHour ? EffortHolder(faction, board, plan) : null, plan.Id);

        private OffensiveTiming Timing(string faction) => new OffensiveTiming(
            settings.OperationMusterSeconds.Value,
            settings.OperationPlanSeconds.Value /
                (planScales.TryGetValue(faction, out float scale) ? scale : 1f),
            settings.OperationWaveSeconds.Value,
            settings.OperationWaveRetrySeconds.Value,
            settings.OperationHoldSeconds.Value,
            settings.OperationAssaultSeconds.Value);

        /// <summary>
        /// A cohesive staff plans faster. Read once a second per faction with a board, from
        /// the host's own command tree rather than whatever staff page happens to be open.
        /// </summary>
        private void RefreshPlanScales()
        {
            if (highCommand == null) ModServices.TryGet(out highCommand);
            planScales.Clear();
            if (highCommand == null || table.Count == 0) return;
            int inspected = 0;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (++inspected > OffensiveTable.MaximumFactions) break;
                if (hq == null || hq.faction == null || !table.TryGet(hq.faction.factionName, out _)) continue;
                if (highCommand.TryGetCohesion(hq, out float cohesion))
                    planScales[hq.faction.factionName] = 0.75f + Mathf.Clamp01(cohesion) * 0.75f;
            }
        }

        private static bool Affordable(FactionHQ hq, float cost)
        {
            float funds = hq.factionFunds;
            return !float.IsNaN(funds) && !float.IsInfinity(funds) && funds >= cost;
        }
    }
}
