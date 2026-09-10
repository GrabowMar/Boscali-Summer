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
        private const int ParatrooperCapacity = 16;
        private const string IbisTroopsMountForward = "Troopsx8_UtilityHelo1_F";
        private const string IbisTroopsMountReverse = "Troopsx8_UtilityHelo1_R";

        private static WeaponMount cachedChimeraTroopsMount;
        private static GameObject cachedTroopsPrefab;

        public static bool IsChimera(Aircraft aircraft)
        {
            if (aircraft == null) return false;
            AircraftDefinition def = aircraft.definition as AircraftDefinition;
            string name = ((def != null ? (def.unitName ?? def.jsonKey ?? "") : "") + " " + (aircraft.name ?? "")).ToLowerInvariant();
            return name.Contains("chimera") || name.Contains("mc260") || name.Contains("mc-260") || name.Contains("aryx") ||
                name.Contains("tarantula") || name.Contains("tarantulla");
        }

        public static bool IsHelicopter(Aircraft aircraft)
        {
            if (aircraft == null) return false;
            AircraftDefinition def = aircraft.definition as AircraftDefinition;
            string name = ((def != null ? (def.unitName ?? def.jsonKey ?? "") : "") + " " + (aircraft.name ?? "")).ToLowerInvariant();
            if (name.Contains("tarantula") || name.Contains("tarantulla"))
                return false;

            return (def != null && def.CanSlingLoad) || name.Contains("ibis") || name.Contains("helo") || name.Contains("utilityhelo");
        }

        public static GameObject GetOrCreateTroopsPrefab(WeaponMount sourceMount)
        {
            if (cachedTroopsPrefab != null) return cachedTroopsPrefab;

            // Keep the template dormant without disabling clones spawned by Hardpoint.SpawnMount.
            GameObject templateRoot = new GameObject("ParatrooperTemplateRoot");
            templateRoot.SetActive(false);
            GameObject.DontDestroyOnLoad(templateRoot);
            GameObject go = sourceMount != null ? sourceMount.prefab : null;
            if (go != null)
            {
                go = UnityEngine.Object.Instantiate(go, templateRoot.transform);
            }
            else
            {
                go = new GameObject("Chimera_Paratroopers_Prefab");
                go.transform.SetParent(templateRoot.transform, false);
            }
            MountedTroops mt = go.GetComponentInChildren<MountedTroops>(true);
            if (mt == null)
            {
                mt = go.AddComponent<MountedTroops>();
            }
            mt.ammo = ParatrooperCapacity;
            // Vanilla Awake and ammo accounting read captureStrength, not the mount's ammo.
            HarmonyLib.AccessTools.Field(typeof(MountedTroops), "captureStrength")
                .SetValue(mt, (float)ParatrooperCapacity);
            mt.Rearmable = true;
            if (mt.info == null && sourceMount != null && sourceMount.info != null)
            {
                mt.info = ScriptableObject.Instantiate(sourceMount.info);
            }

            go.SetActive(true);
            return cachedTroopsPrefab = go;
        }

        public static WeaponMount GetOrCreateChimeraTroopsMount()
        {
            if (cachedChimeraTroopsMount != null) return cachedChimeraTroopsMount;

            WeaponMount sourceTroopsMount = FindIbisTroopsMount();

            if (sourceTroopsMount == null)
            {
                Plugin.Logger.LogWarning("[Chimera Loadout] Could not locate source Troops mount for clone.");
                return null;
            }

            cachedChimeraTroopsMount = ScriptableObject.Instantiate(sourceTroopsMount);
            cachedChimeraTroopsMount.name = "Troopsx16_Chimera";
            cachedChimeraTroopsMount.jsonKey = "Troopsx16_Chimera";
            cachedChimeraTroopsMount.mountName = "Paratroopers (x16)";
            cachedChimeraTroopsMount.ammo = ParatrooperCapacity;
            cachedChimeraTroopsMount.Troops = true;
            cachedChimeraTroopsMount.Cargo = false;

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
                cachedChimeraTroopsMount.info = info;
                cachedChimeraTroopsMount.prefab.GetComponentInChildren<MountedTroops>(true).info = info;
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

                if (ChimeraLoadoutSetRules.IsChimeraCargoSet(set.name))
                {
                    if (set.weaponOptions != null && !set.weaponOptions.Contains(troops))
                    {
                        set.weaponOptions.Add(troops);
                        Plugin.Logger.LogInfo($"[Chimera Loadout] Injected Paratroopers into '{set.name}'.");
                    }
                }
            }
        }

        private static WeaponMount FindIbisTroopsMount()
        {
            WeaponMount sourceTroopsMount = null;

            WeaponMount[] allMounts = Resources.FindObjectsOfTypeAll<WeaponMount>();
            if (allMounts != null)
            {
                for (int i = 0; i < allMounts.Length; i++)
                {
                    WeaponMount wm = allMounts[i];
                    if (wm == null) continue;

                    if (string.Equals(wm.name, IbisTroopsMountForward, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(wm.jsonKey, IbisTroopsMountForward, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(wm.name, IbisTroopsMountReverse, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(wm.jsonKey, IbisTroopsMountReverse, StringComparison.OrdinalIgnoreCase))
                    {
                        return wm;
                    }

                    if (wm.Troops || (wm.info != null && wm.info.troops))
                    {
                        sourceTroopsMount ??= wm;
                    }
                }
            }

            if (sourceTroopsMount == null && Encyclopedia.i != null && Encyclopedia.i.aircraft != null)
            {
                for (int a = 0; a < Encyclopedia.i.aircraft.Count; a++)
                {
                    AircraftDefinition adef = Encyclopedia.i.aircraft[a];
                    if (adef == null || adef.unitPrefab == null)
                        continue;

                    WeaponManager wm = adef.unitPrefab.GetComponentInChildren<WeaponManager>(true);
                    if (wm == null || wm.hardpointSets == null)
                        continue;

                    string sourceName = (adef.unitName ?? adef.jsonKey ?? "").ToLowerInvariant();
                    if (sourceName.IndexOf("ibis", StringComparison.OrdinalIgnoreCase) < 0 &&
                        sourceName.IndexOf("utilityhelo", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    sourceTroopsMount = FindTroopsMountInHardpointSets(wm.hardpointSets);
                    if (sourceTroopsMount != null) break;
                }
            }

            if (sourceTroopsMount == null)
            {
                sourceTroopsMount = FindTroopsMountInDefinitions();
            }

            return sourceTroopsMount;
        }

        private static WeaponMount FindTroopsMountInHardpointSets(HardpointSet[] hardpointSets)
        {
            for (int s = 0; s < hardpointSets.Length; s++)
            {
                HardpointSet hs = hardpointSets[s];
                if (hs == null || hs.weaponOptions == null) continue;

                for (int o = 0; o < hs.weaponOptions.Count; o++)
                {
                    WeaponMount opt = hs.weaponOptions[o];
                    if (opt != null && (opt.Troops || (opt.info != null && opt.info.troops)))
                    {
                        return opt;
                    }
                }
            }

            return null;
        }

        private static WeaponMount FindTroopsMountInDefinitions()
        {
            if (Encyclopedia.i == null || Encyclopedia.i.weaponMounts == null) return null;

            foreach (WeaponMount mount in Encyclopedia.i.weaponMounts)
            {
                if (mount != null && (mount.Troops || (mount.info != null && mount.info.troops)))
                    return mount;
            }

            return null;
        }
    }
}
