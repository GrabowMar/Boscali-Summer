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

                Visuals.KineticRodStrikeVisuals.TriggerImpact(world, __instance.ownerID);
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
            else if (unique.StartsWith("BoscaliSummer:Support:Flare:", StringComparison.Ordinal))
            {
                // Disarm vanilla warhead to suppress conventional missile explosion
                armed = false;
                foreach (var r in __instance.GetComponentsInChildren<Renderer>())
                {
                    r.enabled = false;
                }

                float radius = 4000f;
                float duration = 15f;
                int count = 36;

                var tracker = __instance.GetComponent<Runtime.Actions.FlareMissileFlightTracker>();
                if (tracker != null)
                {
                    tracker.MarkDetonated();
                    radius = tracker.Radius;
                    duration = tracker.Duration;
                    count = tracker.FlareCount;
                }

                Vector3 burstPos = world;
                if (burstPos.y < Datum.LocalSeaY + 5f)
                {
                    burstPos = new Vector3(burstPos.x, Datum.LocalSeaY + 12f, burstPos.z);
                }

                Visuals.FlareMissileBurstVisuals.TriggerBarrage(burstPos, radius, duration, count);
            }
        }
    }
}
