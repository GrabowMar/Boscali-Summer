using System;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Hooks into MountedTroops weapon firing so pressing the weapon trigger while Troops are selected
    /// on an Ibis or transport directly deploys the rope rappelling infantry assault!
    /// </summary>
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
            if (owner is Aircraft aircraft && (aircraft.LocalSim || aircraft.IsLocalPlayer))
            {
                AirAssaultController.Instance?.TriggerAirAssault(aircraft, __instance);
            }
        }
    }
}