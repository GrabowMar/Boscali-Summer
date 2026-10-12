using System;
using BoscaliSummer.Modules.TheaterOps.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Modules.TheaterOps.Patches
{
    [HarmonyPatch(typeof(GroundVehicle), nameof(GroundVehicle.MoveFromDepot))]
    internal static class GroundFrontDepotPatch
    {
        [HarmonyPostfix]
        private static void Postfix(GroundVehicle __instance)
        {
            // An exception here would surface in vanilla's depot delivery, after the original has run.
            try { GroundFrontService.Active?.Enroll(__instance, true); }
            catch (Exception error) { GroundFrontService.LogFault("depot enrolment", error); }
        }
    }
}
