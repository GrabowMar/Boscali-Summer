using System;
using System.Reflection;
using BoscaliSummer.Infrastructure.Diagnostics;
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
            try
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

                    float altitude = Datum.LocalSeaY + Runtime.SupportEffectPolicy.EmpBurstAltitude;
                    if (world.y < altitude)
                        world = new Vector3(world.x, altitude, world.z);

                    float radius = Runtime.SupportEffectPolicy.EmpRadius(unique);
                    Visuals.EmpVisualEffect.Trigger(world, radius);
                    Visuals.CockpitEmpDisruption.CheckLocalDisruption(world, radius);
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
            catch (Exception e)
            {
                PatchGuard.Report("Support.RpcDetonate", e);
            }
        }
    }

    [HarmonyPatch(typeof(Missile), "Detonate")]
    internal static class SupportMissileAuthorityPatch
    {
        private static void Prefix(Missile __instance, out bool __state)
        {
            __state = __instance != null && !__instance.disabled &&
                BoscaliSummer.Runtime.GameAccess.IsServer() &&
                __instance.UniqueName?.StartsWith("BoscaliSummer:Support:Rod:", StringComparison.Ordinal) == true;
        }

        private static void Postfix(Missile __instance, bool __state)
        {
            // Vanilla must set Networkdisabled before damage can call Missile.Detonate again.
            if (__state && __instance != null && __instance.disabled)
                Runtime.RodBlast.Apply(__instance.transform.position, __instance.ownerID);
        }
    }

    [HarmonyPatch(typeof(Missile), "OnStartClient")]
    internal static class SupportMissileDescentPatch
    {
        private static void Postfix(Missile __instance)
        {
            if (__instance?.UniqueName?.StartsWith("BoscaliSummer:Support:Rod:", StringComparison.Ordinal) == true)
                Visuals.KineticRodStrikeVisuals.Track(__instance,
                    new Vector3(__instance.transform.position.x, Datum.LocalSeaY, __instance.transform.position.z));
        }
    }
}
