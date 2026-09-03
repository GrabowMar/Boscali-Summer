using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Patches
{
    [HarmonyPatch]
    internal static class SupportMissileDetonatePatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Missile), "UserCode_RpcDetonate_897349600");

        private static bool Prepare() => TargetMethod() != null;

        private static void Prefix(
            Missile __instance,
            Unit relativeUnit,
            Vector3 pos,
            bool armed)
        {
            if (__instance == null || __instance.UniqueName == null) return;

            string unique = __instance.UniqueName;
            Vector3 world = relativeUnit != null
                ? relativeUnit.transform.TransformPoint(pos)
                : pos + Datum.origin.position;

            if (unique.StartsWith("BoscaliSummer:Support:Rod:", StringComparison.Ordinal))
            {
                Visuals.KineticRodStrikeVisuals.TriggerImpact(world);
            }
            else if (unique.StartsWith("BoscaliSummer:Support:Emp:", StringComparison.Ordinal))
            {
                if (world.y < Datum.LocalSeaY + 2000f)
                    world = new Vector3(world.x, Datum.LocalSeaY + 6000f, world.z);
                Visuals.EmpVisualEffect.Trigger(world, 12000f);
                Visuals.CockpitEmpDisruption.CheckLocalDisruption(world, 12000f);
            }
        }
    }
}
