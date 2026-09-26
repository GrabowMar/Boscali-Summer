using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using BoscaliSummer.Features.TheaterOps.Configuration;
using BoscaliSummer.Features.TheaterOps.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Features.TheaterOps.Runtime
{
    /// <summary>
    /// Host-side destinations for depot-spawned AI vehicles of every faction with a main
    /// effort. Vanilla owns spawning, driving, targeting and player orders; this service only
    /// answers its uncommanded objective query.
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
        private static readonly FieldInfo NavigateObjectives =
            AccessTools.Field(typeof(GroundVehicle), "navigateToObjectives");

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
            nextUpdate = 0f;
        }

        /// <summary>Called only after vanilla's depot exit order has been issued.</summary>
        internal void Enroll(GroundVehicle vehicle)
        {
            if (vehicle == null || vehicle.disabled || vehicle.GetHoldPosition() ||
                vehicle.UnitCommand == null || NavigateObjectives == null ||
                !(bool)NavigateObjectives.GetValue(vehicle) ||
                settings == null || !settings.Enabled.Value ||
                !settings.FrontlineTacticsEnabled.Value || HasRtsCommander() ||
                !GameAccess.IsServer() || !vehicle.IsServer ||
                members.ContainsKey(vehicle) || members.Count >= MaximumMembers ||
                priority == null || !priority.Authoritative)
                return;
            FactionHQ hq = vehicle.NetworkHQ;
            if (hq == null || hq.faction == null ||
                !priority.TryGetDirective(hq.faction.factionName, out PriorityDirective directive))
                return;

            float now = Time.timeSinceLevelLoad;
            Group group = null;
            for (int i = groups.Count - 1; i >= 0; i--)
            {
                Group candidate = groups[i];
                if (candidate.HQ == hq && candidate.Key == directive.Key && !candidate.Sealed &&
                    candidate.Members.Count < FrontlineTactics.GroupSize && now - candidate.Born < JoinSeconds)
                {
                    group = candidate;
                    break;
                }
            }
            if (group == null)
            {
                if (groups.Count >= MaximumGroups) return;
                group = new Group
                {
                    Id = nextGroupId++,
                    HQ = hq,
                    Key = directive.Key,
                    Offensive = operations != null &&
                                operations.IsLaunchedTarget(hq.faction.factionName, directive.Key),
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
            vehicle != null && priority != null && priority.Authoritative && members.ContainsKey(vehicle);

        /// <summary>Patch-side lookup; a failed lookup leaves vanilla's main effort untouched.</summary>
        internal bool TryGetDestination(GroundVehicle vehicle, PriorityDirective directive,
            out GlobalPosition destination)
        {
            destination = default;
            if (vehicle == null || !priority.Authoritative ||
                !members.TryGetValue(vehicle, out Member member) ||
                member.Group.Key != directive.Key || !member.HasDestination)
                return false;
            destination = member.Destination;
            return true;
        }

        private void Update()
        {
            if (Time.timeSinceLevelLoad < nextUpdate) return;
            nextUpdate = Time.timeSinceLevelLoad + UpdateSeconds;
            if (settings == null || !settings.Enabled.Value ||
                !settings.FrontlineTacticsEnabled.Value || HasRtsCommander() ||
                !GameAccess.IsServer() ||
                priority == null || !priority.Authoritative)
            {
                if (groups.Count > 0) ResetForScene();
                nextUpdate = Time.timeSinceLevelLoad + UpdateSeconds;
                return;
            }
            if (groups.Count == 0) return;
            if (territory == null) ModServices.TryGet(out territory);
            float now = Time.timeSinceLevelLoad;
            for (int i = groups.Count - 1; i >= 0; i--)
            {
                Group group = groups[i];
                FactionHQ hq = group.HQ;
                // Each group follows its own side's effort; a side whose effort moved
                // re-keys its groups, and a side with no effort leaves them to vanilla.
                PriorityDirective directive = default;
                bool hasDirective = hq != null && hq.faction != null &&
                            priority.TryGetDirective(hq.faction.factionName, out directive);
                for (int j = group.Members.Count - 1; j >= 0; j--)
                {
                    Member member = group.Members[j];
                    if (member.Vehicle != null && !member.Vehicle.disabled &&
                        ReferenceEquals(member.Vehicle.NetworkHQ, hq))
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
                if (!hasDirective) { ClearDestinations(group); continue; }
                if (group.Key != directive.Key) Rekey(group, directive, now);
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

        private void Rekey(Group group, PriorityDirective directive, float now)
        {
            group.Key = directive.Key;
            group.Offensive = operations != null && group.HQ != null && group.HQ.faction != null &&
                operations.IsLaunchedTarget(group.HQ.faction.factionName, directive.Key);
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
