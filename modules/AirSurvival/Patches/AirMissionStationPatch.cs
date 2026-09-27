using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Features.AirSurvival.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Runtime;
using HarmonyLib;
using NuclearOption.SavedMission;

namespace BoscaliSummer.Features.AirSurvival.Patches
{
    [HarmonyPatch]
    internal static class AirMissionStationPatch
    {
        [HarmonyTargetMethod]
        private static MethodBase Target() => AccessTools.Method(typeof(MissionPosition),
            nameof(MissionPosition.TryGetClosestPosition),
            new[] { typeof(Unit), typeof(GlobalPosition).MakeByRefType() })
            ?? throw new MissingMethodException("MissionPosition.TryGetClosestPosition(Unit, out GlobalPosition)");

        [HarmonyPostfix]
        private static void Postfix(Unit unit, ref GlobalPosition destination, ref bool __result)
        {
            if (!(unit is Aircraft aircraft) || !GameAccess.IsServer() || !aircraft.IsServer ||
                aircraft.disabled || aircraft.Player != null || aircraft.NetworkHQ?.faction == null ||
                aircraft.pilots == null || aircraft.pilots.Length == 0)
                return;

            Pilot pilot = aircraft.pilots[0];
            if (pilot == null || pilot.dead || pilot.playerControlled || pilot.flightInfo.EnemyContact ||
                !(pilot.currentState is AIPilotCombatModes || pilot.currentState is AIHeloCombatState))
                return;

            int identity = aircraft.persistentID.GetHashCode();
            if (!WingLink.TryIsWingMember(identity, out bool isWingMember) || isWingMember)
                return;

            // Squad is optional with Progression. When present it owns its ace flights;
            // Wing Command membership is checked independently above.
            if (ModServices.TryGet(out IAircraftTaskExclusion exclusion) &&
                exclusion.IsExcluded(identity))
                return;

            bool rotary = pilot.currentState is AIHeloCombatState;
            // Rotary AI asks this during FixedUpdate; its authored destination keeps
            // priority, including hidden objectives, without another per-frame scan.
            if (rotary && __result) return;
            if (!rotary && __result && IsHiddenOriginal(aircraft.NetworkHQ, destination)) return;

            if (ModServices.TryGet(out ITheaterAirStationView stations) &&
                stations.TryGetStation(aircraft.NetworkHQ.faction.factionName,
                    out float x, out float z, out float radius) &&
                AirStationPolicy.TryOperationFix(identity, x, z, radius, out x, out z))
            {
                float altitude = __result ? destination.y : aircraft.GlobalPosition().y;
                destination = new GlobalPosition(x, altitude, z);
                __result = true;
                return;
            }

            if (rotary || !__result ||
                !MissionPosition.TryGetActiveObjectives(aircraft.NetworkHQ,
                    out List<Objective> objectives) || objectives == null)
                return;

            float first = float.PositiveInfinity, second = float.PositiveInfinity,
                  third = float.PositiveInfinity;
            GlobalPosition firstFix = destination, secondFix = destination, thirdFix = destination;
            GlobalPosition from = aircraft.GlobalPosition();
            for (int i = 0; i < objectives.Count && i < AirStationPolicy.MaximumObjectives; i++)
            {
                Objective objective = objectives[i];
                if (objective?.SavedObjective == null ||
                    !(objective is IObjectiveWithPosition positioned)) continue;

                if (objective.SavedObjective.Hidden) continue;

                for (int j = 0; j < positioned.Positions.Count && j < 4; j++)
                {
                    GlobalPosition fix = positioned.Positions[j].Position;
                    float dx = fix.x - from.x, dz = fix.z - from.z;
                    float distance = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (float.IsNaN(distance) || float.IsInfinity(distance)) continue;
                    if (distance < first)
                    {
                        third = second; thirdFix = secondFix;
                        second = first; secondFix = firstFix;
                        first = distance; firstFix = fix;
                    }
                    else if (distance < second)
                    {
                        third = second; thirdFix = secondFix;
                        second = distance; secondFix = fix;
                    }
                    else if (distance < third) { third = distance; thirdFix = fix; }
                }
            }

            if (float.IsInfinity(first)) return;
            int rank = AirStationPolicy.ChooseObjectiveRank(identity, first, second, third);
            destination = rank == 2 ? thirdFix : rank == 1 ? secondFix : firstFix;
        }

        private static bool IsHiddenOriginal(FactionHQ hq, GlobalPosition destination)
        {
            if (!MissionPosition.TryGetActiveObjectives(hq, out List<Objective> objectives) ||
                objectives == null) return true;
            if (objectives.Count > AirStationPolicy.MaximumObjectives) return true;
            for (int i = 0; i < objectives.Count && i < AirStationPolicy.MaximumObjectives; i++)
            {
                Objective objective = objectives[i];
                if (objective?.SavedObjective == null || !objective.SavedObjective.Hidden ||
                    !(objective is IObjectiveWithPosition positioned)) continue;
                if (positioned.Positions.Count > 4) return true;
                for (int j = 0; j < positioned.Positions.Count && j < 4; j++)
                {
                    GlobalPosition fix = positioned.Positions[j].Position;
                    if (Math.Abs(fix.x - destination.x) < 1f &&
                        Math.Abs(fix.z - destination.z) < 1f) return true;
                }
            }
            return false;
        }
    }
}
