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

                AiTaskingOrder order = pooledOrders[count];
                order.Unit = aircraft;
                order.Callsign = string.IsNullOrEmpty(aircraft.unitName) ? "AIRCRAFT" : aircraft.unitName;
                order.IsFriendly = isFriendly;
                order.OriginWorld = aircraft.transform.position;

                // Deduce mission state
                if (pilot.currentState is AIPilotCombatModes combatModes && GameAccess.AiPilotCombatAvailable)
                {
                    Unit target = GameAccess.GetAiCurrentTarget(combatModes);
                    int mode = GameAccess.GetAiAttackMode(combatModes);

                    if (target != null && !target.disabled)
                    {
                        order.CurrentTarget = target;
                        order.TargetWorld = target.transform.position;
                        order.TargetName = string.IsNullOrEmpty(target.unitName) ? "TARGET" : target.unitName;

                        bool isAirTarget = target is Aircraft;
                        if (isAirTarget)
                        {
                            order.MissionType = AiMissionType.CombatAirPatrol;
                        }
                        else
                        {
                            order.MissionType = AiMissionType.Strike;
                        }
                    }
                    else
                    {
                        GlobalPosition knownPos = GameAccess.GetAiTargetKnownPosition(combatModes);
                        order.CurrentTarget = null;
                        order.TargetWorld = knownPos.AsVector3();
                        order.TargetName = "PATROL AREA";
                        order.MissionType = AiMissionType.CombatAirPatrol;
                    }
                }
                else if (pilot.currentState is AIPilotLandingState)
                {
                    order.MissionType = AiMissionType.ReturnToBase;
                    order.TargetName = "RTB (AIRBASE)";
                    order.CurrentTarget = null;
                    order.TargetWorld = aircraft.transform.position + aircraft.transform.forward * 3000f;
                }
                else if (pilot.currentState is AIPilotTakeoffState)
                {
                    order.MissionType = AiMissionType.CombatAirPatrol;
                    order.TargetName = "CLIMBOUT";
                    order.CurrentTarget = null;
                    order.TargetWorld = aircraft.transform.position + aircraft.transform.forward * 5000f;
                }
                else
                {
                    order.MissionType = AiMissionType.CombatAirPatrol;
                    order.TargetName = "PATROL";
                    order.CurrentTarget = null;
                    order.TargetWorld = aircraft.transform.position + aircraft.transform.forward * 2000f;
                }

                order.EstimatedRange = Vector3.Distance(order.OriginWorld, order.TargetWorld);
                order.MissionColor = AiTaskingOrder.GetMissionColor(order.MissionType);
                if (!isFriendly)
                {
                    order.MissionColor = new Color(0.95f, 0.2f, 0.2f, 0.85f); // Red for tracked enemies
                }

                outOrders.Add(order);
                count++;
            }

            return count;
        }
    }
}
