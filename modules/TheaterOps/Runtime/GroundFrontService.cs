using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using BoscaliSummer.Modules.TheaterOps.Configuration;
using BoscaliSummer.Modules.TheaterOps.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    /// <summary>
    /// Host-side destinations for uncommanded mobile AI vehicles. One writer assigns groups (AssignGroups, pure GroundTasking) to the staff's operation or the
    /// front they stand near; the MissionPosition advance hook only reads. Vanilla owns spawning, driving, targeting and player orders.
    /// </summary>
    internal sealed partial class GroundFrontService : MonoBehaviour, ISceneService
    {
        internal static GroundFrontService Active { get; private set; }

        private const int MaximumGroups = 32;
        private const int MaximumFactions = 8;
        private const int MaximumMembers = MaximumGroups * FrontlineTactics.GroupSize;
        private const int MaximumFronts = 8;
        private const int MaximumSightings = 256;
        private const int SweepUnitsPerPass = 256;
        private const float UpdateSeconds = 2f;
        private const float TraceSeconds = 5f;
        private const float StaleSeconds = 6f;
        private const float StallLockSeconds = 300f;
        private const float SettleSeconds = 20f;
        private const float PincerFlankMeters = 750f;
        private const float StagingReachMeters = 30000f;
        private static readonly FieldInfo NavigateObjectives =
            AccessTools.Field(typeof(GroundVehicle), "navigateToObjectives");
        private static readonly FieldInfo CommandedDestination =
            AccessTools.Field(typeof(GroundVehicle), "commandedDestination");

        internal sealed class Member
        {
            internal GroundVehicle Vehicle;
            internal Group Group;
            internal int Lane;
            internal bool HasDestination;
            /// <summary>True when no slot candidate could be placed and the member holds where it stood (the hook answers that point, never the enemy objective).</summary>
            internal bool Degraded;
            internal GlobalPosition Destination;
            internal float HoldX, HoldZ;
            /// <summary>The SlotSearch candidate that last worked; -1 tries the staged point first.</summary>
            internal int SlotIndex = -1;
            /// <summary>A frozen member tries the full candidate list again at this mission second.</summary>
            internal float RetryAt;
            /// <summary>The destination is the furthest dry ground toward a staged point that had none (LandMarch); it is asked again at RetryAt.</summary>
            internal bool Marched;
            internal float AnsweredAt = -1000f;
            internal GlobalPosition Answer;
        }

        /// <summary>One faction's front traces, copied every few seconds. Count is -1 until the first copy.</summary>
        private sealed class FrontTrace
        {
            internal readonly FrontlineTracePoint[] Points = new FrontlineTracePoint[FrontlineTraceLimits.MaximumPoints];
            internal readonly int[] Lengths = new int[FrontlineTraceLimits.MaximumTraces];
            internal readonly float[] Pressure = new float[FrontlineTraceLimits.MaximumTraces];
            internal int Count = -1;
            internal float Next;
        }

        internal sealed class Group
        {
            internal int Id;
            internal int Axis;
            internal readonly List<Member> Members = new List<Member>(FrontlineTactics.GroupSize);
            internal FactionHQ HQ;
            internal string Key;
            internal float FixX, FixZ;
            internal bool Offensive, Defending;
            /// <summary>The only definition of "on the effort": the staff's last assignment chose this group for operation OpId.</summary>
            internal bool OnEffort;
            internal int OpId;
            internal bool FromDepot;
            internal bool Sealed, ReportedRally;
            internal int Formed;
            internal GroundStage Stage = GroundStage.Rally;
            internal GroundStage BeforeContact;
            internal float Born, StageSince;
            internal float CenterX, CenterZ, TangentX, TangentZ, FriendlyX, FriendlyZ;
            internal float CentroidX, CentroidZ;
            internal bool HasSide;
            internal ContactClock Contact;
            internal float ContactEntered;
            internal Approach Approach;
            internal bool Closing;
            internal int Eta = -1;
            internal float TargetX, TargetZ;
            internal string StalledKey;
            internal float StalledUntil;
            internal float AllFrozenSince;
            internal float FirstOnEffortAfter = -1f;
            internal int RekeysThisOperation;
            /// <summary>A withdrawing group keeps its key and its place on the effort for the operation it began withdrawing under; a new assignment would re-form it on the spot.</summary>
            internal bool WithdrawSeen;
            internal int WithdrawOpId;
        }

        /// <summary>Everything the service keeps per faction.</summary>
        private sealed class FactionState
        {
            internal FactionHQ HQ;
            internal readonly FrontTrace Trace = new FrontTrace();
            internal readonly List<Vector2> Sightings = new List<Vector2>(MaximumSightings);
            internal readonly List<GroundFront> Fronts = new List<GroundFront>(MaximumFronts);
            /// <summary>Published at the end of each sweep; -1 until the first sweep ends.</summary>
            internal int Eligible = -1, Ungrouped, SupportSeen, SupportNavigating;
            internal int SweepEligible, SweepUngrouped, SweepSupportSeen, SweepSupportNavigating;
            /// <summary>The operation the groups were last assigned against (0 none), for the per-operation counters and the carrier test.</summary>
            internal int OpId;
            internal string OpKey;
            internal float MaxContactDwell;
            internal int Rekeys, EffortSwaps;
        }

        /// <summary>
        /// Sim read: the groups assigned to operation opId, how many carry (x, z) as their fix, and their mean centroid and mean staged-target distance
        /// to it (-1 with no group). Reads only.
        /// </summary>
        internal void EffortDistances(FactionHQ hq, int opId, float x, float z, out int count, out int onFix, out float meanCentroid, out float meanTarget)
        {
            count = onFix = 0;
            float c = 0f, t = 0f;
            for (int i = 0; i < groups.Count; i++)
            {
                Group g = groups[i];
                if (g == null || g.HQ != hq || !g.OnEffort || g.OpId != opId || g.Members.Count == 0) continue;
                count++;
                if (Mathf.Abs(g.FixX - x) < 1f && Mathf.Abs(g.FixZ - z) < 1f) onFix++;
                c += BoscaliSummer.Core.Math.Scalar.Distance2D(g.CentroidX, g.CentroidZ, x, z);
                t += BoscaliSummer.Core.Math.Scalar.Distance2D(g.TargetX, g.TargetZ, x, z);
            }
            meanCentroid = count > 0 ? c / count : -1f;
            meanTarget = count > 0 ? t / count : -1f;
        }

        private readonly Dictionary<GroundVehicle, Member> members = new Dictionary<GroundVehicle, Member>(MaximumMembers);
        private readonly List<Group> groups = new List<Group>(MaximumGroups);
        private readonly Dictionary<FactionHQ, FactionState> factions = new Dictionary<FactionHQ, FactionState>(MaximumFactions);
        private readonly List<FactionHQ> factionHqs = new List<FactionHQ>(MaximumFactions);
        private readonly Dictionary<GroundVehicle, bool> supportCache = new Dictionary<GroundVehicle, bool>(256);

        // Scratch for AssignGroups and Enroll, sized once: a pass allocates nothing.
        private readonly GroupFacts[] facts = new GroupFacts[MaximumGroups];
        private readonly bool[] chosen = new bool[MaximumGroups];
        private readonly Group[] mine = new Group[MaximumGroups];
        private readonly OpenGroup[] open = new OpenGroup[MaximumGroups];
        private readonly Group[] openGroups = new Group[MaximumGroups];
        private readonly float[] frontX = new float[MaximumFronts], frontZ = new float[MaximumFronts];

        /// <summary>Chainloader's plugin list is fixed once the game runs; looked up once.</summary>
        private static int rtsInstalled = -1;

        private TheaterOpsSettings settings;
        private IGroundWarSource war;
        private ITerritoryIngress territory;
        private int nextGroupId = 1;
        private int sweepCursor;
        private PhasedGate updateGate;
        private float lastPass;
        private bool adapterOn, warnedConflict;

        internal void Configure(TheaterOpsSettings config, IGroundWarSource warSource)
        {
            settings = config;
            war = warSource;
        }

        private void Awake() => Active = this;

        private void OnDestroy()
        {
            if (ReferenceEquals(Active, this)) Active = null;
            ResetForScene();
        }

        public void ResetForScene()
        {
            ClearGroups();
            factions.Clear();
            factionHqs.Clear();
            supportCache.Clear();
            territory = null;
            nextGroupId = 1;
            sweepCursor = 0;
            updateGate.Reset(); lastPass = 0f; passPhase = 0;
            adapterOn = false;
            diagnosed.Clear();
            diagnosedTotal = faults = 0;
        }

        private static int faults;

        /// <summary>The patches call this instead of letting an exception reach vanilla's spawn path or its per-vehicle destination query; the first few per scene are logged.</summary>
        internal static void LogFault(string where, Exception error)
        {
            if (faults++ >= 3) return;
            Plugin.Logger?.LogWarning("[THEATER OPS] ground steering fault in " + where + ": " + error);
        }

        private void ClearGroups()
        {
            members.Clear();
            groups.Clear();
            sweepCursor = 0;
        }

        /// <summary>The settings object the proof's Tactics verb writes.</summary>
        internal TheaterOpsSettings Settings => settings;

        /// <summary>The engine's GroundSteering capability: adapter on, host authority, no Ground Control RTS.</summary>
        internal bool SteeringAvailable => AdapterEnabled;

        private bool AdapterEnabled => settings != null && settings.Enabled.Value && settings.FrontlineTacticsEnabled.Value &&
            !HasRtsCommander() && war != null && GameAccess.IsServer();

        private bool Eligible(GroundVehicle vehicle) =>
            AdapterEnabled && AutoNavigating(vehicle) && !vehicle.IsSlung() && !IsSupport(vehicle);

        internal static bool AutoNavigating(GroundVehicle vehicle) =>
            vehicle != null && vehicle.IsServer && !vehicle.disabled && !vehicle.GetHoldPosition() &&
            vehicle.UnitCommand != null && NavigateObjectives != null &&
            CommandedDestination != null && (bool)NavigateObjectives.GetValue(vehicle) &&
            !(bool)CommandedDestination.GetValue(vehicle);

        /// <summary>True while a player's order owns the vehicle: vanilla then never asks the destination query about it.</summary>
        internal static bool IsCommanded(GroundVehicle vehicle) => vehicle != null && CommandedDestination != null && (bool)CommandedDestination.GetValue(vehicle);

        /// <summary>Artillery, rearm and repair vehicles answer the same destination query to shell or serve, so they never join a formation. Typed references: a game update that renames one breaks the build.</summary>
        private bool IsSupport(GroundVehicle vehicle)
        {
            if (vehicle == null) return false;
            if (supportCache.TryGetValue(vehicle, out bool known)) return known;
            bool support = vehicle.GetComponentInChildren<MobileArtilleryAI>(true) != null ||
                           vehicle.GetComponentInChildren<RearmVehicleAI>(true) != null ||
                           vehicle.GetComponentInChildren<Rearmer>(true) != null ||
                           vehicle.GetComponentInChildren<Repairer>(true) != null;
            if (supportCache.Count >= 1024) supportCache.Clear();
            supportCache[vehicle] = support;
            return support;
        }

        internal bool HasRtsCommander()
        {
            if (rtsInstalled < 0)
                rtsInstalled = Chainloader.PluginInfos.ContainsKey("com.groundcontrol.rts") ? 1 : 0;
            bool installed = rtsInstalled == 1;
            if (installed && !warnedConflict)
            {
                warnedConflict = true;
                Plugin.Logger?.LogWarning("[THEATER OPS] Ground Control RTS owns ground orders; frontline tactics disabled.");
            }
            return installed;
        }

        // ---- Enrolment ------------------------------------------------------------------------------------------------------

        /// <summary>Depot-exit patch passes true, the sweep false. No distance gate: a vehicle joins the nearest forming group or starts one.</summary>
        internal void Enroll(GroundVehicle vehicle, bool fromDepot)
        {
            if (!Eligible(vehicle)) return;
            if (factionHqs.Count == 0) ReconcileGroupShares();
            TryEnroll(vehicle, fromDepot);
        }

        private bool TryEnroll(GroundVehicle vehicle, bool fromDepot)
        {
            if (members.ContainsKey(vehicle)) return true;
            if (members.Count >= MaximumMembers) return false;
            FactionHQ hq = vehicle.NetworkHQ;
            if (hq == null || hq.faction == null) return false;

            float now = Time.timeSinceLevelLoad;
            GlobalPosition here = vehicle.GlobalPosition();
            int openCount = 0;
            for (int i = 0; i < groups.Count && openCount < open.Length; i++)
            {
                Group candidate = groups[i];
                if (candidate.HQ != hq || candidate.Stage == GroundStage.Withdraw || candidate.Members.Count == 0) continue;
                // A vehicle that left a depot joins only the wave it belongs to: a group that still counts as a fresh wave. Joining an old group would lose the wave.
                if (fromDepot && !(candidate.FromDepot && now - candidate.Born < GroundTasking.WaveSeconds)) continue;
                GroundVehicle leader = candidate.Members[0].Vehicle;
                if (leader == null) continue;
                GlobalPosition at = leader.GlobalPosition();
                open[openCount] = new OpenGroup(candidate.Id, at.x, at.z, candidate.Members.Count, now - candidate.Born);
                openGroups[openCount++] = candidate;
            }
            int picked = GroupFormation.Pick(open, openCount, here.x, here.z);
            Group group;
            if (picked >= 0) group = openGroups[picked];
            else
            {
                if (!CanCreateGroup(hq)) return false;
                group = new Group { Id = nextGroupId++, HQ = hq, Born = now, StageSince = now, FromDepot = fromDepot };
                groups.Add(group);
            }
            int used = 0;
            foreach (Member other in group.Members) used |= 1 << other.Lane;
            int lane = GroupFormation.FreeLane(used);
            if (lane < 0) return false;
            var member = new Member { Vehicle = vehicle, Group = group, Lane = lane };
            group.Members.Add(member);
            group.Formed++;
            if (group.Members.Count == FrontlineTactics.GroupSize) group.Sealed = true;
            members.Add(vehicle, member);
            return true;
        }

        private bool CanCreateGroup(FactionHQ hq)
        {
            if (groups.Count >= MaximumGroups || !factionHqs.Contains(hq)) return false;
            int owned = 0;
            foreach (Group group in groups)
                if (group.HQ == hq) owned++;
            return owned < MaximumGroups / Math.Max(1, factionHqs.Count);
        }

        private void ReconcileGroupShares()
        {
            factionHqs.Clear();
            foreach (FactionHQ candidate in FactionRegistry.GetAllHQs())
            {
                if (candidate == null || candidate.faction == null ||
                    string.IsNullOrEmpty(candidate.faction.factionName) || factionHqs.Contains(candidate)) continue;
                factionHqs.Add(candidate);
                if (!factions.ContainsKey(candidate)) factions.Add(candidate, new FactionState { HQ = candidate });
                if (factionHqs.Count >= MaximumFactions) break;
            }
            int allowance = MaximumGroups / Math.Max(1, factionHqs.Count);
            // ponytail: at most 32 groups; the bounded double scan avoids another ownership index.
            for (int i = groups.Count - 1; i >= 0; i--)
            {
                Group group = groups[i];
                int owned = 0;
                for (int j = 0; j <= i; j++)
                    if (ReferenceEquals(groups[j].HQ, group.HQ)) owned++;
                if (factionHqs.Contains(group.HQ) && owned <= allowance) continue;
                // Releasing only our membership leaves native commands and navigation intact.
                foreach (Member member in group.Members) members.Remove(member.Vehicle);
                groups.RemoveAt(i);
            }
        }

        /// <summary>
        /// A cursor over the registry's unit list, 256 units a pass: a vehicle the sweep has not met yet (mission-placed, or spawned by anything but a depot) joins
        /// a group. At the end of each sweep the per-faction tallies are published; they are -1 until the first sweep ends.
        /// </summary>
        private void Sweep()
        {
            List<Unit> all = UnitRegistry.allUnits;
            if (all == null) return;
            if (sweepCursor == 0)
                foreach (FactionState fs in factions.Values)
                    fs.SweepEligible = fs.SweepUngrouped = fs.SweepSupportSeen = fs.SweepSupportNavigating = 0;
            int end = Math.Min(all.Count, sweepCursor + SweepUnitsPerPass);
            for (; sweepCursor < end; sweepCursor++)
            {
                if (!(all[sweepCursor] is GroundVehicle vehicle) || vehicle.disabled) continue;
                FactionHQ hq = vehicle.NetworkHQ;
                if (hq == null || !factions.TryGetValue(hq, out FactionState fs)) continue;
                bool support = IsSupport(vehicle);
                if (support) fs.SweepSupportSeen++;
                bool navigating = AutoNavigating(vehicle);
                if (navigating && !vehicle.IsSlung())
                {
                    if (support) { fs.SweepSupportNavigating++; continue; }
                    fs.SweepEligible++;
                    if (!members.ContainsKey(vehicle) && !TryEnroll(vehicle, false)) fs.SweepUngrouped++;
                }
            }
            if (sweepCursor < all.Count) return;
            sweepCursor = 0;
            // A sweep that ends in the first seconds of a scene may have run before the mission put its vehicles out: a zero then is not "no vehicles" yet.
            bool settled = Time.timeSinceLevelLoad >= SettleSeconds;
            foreach (FactionState fs in factions.Values)
            {
                fs.Eligible = fs.SweepEligible == 0 && !settled ? -1 : fs.SweepEligible;
                fs.Ungrouped = fs.SweepUngrouped;
                fs.SupportSeen = fs.SweepSupportSeen;
                fs.SupportNavigating = fs.SweepSupportNavigating;
            }
        }

        // ---- The pass ---------------------------------------------------------------------------------------------------------

        private void Update()
        {
            float now = Time.timeSinceLevelLoad;
            if (passPhase != 0) StepPass();   // the rest of the last pass, a faction or a handful of groups a frame
            if (!updateGate.Due(now, UpdateSeconds, 0.3125f, 1f)) return;   // every 2 s, at .3125 of the second (the first within a second): clear of the 1 Hz slots (table in HighCommandManager)
            passPhase = 0;
            adapterOn = AdapterEnabled;
            if (!adapterOn)
            {
                if (groups.Count > 0 || members.Count > 0) ClearGroups();
                return;
            }
            ReconcileGroupShares();
            if (territory == null) ModuleServices.TryGet(out territory);
            Sweep();
            for (int i = groups.Count - 1; i >= 0; i--)
            {
                Group group = groups[i];
                Prune(group);
                if (group.Members.Count == 0) { groups.RemoveAt(i); continue; }
                Locate(group);
            }
            // The assignments and the group refreshes follow in the frames after this one (StepPass): one faction a frame, then RefreshGroupsPerFrame groups a frame.
            passNow = now;
            passFaction = 0;
            passPhase = 1;
            StepPass();
        }

        // The pass in flight: 0 none, 1 assigning the factions' groups, 2 refreshing the groups; the clock it began on and the next faction / group.
        private int passPhase, passFaction, passGroup;
        private float passNow;
        private const int RefreshGroupsPerFrame = 8;

        private void StepPass()
        {
            float now = passNow;
            if (passPhase == 1)
            {
                while (passFaction < factionHqs.Count)
                {
                    FactionHQ hq = factionHqs[passFaction++];
                    if (!factions.TryGetValue(hq, out FactionState fs)) continue;
                    CopyTrace(fs, now);
                    CopySightings(fs, now);
                    AssignGroups(fs, now);
                    return;
                }
                passPhase = 2;
                passGroup = groups.Count - 1;
            }
            for (int done = 0; done < RefreshGroupsPerFrame && passGroup >= 0; done++, passGroup--)
                if (passGroup < groups.Count) RefreshGroup(groups[passGroup], now);
            if (passGroup >= 0) return;
            passPhase = 0;
            // Stamped when the pass has finished, not when it began: a pass that throws leaves the stamp to age, so within StaleSeconds the hook hands every vehicle back to vanilla.
            lastPass = now;
        }

        /// <summary>
        /// Removes members that are gone: a destroyed vehicle stays a casualty of its group, one a player or another mod took over (commanded, held, captured, slung)
        /// leaves the formation, so it cannot make the rest withdraw.
        /// </summary>
        private void Prune(Group group)
        {
            for (int j = group.Members.Count - 1; j >= 0; j--)
            {
                Member member = group.Members[j];
                GroundVehicle vehicle = member.Vehicle;
                bool destroyed = vehicle == null || vehicle.disabled;
                if (!destroyed && ReferenceEquals(vehicle.NetworkHQ, group.HQ) && AutoNavigating(vehicle) && !vehicle.IsSlung()) continue;
                group.Formed = GroupLedger.FormedAfterLoss(group.Formed, destroyed);
                members.Remove(member.Vehicle);
                group.Members.RemoveAt(j);
                group.Sealed = true;
            }
        }

        /// <summary>The group's position is the centroid of its live members.</summary>
        private static void Locate(Group group)
        {
            float x = 0f, z = 0f;
            foreach (Member member in group.Members)
            {
                GlobalPosition at = member.Vehicle.GlobalPosition();
                x += at.x; z += at.z;
            }
            group.CentroidX = x / group.Members.Count;
            group.CentroidZ = z / group.Members.Count;
        }

        /// <summary>The side's front, re-copied at most every few seconds per faction, for every faction (a faction with no vehicles must still learn it has no front).</summary>
        private void CopyTrace(FactionState fs, float now)
        {
            FrontTrace trace = fs.Trace;
            if (now < trace.Next && trace.Count >= 0) return;
            trace.Next = now + TraceSeconds;
            trace.Count = territory == null ? 0 : Mathf.Clamp(territory.CopyFrontlineTraces(fs.HQ.GetInstanceID(),
                trace.Points, trace.Lengths, trace.Pressure), 0, FrontlineTraceLimits.MaximumTraces);
        }

        /// <summary>
        /// One pass over the tracking database per faction: a hostile vehicle, or a hostile defensive building, seen within the fresh window. The database never ages,
        /// so a sighting counts only while it is fresh. Positions only; each group tests its lead against them.
        /// </summary>
        private static void CopySightings(FactionState fs, float now)
        {
            fs.Sightings.Clear();
            var database = fs.HQ.trackingDatabase;
            if (database == null) return;
            foreach (KeyValuePair<PersistentID, TrackingInfo> pair in database)
            {
                TrackingInfo info = pair.Value;
                if (info == null || !ContactRule.Fresh(now - info.lastSpottedTime)) continue;
                if (!info.TryGetUnit(out Unit unit) || unit == null || unit.disabled || ReferenceEquals(unit.NetworkHQ, fs.HQ)) continue;
                bool vehicle = unit is GroundVehicle;
                bool defence = unit is Building building && building.definition is BuildingDefinition definition && definition.buildingType == BuildingType.DEF;
                if (!vehicle && !defence) continue;
                if (!float.IsFinite(info.lastKnownPosition.x) || !float.IsFinite(info.lastKnownPosition.z)) continue;
                fs.Sightings.Add(new Vector2(info.lastKnownPosition.x, info.lastKnownPosition.z));
                if (fs.Sightings.Count >= MaximumSightings) break;
            }
        }

        // ---- Assignment: the one writer ----------------------------------------------------------------------------------

        /// <summary>
        /// Decides, once per pass, which of the faction's groups carry the staff's operation (pure GroundTasking.Choose) and which hold the front they stand near.
        /// Everything else reads these fields: the hook, the report, the proof. A group that is neither on the effort nor within reach of a front has no key and
        /// reads vanilla.
        /// </summary>
        private void AssignGroups(FactionState fs, float now)
        {
            FactionHQ hq = fs.HQ;
            GroundOperation op = default;
            bool hasOp = war != null && war.TryGetOperation(hq, out op);
            int opId = hasOp ? op.Id : 0;
            if (opId != fs.OpId)
            {
                fs.OpId = opId;
                fs.Rekeys = fs.EffortSwaps = 0;
                foreach (Group group in groups)
                    if (group.HQ == hq) group.RekeysThisOperation = 0;
            }
            fs.OpKey = hasOp ? op.Key : null;

            int fronts = war != null ? Math.Min(war.CopyFronts(hq, fs.Fronts), MaximumFronts) : 0;
            for (int i = 0; i < fronts; i++) { frontX[i] = fs.Fronts[i].X; frontZ[i] = fs.Fronts[i].Z; }

            int count = 0;
            for (int i = 0; i < groups.Count && count < MaximumGroups; i++)
            {
                Group group = groups[i];
                if (group.HQ != hq) continue;
                bool usable = group.Stage != GroundStage.Withdraw && !(hasOp && group.StalledKey == op.Key && now < group.StalledUntil);
                bool held = hasOp && group.OnEffort && group.OpId == op.Id;
                bool wave = group.FromDepot && (now - group.Born < GroundTasking.WaveSeconds || held);
                facts[count] = new GroupFacts(group.Id, group.CentroidX, group.CentroidZ, usable, held, wave);
                mine[count++] = group;
            }
            if (hasOp) GroundTasking.Choose(facts, count, op.X, op.Z, op.Kind, chosen);
            for (int k = 0; k < count; k++)
            {
                Group group = mine[k];
                bool wasOnEffort = facts[k].Held;
                if (group.Stage == GroundStage.Withdraw)
                {
                    if (!group.WithdrawSeen) { group.WithdrawSeen = true; group.WithdrawOpId = opId; }
                    if (group.WithdrawOpId == opId) continue;
                }
                if (hasOp && chosen[k])
                {
                    if (!group.OnEffort || group.OpId != op.Id)
                    {
                        group.OnEffort = true;
                        group.OpId = op.Id;
                        if (group.FirstOnEffortAfter < 0f) group.FirstOnEffortAfter = now - group.Born;
                    }
                    Assign(fs, group, op.Key, op.X, op.Z, op.Kind == TheaterOpKind.Assault, op.Kind == TheaterOpKind.Defend, true, now);
                    continue;
                }
                if (hasOp && facts[k].Held && facts[k].Usable) fs.EffortSwaps++;
                group.OnEffort = false;
                int front = GroundTasking.NearestFront(group.CentroidX, group.CentroidZ, frontX, frontZ, fronts);
                if (front >= 0) Assign(fs, group, fs.Fronts[front].Key, fs.Fronts[front].X, fs.Fronts[front].Z, false, false, wasOnEffort, now);
                else Assign(fs, group, null, 0f, 0f, false, false, wasOnEffort, now);
            }
        }

        /// <summary>onEffort: the group is on the effort after this assignment, or was before it. Only those key changes count toward the flapping bound: a front-holder moving to the next front is no flap.</summary>
        private static void Assign(FactionState fs, Group group, string key, float fixX, float fixZ, bool offensive, bool defending, bool onEffort, float now)
        {
            if (key != group.Key)
            {
                if (group.Key != null && key != null && onEffort)
                {
                    group.RekeysThisOperation++;
                    fs.Rekeys = Math.Max(fs.Rekeys, group.RekeysThisOperation);
                }
                Rekey(fs, group, key, now);
            }
            group.FixX = fixX; group.FixZ = fixZ;
            group.Offensive = offensive; group.Defending = defending;
            // Not on an attack any more: an assaulting group settles on the line.
            if (!offensive && group.Stage == GroundStage.Assault) SetStage(group, GroundStage.Line, now);
            if (!offensive && group.Stage == GroundStage.Contact && group.BeforeContact == GroundStage.Assault) group.BeforeContact = GroundStage.Line;
        }

        /// <summary>A new key puts the group back in Rally, keeping its destinations until the same pass places the new ones (nothing is cleared, so nothing falls back to vanilla in between).</summary>
        private static void Rekey(FactionState fs, Group group, string key, float now)
        {
            if (group.Stage == GroundStage.Contact) EndContact(fs, group, now);
            group.Key = key;
            group.ReportedRally = false;
            group.WithdrawSeen = false;
            if (group.Stage == GroundStage.Withdraw) group.Formed = group.Members.Count;
            group.Stage = GroundStage.Rally;
            group.StageSince = now;
            group.Approach = default;
            group.Closing = false;
            group.Eta = -1;
            group.AllFrozenSince = 0f;
            foreach (Member member in group.Members) { member.SlotIndex = -1; member.RetryAt = 0f; }
        }

        private static void SetStage(Group group, GroundStage stage, float now)
        {
            if (group.Stage == stage) return;
            group.Stage = stage;
            group.StageSince = now;
            group.Approach = default;
            foreach (Member member in group.Members) { member.SlotIndex = -1; member.RetryAt = 0f; }
            ReportStage(group);
        }

        private static void EndContact(FactionState fs, Group group, float now)
        {
            if (group.ContactEntered <= 0f) return;
            fs.MaxContactDwell = Math.Max(fs.MaxContactDwell, now - group.ContactEntered);
            group.ContactEntered = 0f;
        }

        private static void ReportStage(Group group)
        {
            string line = "FRONT GROUP " + group.Id + " — " +
                group.Stage.ToString().ToUpperInvariant() +
                (group.Axis < 0 ? " LEFT AXIS" : group.Axis > 0 ? " RIGHT AXIS" : "") +
                " (" + group.Members.Count + "/" + group.Formed + ")";
            Plugin.Logger?.LogInfo("[THEATER OPS] " + line);
        }

        private int PincerAxisOf(Group group)
        {
            if (!group.Offensive || !group.Sealed || group.Members.Count < 3 ||
                group.Stage == GroundStage.Withdraw) return 0;
            int first = int.MaxValue, second = int.MaxValue, viable = 0;
            foreach (Group candidate in groups)
            {
                if (!candidate.Offensive || !candidate.Sealed || candidate.Members.Count < 3 ||
                    candidate.Stage == GroundStage.Withdraw || candidate.HQ != group.HQ ||
                    candidate.Key != group.Key) continue;
                viable++;
                if (candidate.Id < first) { second = first; first = candidate.Id; }
                else if (candidate.Id < second) second = candidate.Id;
            }
            int rank = group.Id == first ? 0 : group.Id == second ? 1 : 2;
            return FrontlineTactics.PincerAxis(rank, viable);
        }
    }
}
