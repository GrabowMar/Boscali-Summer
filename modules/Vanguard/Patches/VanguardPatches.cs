using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Modules.Vanguard.Patches
{
    // Every Vanguard missile is a CruiseMissile1 clone, so one seeker type carries all five behaviours.
    [HarmonyPatch(typeof(OpticalSeekerCruiseMissile), nameof(OpticalSeekerCruiseMissile.Initialize))]
    internal static class VanguardInitializePatch
    {
        private static void Postfix(Missile ___missile, Unit target, GlobalPosition aimpoint)
        {
            if (___missile == null || !___missile.LocalSim || ___missile.definition == null) return;
            VanguardRole role = VanguardKeys.RoleOf(___missile.definition.jsonKey);
            if (role == VanguardRole.None) return;
            if (role == VanguardRole.Towed) VanguardRegistry.CutTowed(___missile.owner);
            int slot = role == VanguardRole.Drone ? VanguardRegistry.NextDroneSlot(___missile.owner) : 0;
            VanguardRegistry.Add(___missile, new VanguardFlight(___missile, role, target, aimpoint, slot));
        }
    }

    [HarmonyPatch(typeof(OpticalSeekerCruiseMissile), nameof(OpticalSeekerCruiseMissile.Seek))]
    internal static class VanguardSeekPatch
    {
        private static bool Prefix(Missile ___missile)
        {
            if (___missile == null || !VanguardRegistry.TryGet(___missile, out VanguardFlight flight)) return true;
            flight.Tick();
            return false;
        }
    }

    [HarmonyPatch(typeof(OpticalSeekerCruiseMissile), "SlowChecks")]
    internal static class VanguardSlowChecksPatch
    {
        private static bool Prefix(Missile ___missile)
        {
            if (___missile == null || !VanguardRegistry.TryGet(___missile, out VanguardFlight flight)) return true;
            flight.SlowCheck();
            return false;
        }
    }

    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.LockedByMissile))]
    internal static class AegisLockPatch
    {
        private static void Postfix(Aircraft __instance)
        {
            if (__instance.IsServer) AegisService.Watch(__instance);
        }
    }

    // ORCA underwater: skip vanilla water drag/detonation and run the torpedo instead (server-side flights only).
    [HarmonyPatch(typeof(Missile), "DetectCollisions")]
    internal static class TorpedoCollisionsPatch
    {
        private static bool Prefix(Missile __instance)
        {
            if (__instance == null || !VanguardRegistry.TryGet(__instance, out VanguardFlight flight) ||
                flight.Role != VanguardRole.Torpedo) return true;
            return !flight.SwimTick();
        }
    }
}
