using System;
using System.Reflection;
using BoscaliSummer.Infrastructure.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    [HarmonyPatch(typeof(BulletSim.Bullet), "TrajectoryTrace")]
    internal static class BulletImpactPatch
    {
        private static void Prefix(bool ___impacted, out bool __state) => __state = ___impacted;

        private static void Postfix(
            bool __state,
            bool ___impacted,
            GlobalPosition ___position,
            WeaponInfo info,
            bool visualOnly)
        {
            // Runs on every tracer's trajectory trace: a fault here must not stop the bullet
            // simulation, only skip the ignition for this one round.
            try
            {
                if (!__state && ___impacted && info != null && !visualOnly)
                {
                    int salt = Mathf.RoundToInt(info.muzzleVelocity) ^ Mathf.RoundToInt(info.pierceDamage * 0.1f);
                    ImpactFireManager.Instance?.SubmitImpact(
                        ___position, false, salt);
                }
            }
            catch (Exception e)
            {
                PatchGuard.Report("Fire.BulletImpact", e);
            }
        }
    }

    [HarmonyPatch]
    internal static class MissileImpactPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Missile), "UserCode_RpcDetonate_897349600");

        private static bool Prepare() => TargetMethod() != null;

        private static void Prefix(
            Unit relativeUnit,
            Vector3 pos,
            bool armed,
            float ___blastYield)
        {
            if (!armed) return;
            // Runs on every client for every missile detonation; a fault must not swallow the
            // detonation RPC, only the fire and scorch this mod adds on top of it.
            try
            {
                Vector3 world = relativeUnit != null
                    ? relativeUnit.transform.TransformPoint(pos)
                    : pos + Datum.origin.position;
                GlobalPosition worldPosition = world.ToGlobalPosition();
                ImpactFireManager.Instance?.SubmitImpact(
                    worldPosition, true, Mathf.RoundToInt(___blastYield));
                // Local cosmetic only, so it does not go through SubmitImpact: that early-returns
                // on !IsServer() and on FiresEnabled, whereas this patch runs on every client and
                // a scorch decal needs no authority.
                ImpactScorchManager.Instance?.SubmitExplosion(worldPosition, ___blastYield);
            }
            catch (Exception e)
            {
                PatchGuard.Report("Fire.MissileImpact", e);
            }
        }
    }

    // GroundVehicle does not expose a separate explosion callback. UnitDisabled is
    // invoked by the authoritative damage system immediately before wreck spawning,
    // which makes the false -> true transition the least invasive destruction hook.
    [HarmonyPatch(typeof(GroundVehicle), nameof(GroundVehicle.UnitDisabled))]
    internal static class GroundVehicleDestructionPatch
    {
        private static void Postfix(GroundVehicle __instance, bool oldState, bool newState)
        {
            try
            {
                if (!oldState && newState && __instance != null)
                    ImpactFireManager.Instance?.SubmitVehicleExplosion(
                        __instance.transform.position.ToGlobalPosition(), __instance.GetInstanceID());
            }
            catch (Exception e)
            {
                PatchGuard.Report("Fire.GroundVehicleDestruction", e);
            }
        }
    }
}
