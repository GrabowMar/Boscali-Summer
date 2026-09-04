using System;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    [HarmonyPatch(typeof(MountedTroops), nameof(MountedTroops.Fire))]
    internal static class MountedTroopsFirePatch
    {
        private static void Postfix(
            MountedTroops __instance,
            Unit owner,
            Unit target,
            Vector3 inheritedVelocity,
            WeaponStation weaponStation,
            GlobalPosition aimpoint)
        {
            if (__instance == null) return;
            Aircraft aircraft = owner as Aircraft ?? __instance.GetComponentInParent<Aircraft>();
            if (aircraft != null && (aircraft.LocalSim || aircraft.IsLocalPlayer))
            {
                AirAssaultController.Instance?.DeployFromWeaponStation(aircraft, __instance, inheritedVelocity);
            }
        }
    }
}
