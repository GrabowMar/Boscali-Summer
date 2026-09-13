using System;
using System.Collections.Generic;
using BoscaliSummer.Features.DynamicOperations.Domain;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.DynamicOperations.Runtime
{
    internal sealed partial class OperationsManager
    {
        private sealed class Candidate
        {
            public Unit Unit;
            public Airbase Base;
            public int Count;
        }

        private sealed class JammerContact
        {
            public Unit Source;
            public FactionHQ Victim;
            public float Time;
        }

        private readonly Candidate[] candidates = CreateCandidates();
        private readonly List<JammerContact> jammers = new List<JammerContact>(32);
        private int sightlineQueries;

        private static Candidate[] CreateCandidates()
        {
            var result = new Candidate[Enum.GetValues(typeof(OperationKind)).Length];
            for (int i = 0; i < result.Length; i++) result[i] = new Candidate();
            return result;
        }

        private static bool IsExtended(OperationKind kind) => kind >= OperationKind.Rescue;

        private void GatherMissionCandidates(FactionHQ hq, FactionBoard board, List<Unit> units, int count, float now)
        {
            foreach (Candidate candidate in candidates) { candidate.Unit = null; candidate.Base = null; candidate.Count = 0; }
            bool hasEngineers = false;
            // One shared 4096-unit pass for all additional families, rather than a scan per family.
            for (int i = 0; i < count; i++)
            {
                Unit unit = units[i];
                if (unit == null || unit.NetworkHQ == null) continue;
                bool friendly = unit.NetworkHQ == hq;
                Vector3 position;
                if (friendly) position = unit.transform.position.ToGlobalPosition().AsVector3();
                else if (unit.disabled || !TryKnownPosition(hq, unit, out position)) continue;
                if (!Finite(position) || DistanceToOwnedBase(position, hq, out Airbase anchor) > 25000f * 25000f || anchor == null) continue;
                bool ground = unit is GroundVehicle || unit is Building;
                if (friendly)
                {
                    if (!unit.disabled && unit.TryGetComponent(out Repairer _)) hasEngineers = true;
                    if (unit is PilotDismounted && !unit.disabled) Choose(OperationKind.Rescue);
                    if (unit is GroundVehicle && !unit.disabled && unit.TryGetComponent(out Rearmer rearmer) &&
                        Operation.Finite(rearmer.Capacity) && rearmer.Capacity > 0f) Choose(OperationKind.SupplyEscort);
                    if (unit is Building building && building.NeedsRepair() && building.IsRepairable()) Choose(OperationKind.RepairCover);
                    if (ground && (unit.disabled || unit is Building damaged && damaged.NeedsRepair())) Choose(OperationKind.BattlefieldSurvey);
                }
                else
                {
                    if (unit is Aircraft) Choose(OperationKind.Intercept);
                    if (ground)
                    {
                        Choose(OperationKind.Recon);
                        if (unit is Building) Choose(OperationKind.DamageAssessment);
                        Choose(OperationKind.SortieReport);
                        if (unit.HasRadarEmission()) Choose(OperationKind.Jam);
                        if (unit is GroundVehicle && unit.TryGetComponent(out Rearmer _)) Choose(OperationKind.SupplyInterdict);
                    }
                    for (int j = 0; j < jammers.Count; j++)
                        if (jammers[j].Source == unit && jammers[j].Victim == hq && now - jammers[j].Time <= 60f)
                        { Choose(OperationKind.ElectronicWarfare); break; }
                }

                void Choose(OperationKind kind)
                {
                    if (board.Rules.WasIssued(kind, unit.GetInstanceID())) return;
                    Candidate candidate = candidates[(int)kind];
                    if (random.Next(++candidate.Count) != 0) return;
                    candidate.Unit = unit; candidate.Base = anchor;
                }
            }
            if (!hasEngineers) candidates[(int)OperationKind.RepairCover].Unit = null;
        }

        private void AddMissionCandidate(FactionHQ hq, FactionBoard board, OperationKind kind, float now)
        {
            Candidate candidate = candidates[(int)kind];
            if (candidate.Unit == null || candidate.Base == null) return;
            int money = kind switch
            {
                OperationKind.Rescue => 1800, OperationKind.Recon => 900, OperationKind.DamageAssessment => 1200,
                OperationKind.SupplyEscort => 1400, OperationKind.SupplyInterdict => 1000, OperationKind.RepairCover => 1400,
                OperationKind.SortieReport => 1500, OperationKind.BattlefieldSurvey => 700,
                OperationKind.Intercept => 1600, _ => 1400
            };
            Target target = Add(board, now, kind, candidate.Base, candidate.Unit, hq, OperationReward.None, money,
                kind == OperationKind.BattlefieldSurvey ? 60 : kind == OperationKind.Recon ? 75 : 125);
            if (target == null) return;
            target.Radius = target.Mission.IsStrike || kind == OperationKind.Jam || kind == OperationKind.Rescue ? 0f : 1500f;
            if (candidate.Unit.NetworkHQ == hq) target.Position = candidate.Unit.transform.position.ToGlobalPosition().AsVector3();
        }

        private bool ExtendedValid(FactionHQ hq, Target target)
        {
            Operation op = target.Mission;
            if (op.Returning)
                return Usable(target.Base) && target.Base.CurrentHQ == hq && PlayerAircraft(hq, target.ReturnAircraft);
            if (op.Kind == OperationKind.DamageAssessment && target.Life.Neutralized) return true;
            if ((op.Kind == OperationKind.SupplyEscort || op.Kind == OperationKind.RepairCover) && target.Serviced) return true;
            if (op.IsStrike && target.Life.Neutralized) return true;
            if (target.Unit == null || target.Unit.NetworkHQ != target.OriginalOwner || target.Life.Despawned) return false;
            if (op.Kind == OperationKind.RepairCover)
                return target.Unit is Building building && building.NeedsRepair();
            if (op.Kind == OperationKind.BattlefieldSurvey)
                return target.Unit.disabled || target.Unit is Building damaged && damaged.NeedsRepair();
            return !target.Unit.disabled;
        }

        private void TickExtended(FactionHQ hq, Target target, float now, float elapsed)
        {
            Operation op = target.Mission;
            if (!op.IsLive) return;
            bool valid = ExtendedValid(hq, target);
            // Refresh only observed enemy positions; the strike site freezes at the last known sample.
            bool visible = MarkerPosition(hq, target);
            bool observation = op.Kind == OperationKind.Recon || op.Kind == OperationKind.SortieReport;
            bool survey = observation || op.Kind == OperationKind.DamageAssessment || op.Kind == OperationKind.BattlefieldSurvey;
            bool present = false;
            if (op.State == OperationState.Active && valid && !op.Returning && visible)
            {
                Aircraft observer = FindObserver(hq, target, survey);
                present = observer != null;
                if (observation && !FreshContact(hq, target.Unit)) present = false;
                if (observer != target.Observer)
                {
                    // A different aircraft starts its own continuous observation/cover interval.
                    op.Observe(now, 0f, valid, true, target.Life.Neutralized, false);
                    target.Observer = observer;
                }
            }
            bool returned = op.Returning && valid && target.ReturnAircraft.IsLanded() &&
                (target.ReturnAircraft.transform.position - target.Base.center.position).sqrMagnitude <= 1000f * 1000f;
            op.Observe(now, elapsed, valid, true, target.Life.Neutralized, present || target.Serviced,
                returned: returned, serviced: target.Serviced);
            if (op.Kind == OperationKind.SortieReport && op.HoldSeconds >= op.HoldRequired && target.Observer != null && op.BeginReturn(now))
            {
                target.ReturnAircraft = target.Observer;
                target.Outcome = "Intelligence recorded. Land this aircraft at " + target.Base.name + ".";
            }
            if (op.Kind == OperationKind.DamageAssessment && op.State == OperationState.Active)
                target.Outcome = target.Life.Neutralized ? "Strike recorded; survey the last known site to confirm. Faction morale +3 on success." :
                    "Awaiting target neutralization, then a 20s site survey. Faction morale +3 on success.";
        }

        private static bool PlayerAircraft(FactionHQ hq, Aircraft aircraft) => aircraft != null && !aircraft.disabled &&
            aircraft.NetworkHQ == hq && aircraft.Player != null && aircraft.Player.HQ == hq && aircraft.Player.Aircraft == aircraft;

        private Aircraft FindObserver(FactionHQ hq, Target target, bool lineOfSight)
        {
            // Prefer the previous observer so two players cannot alternate and lose continuous progress.
            int initialQueries = sightlineQueries;
            if (OnStation(hq, target.Observer, target, lineOfSight)) return target.Observer;
            for (int i = 0; i < Math.Min(64, hq.factionPlayers.Count); i++)
            {
                if (lineOfSight && sightlineQueries - initialQueries >= 2) break;
                Aircraft aircraft = hq.factionPlayers[i].Player?.Aircraft;
                if (aircraft == target.Observer) continue;
                if (OnStation(hq, aircraft, target, lineOfSight)) return aircraft;
            }
            return null;
        }

        private bool OnStation(FactionHQ hq, Aircraft aircraft, Target target, bool lineOfSight)
        {
            if (!PlayerAircraft(hq, aircraft)) return false;
            Vector3 local = aircraft.transform.position;
            Vector3 delta = local.ToGlobalPosition().AsVector3() - target.Position;
            if (!Finite(delta) || delta.y < 50f || delta.sqrMagnitude > 1500f * 1500f) return false;
            if (!lineOfSight) return true;
            if (sightlineQueries >= 32) return false;
            sightlineQueries++;
            Vector3 endpoint = new GlobalPosition(target.Position.x, target.Position.y, target.Position.z).ToLocalPosition() + Vector3.up * 5f;
            return !Physics.Linecast(local, endpoint, out RaycastHit hit, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore) ||
                target.Unit != null && hit.collider != null && hit.collider.GetComponentInParent<Unit>() == target.Unit;
        }

        private static bool FreshContact(FactionHQ hq, Unit unit)
        {
            if (unit == null || !TryKnownPosition(hq, unit, out _)) return false;
            TrackingInfo tracking = hq.GetTrackingData(unit.persistentID);
            float age = Time.timeSinceLevelLoad - tracking.lastSpottedTime;
            return Operation.Finite(age) && age >= 0f && age <= 2f;
        }

        private static bool MarkerPosition(FactionHQ hq, Target target)
        {
            if (target.Mission.Returning)
            {
                if (!Usable(target.Base) || target.Base.CurrentHQ != hq) return false;
                target.Position = target.Base.center.GlobalPosition().AsVector3(); target.Radius = 1000f;
                return Finite(target.Position);
            }
            if (target.Mission.Kind == OperationKind.DamageAssessment && target.Life.Neutralized) return Finite(target.Position);
            if (ReferenceEquals(target.Unit, null)) return true;
            if (target.Unit == null) return false;
            if (target.Unit.NetworkHQ == hq)
            { target.Position = target.Unit.transform.position.ToGlobalPosition().AsVector3(); return Finite(target.Position); }
            return TryKnownPosition(hq, target.Unit, out Vector3 known) && UpdatePosition(target, known);
        }

        private static bool UpdatePosition(Target target, Vector3 position) { target.Position = position; return true; }

        private void RememberJammer(Unit victim, Unit source)
        {
            if (victim.NetworkHQ == null || victim.NetworkHQ == source.NetworkHQ) return;
            float now = NetworkSceneSingleton<MissionManager>.i.MissionTime;
            for (int i = jammers.Count - 1; i >= 0; i--)
            {
                JammerContact contact = jammers[i];
                if (contact.Source == null || now - contact.Time > 60f) { jammers.RemoveAt(i); continue; }
                if (contact.Source == source && contact.Victim == victim.NetworkHQ) { contact.Time = now; return; }
            }
            if (jammers.Count < 32) jammers.Add(new JammerContact { Source = source, Victim = victim.NetworkHQ, Time = now });
        }

        private bool Observing => GameAccess.IsServer() && settings?.Enabled.Value == true && MissionManager.IsRunning &&
            ReferenceEquals(missionIdentity, MissionManager.CurrentMission);

        internal void ObserveRescue(PilotDismounted pilot, Aircraft aircraft)
        {
            if (!Observing || pilot == null || aircraft == null || pilot.NetworkHQ == null ||
                pilot.NetworkHQ != aircraft.NetworkHQ || pilot.NetworkunitState != Unit.UnitState.Returned ||
                !PlayerAircraft(pilot.NetworkHQ, aircraft) || !boards.TryGetValue(pilot.NetworkHQ, out FactionBoard board)) return;
            float now = NetworkSceneSingleton<MissionManager>.i.MissionTime;
            foreach (Target target in board.Targets)
                if (target.Unit == pilot && target.Mission.Kind == OperationKind.Rescue && target.Mission.BeginReturn(now))
                {
                    target.ReturnAircraft = aircraft;
                    target.Outcome = "Native rescue confirmed. Land this aircraft at " + target.Base.name + ".";
                }
        }

        internal void ObserveService(Unit source, Unit recipient, bool repair)
        {
            if (!Observing || source == null || recipient == null || source == recipient || source.NetworkHQ == null ||
                source.NetworkHQ != recipient.NetworkHQ || !boards.TryGetValue(source.NetworkHQ, out FactionBoard board)) return;
            foreach (Target target in board.Targets)
            {
                Operation op = target.Mission;
                if (op.State != OperationState.Active || op.HoldSeconds < op.HoldRequired ||
                    op.Kind != (repair ? OperationKind.RepairCover : OperationKind.SupplyEscort) ||
                    target.Unit != (repair ? recipient : source) || !OnStation(source.NetworkHQ, target.Observer, target, false)) continue;
                target.Serviced = true;
                // Latch completion at the native service event, before a single-use truck can disappear
                // or an observer can change. Payment stays in the ordinary once-only board tick.
                op.Observe(NetworkSceneSingleton<MissionManager>.i.MissionTime, 0f, true, true, false, true, serviced: true);
            }
        }
    }
}
