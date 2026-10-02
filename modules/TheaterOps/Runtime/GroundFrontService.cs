using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using BoscaliSummer.Modules.TheaterOps.Configuration;
using BoscaliSummer.Modules.TheaterOps.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    /// <summary>
    /// Host-side destinations for uncommanded mobile AI vehicles. Groups may be assigned to
    /// separate living-front sectors. Vanilla owns spawning, driving, targeting and player orders.
    /// </summary>
    internal sealed class GroundFrontService : MonoBehaviour, ISceneService
    {
        internal static GroundFrontService Active { get; private set; }

        private const int MaximumGroups = 16;
        private const int MaximumMembers = MaximumGroups * FrontlineTactics.GroupSize;
        private const float UpdateSeconds = 2f;
        private const float TraceSeconds = 5f;
        private const float JoinSeconds = 20f;
        private const float ContactMeters = 700f;
        private const float ContactHoldSeconds = 25f;
        private const float PincerFlankMeters = 750f;
        private const int ScanCellsPerUpdate = 128;
        private const int ScanUnitsPerUpdate = 64;
        private static readonly FieldInfo NavigateObjectives =
            AccessTools.Field(typeof(GroundVehicle), "navigateToObjectives");
        private static readonly FieldInfo CommandedDestination =
            AccessTools.Field(typeof(GroundVehicle), "commandedDestination");

        private enum Stage { Rally, Line, Assault, Contact, Withdraw }

        private sealed class Member
        {
            internal GroundVehicle Vehicle;
            internal Group Group;
            internal int Lane;
            internal bool HasDestination;
            internal GlobalPosition Destination;
            internal float HoldX, HoldZ;
        }

        /// <summary>One faction's front traces, copied every few seconds.</summary>
        private sealed class FrontTrace
        {
            internal readonly FrontlineTracePoint[] Points = new FrontlineTracePoint[FrontlineTraceLimits.MaximumPoints];
            internal readonly int[] Lengths = new int[FrontlineTraceLimits.MaximumTraces];
            internal readonly float[] Pressure = new float[FrontlineTraceLimits.MaximumTraces];
            internal int Count;
            internal float Next;
        }

        private sealed class Group
        {
            internal int Id;
            internal int Axis;
            internal readonly List<Member> Members = new List<Member>(FrontlineTactics.GroupSize);
            internal FactionHQ HQ;
            internal string Key;
            internal bool Offensive;
            internal bool Sealed;
            internal bool ReportedRally;
            internal int Formed;
            internal Stage Stage;
            internal Stage BeforeContact;
            internal float Born, StageSince, ContactUntil;
            internal float NoAssignmentSince;
            internal float CenterX, CenterZ, TangentX, TangentZ, FriendlyX, FriendlyZ;
            internal bool HasFront;
        }

        private readonly Dictionary<GroundVehicle, Member> members =
            new Dictionary<GroundVehicle, Member>(MaximumMembers);
        private readonly List<Group> groups = new List<Group>(MaximumGroups);
        private readonly Dictionary<FactionHQ, FrontTrace> traces = new Dictionary<FactionHQ, FrontTrace>(8);

        /// <summary>Chainloader's plugin list is fixed once the game runs; looked up once.</summary>
        private static int rtsInstalled = -1;

        private TheaterOpsSettings settings;
        private TheaterPriorityService priority;
        private TheaterOperationsService operations;
        private TheaterDirectorService director;
        private ManualLogSource logger;
        private ITerritoryIngress territory;
        private int nextGroupId = 1;
        private int nextScanCell;
        private float nextUpdate;
        private bool warnedConflict;

        internal void Configure(TheaterOpsSettings config, TheaterPriorityService priorityService,
            TheaterOperationsService operationService, TheaterDirectorService directorService,
            ManualLogSource log)
        {
            settings = config;
            priority = priorityService;
            operations = operationService;
            director = directorService;
            logger = log;
        }

        private void Awake() => Active = this;

        private void OnDestroy()
        {
            if (ReferenceEquals(Active, this)) Active = null;
            ResetForScene();
        }

        public void ResetForScene()
        {
            members.Clear();
            groups.Clear();
            traces.Clear();
            territory = null;
            nextGroupId = 1;
            nextScanCell = 0;
            nextUpdate = 0f;
        }

        /// <summary>Called after depot exit, or by the bounded scene-unit scan.</summary>
        internal void Enroll(GroundVehicle vehicle)
        {
            if (!AutoNavigating(vehicle) ||
                settings == null || !settings.Enabled.Value ||
                !settings.FrontlineTacticsEnabled.Value || HasRtsCommander() ||
                !GameAccess.IsServer() || !vehicle.IsServer ||
                members.ContainsKey(vehicle) || members.Count >= MaximumMembers ||
                !HasAuthority)
                return;
            FactionHQ hq = vehicle.NetworkHQ;
            if (hq == null || hq.faction == null) return;

            float now = Time.timeSinceLevelLoad;
            GlobalPosition here = vehicle.GlobalPosition();
            Group group = null;
            PriorityDirective directive = default;
            bool offensive = false;
            for (int i = groups.Count - 1; i >= 0; i--)
            {
                Group candidate = groups[i];
                if (candidate.HQ == hq && !candidate.Sealed &&
                    candidate.Members.Count < FrontlineTactics.GroupSize && now - candidate.Born < JoinSeconds &&
                    TryAssign(hq, candidate.Id, here.x, here.z, out directive, out offensive) &&
                    candidate.Key == directive.Key)
                {
                    group = candidate;
                    break;
                }
            }
            if (group == null)
            {
                if (groups.Count >= MaximumGroups) return;
                if (!TryAssign(hq, nextGroupId, here.x, here.z, out directive, out offensive)) return;
                group = new Group
                {
                    Id = nextGroupId++,
                    HQ = hq,
                    Key = directive.Key,
                    Offensive = offensive,
                    Born = now,
                    StageSince = now
                };
                groups.Add(group);
            }
            var member = new Member { Vehicle = vehicle, Group = group, Lane = group.Members.Count };
            group.Members.Add(member);
            group.Formed++;
            if (group.Members.Count == FrontlineTactics.GroupSize) group.Sealed = true;
            members.Add(vehicle, member);
            nextUpdate = 0f;
        }

        /// <summary>Patch-side membership test: only enrolled vehicles follow the effort.</summary>
        internal bool IsEnrolled(GroundVehicle vehicle) =>
            HasAuthority && AutoNavigating(vehicle) && members.ContainsKey(vehicle);

        /// <summary>Patch-side lookup; failure leaves the vanilla destination untouched.</summary>
        internal bool TryGetDestination(GroundVehicle vehicle, out GlobalPosition destination)
        {
            destination = default;
            if (!HasAuthority || !AutoNavigating(vehicle) ||
                !members.TryGetValue(vehicle, out Member member) ||
                !member.HasDestination)
                return false;
            if (LivingFrontService.Active?.Authoritative != true &&
                (priority == null || vehicle.NetworkHQ?.faction == null ||
                 !priority.TryGetDirective(vehicle.NetworkHQ.faction.factionName,
                     out PriorityDirective current) || member.Group.Key != current.Key))
                return false;
            destination = member.Destination;
            return true;
        }

        internal int CountGroups(string faction, string key)
        {
            if (string.IsNullOrEmpty(faction) || string.IsNullOrEmpty(key)) return 0;
            int count = 0;
            foreach (Group group in groups)
                if (group.HQ != null && group.HQ.faction != null &&
                    group.HQ.faction.factionName == faction && group.Key == key &&
                    group.Members.Count > 0) count++;
            return count;
        }

        private void Update()
        {
            if (Time.timeSinceLevelLoad < nextUpdate) return;
            nextUpdate = Time.timeSinceLevelLoad + UpdateSeconds;
            if (settings == null || !settings.Enabled.Value ||
                !settings.FrontlineTacticsEnabled.Value || HasRtsCommander() ||
                !GameAccess.IsServer() ||
                !HasAuthority)
            {
                if (groups.Count > 0) ResetForScene();
                nextUpdate = Time.timeSinceLevelLoad + UpdateSeconds;
                return;
            }
            ScanInitialUnits();
            if (groups.Count == 0) return;
            if (territory == null) ModuleServices.TryGet(out territory);
            float now = Time.timeSinceLevelLoad;
            for (int i = groups.Count - 1; i >= 0; i--)
            {
                Group group = groups[i];
                FactionHQ hq = group.HQ;
                // A group keeps its identity while the war changes its sector assignment.
                PriorityDirective directive = default;
                bool offensive = false;
                GlobalPosition groupPosition = group.Members.Count > 0 &&
                    group.Members[0].Vehicle != null
                    ? group.Members[0].Vehicle.GlobalPosition()
                    : default;
                bool hasDirective = hq != null && hq.faction != null &&
                    TryAssign(hq, group.Id, groupPosition.x, groupPosition.z,
                        out directive, out offensive);
                for (int j = group.Members.Count - 1; j >= 0; j--)
                {
                    Member member = group.Members[j];
                    if (member.Vehicle != null && !member.Vehicle.disabled &&
                        ReferenceEquals(member.Vehicle.NetworkHQ, hq) &&
                        AutoNavigating(member.Vehicle))
                        continue;
                    members.Remove(member.Vehicle);
                    group.Members.RemoveAt(j);
                    group.Sealed = true;
                }
                // A spent (withdrawing) group is released to vanilla when the effort moves on, so
                // the 16-group table keeps room for the next offensive's waves.
                if (group.Members.Count == 0 ||
                    (hasDirective && group.Key != directive.Key && group.Stage == Stage.Withdraw))
                {
                    foreach (Member member in group.Members) members.Remove(member.Vehicle);
                    groups.RemoveAt(i);
                    continue;
                }
                // A new effort re-keys the group; its vehicles stay together instead of
                // dissolving back to vanilla's nearest-objective spread.
                if (!hasDirective)
                {
                    ClearDestinations(group);
                    if (LivingFrontService.Active?.Authoritative == true)
                    {
                        if (group.NoAssignmentSince <= 0f) group.NoAssignmentSince = now;
                        else if (now - group.NoAssignmentSince >= 30f)
                        {
                            foreach (Member member in group.Members) members.Remove(member.Vehicle);
                            groups.RemoveAt(i);
                        }
                    }
                    continue;
                }
                group.NoAssignmentSince = 0f;
                if (group.Key != directive.Key) Rekey(group, directive, offensive, now);
                else
                {
                    group.Offensive = offensive;
                    if (!offensive && group.Stage == Stage.Assault)
                        SetStage(group, Stage.Line, now);
                    if (!offensive && group.Stage == Stage.Contact &&
                        group.BeforeContact == Stage.Assault)
                        group.BeforeContact = Stage.Line;
                }
                if (!group.Sealed && now - group.Born >= JoinSeconds) group.Sealed = true;
                RefreshGroup(group, directive, TraceOf(hq, now), now);
            }
        }

        /// <summary>The side's front, re-copied at most every few seconds per faction.</summary>
        private FrontTrace TraceOf(FactionHQ hq, float now)
        {
            if (!traces.TryGetValue(hq, out FrontTrace trace))
            {
                if (traces.Count >= 8) traces.Clear();
                trace = new FrontTrace();
                traces.Add(hq, trace);
            }
            if (now >= trace.Next)
            {
                trace.Next = now + TraceSeconds;
                trace.Count = territory == null ? 0 : Mathf.Clamp(territory.CopyFrontlineTraces(hq.GetInstanceID(),
                    trace.Points, trace.Lengths, trace.Pressure), 0, FrontlineTraceLimits.MaximumTraces);
            }
            return trace;
        }

        private void RefreshGroup(Group group, PriorityDirective directive, FrontTrace trace, float now)
        {
            group.HasFront = trace.Count > 0 && FrontlineTactics.TrySlot(trace.Points, trace.Lengths, trace.Count,
                directive.X, directive.Z, 0, out group.CenterX, out group.CenterZ,
                out group.TangentX, out group.TangentZ);
            if (!group.HasFront) { ClearDestinations(group); return; }

            int axis = PincerAxisOf(group);
            if (group.Axis != axis)
            {
                group.Axis = axis;
                if (group.ReportedRally) ReportStage(group);
            }
            group.CenterX += group.Axis * PincerFlankMeters * group.TangentX;
            group.CenterZ += group.Axis * PincerFlankMeters * group.TangentZ;

            float nx = -group.TangentZ, nz = group.TangentX;
            int factionId = group.HQ.GetInstanceID();
            float sign = 0f;
            // Command's control field has kilometre cells; close probes often share one cell.
            for (int probe = 450; probe <= 3600 && sign == 0f; probe *= 2)
            {
                bool left = territory.TryGetHoldStrength(factionId,
                    group.CenterX + nx * probe, group.CenterZ + nz * probe, out float leftHold);
                bool right = territory.TryGetHoldStrength(factionId,
                    group.CenterX - nx * probe, group.CenterZ - nz * probe, out float rightHold);
                if (left && right && Mathf.Abs(leftHold - rightHold) >= .05f)
                    sign = leftHold > rightHold ? 1f : -1f;
            }
            if (sign == 0f)
            {
                // An ambiguous or off-map normal must never send a group behind enemy lines.
                ClearDestinations(group);
                return;
            }
            group.FriendlyX = nx * sign;
            group.FriendlyZ = nz * sign;
            if (group.Sealed && !group.ReportedRally)
            {
                group.ReportedRally = true;
                ReportStage(group);
            }

            if (group.Sealed && FrontlineTactics.ShouldWithdraw(group.Members.Count, group.Formed))
                SetStage(group, Stage.Withdraw, now);
            else if (group.Stage != Stage.Withdraw)
            {
                GroundVehicle lead = group.Members[0].Vehicle;
                bool contact = lead != null && group.HQ.TryGetNearestGroundEnemy(
                    lead.GlobalPosition(), out TrackingInfo tracked) && tracked != null &&
                    DistanceSquared(lead.GlobalPosition().x, lead.GlobalPosition().z,
                        tracked.lastKnownPosition.x, tracked.lastKnownPosition.z) <
                    ContactMeters * ContactMeters;
                if (contact)
                {
                    group.ContactUntil = now + ContactHoldSeconds;
                    if (group.Stage != Stage.Contact)
                    {
                        group.BeforeContact = group.Stage;
                        foreach (Member member in group.Members)
                        {
                            GlobalPosition here = member.Vehicle.GlobalPosition();
                            member.HoldX = here.x;
                            member.HoldZ = here.z;
                        }
                        SetStage(group, Stage.Contact, now);
                    }
                }
                else if (group.Stage == Stage.Contact && now >= group.ContactUntil)
                    SetStage(group, group.BeforeContact, now);

                if (group.Stage == Stage.Rally || group.Stage == Stage.Line)
                {
                    int ready = 0;
                    float depth = group.Stage == Stage.Rally ? 650f : 180f;
                    foreach (Member member in group.Members)
                    {
                        GlobalPosition here = member.Vehicle.GlobalPosition();
                        float x = group.CenterX + group.FriendlyX * depth +
                                  LaneOffset(member.Lane) * group.TangentX;
                        float z = group.CenterZ + group.FriendlyZ * depth +
                                  LaneOffset(member.Lane) * group.TangentZ;
                        if (DistanceSquared(here.x, here.z, x, z) < 300f * 300f) ready++;
                    }
                    if (group.Stage == Stage.Rally && group.Sealed &&
                        FrontlineTactics.ShouldAdvance(ready, group.Members.Count, now - group.StageSince))
                        SetStage(group, Stage.Line, now);
                    else if (group.Stage == Stage.Line && group.Offensive &&
                        now - group.StageSince >= 15f &&
                        FrontlineTactics.ShouldAdvance(ready, group.Members.Count, now - group.StageSince))
                        SetStage(group, Stage.Assault, now);
                }
            }

            foreach (Member member in group.Members)
            {
                float lateral = LaneOffset(member.Lane);
                float x, z;
                switch (group.Stage)
                {
                    case Stage.Rally:
                        x = group.CenterX + group.FriendlyX * 650f + group.TangentX * lateral;
                        z = group.CenterZ + group.FriendlyZ * 650f + group.TangentZ * lateral;
                        break;
                    case Stage.Line:
                        x = group.CenterX + group.FriendlyX * 180f + group.TangentX * lateral;
                        z = group.CenterZ + group.FriendlyZ * 180f + group.TangentZ * lateral;
                        break;
                    case Stage.Contact:
                        x = member.HoldX; z = member.HoldZ;
                        break;
                    case Stage.Withdraw:
                        x = group.CenterX + group.FriendlyX * 1400f + group.TangentX * lateral;
                        z = group.CenterZ + group.FriendlyZ * 1400f + group.TangentZ * lateral;
                        break;
                    default:
                        x = directive.X + group.TangentX *
                            (lateral + group.Axis * PincerFlankMeters);
                        z = directive.Z + group.TangentZ *
                            (lateral + group.Axis * PincerFlankMeters);
                        break;
                }
                RaycastHit ground = default;
                member.HasDestination = Finite(x) && Finite(z) &&
                    PathfindingAgent.RaycastTerrain(new GlobalPosition(x, 0f, z), out ground) &&
                    ground.point.y >= Datum.LocalSeaY && ground.normal.y >= .55f;
                if (member.HasDestination)
                    member.Destination = ground.point.ToGlobalPosition();
            }
        }

        private void Rekey(Group group, PriorityDirective directive, bool offensive, float now)
        {
            group.Key = directive.Key;
            group.Offensive = offensive;
            group.ReportedRally = false;
            ClearDestinations(group);
            SetStage(group, Stage.Rally, now);
        }

        private void SetStage(Group group, Stage stage, float now)
        {
            if (group.Stage == stage) return;
            group.Stage = stage;
            group.StageSince = now;
            ReportStage(group);
        }

        private void ReportStage(Group group)
        {
            string line = "FRONT GROUP " + group.Id + " — " +
                group.Stage.ToString().ToUpperInvariant() +
                (group.Axis < 0 ? " LEFT AXIS" : group.Axis > 0 ? " RIGHT AXIS" : "") +
                " (" + group.Members.Count + "/" + group.Formed + ")";
            director?.ReportBattle(group.HQ.faction.factionName, line);
            logger?.LogInfo("[THEATER OPS] " + line);
        }

        private int PincerAxisOf(Group group)
        {
            if (!group.Offensive || !group.Sealed || group.Members.Count < 3 ||
                group.Stage == Stage.Withdraw) return 0;
            int first = int.MaxValue, second = int.MaxValue, viable = 0;
            foreach (Group candidate in groups)
            {
                if (!candidate.Offensive || !candidate.Sealed || candidate.Members.Count < 3 ||
                    candidate.Stage == Stage.Withdraw || candidate.HQ != group.HQ ||
                    candidate.Key != group.Key) continue;
                viable++;
                if (candidate.Id < first) { second = first; first = candidate.Id; }
                else if (candidate.Id < second) second = candidate.Id;
            }
            int rank = group.Id == first ? 0 : group.Id == second ? 1 : 2;
            return FrontlineTactics.PincerAxis(rank, viable);
        }

        private static float LaneOffset(int lane)
        {
            int column = lane == 0 ? 0 : (lane + 1) / 2 * (lane % 2 == 1 ? 1 : -1);
            return column * FrontlineTactics.LaneSpacing;
        }

        private static void ClearDestinations(Group group)
        {
            foreach (Member member in group.Members) member.HasDestination = false;
        }

        private static float DistanceSquared(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return dx * dx + dz * dz;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private bool HasAuthority => LivingFrontService.Active?.Authoritative == true ||
            priority?.Authoritative == true;

        private bool TryAssign(FactionHQ hq, int groupId, float x, float z,
            out PriorityDirective directive, out bool offensive)
        {
            directive = default;
            offensive = false;
            LivingFrontService war = LivingFrontService.Active;
            if (war != null && war.Authoritative)
                return war.TryGetGroundAssignment(hq, groupId, x, z,
                    out directive, out offensive) && directive.IsValid;
            if (priority == null || !priority.Authoritative || hq == null || hq.faction == null ||
                !priority.TryGetDirective(hq.faction.factionName, out directive)) return false;
            offensive = operations != null &&
                operations.IsLaunchedTarget(hq.faction.factionName, directive.Key);
            return directive.IsValid;
        }

        private static bool AutoNavigating(GroundVehicle vehicle) =>
            vehicle != null && !vehicle.disabled && !vehicle.GetHoldPosition() &&
            vehicle.UnitCommand != null && NavigateObjectives != null &&
            CommandedDestination != null && (bool)NavigateObjectives.GetValue(vehicle) &&
            !(bool)CommandedDestination.GetValue(vehicle);

        private void ScanInitialUnits()
        {
            if (members.Count >= MaximumMembers || groups.Count >= MaximumGroups) return;
            GridSquare[] cells = BattlefieldGrid.gridLookup;
            if (cells == null || cells.Length == 0) return;
            int checkedUnits = 0;
            for (int visited = 0; visited < ScanCellsPerUpdate; visited++)
            {
                if (nextScanCell >= cells.Length) nextScanCell = 0;
                GridSquare cell = cells[nextScanCell++];
                if (cell?.units == null) continue;
                foreach (Unit unit in cell.units)
                {
                    if (++checkedUnits > ScanUnitsPerUpdate) return;
                    if (unit is GroundVehicle vehicle) Enroll(vehicle);
                }
            }
        }

        internal bool HasRtsCommander()
        {
            if (rtsInstalled < 0)
                rtsInstalled = Chainloader.PluginInfos.ContainsKey("com.groundcontrol.rts") ? 1 : 0;
            bool installed = rtsInstalled == 1;
            if (installed && !warnedConflict)
            {
                warnedConflict = true;
                logger?.LogWarning("[THEATER OPS] Ground Control RTS owns ground orders; frontline tactics disabled.");
            }
            return installed;
        }
    }
}
