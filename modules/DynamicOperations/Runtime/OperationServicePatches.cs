using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Features.DynamicOperations.Runtime
{
    [HarmonyPatch(typeof(PilotDismounted), nameof(PilotDismounted.Capture))]
    internal static class OperationRescuePatch
    {
        private static void Prefix(PilotDismounted __instance, out bool __state) =>
            __state = __instance.IsServer && !__instance.disabled;

        private static void Postfix(PilotDismounted __instance, Unit capturingUnit, bool __state)
        {
            if (__state) OperationsManager.Active?.ObserveRescue(__instance, capturingUnit as Aircraft);
        }
    }

    [HarmonyPatch(typeof(Building), nameof(Building.Repair))]
    internal static class OperationRepairPatch
    {
        private static void Prefix(Building __instance, out bool __state) =>
            __state = __instance.IsServer && __instance.NeedsRepair();

        private static void Postfix(Building __instance, Unit repairer, bool __state)
        {
            if (__state && !__instance.NeedsRepair() && !__instance.disabled)
                OperationsManager.Active?.ObserveService(repairer, __instance, true);
        }
    }

    [HarmonyPatch(typeof(Rearmer), nameof(Rearmer.ProcessRearmRequest))]
    internal static class OperationSupplyPatch
    {
        private static void Postfix(Rearmer __instance, Unit unitToRearm, bool __result)
        {
            if (__result) OperationsManager.Active?.ObserveService(__instance.Unit, unitToRearm, false);
        }
    }

    [HarmonyPatch(typeof(Rearmer), nameof(Rearmer.RefillOtherRearmer))]
    internal static class OperationSupplyTransferPatch
    {
        private static void Prefix(Rearmer toRearmer, out float __state) =>
            __state = toRearmer != null ? toRearmer.Capacity : float.NaN;

        private static void Postfix(Rearmer __instance, Rearmer toRearmer, float __state)
        {
            if (toRearmer != null && !float.IsNaN(__state) && !float.IsInfinity(__state) &&
                toRearmer.Capacity > __state && Mathf.Abs(toRearmer.Capacity) < float.MaxValue)
                OperationsManager.Active?.ObserveService(__instance.Unit, toRearmer.Unit, false);
        }
    }
}
