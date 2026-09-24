using System;
using System.Reflection;
using BoscaliSummer.Features.TheaterOps.Domain;
using BoscaliSummer.Features.TheaterOps.Runtime;
using BoscaliSummer.Runtime;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Features.TheaterOps.Patches
{
    /// <summary>
    /// Applies the host's main effort at the two places the game asks "where should this
    /// unit head next?". Both are pure queries on MissionPosition, so the override changes
    /// no state of its own and vanilla behavior resumes the moment the priority is cleared:
    ///
    /// <list type="bullet">
    /// <item>advance — an advancing ground vehicle, mobile artillery, or an idle aircraft
    /// with no contact;</item>
    /// <item>delivery — the sort key FactionHQ uses to pick which depot or airbase delivers
    /// reinforcements.</item>
    /// </list>
    ///
    /// <para>Both targets take an <c>out</c> parameter, so the overloads are resolved by
    /// <see cref="AccessTools.Method(Type, string, Type[])"/> with the byref type at runtime:
    /// a byref type is not a legal attribute argument, and a name-only patch would be
    /// ambiguous.</para>
    /// </summary>
    internal static class MissionPositionPriorityQuery
    {
        internal static bool TryGetDirective(
            TheaterPriorityService service, FactionHQ hq, out PriorityDirective directive)
        {
            directive = default;
            return service != null && hq != null && hq.faction != null &&
                   service.TryGetDirective(hq.faction.factionName, out directive);
        }
    }

    [HarmonyPatch]
    internal static class MissionPositionAdvancePriorityPatch
    {
        [HarmonyTargetMethod]
        private static MethodBase Target() => ResolveOrThrow(
            new[] { typeof(Unit), typeof(GlobalPosition).MakeByRefType() });

        [HarmonyPostfix]
        private static void Postfix(Unit unit, ref GlobalPosition destination, ref bool __result)
        {
            TheaterPriorityService service = TheaterPriorityService.Active;
            if (unit == null ||
                !MissionPositionPriorityQuery.TryGetDirective(service, unit.NetworkHQ, out PriorityDirective directive))
                return;
            if (unit is Aircraft aircraft &&
                (aircraft.Player != null || WingLink.IsWingMember(aircraft.persistentID.GetHashCode())))
                return;
            if (unit is GroundVehicle && GroundFrontService.Active?.HasRtsCommander() == true)
                return;

            if (unit is GroundVehicle vehicle &&
                GroundFrontService.Active != null &&
                GroundFrontService.Active.TryGetDestination(vehicle, directive, out GlobalPosition frontDestination))
            {
                destination = frontDestination;
                __result = true;
                return;
            }

            destination = new GlobalPosition(directive.X, directive.Y, directive.Z);
            __result = true;
        }

        private static MethodBase ResolveOrThrow(Type[] parameters)
        {
            MethodInfo target = AccessTools.Method(typeof(MissionPosition), "TryGetClosestPosition", parameters);
            if (target == null)
                throw new MissingMethodException(
                    "MissionPosition.TryGetClosestPosition(Unit, out GlobalPosition)");
            return target;
        }
    }

    [HarmonyPatch]
    internal static class MissionPositionDeliveryPriorityPatch
    {
        [HarmonyTargetMethod]
        private static MethodBase Target() => ResolveOrThrow(
            new[] { typeof(FactionHQ), typeof(Transform), typeof(float).MakeByRefType() });

        [HarmonyPostfix]
        private static void Postfix(
            FactionHQ factionHQ, Transform transform, ref float distance, ref bool __result)
        {
            TheaterPriorityService service = TheaterPriorityService.Active;
            if (transform == null ||
                !MissionPositionPriorityQuery.TryGetDirective(service, factionHQ, out PriorityDirective directive))
                return;

            distance = FastMath.Distance(
                new GlobalPosition(directive.X, directive.Y, directive.Z), transform.GlobalPosition());
            __result = true;
        }

        private static MethodBase ResolveOrThrow(Type[] parameters)
        {
            MethodInfo target = AccessTools.Method(typeof(MissionPosition), "TryGetClosestDistance", parameters);
            if (target == null)
                throw new MissingMethodException(
                    "MissionPosition.TryGetClosestDistance(FactionHQ, Transform, out float)");
            return target;
        }
    }
}
