using System;
using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Adds a MountedTroops station to MC-260 Chimera cargo and mission bays, not helicopters.
    /// </summary>
    internal static class ChimeraInfantryLoadoutAdapter
    {
        private static WeaponMount cachedChimeraTroopsMount;
        private static GameObject cachedTroopsPrefab;

        public static bool IsChimera(Aircraft aircraft)
        {
            if (aircraft == null) return false;
            AircraftDefinition def = aircraft.definition as AircraftDefinition;
            string name = ((def != null ? (def.unitName ?? def.jsonKey ?? "") : "") + " " + (aircraft.name ?? "")).ToLowerInvariant();
            return name.Contains("chimera") || name.Contains("mc260") || name.Contains("mc-260") || name.Contains("aryx");
        }

        public static bool IsHelicopter(Aircraft aircraft)
        {
            if (aircraft == null) return false;
            AircraftDefinition def = aircraft.definition as AircraftDefinition;
            string name = ((def != null ? (def.unitName ?? def.jsonKey ?? "") : "") + " " + (aircraft.name ?? "")).ToLowerInvariant();
            return (def != null && def.CanSlingLoad) || name.Contains("ibis") || name.Contains("helo") || name.Contains("utilityhelo");
        }

        public static GameObject GetOrCreateTroopsPrefab(WeaponMount sourceMount)
        {
            if (cachedTroopsPrefab != null) return cachedTroopsPrefab;

            GameObject go = new GameObject("Chimera_Paratroopers_Prefab");
            GameObject.DontDestroyOnLoad(go);

            MountedTroops mt = go.AddComponent<MountedTroops>();
            mt.ammo = 16;
            mt.Rearmable = true;

            if (sourceMount != null && sourceMount.info != null)
            {
                WeaponInfo info = ScriptableObject.Instantiate(sourceMount.info);
                info.name = "Paratroopers_WeaponInfo";
                info.weaponName = "Airborne Paratroopers";
                info.shortName = "Troops";
                info.description = "Airborne paratrooper company deployed via static-line combat parachutes from the rear cargo ramp.";
                info.weaponIcon = sourceMount.info.weaponIcon;
                info.troops = true;
                info.cargo = false; // Enabled as standard selectable weapon station
                info.costPerRound = 0.1f;
                info.massPerRound = 0.12f;
                mt.info = info;
            }

            go.SetActive(false);
            return cachedTroopsPrefab = go;
        }

        public static WeaponMount GetOrCreateChimeraTroopsMount()
        {
            if (cachedChimeraTroopsMount != null) return cachedChimeraTroopsMount;

            WeaponMount sourceTroopsMount = null;

            // 1. Search all loaded WeaponMount objects
            WeaponMount[] allMounts = Resources.FindObjectsOfTypeAll<WeaponMount>();
            for (int i = 0; i < allMounts.Length; i++)
            {
                WeaponMount wm = allMounts[i];
                if (wm == null) continue;
                if (string.Equals(wm.name, "Troopsx8_UtilityHelo1_F", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(wm.jsonKey, "Troopsx8_UtilityHelo1_F", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(wm.name, "Troopsx8_UtilityHelo1_R", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(wm.jsonKey, "Troopsx8_UtilityHelo1_R", StringComparison.OrdinalIgnoreCase))
                {
                    sourceTroopsMount = wm;
                    break;
                }
                if (wm.Troops || (wm.info != null && wm.info.troops))
                {
                    sourceTroopsMount = wm;
                }
            }

            // 2. Fallback: search in UtilityHelo1's hardpoint sets
            if (sourceTroopsMount == null && Encyclopedia.i != null && Encyclopedia.i.aircraft != null)
            {
                for (int a = 0; a < Encyclopedia.i.aircraft.Count; a++)
                {
                    AircraftDefinition adef = Encyclopedia.i.aircraft[a];
                    if (adef != null && adef.unitPrefab != null)
                    {
                        WeaponManager wm = adef.unitPrefab.GetComponentInChildren<WeaponManager>();
                        if (wm != null && wm.hardpointSets != null)
                        {
                            for (int s = 0; s < wm.hardpointSets.Length; s++)
                            {
                                HardpointSet hs = wm.hardpointSets[s];
                                if (hs != null && hs.weaponOptions != null)
                                {
                                    for (int o = 0; o < hs.weaponOptions.Count; o++)
                                    {
                                        WeaponMount opt = hs.weaponOptions[o];
                                        if (opt != null && (opt.Troops || (opt.info != null && opt.info.troops)))
                                        {
                                            sourceTroopsMount = opt;
                                            break;
                                        }
                                    }
                                }
                                if (sourceTroopsMount != null) break;
                            }
                        }
                    }
                    if (sourceTroopsMount != null) break;
                }
            }

            if (sourceTroopsMount == null)
            {
                Plugin.Logger.LogWarning("[Chimera Loadout] Could not locate source Troops mount for clone.");
                return null;
            }

            // Clone mount with heavy paratrooper capacity and functional prefab
            cachedChimeraTroopsMount = ScriptableObject.Instantiate(sourceTroopsMount);
            cachedChimeraTroopsMount.name = "Troopsx16_Chimera";
            cachedChimeraTroopsMount.jsonKey = "Troopsx16_Chimera";
            cachedChimeraTroopsMount.mountName = "Paratroopers (x16)";
            cachedChimeraTroopsMount.ammo = 16;
            cachedChimeraTroopsMount.emptyCost = 1.8f;
            cachedChimeraTroopsMount.mass = 2.4f;
            cachedChimeraTroopsMount.emptyMass = 0.4f;
            cachedChimeraTroopsMount.Troops = true;
            cachedChimeraTroopsMount.Cargo = false; // Selectable in cockpit weapon switch

            // Assign functional prefab with MountedTroops component
            cachedChimeraTroopsMount.prefab = GetOrCreateTroopsPrefab(sourceTroopsMount);

            if (sourceTroopsMount.info != null)
            {
                WeaponInfo info = ScriptableObject.Instantiate(sourceTroopsMount.info);
                info.name = "Troopsx16_Chimera_info";
                info.weaponName = "Airborne Paratroopers";
                info.shortName = "Troops";
                info.description = "Airborne infantry company equipped with static-line combat parachutes. Drops out the rear cargo hold ramp over hostile or friendly territory to capture strategic urban buildings or establish fortified combat encampments.";
                info.weaponIcon = sourceTroopsMount.info.weaponIcon;
                info.troops = true;
                info.cargo = false;
                info.costPerRound = 0.1f;
                info.massPerRound = 0.12f;
                cachedChimeraTroopsMount.info = info;
            }

            if (Encyclopedia.i != null && Encyclopedia.i.weaponMounts != null && !Encyclopedia.i.weaponMounts.Contains(cachedChimeraTroopsMount))
                Encyclopedia.i.weaponMounts.Add(cachedChimeraTroopsMount);
            if (Encyclopedia.WeaponLookup != null && !Encyclopedia.WeaponLookup.ContainsKey(cachedChimeraTroopsMount.jsonKey))
                Encyclopedia.WeaponLookup[cachedChimeraTroopsMount.jsonKey] = cachedChimeraTroopsMount;

            Plugin.Logger.LogInfo("[Chimera Loadout] Successfully generated Paratrooper Troops mount for MC-260 Chimera using Ibis troops icon.");
            return cachedChimeraTroopsMount;
        }

        public static void InjectIntoAircraft(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.weaponManager == null || aircraft.weaponManager.hardpointSets == null)
                return;

            // Strictly disallow Paratroopers on helicopters and non-Chimera!
            if (IsHelicopter(aircraft) || !IsChimera(aircraft))
                return;

            WeaponMount troops = GetOrCreateChimeraTroopsMount();
            if (troops == null) return;

            for (int i = 0; i < aircraft.weaponManager.hardpointSets.Length; i++)
            {
                HardpointSet set = aircraft.weaponManager.hardpointSets[i];
                if (set == null || string.IsNullOrEmpty(set.name)) continue;

                if (set.name.IndexOf("Cargo Bay", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    set.name.IndexOf("Mission Bay", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (set.weaponOptions != null && !set.weaponOptions.Contains(troops))
                    {
                        set.weaponOptions.Add(troops);
                        Plugin.Logger.LogInfo($"[Chimera Loadout] Injected Paratroopers into '{set.name}'.");
                    }
                }
            }
        }
    }
}