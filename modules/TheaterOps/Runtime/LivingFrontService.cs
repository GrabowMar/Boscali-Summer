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
    /// <summary>One host-run staff per faction. It reads real units and front geometry.</summary>
    internal sealed class LivingFrontService : MonoBehaviour, ISceneService, ITheaterWarView,
        ITheaterAirStationView
    {
        internal static LivingFrontService Active { get; private set; }
        internal bool Authoritative => settings != null && settings.Enabled.Value && GameAccess.IsServer();

        private const int MaximumFactions = 8;
        private const int MaximumUnitScan = 4096;
        private const float ReviewSeconds = 10f;
        private const float PresenceRadius = 3000f;
        private const float LocalSectorSize = 4000f;
        private const float ReinforceSeconds = 120f;
        private const float AssignmentRadius = 12000f;
        private readonly FrontlineTracePoint[] tracePoints =
            new FrontlineTracePoint[FrontlineTraceLimits.MaximumPoints];
        private readonly int[] traceLengths = new int[FrontlineTraceLimits.MaximumTraces];
        private readonly float[] tracePressure = new float[FrontlineTraceLimits.MaximumTraces];
        private readonly List<WarOffer> choices = new List<WarOffer>(LivingWarRules.MaximumOffers);
        private readonly Dictionary<string, FactionWar> wars = new Dictionary<string, FactionWar>(MaximumFactions);
        private readonly List<FactionHQ> hqs = new List<FactionHQ>(MaximumFactions);
        private ITerritoryIngress territory;
        private TheaterOpsSettings settings;
        private TheaterPriorityService priority;
        private TheaterLogisticsService logistics;
        private LivingFrontNet network;
        private NavalFrontService naval;
        private ManualLogSource logger;
        private float nextRoll;
        private int cursor;

        private sealed class FrontCandidate
        {
            internal string Key, Label;
            internal float X, Z, Pressure;
            internal int Friendly, Hostile;
            internal bool Objective, Held;
        }

        private sealed class Operation
        {
            internal int Id, Revision;
            internal string Key, Label, Kind, Phase;
            internal bool Objective;
            internal float X, Z, Started, InitialHold;
            internal int InitialFriendly;
            internal int GroundGroups, AirGroups, NavalGroups;
            internal string Summary;
        }

        private sealed class FactionWar
        {
            internal FactionHQ HQ;
            internal readonly List<FrontCandidate> Candidates = new List<FrontCandidate>(LivingWarRules.MaximumFronts);
            internal readonly List<WarFrontRead> Reads = new List<WarFrontRead>(LivingWarRules.MaximumFronts);
            internal readonly List<TheaterFrontView> Fronts = new List<TheaterFrontView>(LivingWarRules.MaximumFronts);
            internal readonly List<TheaterProposalView> Proposals =
                new List<TheaterProposalView>(LivingWarRules.MaximumOffers);
            internal readonly StaffLog Log = new StaffLog();
            internal readonly Dictionary<int, string> GroupAssignments = new Dictionary<int, string>(16);
            internal TheaterWarPosture Posture = TheaterWarPosture.Steady;
            internal Operation Active;
            internal int Revision, NextId = 1;
            internal float NextReview, NextOffer, OfferDeadline, NextReinforce;
            internal float NextClock;
            internal bool OfferedOnce;
        }

        internal void Configure(TheaterOpsSettings config, TheaterPriorityService priorityService,
            TheaterLogisticsService logisticsService, LivingFrontNet transport,
            NavalFrontService navalService, ManualLogSource log)
        {
            settings = config;
            priority = priorityService;
            logistics = logisticsService;
            network = transport;
            naval = navalService;
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
            wars.Clear(); hqs.Clear(); choices.Clear(); territory = null;
            nextRoll = 0f; cursor = 0;
            network?.ResetScene();
        }

        private void Update()
        {
            if (!Authoritative) return;
            float now = Time.timeSinceLevelLoad;
            if (now >= nextRoll)
            {
                nextRoll = now + 1f;
                RollCall();
            }
            if (hqs.Count == 0) return;
            // One faction per frame keeps the unit and front scans bounded.
            if (cursor >= hqs.Count) cursor = 0;
            FactionHQ hq = hqs[cursor++];
            if (hq == null || hq.faction == null) return;
            FactionWar war = StateOf(hq.faction.factionName);
            if (war == null) return;
            war.HQ = hq;
            if (now >= war.NextReview)
            {
                war.NextReview = now + ReviewSeconds;
                Review(war, now);
            }
            if (war.Proposals.Count > 0 && now >= war.OfferDeadline)
                Pick(war, war.Proposals[0].Id, war.Proposals[0].Revision, true);
        }

        private void RollCall()
        {
            hqs.Clear();
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (hqs.Count >= MaximumFactions) break;
                if (hq == null || hq.faction == null || string.IsNullOrEmpty(hq.faction.factionName)) continue;
                hqs.Add(hq);
            }
        }

        private FactionWar StateOf(string faction)
        {
            if (string.IsNullOrEmpty(faction)) return null;
            if (wars.TryGetValue(faction, out FactionWar existing)) return existing;
            if (wars.Count >= MaximumFactions) return null;
            var state = new FactionWar { NextOffer = Time.timeSinceLevelLoad + 60f };
            wars.Add(faction, state);
            return state;
        }

        private FactionWar LocalState()
        {
            return GameAccess.TryGetLocalFaction(out FactionHQ hq) && hq?.faction != null &&
                wars.TryGetValue(hq.faction.factionName, out FactionWar war) ? war : null;
        }

        private void Review(FactionWar war, float now)
        {
            if (territory == null) ModServices.TryGet(out territory);
            bool contactChanged = Sense(war);
            bool finished = AdvanceOperation(war, now);
            float largestChange = 0f;
            for (int i = 0; i < war.Fronts.Count; i++)
                largestChange = Mathf.Max(largestChange, Mathf.Abs(war.Fronts[i].Trend));
            if (war.Active == null && war.Proposals.Count == 0 &&
                (now >= war.NextOffer && !war.OfferedOnce ||
                 LivingWarRules.Opening(0f, largestChange, contactChanged, finished, now, war.NextOffer)))
                Offer(war, now);
            if (war.Active != null && now >= war.NextReinforce && logistics != null)
            {
                war.NextReinforce = now + ReinforceSeconds;
                float spendable = Mathf.Max(0f, war.HQ.factionFunds * .35f);
                if (logistics.FundCombat(war.HQ, spendable, war.Active.Id,
                        out string group, out _))
                    war.Log.Add("REINFORCING " + war.Active.Label + " · " + group);
            }
            if (war.Active != null && naval != null)
            {
                NavalRole role = war.Active.Kind == "DEFEND" ? NavalRole.Screen : NavalRole.CoastalSupport;
                naval.SetObjective(war.HQ, war.Active.Key,
                    new GlobalPosition(war.Active.X, 0f, war.Active.Z), role);
            }
            if (war.Active != null) CountOperationForces(war);
            network?.Broadcast(war.HQ.faction.factionName);
        }

        private static void CountOperationForces(FactionWar war)
        {
            Operation op = war.Active;
            op.GroundGroups = GroundFrontService.Active?.CountGroups(
                war.HQ.faction.factionName, op.Key) ?? 0;
            op.AirGroups = 0;
            op.NavalGroups = 0;
            List<Unit> units = UnitRegistry.allUnits;
            if (units == null) return;
            int cap = Math.Min(units.Count, MaximumUnitScan);
            for (int i = 0; i < cap; i++)
            {
                Unit unit = units[i];
                if (unit == null || unit.disabled || !ReferenceEquals(unit.NetworkHQ, war.HQ))
                    continue;
                GlobalPosition point = unit.GlobalPosition();
                if (DistanceSquared(point.x, point.z, op.X, op.Z) >
                    PresenceRadius * PresenceRadius) continue;
                if (unit is Aircraft) op.AirGroups++;
                else if (unit is Ship) op.NavalGroups++;
            }
            op.Summary = op.GroundGroups + " ground groups · " + op.AirGroups +
                " aircraft · " + op.NavalGroups + " ships";
        }

        private bool Sense(FactionWar war)
        {
            Dictionary<string, float> previous = new Dictionary<string, float>(war.Fronts.Count);
            Dictionary<string, string> previousStatus = new Dictionary<string, string>(war.Fronts.Count);
            for (int i = 0; i < war.Fronts.Count; i++)
            {
                previous[war.Fronts[i].Key] = war.Fronts[i].Pressure;
                previousStatus[war.Fronts[i].Key] = war.Fronts[i].Status;
            }
            war.Candidates.Clear();
            SenseObjectives(war);
            SenseFront(war);
            CountUnits(war);
            war.Reads.Clear(); war.Fronts.Clear();
            bool contactChanged = false;
            for (int i = 0; i < war.Candidates.Count; i++)
            {
                FrontCandidate front = war.Candidates[i];
                if (front.Objective && TryGetAirbaseOwner(front.X, front.Z, war.HQ,
                        out bool heldAirbase)) front.Held = heldAirbase;
                else if (front.Objective) front.Held = false;
                else if (territory != null &&
                         territory.TryGetHoldStrength(war.HQ.GetInstanceID(),
                             front.X, front.Z, out float hold))
                    front.Held = hold > .35f;
                bool observed = front.Objective || front.Hostile > 0;
                float pressure = Mathf.Clamp01(Mathf.Max(front.Pressure,
                    front.Hostile / (float)Mathf.Max(1, front.Friendly + front.Hostile)));
                float before = previous.TryGetValue(front.Key, out float old) ? old : 0f;
                float trend = pressure - before;
                string status = front.Friendly > 0 && front.Hostile > 0 ? "IN CONTACT"
                    : front.Hostile > front.Friendly ? "UNDER PRESSURE"
                    : front.Friendly > 0 ? "HOLDING" : "UNCONFIRMED";
                if (Mathf.Abs(trend) >= .2f) contactChanged = true;
                if (previousStatus.TryGetValue(front.Key, out string oldStatus) &&
                    oldStatus != status)
                {
                    contactChanged = true;
                    war.Log.Add(front.Label + " · " + status);
                }
                war.Reads.Add(new WarFrontRead(front.Key, front.Label, front.X, front.Z,
                    pressure, front.Friendly, front.Hostile, front.Objective, front.Held, observed));
                war.Fronts.Add(new TheaterFrontView(front.Key, front.Label, front.X, front.Z,
                    status, pressure, trend, observed, observed ? 0f : -1f));
            }
            return contactChanged;
        }

        private void SenseObjectives(FactionWar war)
        {
            if (!MissionPosition.TryGetActiveObjectives(war.HQ, out List<Objective> active) || active == null)
                return;
            for (int i = 0; i < active.Count && war.Candidates.Count < 4; i++)
            {
                Objective objective = active[i];
                if (objective?.SavedObjective == null || objective.SavedObjective.Hidden ||
                    !(objective is IObjectiveWithPosition positioned) || positioned.Positions.Count == 0)
                    continue;
                GlobalPosition point = positioned.Positions[0].Position;
                if (!Finite(point.x) || !Finite(point.z)) continue;
                string key = objective.SavedObjective.UniqueName;
                if (string.IsNullOrEmpty(key)) continue;
                war.Candidates.Add(new FrontCandidate
                {
                    Key = key,
                    Label = string.IsNullOrEmpty(objective.SavedObjective.DisplayName)
                        ? key : objective.SavedObjective.DisplayName,
                    X = point.x, Z = point.z, Objective = true,
                });
            }
        }

        private void SenseFront(FactionWar war)
        {
            if (territory == null) return;
            int count = Mathf.Clamp(territory.CopyFrontlineTraces(war.HQ.GetInstanceID(),
                tracePoints, traceLengths, tracePressure), 0, FrontlineTraceLimits.MaximumTraces);
            int start = 0;
            for (int trace = 0; trace < count && war.Candidates.Count < LivingWarRules.MaximumFronts; trace++)
            {
                int length = Mathf.Clamp(traceLengths[trace], 0, tracePoints.Length - start);
                for (int part = 1; part <= 3 && war.Candidates.Count < LivingWarRules.MaximumFronts; part++)
                {
                    if (length < 2) break;
                    FrontlineTracePoint point = tracePoints[start + (length - 1) * part / 4];
                    if (!Finite(point.X) || !Finite(point.Z)) continue;
                    int gx = Mathf.FloorToInt(point.X / LocalSectorSize);
                    int gz = Mathf.FloorToInt(point.Z / LocalSectorSize);
                    string key = "S:" + gx + ":" + gz;
                    bool duplicate = false;
                    for (int i = 0; i < war.Candidates.Count; i++)
                        if (war.Candidates[i].Key == key ||
                            (!war.Candidates[i].Objective &&
                             DistanceSquared(war.Candidates[i].X, war.Candidates[i].Z,
                                point.X, point.Z) < 2000f * 2000f)) { duplicate = true; break; }
                    if (duplicate) continue;
                    war.Candidates.Add(new FrontCandidate
                    {
                        Key = key, Label = "FRONT " + gx + "/" + gz,
                        X = point.X, Z = point.Z, Pressure = Mathf.Clamp01(tracePressure[trace]),
                    });
                }
                start += length;
            }
        }

        private static void CountUnits(FactionWar war)
        {
            List<Unit> all = UnitRegistry.allUnits;
            if (all == null) return;
            ModServices.TryGet(out IAircraftTaskExclusion excludedAircraft);
            int cap = Math.Min(all.Count, MaximumUnitScan);
            for (int i = 0; i < cap; i++)
            {
                Unit unit = all[i];
                if (unit == null || unit.disabled || unit.NetworkHQ == null) continue;
                if (unit is Aircraft aircraft)
                {
                    if (aircraft.Player != null || aircraft.pilots == null ||
                        aircraft.pilots.Length == 0 || aircraft.pilots[0] == null ||
                        aircraft.pilots[0].playerControlled ||
                        (excludedAircraft != null &&
                         excludedAircraft.IsExcluded(aircraft.persistentID.GetHashCode())) ||
                        !WingLink.TryIsWingMember(aircraft.persistentID.GetHashCode(),
                            out bool wingMember) || wingMember) continue;
                }
                else if (!(unit is GroundVehicle || unit is Ship)) continue;
                bool friendly = ReferenceEquals(unit.NetworkHQ, war.HQ);
                if (!friendly && !war.HQ.IsTargetBeingTracked(unit)) continue;
                GlobalPosition position = unit.GlobalPosition();
                for (int j = 0; j < war.Candidates.Count; j++)
                {
                    FrontCandidate front = war.Candidates[j];
                    if (DistanceSquared(position.x, position.z, front.X, front.Z) >
                        PresenceRadius * PresenceRadius) continue;
                    if (friendly) front.Friendly++; else front.Hostile++;
                }
            }
        }

        private static bool TryGetAirbaseOwner(float x, float z, FactionHQ hq, out bool owned)
        {
            owned = false;
            if (FactionRegistry.airbaseLookup == null) return false;
            float nearest = 1500f * 1500f;
            bool found = false;
            int checkedBases = 0;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (++checkedBases > 64) break;
                if (airbase == null || airbase.AttachedAirbase || airbase.UnitDestroyed()) continue;
                Transform center = airbase.center != null ? airbase.center : airbase.transform;
                GlobalPosition position = center.GlobalPosition();
                float distance = DistanceSquared(x, z, position.x, position.z);
                if (distance > nearest) continue;
                nearest = distance;
                owned = ReferenceEquals(airbase.CurrentHQ, hq);
                found = true;
            }
            return found;
        }

        private void Offer(FactionWar war, float now)
        {
            LivingWarRules.Choose(war.Reads, war.Posture, choices);
            if (choices.Count == 0) { war.NextOffer = now + 30f; return; }
            war.Proposals.Clear();
            war.Revision++;
            war.OfferDeadline = now + LivingWarRules.OfferSeconds;
            for (int i = 0; i < choices.Count; i++)
            {
                WarOffer offer = choices[i];
                string brief = offer.Kind == "DEFEND" ? "Hold this threatened ground"
                    : offer.Kind == "RECON" ? "Scout this uncertain front" : "Press this opening";
                string risk = !offer.Front.Observed ? "UNKNOWN" : offer.Front.Hostile > offer.Front.Friendly
                    ? "HIGH" : "MODERATE";
                war.Proposals.Add(new TheaterProposalView(war.NextId++, war.Revision,
                    offer.Kind, offer.Front.Label, offer.Front.Key, offer.Front.X, offer.Front.Z,
                    brief, risk, "MISSION FORCES", LivingWarRules.OfferSeconds));
            }
            war.OfferedOnce = true;
            war.NextOffer = now + LivingWarRules.MinimumOfferGap;
            war.Log.Add("STAFF OFFERS " + war.Proposals.Count + " OPTIONS");
        }

        private bool AdvanceOperation(FactionWar war, float now)
        {
            Operation op = war.Active;
            if (op == null) return false;
            WarFrontRead front = default;
            bool found = false;
            for (int i = 0; i < war.Reads.Count; i++)
                if (war.Reads[i].Key == op.Key) { front = war.Reads[i]; found = true; break; }
            float elapsed = now - op.Started;
            string phase = elapsed < 35f ? "FORMING" : found && front.Friendly > 0 && front.Hostile > 0
                ? "IN CONTACT" : "ADVANCING";
            if (op.Phase != phase && elapsed < LivingWarRules.OperationLimitSeconds)
            {
                op.Phase = phase;
                war.Log.Add(op.Label + " · " + phase);
            }
            float hold = op.InitialHold;
            bool hasHold = territory != null && territory.TryGetHoldStrength(war.HQ.GetInstanceID(),
                op.X, op.Z, out hold);
            bool objectiveClosed = false, objectiveSecured = false;
            if (op.Objective && op.Kind != "DEFEND" &&
                MissionPosition.TryGetActiveObjectives(war.HQ, out List<Objective> active) &&
                active != null)
            {
                objectiveClosed = true;
                for (int i = 0; i < active.Count; i++)
                    if (active[i]?.SavedObjective?.UniqueName == op.Key)
                    { objectiveClosed = false; break; }
                if (objectiveClosed)
                {
                    Objective resolved = MissionManager.Objectives?.GetObjective(op.Key);
                    objectiveSecured = resolved != null &&
                        resolved.Status == ObjectiveStatus.Complete;
                }
            }
            bool secured = op.Kind == "DEFEND"
                ? found && front.Friendly > 0 && front.Hostile == 0 && elapsed > 45f
                : op.Kind == "RECON" ? found && front.Observed && elapsed > 45f
                : op.Objective ? objectiveSecured
                : hasHold && hold > .55f && hold - op.InitialHold > .25f;
            bool repulsed = found && front.Friendly == 0 && op.InitialFriendly > 0 && elapsed > 45f;
            if (objectiveClosed && !objectiveSecured) repulsed = true;
            bool timedOut = elapsed >= LivingWarRules.OperationLimitSeconds;
            if (!secured && !repulsed && !timedOut) return false;
            string outcome = secured ? "SECURED" : repulsed ? "REPULSED" : "STALLED";
            war.Log.Add(op.Label + " · " + outcome);
            logger?.LogInfo("Living Front " + war.HQ.faction.factionName + " " + op.Label + " " + outcome);
            priority?.ClearDirective(war.HQ);
            naval?.ClearObjective(war.HQ, op.Key);
            war.Active = null;
            war.GroupAssignments.Clear();
            war.NextOffer = now + 20f;
            war.OfferedOnce = false;
            return true;
        }

        private bool Pick(FactionWar war, int id, int revision, bool automatic)
        {
            if (!Authoritative || war == null || war.HQ == null || war.Active != null ||
                war.Revision != revision || war.Proposals.Count == 0) return false;
            TheaterProposalView chosen = null;
            for (int i = 0; i < war.Proposals.Count; i++)
                if (war.Proposals[i].Id == id && war.Proposals[i].Revision == revision)
                    chosen = war.Proposals[i];
            if (chosen == null) return false;
            WarFrontRead front = default;
            for (int i = 0; i < war.Reads.Count; i++)
                if (war.Reads[i].Key == chosen.TargetKey) { front = war.Reads[i]; break; }
            float hold = 0f;
            territory?.TryGetHoldStrength(war.HQ.GetInstanceID(), chosen.X, chosen.Z, out hold);
            war.Active = new Operation
            {
                Id = chosen.Id, Revision = ++war.Revision, Kind = chosen.Kind,
                Key = chosen.TargetKey, Label = chosen.Label, X = chosen.X, Z = chosen.Z,
                Phase = "FORMING", Started = Time.timeSinceLevelLoad, InitialHold = hold,
                InitialFriendly = front.Friendly, Objective = front.Objective,
            };
            war.Proposals.Clear();
            war.GroupAssignments.Clear();
            priority?.SetDirectedFix(war.HQ, chosen.TargetKey, chosen.Label, chosen.X, chosen.Z);
            war.NextReinforce = Time.timeSinceLevelLoad;
            war.Log.Add((automatic ? "STAFF SELECTED " : "SELECTED ") + chosen.Kind + " " + chosen.Label);
            network?.Broadcast(war.HQ.faction.factionName);
            return true;
        }

        internal bool ApplyIntent(FactionHQ hq, byte kind, int id, int revision, byte posture)
        {
            if (!Authoritative || hq?.faction == null ||
                !wars.TryGetValue(hq.faction.factionName, out FactionWar war)) return false;
            if (kind == 0) return Pick(war, id, revision, false);
            if (kind == 1)
            {
                if (war.Active == null || war.Active.Id != id || war.Active.Revision != revision)
                    return false;
                war.Log.Add("CANCELLED " + war.Active.Label);
                priority?.ClearDirective(hq);
                naval?.ClearObjective(hq, war.Active.Key);
                war.Active = null;
                war.GroupAssignments.Clear();
                war.Revision++;
                // Calling off is also the route to a new choice when the field changes.
                Offer(war, Time.timeSinceLevelLoad);
                network?.Broadcast(hq.faction.factionName);
                return true;
            }
            if (kind != 2 || posture > (byte)TheaterWarPosture.Bold) return false;
            war.Posture = (TheaterWarPosture)posture;
            war.Log.Add("POSTURE " + war.Posture.ToString().ToUpperInvariant());
            network?.Broadcast(hq.faction.factionName);
            return true;
        }

        internal bool TryGetGroundAssignment(FactionHQ hq, int groupId, float x, float z,
            out PriorityDirective directive, out bool offensive)
        {
            directive = default; offensive = false;
            if (!Authoritative || hq?.faction == null ||
                !wars.TryGetValue(hq.faction.factionName, out FactionWar war)) return false;
            if (war.GroupAssignments.TryGetValue(groupId, out string assigned))
            {
                if (war.Active != null && war.Active.Key == assigned)
                {
                    Operation active = war.Active;
                    directive = new PriorityDirective(active.Key, active.Label, active.X, 0f, active.Z);
                    offensive = active.Kind != "DEFEND";
                    return true;
                }
                for (int i = 0; i < war.Fronts.Count; i++)
                {
                    TheaterFrontView held = war.Fronts[i];
                    if (held.Key != assigned) continue;
                    directive = new PriorityDirective(held.Key, held.Label, held.X, 0f, held.Z);
                    return true;
                }
                war.GroupAssignments.Remove(groupId);
            }
            float maximum = AssignmentRadius * AssignmentRadius;
            float nearest = maximum;
            string key = null, label = null;
            float targetX = 0f, targetZ = 0f;
            bool attack = false;
            // Reserve some local groups for the chosen operation; the rest keep nearby
            // stretches of the front alive. Never send a local group across the map.
            if (war.Active != null && groupId % 3 == 0)
            {
                Operation op = war.Active;
                float distance = DistanceSquared(x, z, op.X, op.Z);
                if (distance <= nearest)
                {
                    nearest = distance; key = op.Key; label = op.Label;
                    targetX = op.X; targetZ = op.Z; attack = op.Kind != "DEFEND";
                }
            }
            if (key == null)
                for (int i = 0; i < war.Fronts.Count; i++)
                {
                    TheaterFrontView front = war.Fronts[i];
                    float distance = DistanceSquared(x, z, front.X, front.Z);
                    if (distance > nearest) continue;
                    nearest = distance; key = front.Key; label = front.Label;
                    targetX = front.X; targetZ = front.Z;
                }
            if (key == null) return false;
            if (war.GroupAssignments.Count < 16)
                war.GroupAssignments[groupId] = key;
            directive = new PriorityDirective(key, label, targetX, 0f, targetZ);
            offensive = attack;
            return true;
        }

        public bool TryGetStation(string faction, out float x, out float z, out float radiusMeters)
        {
            x = z = radiusMeters = 0f;
            if (!Authoritative || string.IsNullOrEmpty(faction) ||
                !wars.TryGetValue(faction, out FactionWar war) || war.Active == null) return false;
            x = war.Active.X; z = war.Active.Z; radiusMeters = 4000f;
            return true;
        }

        // The view is always scoped to the local player's faction. Clients only read host snapshots.
        public bool Available => settings != null && settings.Enabled.Value;
        public bool CanCommand => Available && GameAccess.TryGetLocalFaction(out _);
        public TheaterWarPosture Posture => LocalState()?.Posture ?? TheaterWarPosture.Steady;
        public IReadOnlyList<TheaterFrontView> Fronts =>
            (IReadOnlyList<TheaterFrontView>)LocalState()?.Fronts ?? Array.Empty<TheaterFrontView>();
        public IReadOnlyList<TheaterProposalView> Proposals
        {
            get
            {
                FactionWar war = LocalState();
                UpdateProposalClock(war);
                return (IReadOnlyList<TheaterProposalView>)war?.Proposals ??
                    Array.Empty<TheaterProposalView>();
            }
        }
        public TheaterLiveOperationView ActiveOperation
        {
            get
            {
                Operation op = LocalState()?.Active;
                if (op == null) return null;
                return new TheaterLiveOperationView(op.Id, op.Revision, op.Kind, op.Key, op.Label,
                    op.X, op.Z, op.Phase, op.Summary ?? "Forces moving",
                    op.GroundGroups, op.AirGroups, op.NavalGroups);
            }
        }
        public IReadOnlyList<string> StaffLog => LocalState()?.Log.Entries ?? Array.Empty<string>();
        public void Refresh() => UpdateProposalClock(LocalState());

        private static void UpdateProposalClock(FactionWar war)
        {
            if (war == null || war.Proposals.Count == 0 || Time.timeSinceLevelLoad < war.NextClock)
                return;
            war.NextClock = Time.timeSinceLevelLoad + 1f;
            float remaining = Mathf.Max(0f, war.OfferDeadline - Time.timeSinceLevelLoad);
            for (int i = 0; i < war.Proposals.Count; i++)
            {
                TheaterProposalView old = war.Proposals[i];
                war.Proposals[i] = new TheaterProposalView(old.Id, old.Revision, old.Kind,
                    old.Label, old.TargetKey, old.X, old.Z, old.Brief, old.Risk,
                    old.Forces, remaining);
            }
        }
        public bool RequestPick(int proposalId, int revision) =>
            GameAccess.TryGetLocalFaction(out FactionHQ hq) &&
            (Authoritative ? ApplyIntent(hq, 0, proposalId, revision, 0)
                : network != null && network.SendIntent(0, proposalId, revision, 0));
        public bool RequestCancel(int operationId, int revision) =>
            GameAccess.TryGetLocalFaction(out FactionHQ hq) &&
            (Authoritative ? ApplyIntent(hq, 1, operationId, revision, 0)
                : network != null && network.SendIntent(1, operationId, revision, 0));
        public bool RequestPosture(TheaterWarPosture posture) =>
            GameAccess.TryGetLocalFaction(out FactionHQ hq) &&
            (Authoritative ? ApplyIntent(hq, 2, 0, 0, (byte)posture)
                : network != null && network.SendIntent(2, 0, 0, (byte)posture));

        internal void CopySnapshot(string faction, List<TheaterFrontView> fronts,
            List<TheaterProposalView> proposals, out TheaterLiveOperationView operation,
            out TheaterWarPosture posture, List<string> log)
        {
            fronts.Clear(); proposals.Clear(); log.Clear();
            operation = null; posture = TheaterWarPosture.Steady;
            if (!wars.TryGetValue(faction, out FactionWar war)) return;
            UpdateProposalClock(war);
            posture = war.Posture;
            for (int i = 0; i < war.Fronts.Count; i++) fronts.Add(war.Fronts[i]);
            for (int i = 0; i < war.Proposals.Count; i++) proposals.Add(war.Proposals[i]);
            Operation op = war.Active;
            if (op != null)
                operation = new TheaterLiveOperationView(op.Id, op.Revision, op.Kind, op.Key,
                    op.Label, op.X, op.Z, op.Phase, op.Summary ?? "Forces moving",
                    op.GroundGroups, op.AirGroups, op.NavalGroups);
            for (int i = 0; i < war.Log.Count; i++) log.Add(war.Log.Entries[i]);
        }

        internal void ApplyRemote(string faction, IReadOnlyList<TheaterFrontView> fronts,
            IReadOnlyList<TheaterProposalView> proposals, TheaterLiveOperationView operation,
            TheaterWarPosture posture, IReadOnlyList<string> log)
        {
            if (Authoritative || string.IsNullOrEmpty(faction)) return;
            FactionWar war = StateOf(faction);
            if (war == null) return;
            war.Fronts.Clear(); war.Proposals.Clear(); war.Log.Clear();
            for (int i = 0; fronts != null && i < fronts.Count && i < LivingWarRules.MaximumFronts; i++)
                war.Fronts.Add(fronts[i]);
            for (int i = 0; proposals != null && i < proposals.Count && i < LivingWarRules.MaximumOffers; i++)
                war.Proposals.Add(proposals[i]);
            war.OfferDeadline = Time.timeSinceLevelLoad +
                (war.Proposals.Count > 0 ? Mathf.Max(0f, war.Proposals[0].SecondsRemaining) : 0f);
            war.NextClock = 0f;
            for (int i = log != null ? Math.Min(log.Count,
                BoscaliSummer.Features.TheaterOps.Domain.StaffLog.MaximumEntries) - 1 : -1; i >= 0; i--)
                war.Log.Add(log[i]);
            war.Posture = posture;
            war.Active = operation == null ? null : new Operation
            {
                Id = operation.Id, Revision = operation.Revision, Kind = operation.Kind,
                Key = operation.TargetKey, Label = operation.Label, X = operation.X,
                Z = operation.Z, Phase = operation.Phase, Summary = operation.Summary,
                GroundGroups = operation.GroundGroups, AirGroups = operation.AirGroups,
                NavalGroups = operation.NavalGroups,
            };
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float DistanceSquared(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return dx * dx + dz * dz;
        }
    }
}
