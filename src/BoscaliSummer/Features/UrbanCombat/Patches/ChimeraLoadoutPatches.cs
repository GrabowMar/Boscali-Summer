using System;
using System.Collections.Generic;
using System.Reflection;
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
                if (aircraft != null && ChimeraInfantryLoadoutAdapter.IsChimera(aircraft))
                {
                    ChimeraInfantryLoadoutAdapter.InjectIntoAircraft(aircraft);
                }
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
        private static readonly MethodInfo OrganizeMethod =
            typeof(WeaponManager).GetMethod("OrganizeWeaponStations", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static void Prefix(WeaponManager __instance)
        {
            try
            {
                if (__instance != null)
                {
                    Aircraft aircraft = __instance.GetComponent<Aircraft>();
                    if (aircraft != null && ChimeraInfantryLoadoutAdapter.IsChimera(aircraft))
                        ChimeraInfantryLoadoutAdapter.InjectIntoAircraft(aircraft);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning("[Chimera Loadout] Error in WeaponManager Prefix: " + ex);
            }
        }

        private static void Postfix(WeaponManager __instance)
        {
            try
            {
                if (__instance == null) return;
                Aircraft aircraft = __instance.GetComponent<Aircraft>();
                if (aircraft == null || !ChimeraInfantryLoadoutAdapter.IsChimera(aircraft)) return;

                // Ensure paratroopers weapon station exists on the aircraft
                if (__instance.hardpointSets != null)
                {
                    for (int i = 0; i < __instance.hardpointSets.Length; i++)
                    {
                        HardpointSet hs = __instance.hardpointSets[i];
                        if (hs == null || hs.weaponMount == null) continue;

                        if (hs.weaponMount.name.IndexOf("Troops", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            hs.weaponMount.mountName.IndexOf("Paratrooper", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            // Check if already in aircraft.weaponStations
                            bool stationFound = false;
                            if (aircraft.weaponStations != null)
                            {
                                for (int s = 0; s < aircraft.weaponStations.Count; s++)
                                {
                                    WeaponStation st = aircraft.weaponStations[s];
                                    if (st != null && st.WeaponInfo != null &&
                                        (st.WeaponInfo.troops || st.WeaponInfo.weaponName.IndexOf("Paratrooper", StringComparison.OrdinalIgnoreCase) >= 0))
                                    {
                                        stationFound = true;
                                        break;
                                    }
                                }
                            }

                            if (!stationFound && hs.hardpoints != null && hs.hardpoints.Count > 0)
                            {
                                Hardpoint hp = hs.hardpoints[0];
                                if (hp != null)
                                {
                                    GameObject mountGo = new GameObject("Chimera_MountedTroops_Instance");
                                    mountGo.transform.SetParent(hp.transform, false);
                                    MountedTroops mt = mountGo.AddComponent<MountedTroops>();
                                    mt.ammo = 16;
                                    mt.info = hs.weaponMount.info;
                                    __instance.RegisterWeapon(mt, hs.weaponMount, hp);
                                    OrganizeMethod?.Invoke(__instance, null);
                                    Plugin.Logger.LogInfo("[Chimera Loadout] Registered Paratroopers weapon station into aircraft.weaponStations.");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning("[Chimera Loadout] Error in WeaponManager Postfix: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(WeaponSelector), "PopulateOptions")]
    internal static class ChimeraWeaponSelectorPopulatePatch
    {
        private static readonly FieldInfo LoadoutAircraftField =
            typeof(LoadoutSelector).GetField("aircraft", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static void Prefix(WeaponSelector __instance, HardpointSet hardpointSet)
        {
            try
            {
                if (hardpointSet == null || hardpointSet.weaponOptions == null) return;

                WeaponMount troopsMount = ChimeraInfantryLoadoutAdapter.GetOrCreateChimeraTroopsMount();

                // Primary: resolve aircraft from the parent LoadoutSelector (always populated in the hangar UI)
                Aircraft ac = null;
                LoadoutSelector ls = __instance.GetComponentInParent<LoadoutSelector>();
                if (ls != null && LoadoutAircraftField != null)
                    ac = LoadoutAircraftField.GetValue(ls) as Aircraft;

                // Fallback: try hardpoint parent hierarchy (works in-flight/spawned context)
                if (ac == null && hardpointSet.hardpoints != null && hardpointSet.hardpoints.Count > 0 && hardpointSet.hardpoints[0] != null)
                    ac = hardpointSet.hardpoints[0].transform.GetComponentInParent<Aircraft>();

                // Only inject when confirmed Chimera — strip from everything else (Ibis, unknown, helicopters)
                bool isConfirmedChimera = ac != null && ChimeraInfantryLoadoutAdapter.IsChimera(ac) && !ChimeraInfantryLoadoutAdapter.IsHelicopter(ac);
                if (!isConfirmedChimera)
                {
                    if (troopsMount != null) hardpointSet.weaponOptions.Remove(troopsMount);
                    return;
                }

                // Only inject into Chimera cargo/mission bays
                string name = hardpointSet.name ?? "";
                if (name.IndexOf("Cargo Bay", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Mission Bay", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (troopsMount != null && !hardpointSet.weaponOptions.Contains(troopsMount))
                    {
                        hardpointSet.weaponOptions.Add(troopsMount);
                        Plugin.Logger.LogInfo($"[Chimera Loadout] PopulateOptions injected Paratroopers into '{name}'.");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning("[Chimera Loadout] Error injecting selector options: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), nameof(WeaponChecker.GetAvailableWeaponsNonAlloc))]
    internal static class ChimeraWeaponCheckerAvailablePatch
    {
        private static void Postfix(HardpointSet hardpointSet, List<WeaponMount> outAvailable)
        {
            try
            {
                if (hardpointSet != null && outAvailable != null)
                {
                    WeaponMount troops = ChimeraInfantryLoadoutAdapter.GetOrCreateChimeraTroopsMount();

                    Aircraft ac = null;
                    if (hardpointSet.hardpoints != null && hardpointSet.hardpoints.Count > 0 && hardpointSet.hardpoints[0] != null)
                    {
                        ac = hardpointSet.hardpoints[0].transform.GetComponentInParent<Aircraft>();
                    }

                    // Strictly remove from everything that isn't confirmed Chimera (helicopter, Ibis, unknown)
                    bool isConfirmedChimera = ac != null && ChimeraInfantryLoadoutAdapter.IsChimera(ac) && !ChimeraInfantryLoadoutAdapter.IsHelicopter(ac);
                    if (!isConfirmedChimera)
                    {
                        if (troops != null) outAvailable.Remove(troops);
                        return;
                    }

                    string name = hardpointSet.name ?? "";
                    if (name.IndexOf("Cargo Bay", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Mission Bay", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (troops != null && !outAvailable.Contains(troops))
                        {
                            outAvailable.Add(troops);
                            Plugin.Logger.LogInfo($"[Chimera Loadout] WeaponChecker added Paratroopers to '{name}' available list.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning("[Chimera Loadout] Error in WeaponChecker patch: " + ex);
            }
        }
    }
}
