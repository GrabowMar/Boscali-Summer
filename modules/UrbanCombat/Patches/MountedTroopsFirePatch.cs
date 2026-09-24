using System;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    // Vanilla replays Fire on the owner, the server and observers (MountedTroops.Fire ->
    // Cmd/RpcLaunchMissile -> WeaponStation.LaunchMount); the controller sorts out who does what.
    [HarmonyPatch(typeof(MountedTroops), nameof(MountedTroops.Fire))]
    internal static class MountedTroopsFirePatch
    {
        private static void Postfix(MountedTroops __instance, Unit owner, WeaponStation weaponStation)
        {
            AirAssaultController controller = AirAssaultController.Instance;
            if (__instance == null || controller == null) return;
            Aircraft aircraft = owner as Aircraft;
            if (aircraft == null) aircraft = __instance.GetComponentInParent<Aircraft>();
            if (aircraft != null)
                controller.DeployFromWeaponStation(aircraft, __instance, weaponStation);
        }
    }
}
