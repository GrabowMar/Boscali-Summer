using HarmonyLib;

namespace BoscaliSummer.Modules.DynamicOperations.Runtime
{
    [HarmonyPatch(typeof(Unit), nameof(Unit.Jam))]
    internal static class OperationJamPatch
    {
        private static void Postfix(Unit __instance, Unit.JamEventArgs args) =>
            OperationsManager.Active?.ObserveJam(__instance, args);
    }
}
