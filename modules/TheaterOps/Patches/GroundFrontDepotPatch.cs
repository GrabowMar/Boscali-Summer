using BoscaliSummer.Modules.TheaterOps.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Modules.TheaterOps.Patches
{
    [HarmonyPatch(typeof(GroundVehicle), nameof(GroundVehicle.MoveFromDepot))]
    internal static class GroundFrontDepotPatch
    {
        [HarmonyPostfix]
        private static void Postfix(GroundVehicle __instance) =>
            GroundFrontService.Active?.Enroll(__instance);
    }
}
