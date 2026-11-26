using System;
using System.Reflection;
using BoscaliSummer.Core.Diagnostics;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    internal sealed class MegaplaneBatteryMount : MonoBehaviour
    {
        private static readonly AccessTools.FieldRef<Turret, bool> Stowed = AccessTools.FieldRefAccess<Turret, bool>("stowed");
        private static readonly AccessTools.FieldRef<Turret, bool> Manual = AccessTools.FieldRefAccess<Turret, bool>("manual");
        [SerializeField] private AeroPart part;
        [SerializeField] private Turret turret;

        internal Turret Turret => turret;
        internal bool Available => turret != null && part != null && part.hitPoints > 0f && !part.IsDetached() && !Stowed(turret);

        internal void Bind(AeroPart owner, Turret mount)
        {
            part = owner;
            turret = mount;
        }

        internal bool AimsAt(Unit target) => Manual(turret) || (target != null && turret.GetTarget() == target);
    }

    [HarmonyPatch]
    internal static class MegaplaneBatteryFirePatch
    {
        private static readonly AccessTools.FieldRef<Gun, Transform[]> Muzzles = AccessTools.FieldRefAccess<Gun, Transform[]>("muzzles");
        private static readonly RaycastHit[] Hits = new RaycastHit[32];

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Gun), nameof(Gun.Fire));
        private static bool Prepare() => TargetMethod() != null;

        private static bool Prefix(Gun __instance, Unit firingUnit, Unit target, WeaponStation weaponStation)
        {
            if (!MegaplaneModule.Is(firingUnit)) return true;
            try
            {
                MegaplaneBatteryMount mount = __instance.GetComponentInParent<MegaplaneBatteryMount>();
                if (mount == null) return true;
                if (firingUnit.disabled || !mount.Available) return false;
                // Native remote continuous-fire replays carry no target. The turret aim is synchronized separately.
                if (weaponStation.TurretCount() > 1 && (!mount.Turret.IsOnTarget() || firingUnit.LocalSim && !mount.AimsAt(target))) return false;
                foreach (Transform muzzle in Muzzles(__instance))
                {
                    if (muzzle == null) continue;
                    int hitCount = Physics.RaycastNonAlloc(muzzle.position, muzzle.forward, Hits, 230f,
                        ~(int)PhysicsLayers.ExclusionZonesMask, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < hitCount; i++)
                    {
                        MegaplaneHitbox hull = Hits[i].collider.GetComponent<MegaplaneHitbox>();
                        if (hull != null && hull.Owner != null && hull.Owner.parentUnit == firingUnit) return false;
                    }
                }
                return true;
            }
            catch (Exception e) { PatchGuard.Report("Megaplane.BatteryGunFire", e); return false; }
        }
    }

    [HarmonyPatch]
    internal static class MegaplaneBatteryAimPatch
    {
        private static readonly AccessTools.FieldRef<Turret, AimSolver> Solver = AccessTools.FieldRefAccess<Turret, AimSolver>("aimSolver");
        private static readonly AccessTools.FieldRef<Turret, Transform> Elevation = AccessTools.FieldRefAccess<Turret, Transform>("elevationTransform");

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Turret), nameof(Turret.SetTarget));
        private static bool Prepare() => TargetMethod() != null;

        private static void Postfix(Turret __instance)
        {
            Unit owner = __instance.GetAttachedUnit();
            if (!MegaplaneModule.Is(owner) || owner.disabled) return;
            try
            {
                if (__instance.GetComponentInParent<MegaplaneBatteryMount>() == null) return;
                // Native SetTarget uses Weapons[0] as the origin, which in a battery belongs to a different mount.
                Solver(__instance).SetTarget(owner, __instance.GetTarget(), Elevation(__instance), __instance.GetWeaponStation().WeaponInfo);
            }
            catch (Exception e) { PatchGuard.Report("Megaplane.BatteryAim", e); }
        }
    }

    internal static class MegaplaneSiloBattery
    {
        internal static readonly AccessTools.FieldRef<WeaponStation, int> NextCell = AccessTools.FieldRefAccess<WeaponStation, int>("weaponIndex");
        private static readonly AccessTools.FieldRef<Weapon, float> LastLaunch = AccessTools.FieldRefAccess<Weapon, float>("lastFired");
        private static readonly AccessTools.FieldRef<MissileLauncher, float> LaunchInterval = AccessTools.FieldRefAccess<MissileLauncher, float>("fireInterval");

        internal static bool IsSilo(WeaponStation station) => station != null && station.Weapons.Count > 0 && station.Weapons[0] is MissileLauncher;

        internal static bool CanLaunch(Aircraft aircraft, Unit target, WeaponStation station, bool replay)
        {
            if (aircraft.disabled || target == null || target.disabled || target.NetworkHQ == aircraft.NetworkHQ || station.SafetyIsOn(aircraft)) return false;
            if (replay) return true;
            float distance = Vector3.Distance(target.transform.position, aircraft.transform.position);
            TargetRequirements range = station.WeaponInfo.targetRequirements;
            return distance >= range.minRange && distance <= range.maxRange && station.Ready();
        }

        internal static bool CanUse(Aircraft aircraft, Unit target, WeaponStation station, MissileLauncher launcher)
        {
            if (launcher.ammo <= 0 || Time.timeSinceLevelLoad - LastLaunch(launcher) < LaunchInterval(launcher)) return false;
            MegaplaneBatteryMount mount = launcher.GetComponentInParent<MegaplaneBatteryMount>();
            // A remote owner's launch command has passed native aircraft authority. A dedicated server can receive it
            // without consuming its own ClientRpc for that player's selected station.
            return mount != null && mount.Available && (!aircraft.LocalSim || mount.AimsAt(target) || aircraft.weaponManager.currentWeaponStation == station);
        }
    }

    [HarmonyPatch]
    // CYBER checks the launch before the silo dispatch handles this battery.
    [HarmonyAfter("com.marci.boscalisummer.module.support")]
    internal static class MegaplaneSiloSelectPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(WeaponStation), nameof(WeaponStation.LaunchMount));
        private static bool Prepare() => TargetMethod() != null;

        private static bool Prefix(WeaponStation __instance, Unit owner, Unit target, GlobalPosition aimpoint, bool __runOriginal)
        {
            if (!(owner is Aircraft aircraft) || !MegaplaneModule.Is(aircraft) || !MegaplaneSiloBattery.IsSilo(__instance)) return true;
            if (!__runOriginal) return false;
            try
            {
                bool replay = !aircraft.IsServer && !aircraft.LocalSim;
                if (!MegaplaneSiloBattery.CanLaunch(aircraft, target, __instance, replay)) return false;
                int count = __instance.Weapons.Count;
                int next = Mathf.Max(MegaplaneSiloBattery.NextCell(__instance), 0) % count;
                for (int step = 0; step < count; step++)
                {
                    int index = (next + step) % count;
                    if (!(__instance.Weapons[index] is MissileLauncher launcher) || launcher == null ||
                        !MegaplaneSiloBattery.CanUse(aircraft, target, __instance, launcher)) continue;
                    launcher.Fire(aircraft, target, aircraft.rb.velocity, __instance, aimpoint);
                    break;
                }
            }
            catch (Exception e) { PatchGuard.Report("Megaplane.SiloSelection", e); }
            return false;
        }
    }

    [HarmonyPatch]
    internal static class MegaplaneSiloFirePatch
    {
        private static readonly AccessTools.FieldRef<MissileLauncher, Transform> LaunchTransform = AccessTools.FieldRefAccess<MissileLauncher, Transform>("launchTransform");
        private static readonly AccessTools.FieldRef<MissileLauncher, Vector3> Ejection = AccessTools.FieldRefAccess<MissileLauncher, Vector3>("ejectionVelocity");

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(MissileLauncher), nameof(MissileLauncher.Fire));
        private static bool Prepare() => TargetMethod() != null;

        private static bool Prefix(MissileLauncher __instance, Unit owner, Unit target, WeaponStation weaponStation, GlobalPosition aimpoint,
            out (int LauncherAmmo, int StationAmmo)? __state)
        {
            __state = null;
            if (!(owner is Aircraft aircraft) || !MegaplaneModule.Is(aircraft) || !MegaplaneSiloBattery.IsSilo(weaponStation)) return true;
            try
            {
                bool replay = !aircraft.IsServer && !aircraft.LocalSim;
                if (!MegaplaneSiloBattery.CanLaunch(aircraft, target, weaponStation, replay) ||
                    !MegaplaneSiloBattery.CanUse(aircraft, target, weaponStation, __instance)) return false;
                if (!aircraft.IsServer && aircraft.LocalSim)
                {
                    // The vanilla ship launcher spawns on LocalSim, which on a client is the wrong peer.
                    if (!aircraft.HasAuthority) return false;
                    aircraft.CmdLaunchMissile(weaponStation.Number, target, aimpoint);
                    weaponStation.LastFiredTime = Time.timeSinceLevelLoad;
                    return false;
                }
                __state = (__instance.ammo, weaponStation.Ammo);
                return true;
            }
            catch (Exception e) { PatchGuard.Report("Megaplane.SiloFire", e); return false; }
        }

        private static void Postfix(MissileLauncher __instance, Unit owner, Unit target, Vector3 inheritedVelocity,
            WeaponStation weaponStation, GlobalPosition aimpoint, (int LauncherAmmo, int StationAmmo)? __state)
        {
            if (!__state.HasValue || __instance.ammo >= __state.Value.LauncherAmmo || !(owner is Aircraft aircraft)) return;
            try
            {
                MegaplaneSiloBattery.NextCell(weaponStation) = (weaponStation.Weapons.IndexOf(__instance) + 1) % weaponStation.Weapons.Count;
                if (!aircraft.IsServer)
                {
                    // Cosmetic replay after the server's authoritative ammo update.
                    weaponStation.Ammo = __state.Value.StationAmmo;
                    weaponStation.Updated();
                    return;
                }
                if (!aircraft.LocalSim)
                {
                    // Native Fire has updated the selected cell and emitted effects. Remote pilots still need a host spawn.
                    Transform cell = LaunchTransform(__instance);
                    Vector3 velocity = inheritedVelocity + cell.TransformDirection(Ejection(__instance));
                    NetworkSceneSingleton<Spawner>.i.SpawnMissile(__instance.missile, cell.position, cell.rotation, velocity, target, aircraft);
                }
                aircraft.RpcLaunchMissile(weaponStation.Number, target, aimpoint);
            }
            catch (Exception e) { PatchGuard.Report("Megaplane.SiloReplication", e); }
        }
    }
}
