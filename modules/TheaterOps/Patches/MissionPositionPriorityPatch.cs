using System;
using System.Reflection;
using BoscaliSummer.Modules.TheaterOps.Domain;
using BoscaliSummer.Modules.TheaterOps.Runtime;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.TheaterOps.Patches
{
    /// <summary>
    /// Applies the host's main effort at the two places the game asks "where should this
    /// unit head next?". Both are pure queries on MissionPosition, so the override changes
    /// no state of its own and vanilla behavior resumes the moment the priority is cleared:
    ///
    /// <list type="bullet">
    /// <item>advance — a ground vehicle enrolled in a battle group (nothing else is
    /// steered; the hook only reads what GroundFrontService decided);</item>
    /// <item>delivery — the sort key FactionHQ uses to pick which depot delivers
    /// reinforcements (depots only: an airbase keeps vanilla's order).</item>
    /// </list>
    ///
    /// <para>Both targets take an <c>out</c> parameter, so the overloads are resolved by
    /// <c>AccessTools.Method</c> with the byref type at runtime:
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
            // Each enrolled group follows its own assignment. Unenrolled units and every aircraft keep vanilla's own answer.
            GroundProbe.AdvanceRan++;
            if (!(unit is GroundVehicle vehicle)) return;
            GroundProbe.AdvanceGround++;
            if (!(GroundFrontService.Active is { } front)) return;
            // The poll runs inside vanilla's per-vehicle navigation: a fault in the read must hand the answer back to vanilla, not unwind into it.
            try
            {
                if (!front.TryGetDestination(vehicle, out GlobalPosition frontDestination)) return;
                destination = frontDestination;
                __result = true;
            }
            catch (Exception error) { GroundFrontService.LogFault("destination query", error); }
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
            GroundProbe.DeliveryRan++;
            TheaterPriorityService service = TheaterPriorityService.Active;
            if (transform == null ||
                !MissionPositionPriorityQuery.TryGetDirective(service, factionHQ, out PriorityDirective directive))
                return;
            // airbasesSorted decides which field launches the faction's AI jets: funnelling every sortie through the airbase nearest the effort would
            // punish a pilot who ignores the staff. Only the depot order (where a bought wave leaves) follows the effort.
            if (transform.GetComponent<VehicleDepot>() == null) { GroundProbe.AirbaseSkips++; return; }

            distance = FastMath.Distance(
                new GlobalPosition(directive.X, directive.Y, directive.Z), transform.GlobalPosition());
            GroundProbe.DepotRewrites++;
            GroundProbe.DeliveryReplaced++;
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
