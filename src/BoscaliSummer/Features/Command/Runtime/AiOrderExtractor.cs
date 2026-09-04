using System.Collections.Generic;
using BoscaliSummer.Features.Command.Domain;
using BoscaliSummer.Runtime;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Runtime
{
    internal sealed class AiOrderExtractor
    {
        private readonly List<AiTaskingOrder> pooledOrders = new List<AiTaskingOrder>(64);
        private readonly int maxCapacity;

        public AiOrderExtractor(int maxOrders = 48)
        {
            maxCapacity = Mathf.Clamp(maxOrders, 16, 96);
            for (int i = 0; i < maxCapacity; i++)
            {
                pooledOrders.Add(new AiTaskingOrder());
            }
        }

        public int ExtractActiveOrders(FactionHQ localHq, List<AiTaskingOrder> outOrders)
        {
            if (outOrders == null || localHq == null) return 0;
            outOrders.Clear();

            IReadOnlyList<Aircraft> aircraftList = UnitRegistry.allAircraft;
            if (aircraftList == null) return 0;

            int count = 0;
            for (int i = 0; i < aircraftList.Count && count < maxCapacity; i++)
            {
                Aircraft aircraft = aircraftList[i];
                if (aircraft == null || aircraft.disabled || aircraft.Player != null) continue;
                if (PresenceBoard.Contains(
                    PresenceBoard.GetInts(PresenceBoard.WingMemberIds),
                    aircraft.persistentID.GetHashCode()))
                    continue;

                Pilot pilot = aircraft.pilots != null && aircraft.pilots.Length > 0 ? aircraft.pilots[0] : null;
                if (pilot == null || pilot.currentState == null) continue;

                bool isFriendly = aircraft.NetworkHQ == localHq;
                // For enemy aircraft, only show if being actively tracked by our HQ
                if (!isFriendly && !localHq.IsTargetBeingTracked(aircraft)) continue;

                // Only draw flight order vectors when aircraft is actively engaging a valid, living target
                if (pilot.currentState is AIPilotCombatModes combatModes && GameAccess.AiPilotCombatAvailable)
                {
                    Unit target = GameAccess.GetAiCurrentTarget(combatModes);
                    if (target == null || target.disabled || target == aircraft) continue;

                    Vector3 origin = aircraft.GlobalPosition().AsVector3();
                    Vector3 targetPos = target.GlobalPosition().AsVector3();
                    float dist = Vector3.Distance(origin, targetPos);
                    if (dist < 1000f || dist > 80000f) continue;

                    AiTaskingOrder order = pooledOrders[count];
                    order.Unit = aircraft;
                    order.Callsign = string.IsNullOrEmpty(aircraft.unitName) ? "AIRCRAFT" : aircraft.unitName;
                    order.IsFriendly = isFriendly;
                    order.OriginWorld = origin;
                    order.CurrentTarget = target;
                    order.TargetWorld = targetPos;
                    order.TargetName = string.IsNullOrEmpty(target.unitName) ? "TARGET" : target.unitName;
                    order.EstimatedRange = dist;

                    bool isAirTarget = target is Aircraft;
                    order.MissionType = isAirTarget ? AiMissionType.CombatAirPatrol : AiMissionType.Strike;
                    order.MissionColor = isFriendly
                        ? (isAirTarget ? new Color(0.2f, 0.8f, 1.0f, 0.85f) : new Color(1.0f, 0.55f, 0.15f, 0.85f))
                        : new Color(0.95f, 0.2f, 0.2f, 0.85f); // Red for tracked enemies

                    outOrders.Add(order);
                    count++;
                }
            }

            return count;
        }
    }
}
