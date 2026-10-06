using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Modules.TheaterOps.Configuration;
using BoscaliSummer.Modules.TheaterOps.Domain;
using BoscaliSummer.Modules.TheaterOps.Networking;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using NuclearOption.SavedMission;
using NuclearOption.SavedMission.Objectives;
using UnityEngine;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    /// <summary>One host-run staff per faction. It reads real units and front geometry.</summary>
    internal sealed class LivingFrontService : MonoBehaviour, ISceneService, ITheaterWarView,
        ITheaterAirStationView
    {
        internal static LivingFrontService Active { get; private set; }
        internal bool Authoritative => settings != null && settings.Enabled.Value && GameAccess.IsServer();

        private const int MaximumFactions = 8;
        private const int MaximumUnitScan = 4096;
        private const int MaximumObjectiveScan = 128;
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
        private float nextRoll;
        private int cursor;
        private string localCommandStatus = "";
        private FactionHQ viewHq;

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
            internal readonly Dictionary<int, (string Key, bool Offensive)> GroupAssignments =
                new Dictionary<int, (string, bool)>(16);
            internal TheaterWarPosture Posture = TheaterWarPosture.Steady;
            internal Operation Active;
            internal int Revision, NextId = 1;
            internal float NextReview, NextOffer, OfferDeadline, NextReinforce;
            internal float NextClock;
            internal bool OfferedOnce;
            internal bool HasSnapshot;
            internal int ObjectiveCursor;
            internal float SnapshotAt, FrontClockAt;
        }

        internal void Configure(TheaterOpsSettings config, TheaterPriorityService priorityService,
            TheaterLogisticsService logisticsService, LivingFrontNet transport,
            NavalFrontService navalService)
        {
            settings = config;
            priority = priorityService;
            logistics = logisticsService;
            network = transport;
            naval = navalService;
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
            viewHq = null; localCommandStatus = "";
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
            {
                // Validate the painted deck in order; an invalid first option cannot
                // strand a later valid option or silently become another target.
                TheaterProposalView[] offered = war.Proposals.ToArray();
                bool picked = false;
                for (int i = 0; i < offered.Length && !picked; i++)
                    picked = Pick(war, offered[i].Id, offered[i].Revision, true);
                if (!picked)
                {
                    war.Log.Add("STAFF OPTIONS EXPIRED · REASSESSING");
                    Offer(war, now);
                    network?.Broadcast(war.HQ.faction.factionName);
                }
            }
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
            if (!GameAccess.TryGetLocalFaction(out FactionHQ hq) || hq?.faction == null) return null;
            return wars.TryGetValue(hq.faction.factionName, out FactionWar war) ? war : null;
        }

        internal void ClearRemoteState()
        {
            if (GameAccess.IsServer()) return;
            wars.Clear(); localCommandStatus = "";
        }

        private void Review(FactionWar war, float now)
        {
            if (territory == null) ModuleServices.TryGet(out territory);
            bool contactChanged = Sense(war);
            if (war.Proposals.Count > 0 && !RefreshOffers(war))
                Offer(war, now, war.OfferDeadline);
            war.HasSnapshot = true;
            war.SnapshotAt = Time.unscaledTime;
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
                NavalRole role = war.Active.Kind == "DEFEND" ? NavalRole.Screen
                    : war.Active.Kind == "RECON" ? NavalRole.Patrol : NavalRole.CoastalSupport;
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
                if (Scalar.Distance2DSquared(point.x, point.z, op.X, op.Z) >
                    PresenceRadius * PresenceRadius) continue;
                if (unit is Aircraft) op.AirGroups++;
                else if (unit is Ship) op.NavalGroups++;
            }
            op.Summary = AimOf(op.Kind, op.Key, op.Objective) + ". Assigned: " +
                op.GroundGroups + " ground groups. Nearby: " + op.AirGroups +
                " aircraft, " + op.NavalGroups + " ships. Convoy <=35% pool /120s; native gates.";
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
            ModuleServices.TryGet(out IThreatPicture picture);
            war.Reads.Clear(); war.Fronts.Clear();
            war.FrontClockAt = Time.unscaledTime;
            bool contactChanged = false;
            for (int i = 0; i < war.Candidates.Count; i++)
            {
                FrontCandidate front = war.Candidates[i];
                if (front.Objective && TryGetCaptureOwner(front.Key, front.X, front.Z, war.HQ,
                        out bool heldAirbase)) front.Held = heldAirbase;
                else if (front.Objective) front.Held = false;
                else if (territory != null &&
                         territory.TryGetHoldStrength(war.HQ.GetInstanceID(),
                             front.X, front.Z, out float hold))
                    front.Held = hold > .35f;
                bool observed = front.Hostile > 0 || picture != null &&
                    picture.TryGetAreaIntel(war.HQ.GetInstanceID(), front.X, front.Z,
                        PresenceRadius, out AreaIntel intel) && intel.Scouted;
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
            // Keep live operation/offer targets in the sample while the spare slots
            // rotate. At most128 ordinary objective entries are inspected per review.
            if (war.Active?.Objective == true)
                AddObjective(war, MissionManager.Objectives?.GetObjective(war.Active.Key), active);
            for (int i = 0; i < war.Proposals.Count; i++)
                AddObjective(war, MissionManager.Objectives?.GetObjective(war.Proposals[i].TargetKey), active);
            if (active.Count == 0) return;
            int inspected = 0;
            while (inspected < Math.Min(active.Count, MaximumObjectiveScan) && war.Candidates.Count < 4)
            {
                int index = (war.ObjectiveCursor + inspected++) % active.Count;
                AddObjective(war, active[index], null);
            }
            war.ObjectiveCursor = (war.ObjectiveCursor + inspected) % active.Count;
        }

        private static void AddObjective(FactionWar war, Objective objective, List<Objective> active)
        {
                if (war.Candidates.Count >= 4 || objective?.SavedObjective == null ||
                    objective.SavedObjective.Hidden || objective.Status == ObjectiveStatus.Complete ||
                    (objective.SavedObjective.ObjectiveTypeEnum != ObjectiveType.CaptureAirbase &&
                     objective.SavedObjective.ObjectiveTypeEnum != ObjectiveType.DestroyUnits) ||
                    active != null && !active.Contains(objective) ||
                    !(objective is IObjectiveWithPosition positioned) || positioned.Positions.Count == 0)
                    return;
                GlobalPosition point = positioned.Positions[0].Position;
                if (!Finite(point.x) || !Finite(point.z)) return;
                string key = objective.SavedObjective.UniqueName;
                if (string.IsNullOrEmpty(key)) return;
                for (int i = 0; i < war.Candidates.Count; i++)
                    if (war.Candidates[i].Key == key) return;
                war.Candidates.Add(new FrontCandidate
                {
                    Key = key,
                    Label = string.IsNullOrEmpty(objective.SavedObjective.DisplayName)
                        ? key : objective.SavedObjective.DisplayName,
                    X = point.x, Z = point.z, Objective = true,
                });
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
                             Scalar.Distance2DSquared(war.Candidates[i].X, war.Candidates[i].Z,
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
            ModuleServices.TryGet(out IAircraftTaskExclusion excludedAircraft);
            int cap = Math.Min(all.Count, MaximumUnitScan);
            for (int i = 0; i < cap; i++)
            {
                Unit unit = all[i];
                if (unit == null || unit.disabled || unit.NetworkHQ == null) continue;
                bool friendly = ReferenceEquals(unit.NetworkHQ, war.HQ);
                if (unit is Aircraft aircraft)
                {
                    if (friendly && (aircraft.Player != null || aircraft.pilots == null ||
                        aircraft.pilots.Length == 0 || aircraft.pilots[0] == null ||
                        aircraft.pilots[0].playerControlled ||
                        (excludedAircraft != null &&
                         excludedAircraft.IsExcluded(aircraft.persistentID.GetHashCode())) ||
                        !WingLink.TryIsWingMember(aircraft.persistentID.GetHashCode(),
                            out bool wingMember) || wingMember)) continue;
                }
                else if (!(unit is GroundVehicle || unit is Ship)) continue;
                if (!friendly && !war.HQ.IsTargetBeingTracked(unit)) continue;
                GlobalPosition position;
                if (friendly) position = unit.GlobalPosition();
                else if (!war.HQ.TryGetKnownPosition(unit, out position)) continue;
                if (!Finite(position.x) || !Finite(position.z)) continue;
                for (int j = 0; j < war.Candidates.Count; j++)
                {
                    FrontCandidate front = war.Candidates[j];
                    if (Scalar.Distance2DSquared(position.x, position.z, front.X, front.Z) >
                        PresenceRadius * PresenceRadius) continue;
                    if (friendly) front.Friendly++; else front.Hostile++;
                }
            }
        }

        private static bool TryGetCaptureOwner(string key, float x, float z, FactionHQ hq, out bool owned)
        {
            owned = false;
            if (!(MissionManager.Objectives?.GetObjective(key) is CaptureAirbaseObjective capture) ||
                capture.Saved?.targetAirbases == null || FactionRegistry.airbaseLookup == null) return false;
            float nearest = 1500f * 1500f;
            bool found = false;
            int checkedBases = 0;
            foreach (string name in capture.Saved.targetAirbases)
            {
                if (++checkedBases > 64) break;
                if (string.IsNullOrEmpty(name) ||
                    !FactionRegistry.airbaseLookup.TryGetValue(name, out Airbase airbase) ||
                    airbase == null || airbase.AttachedAirbase || airbase.UnitDestroyed()) continue;
                Transform center = airbase.center != null ? airbase.center : airbase.transform;
                GlobalPosition position = center.GlobalPosition();
                float distance = Scalar.Distance2DSquared(x, z, position.x, position.z);
                if (distance > nearest) continue;
                nearest = distance;
                owned = ReferenceEquals(airbase.CurrentHQ, hq);
                found = true;
            }
            return found;
        }

        private static bool RefreshOffers(FactionWar war)
        {
            for (int i = 0; i < war.Proposals.Count; i++)
            {
                TheaterProposalView offer = war.Proposals[i];
                if (!TryCurrentOffer(war, offer, out WarFrontRead front)) return false;
                war.Proposals[i] = new TheaterProposalView(offer.Id, offer.Revision, offer.Kind,
                    offer.Label, offer.TargetKey, offer.X, offer.Z, offer.Brief,
                    RiskOf(front), ForcesOf(front), offer.SecondsRemaining);
            }
            return true;
        }

        private static string RiskOf(WarFrontRead front) => !front.Observed ? "UNKNOWN"
            : front.Hostile > front.Friendly ? "HIGH" : "MODERATE";

        private static string ForcesOf(WarFrontRead front) => front.Friendly + " nearby friendly units / " +
            (front.Observed ? front.Hostile + " tracked hostile" : "resistance unknown");

        private static string AimOf(string kind, string key, bool objective) =>
            kind == "DEFEND" ? "Retain held ground; clear observed pressure"
            : kind == "RECON" ? "Observe this uncertain front; resistance unknown"
            : !objective ? "Gain signed control of this sector"
            : MissionManager.Objectives?.GetObjective(key)?.SavedObjective?.ObjectiveTypeEnum ==
                ObjectiveType.DestroyUnits ? "Destroy native mission targets at this fix"
                : "Secure the capture target at this fix";

        private static bool TryCurrentOffer(FactionWar war, TheaterProposalView offer, out WarFrontRead front)
        {
            front = default;
            for (int i = 0; i < war.Reads.Count; i++)
                if (war.Reads[i].Key == offer.TargetKey)
                {
                    front = war.Reads[i];
                    return LivingWarRules.Score(front, war.Posture, out string kind) > 0f &&
                        kind == offer.Kind && Scalar.Distance2DSquared(offer.X, offer.Z, front.X, front.Z) <= 1f;
                }
            return false;
        }

        private void Offer(FactionWar war, float now, float deadline = -1f)
        {
            LivingWarRules.Choose(war.Reads, war.Posture, choices);
            war.Proposals.Clear();
            war.Revision++;
            if (choices.Count == 0) { war.NextOffer = now + 30f; return; }
            war.OfferDeadline = deadline >= 0f ? deadline : now + LivingWarRules.OfferSeconds;
            for (int i = 0; i < choices.Count; i++)
            {
                WarOffer offer = choices[i];
                string brief = AimOf(offer.Kind, offer.Front.Key, offer.Front.Objective);
                war.Proposals.Add(new TheaterProposalView(war.NextId++, war.Revision,
                    offer.Kind, offer.Front.Label, offer.Front.Key, offer.Front.X, offer.Front.Z,
                    brief + ". Native convoy <=35% pool /120s.", RiskOf(offer.Front), ForcesOf(offer.Front),
                    Mathf.Max(0f, war.OfferDeadline - now)));
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
                ? "IN CONTACT" : op.Kind == "DEFEND" ? "HOLDING"
                : op.Kind == "RECON" ? "SCOUTING" : "ADVANCING";
            if (op.Phase != phase && elapsed < LivingWarRules.OperationLimitSeconds)
            {
                op.Phase = phase;
                war.Log.Add(op.Label + " · " + phase);
            }
            float hold = op.InitialHold;
            bool hasHold = territory != null && territory.TryGetHoldStrength(war.HQ.GetInstanceID(),
                op.X, op.Z, out hold);
            bool objectiveClosed = false, objectiveSecured = false;
            if (op.Objective &&
                MissionPosition.TryGetActiveObjectives(war.HQ, out List<Objective> active) &&
                active != null)
            {
                Objective resolved = MissionManager.Objectives?.GetObjective(op.Key);
                objectiveSecured = resolved != null && resolved.Status == ObjectiveStatus.Complete;
                objectiveClosed = resolved == null || !active.Contains(resolved);
            }
            bool secured = objectiveSecured || (op.Kind == "DEFEND"
                ? found && front.Held && front.Friendly > 0 && front.Hostile == 0 && elapsed > 45f
                : op.Kind == "RECON" ? found && front.Observed && elapsed > 45f
                : !op.Objective && hasHold && hold > .55f && hold - op.InitialHold > .25f);
            bool repulsed = found && front.Friendly == 0 && op.InitialFriendly > 0 && elapsed > 45f;
            bool lostDefense = op.Kind == "DEFEND" && (found && !front.Held ||
                op.Objective && TryGetCaptureOwner(op.Key, op.X, op.Z, war.HQ, out bool held) && !held);
            if (lostDefense || objectiveClosed && !objectiveSecured)
            { repulsed = true; secured = false; }
            bool timedOut = elapsed >= LivingWarRules.OperationLimitSeconds;
            if (!secured && !repulsed && !timedOut) return false;
            string outcome = secured ? "SECURED" : repulsed ? "REPULSED" : "STALLED";
            war.Log.Add(op.Label + " · " + outcome);
            Plugin.Logger?.LogInfo("Living Front " + war.HQ.faction.factionName + " " + op.Label + " " + outcome);
            ClearOperationDirective(war);
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
                war.Revision != revision || war.Proposals.Count == 0 ||
                !automatic && Time.timeSinceLevelLoad >= war.OfferDeadline) return false;
            TheaterProposalView chosen = null;
            for (int i = 0; i < war.Proposals.Count; i++)
                if (war.Proposals[i].Id == id && war.Proposals[i].Revision == revision)
                    chosen = war.Proposals[i];
            if (chosen == null) return false;
            // Both manual and deadline choices are revalidated against live native
            // objectives, current ownership, observed contacts and posture eligibility.
            Sense(war);
            if (!TryCurrentOffer(war, chosen, out WarFrontRead front)) return false;
            float hold = 0f;
            territory?.TryGetHoldStrength(war.HQ.GetInstanceID(), chosen.X, chosen.Z, out hold);
            war.Active = new Operation
            {
                Id = chosen.Id, Revision = ++war.Revision, Kind = chosen.Kind,
                Key = chosen.TargetKey, Label = chosen.Label, X = chosen.X, Z = chosen.Z,
                Phase = "FORMING", Started = Time.timeSinceLevelLoad, InitialHold = hold,
                InitialFriendly = front.Friendly, Objective = front.Objective,
            };
            CountOperationForces(war);
            war.Proposals.Clear();
            war.GroupAssignments.Clear();
            priority?.SetDirectedFix(war.HQ, chosen.TargetKey, chosen.Label, chosen.X, chosen.Z);
            // NextReinforce belongs to the faction, not the selected operation.
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
                ClearOperationDirective(war);
                naval?.ClearObjective(hq, war.Active.Key);
                war.Active = null;
                war.GroupAssignments.Clear();
                war.Revision++;
                // Calling off is also the route to a new choice when the field changes.
                Sense(war);
                Offer(war, Time.timeSinceLevelLoad);
                network?.Broadcast(hq.faction.factionName);
                return true;
            }
            if (kind != 2 || posture > (byte)TheaterWarPosture.Bold) return false;
            war.Posture = (TheaterWarPosture)posture;
            war.Log.Add("POSTURE " + war.Posture.ToString().ToUpperInvariant());
            if (war.Active == null && war.Proposals.Count > 0)
            {
                Sense(war);
                Offer(war, Time.timeSinceLevelLoad, war.OfferDeadline);
            }
            network?.Broadcast(hq.faction.factionName);
            return true;
        }

        private void ClearOperationDirective(FactionWar war)
        {
            if (priority != null && war.Active != null &&
                priority.TryGetDirective(war.HQ.faction.factionName, out PriorityDirective current) &&
                current.Key == war.Active.Key) priority.ClearDirective(war.HQ);
        }

        internal bool IsDefending(FactionHQ hq, string key) => Authoritative && hq?.faction != null &&
            wars.TryGetValue(hq.faction.factionName, out FactionWar war) &&
            war.Active?.Kind == "DEFEND" && war.Active.Key == key;

        internal bool TryGetGroundAssignment(FactionHQ hq, int groupId, float x, float z,
            out PriorityDirective directive, out bool offensive)
        {
            directive = default; offensive = false;
            if (!Authoritative || !Finite(x) || !Finite(z) || hq?.faction == null ||
                !wars.TryGetValue(hq.faction.factionName, out FactionWar war)) return false;
            if (war.GroupAssignments.TryGetValue(groupId, out var assigned))
            {
                if (war.Active != null && war.Active.Key == assigned.Key &&
                    Scalar.Distance2DSquared(x, z, war.Active.X, war.Active.Z) <= AssignmentRadius * AssignmentRadius)
                {
                    Operation active = war.Active;
                    directive = new PriorityDirective(active.Key, active.Label, active.X, 0f, active.Z);
                    offensive = assigned.Offensive;
                    return true;
                }
                for (int i = 0; i < war.Fronts.Count; i++)
                {
                    TheaterFrontView held = war.Fronts[i];
                    if (held.Key != assigned.Key || Scalar.Distance2DSquared(x, z, held.X, held.Z) >
                        AssignmentRadius * AssignmentRadius) continue;
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
                float distance = Scalar.Distance2DSquared(x, z, op.X, op.Z);
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
                    float distance = Scalar.Distance2DSquared(x, z, front.X, front.Z);
                    if (distance > nearest) continue;
                    nearest = distance; key = front.Key; label = front.Label;
                    targetX = front.X; targetZ = front.Z;
                }
            if (key == null) return false;
            if (war.GroupAssignments.Count < 16)
                war.GroupAssignments[groupId] = (key, attack);
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
        public bool HasSnapshot => Available && LocalState()?.HasSnapshot == true;
        public float SnapshotAgeSeconds => !HasSnapshot ? -1f : Authoritative ? 0f
            : Mathf.Max(0f, Time.unscaledTime - LocalState().SnapshotAt);
        public bool CommandPending => network?.CommandPending == true;
        public string CommandStatus => Authoritative ? localCommandStatus : network?.CommandStatus ?? "";
        public bool CanCommand => HasSnapshot && SnapshotAgeSeconds <= LivingWarRules.SnapshotFreshSeconds &&
            !CommandPending && GameAccess.TryGetLocalFaction(out _);
        public TheaterWarPosture Posture => LocalState()?.Posture ?? TheaterWarPosture.Steady;
        public IReadOnlyList<TheaterFrontView> Fronts
        {
            get { FactionWar war = LocalState(); UpdateFrontClock(war);
                return (IReadOnlyList<TheaterFrontView>)war?.Fronts ?? Array.Empty<TheaterFrontView>(); }
        }
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
        public void Refresh()
        {
            GameAccess.TryGetLocalFaction(out FactionHQ hq);
            if (!ReferenceEquals(hq, viewHq))
            {
                viewHq = hq;
                if (!GameAccess.IsServer()) { ClearRemoteState(); network?.ClearClientState(); }
                localCommandStatus = "";
            }
            FactionWar war = LocalState();
            UpdateProposalClock(war); UpdateFrontClock(war);
        }

        private static void UpdateFrontClock(FactionWar war)
        {
            if (war == null) return;
            float elapsed = Mathf.Max(0f, Time.unscaledTime - war.FrontClockAt);
            if (elapsed < 1f) return;
            war.FrontClockAt = Time.unscaledTime;
            for (int i = 0; i < war.Fronts.Count; i++)
            {
                TheaterFrontView old = war.Fronts[i];
                war.Fronts[i] = new TheaterFrontView(old.Key, old.Label, old.X, old.Z,
                    old.Status, old.Pressure, old.Trend, old.Observed,
                    old.AgeSeconds < 0f ? -1f : old.AgeSeconds + elapsed);
            }
        }

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
            Request(0, proposalId, revision, 0);
        public bool RequestCancel(int operationId, int revision) =>
            Request(1, operationId, revision, 0);
        public bool RequestPosture(TheaterWarPosture posture) =>
            Request(2, 0, 0, (byte)posture);

        private bool Request(byte kind, int id, int revision, byte posture)
        {
            if (!CanCommand || !GameAccess.TryGetLocalFaction(out FactionHQ hq)) return false;
            if (!Authoritative) return network != null && network.SendIntent(kind, id, revision, posture);
            bool accepted = ApplyIntent(hq, kind, id, revision, posture);
            localCommandStatus = accepted ? "ACCEPTED · staff updated"
                : "REJECTED · choice changed; review current options";
            return accepted;
        }

        internal void CopySnapshot(string faction, List<TheaterFrontView> fronts,
            List<TheaterProposalView> proposals, out TheaterLiveOperationView operation,
            out TheaterWarPosture posture, List<string> log)
        {
            fronts.Clear(); proposals.Clear(); log.Clear();
            operation = null; posture = TheaterWarPosture.Steady;
            if (!wars.TryGetValue(faction, out FactionWar war)) return;
            UpdateProposalClock(war); UpdateFrontClock(war);
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
            if (GameAccess.IsServer() || string.IsNullOrEmpty(faction) ||
                !GameAccess.TryGetLocalFaction(out FactionHQ hq) || hq?.faction?.factionName != faction) return;
            viewHq = hq;
            FactionWar war = StateOf(faction);
            if (war == null) return;
            war.Fronts.Clear(); war.Proposals.Clear(); war.Log.Clear();
            war.HasSnapshot = true;
            war.SnapshotAt = war.FrontClockAt = Time.unscaledTime;
            for (int i = 0; fronts != null && i < fronts.Count && i < LivingWarRules.MaximumFronts; i++)
                war.Fronts.Add(fronts[i]);
            for (int i = 0; proposals != null && i < proposals.Count && i < LivingWarRules.MaximumOffers; i++)
                war.Proposals.Add(proposals[i]);
            war.OfferDeadline = Time.timeSinceLevelLoad +
                (war.Proposals.Count > 0 ? Mathf.Max(0f, war.Proposals[0].SecondsRemaining) : 0f);
            war.NextClock = 0f;
            for (int i = log != null ? Math.Min(log.Count,
                BoscaliSummer.Modules.TheaterOps.Domain.StaffLog.MaximumEntries) - 1 : -1; i >= 0; i--)
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

        internal bool HasSnapshotFor(string faction) =>
            wars.TryGetValue(faction, out FactionWar war) && war.HasSnapshot;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);    }
}
