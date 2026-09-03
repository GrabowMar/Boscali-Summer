using System;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    [HarmonyPatch(typeof(LoadoutSelector), nameof(LoadoutSelector.AssignAircraft))]
    internal static class ChimeraLoadoutAssignAircraftPatch
    {
        private static void Prefix(Aircraft aircraft)
        {
            try
            {
                ChimeraInfantryLoadoutAdapter.InjectIntoAircraft(aircraft);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning("[Chimera Loadout] Error injecting loadout options: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.InitializeWeaponManager))]
    internal static class ChimeraWeaponManagerInitPatch
    {
        private static void Postfix(WeaponManager __instance)
        {
            try
            {
                if (__instance != null)
                {
                    Aircraft aircraft = __instance.GetComponent<Aircraft>();
                    if (aircraft != null)
                        ChimeraInfantryLoadoutAdapter.InjectIntoAircraft(aircraft);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning("[Chimera Loadout] Error injecting weapon manager options: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(WeaponSelector), "PopulateOptions")]
    internal static class ChimeraWeaponSelectorPopulatePatch
    {
        private static void Prefix(WeaponSelector __instance, HardpointSet hardpointSet)
        {
            try
            {
                if (hardpointSet != null && hardpointSet.weaponOptions != null)
                {
                    string name = hardpointSet.name ?? "";
                    if (name.IndexOf("Cargo Bay", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Mission Bay", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        WeaponMount troops = ChimeraInfantryLoadoutAdapter.GetOrCreateChimeraTroopsMount();
                        if (troops != null && !hardpointSet.weaponOptions.Contains(troops))
                        {
                            hardpointSet.weaponOptions.Add(troops);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning("[Chimera Loadout] Error injecting selector options: " + ex);
            }
        }
    }
}