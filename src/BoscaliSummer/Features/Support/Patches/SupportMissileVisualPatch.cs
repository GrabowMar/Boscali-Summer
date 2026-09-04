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
            ref bool armed)
        {
            if (__instance == null || __instance.UniqueName == null) return;

            string unique = __instance.UniqueName;
            Vector3 world = relativeUnit != null
                ? relativeUnit.transform.TransformPoint(pos)
                : pos + Datum.origin.position;

            if (unique.StartsWith("BoscaliSummer:Support:Rod:", StringComparison.Ordinal))
            {
                // Align precisely to terrain ground level if statics are near
                if (Physics.Raycast(world + Vector3.up * 25f, Vector3.down, out var groundHit, 500f, PhysicsLayers.StaticsMask))
                {
                    world = groundHit.point;
                }

                // Inform descent effect that detonation has been triggered so it won't duplicate on destruction
                var descent = __instance.GetComponent<Visuals.KineticRodDescentEffect>();
                if (descent != null)
                {
                    descent.MarkDetonated();
                }

                // Disarm vanilla warhead to prevent tiny conventional bomb VFX from popping inside the rod effect
                armed = false;
                foreach (var r in __instance.GetComponentsInChildren<Renderer>())
                {
                    r.enabled = false;
                }

                Visuals.KineticRodStrikeVisuals.TriggerImpact(world);
            }
            else if (unique.StartsWith("BoscaliSummer:Support:Emp:", StringComparison.Ordinal))
            {
                // Disarm vanilla warhead to suppress conventional missile explosion
                armed = false;
                foreach (var r in __instance.GetComponentsInChildren<Renderer>())
                {
                    r.enabled = false;
                }

                if (world.y < Datum.LocalSeaY + 2000f)
                    world = new Vector3(world.x, Datum.LocalSeaY + 6000f, world.z);

                Visuals.EmpVisualEffect.Trigger(world, 12000f);
                Visuals.CockpitEmpDisruption.CheckLocalDisruption(world, 12000f);
            }
        }
    }
}
