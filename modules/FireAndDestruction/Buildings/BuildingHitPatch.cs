using System;
using System.Reflection;
using BoscaliSummer.Infrastructure.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Feeds the client-local hit ledger from every frag-trace shockwave on a building.
    /// Vanilla's TakeShockwave body is empty; the call itself is the per-hit notification.
    /// </summary>
    [HarmonyPatch]
    internal static class BuildingHitPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(MapBuilding), "TakeShockwave");
        private static bool Prepare() => TargetMethod() != null;

        private static void Postfix(MapBuilding __instance, Vector3 origin, float blastPower)
        {
            // One bad raycast must not break the detonation that carries it.
            try
            {
                BuildingHitLedger.Instance?.SubmitShockwave(__instance, origin, blastPower);
            }
            catch (Exception e)
            {
                PatchGuard.Report("Fire.BuildingHit", e);
            }
        }
    }
}
