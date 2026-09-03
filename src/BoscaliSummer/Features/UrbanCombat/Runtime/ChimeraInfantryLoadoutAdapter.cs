using System;
using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Injects Infantry / Paratrooper troop mounts into the MC-260 Chimera's
    /// cargo and mission bays (Cargo Bay Rear, Cargo Bay Front, Mission Bay)
    /// using the exact same icon as the UH-90 Ibis.
    /// </summary>
    internal static class ChimeraInfantryLoadoutAdapter
    {
        private static WeaponMount cachedChimeraTroopsMount;

        public static WeaponMount GetOrCreateChimeraTroopsMount()
        {
            if (cachedChimeraTroopsMount != null) return cachedChimeraTroopsMount;
            if (Encyclopedia.i == null || Encyclopedia.i.weaponMounts == null) return null;

            WeaponMount sourceTroopsMount = null;
            for (int i = 0; i < Encyclopedia.i.weaponMounts.Count; i++)
            {
                WeaponMount wm = Encyclopedia.i.weaponMounts[i];
                if (wm == null) continue;
                if (string.Equals(wm.jsonKey, "Troopsx8_UtilityHelo1_F", StringComparison.OrdinalIgnoreCase) ||
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

            if (sourceTroopsMount == null) return null;

            cachedChimeraTroopsMount = ScriptableObject.Instantiate(sourceTroopsMount);
            cachedChimeraTroopsMount.name = "Troopsx16_Chimera";
            cachedChimeraTroopsMount.jsonKey = "Troopsx16_Chimera";
            cachedChimeraTroopsMount.mountName = "Paratroopers (x16)";
            cachedChimeraTroopsMount.ammo = 16;
            cachedChimeraTroopsMount.emptyCost = 1.8f;
            cachedChimeraTroopsMount.mass = 2.4f;
            cachedChimeraTroopsMount.emptyMass = 0.4f;
            cachedChimeraTroopsMount.Troops = true;
            cachedChimeraTroopsMount.Cargo = true;

            if (sourceTroopsMount.info != null)
            {
                WeaponInfo info = ScriptableObject.Instantiate(sourceTroopsMount.info);
                info.name = "Troopsx16_Chimera_info";
                info.weaponName = "Airborne Paratroopers";
                info.shortName = "Troops";
                info.description = "Airborne infantry company equipped with static-line combat parachutes. Drops over hostile or friendly territory to capture strategic urban buildings or establish fortified combat encampments.";
                info.weaponIcon = sourceTroopsMount.info.weaponIcon; // EXACT SAME ICON AS IBIS!
                info.troops = true;
                info.cargo = true;
                info.costPerRound = 0.1f;
                info.massPerRound = 0.12f;
                cachedChimeraTroopsMount.info = info;
            }

            if (!Encyclopedia.i.weaponMounts.Contains(cachedChimeraTroopsMount))
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

            AircraftDefinition def = aircraft.definition as AircraftDefinition;
            string name = def != null ? (def.unitName ?? def.jsonKey ?? "") : aircraft.name ?? "";

            bool isChimera = name.IndexOf("chimera", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             name.IndexOf("mc260", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             name.IndexOf("mc-260", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             (def != null && def.jsonKey != null && def.jsonKey.IndexOf("chimera", StringComparison.OrdinalIgnoreCase) >= 0);

            if (!isChimera) return;

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