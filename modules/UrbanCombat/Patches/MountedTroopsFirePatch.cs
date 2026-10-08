using HarmonyLib;

namespace BoscaliSummer.Garrisons
{
    // Vanilla replays Fire on the owner, the server and observers (MountedTroops.Fire ->
    // Cmd/RpcLaunchMissile -> WeaponStation.LaunchMount) with the same target and aimpoint, so the
    // HALO glide plan built from them matches on every peer; the controller sorts out who does what.
    [HarmonyPatch(typeof(MountedTroops), nameof(MountedTroops.Fire))]
    internal static class MountedTroopsFirePatch
    {
        private static void Postfix(MountedTroops __instance, Unit owner, Unit target, WeaponStation weaponStation, GlobalPosition aimpoint)
        {
            AirAssaultController controller = AirAssaultController.Instance;
            if (__instance == null || controller == null) return;
            Aircraft aircraft = owner as Aircraft;
            if (aircraft == null) aircraft = __instance.GetComponentInParent<Aircraft>();
            if (aircraft != null)
                controller.DeployFromWeaponStation(aircraft, __instance, weaponStation, target, aimpoint);
        }
    }
}
