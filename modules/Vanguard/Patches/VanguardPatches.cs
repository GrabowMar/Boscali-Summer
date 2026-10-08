using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Presentation;
using BoscaliSummer.Modules.Vanguard.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Modules.Vanguard.Patches
{
    // StartMissile runs on every peer; seeker.Initialize is called only by the simulating peer's LocalStart.
    [HarmonyPatch(typeof(Missile), "StartMissile")]
    internal static class VanguardArticulationPatch
    {
        private static void Postfix(Missile __instance)
        {
            if (__instance == null || __instance.definition == null) return;
            GlaiveTurret.Attach(__instance); // Cache deployed wing bases before launch presentation folds them.
            WeaponArticulation.Attach(__instance);
            if (VanguardKeys.RoleOf(__instance.definition.jsonKey) == VanguardRole.Towed) TowAnchor.Capture(__instance);
        }
    }

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

    [HarmonyPatch(typeof(Missile), "ServerFixedUpdate")]
    internal static class GlaiveSuspendedPhysicsPatch
    {
        private static bool Prefix(Missile __instance)
        {
            if (!VanguardRegistry.TryGet(__instance,out VanguardFlight flight) || !flight.GunDeployed) return true;
            flight.TickGun();
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

    // SKYWELL: "firing" the kit toggles it on the owner peer. Cargo stations launch through LaunchMount, which
    // also advances the weapon index, so the whole call is skipped and the single round stays fireable.
    [HarmonyPatch(typeof(WeaponStation), nameof(WeaponStation.LaunchMount))]
    internal static class SkywellLaunchPatch
    {
        private static bool Prefix(WeaponStation __instance, Unit owner) =>
            !SkywellFire.Intercept(__instance, owner);
    }

    [HarmonyPatch(typeof(MountedCargo), nameof(MountedCargo.Fire))]
    internal static class SkywellFirePatch
    {
        private static bool Prefix(Unit owner, WeaponStation weaponStation) =>
            !SkywellFire.Intercept(weaponStation, owner);
    }

    internal static class SkywellFire
    {
        public static bool Intercept(WeaponStation station, Unit owner)
        {
            if (station?.WeaponInfo == null || !station.WeaponInfo.name.Contains(VanguardKeys.SkywellInfo)) return false;
            // AI tankers are deployed by SkywellService; their trigger is swallowed.
            if (owner is Aircraft aircraft && aircraft.LocalSim && aircraft.Player != null) Networking.SkywellNet.RequestToggle(aircraft);
            return true;
        }
    }

    // LANCE: the trigger charges the capacitor instead of firing; LanceService fires on release.
    [HarmonyPatch(typeof(Gun), nameof(Gun.Fire))]
    internal static class LanceFirePatch
    {
        private static bool Prefix(Gun __instance, WeaponStation weaponStation)
        {
            if (__instance == null || weaponStation == null || !LanceService.IsLance(__instance)) return true;
            if (!LanceAccess.Ensure()) return true; // reflection failed: vanilla behaviour
            LanceService.Hold(__instance, weaponStation);
            return false;
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
