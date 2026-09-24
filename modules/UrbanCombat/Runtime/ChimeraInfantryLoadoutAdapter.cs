using System;
using System.Collections.Generic;
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
        private const string MountKey = "Troopsx16_Chimera";
        private const string IbisTroopsMountForward = "Troopsx8_UtilityHelo1_F";
        private const string IbisTroopsMountReverse = "Troopsx8_UtilityHelo1_R";

        private static WeaponMount chimeraTroopsMount;

        /// <summary>
        /// Registers the paratrooper mount once per Encyclopedia load, identically on every peer.
        /// Encyclopedia.AfterLoad numbers every definition by list position and Mirage writes
        /// loadout mounts as that index (DefinitionWriters), so the mount is appended right
        /// after vanilla indexing, before any loadout exists. It is also mirrored into the Chimera
        /// prefabs: WeaponChecker.VetLoadout validates spawn requests against
        /// AircraftDefinition.unitPrefab and nulls mounts missing from its hardpoint options.
        /// </summary>
        public static void Register(Encyclopedia encyclopedia)
        {
            if (encyclopedia == null || encyclopedia.weaponMounts == null || encyclopedia.IndexLookup == null ||
                encyclopedia.aircraft == null)
                return;

            WeaponMount troops = chimeraTroopsMount != null ? chimeraTroopsMount : CreateChimeraTroopsMount(encyclopedia.weaponMounts);
            if (troops == null) return;

            if (!encyclopedia.weaponMounts.Contains(troops))
                encyclopedia.weaponMounts.Add(troops);
            if (Encyclopedia.WeaponLookup != null)
                Encyclopedia.WeaponLookup[troops.jsonKey] = troops;
            if (!encyclopedia.IndexLookup.Contains(troops))
            {
                ((INetworkDefinition)troops).LookupIndex = encyclopedia.IndexLookup.Count;
                encyclopedia.IndexLookup.Add(troops);
            }

            int carriers = 0;
            for (int i = 0; i < encyclopedia.aircraft.Count; i++)
            {
                AircraftDefinition definition = encyclopedia.aircraft[i];
                if (definition == null || definition.unitPrefab == null || !IsParadropCarrier(definition)) continue;
                Aircraft prefab = definition.unitPrefab.GetComponent<Aircraft>();
                if (prefab == null || prefab.weaponManager == null || prefab.weaponManager.hardpointSets == null) continue;
                InjectIntoHardpointSets(prefab.weaponManager.hardpointSets, troops);
                carriers++;
            }

            Plugin.Logger.LogInfo($"[Chimera Loadout] Paratrooper mount registered at network index " +
                $"{((INetworkDefinition)troops).LookupIndex} for {carriers} carrier prefab(s).");
        }

        private static bool IsParadropCarrier(AircraftDefinition definition)
        {
            string name = ((definition.unitName ?? "") + " " + (definition.jsonKey ?? "") + " " + definition.unitPrefab.name).ToLowerInvariant();
            bool tarantula = name.Contains("tarantula") || name.Contains("tarantulla");
            bool chimera = tarantula || name.Contains("chimera") || name.Contains("mc260") || name.Contains("mc-260") || name.Contains("aryx");
            // Strictly no paratroopers on helicopters.
            bool helicopter = !tarantula && (definition.CanSlingLoad || name.Contains("ibis") || name.Contains("helo"));
            return chimera && !helicopter;
        }

        private static WeaponMount CreateChimeraTroopsMount(List<WeaponMount> mounts)
        {
            WeaponMount source = FindIbisTroopsMount(mounts);
            if (source == null)
            {
                Plugin.Logger.LogWarning("[Chimera Loadout] Could not locate source Troops mount for clone.");
                return null;
            }

            WeaponMount mount = ScriptableObject.Instantiate(source);
            mount.name = MountKey;
            mount.jsonKey = MountKey;
            mount.mountName = "Paratroopers (x16)";
            mount.ammo = ParatrooperCapacity;
            mount.Troops = true;
            mount.Cargo = false;

            WeaponInfo info = null;
            if (source.info != null)
            {
                info = ScriptableObject.Instantiate(source.info);
                info.name = MountKey + "_info";
                info.weaponName = "Airborne Paratroopers";
                info.shortName = "Troops";
                info.description = "Airborne infantry company equipped with static-line combat parachutes. Drops out the rear cargo hold ramp over hostile or friendly territory to capture strategic urban buildings or establish fortified combat encampments.";
                info.weaponIcon = source.info.weaponIcon;
                info.troops = true;
                info.cargo = false;
                mount.info = info;
            }

            mount.prefab = CreateTroopsPrefab(source, info);
            return chimeraTroopsMount = mount;
        }

        private static GameObject CreateTroopsPrefab(WeaponMount source, WeaponInfo info)
        {
            // Keep the template dormant without disabling clones spawned by Hardpoint.SpawnMount.
            GameObject templateRoot = new GameObject("ParatrooperTemplateRoot");
            templateRoot.SetActive(false);
            GameObject.DontDestroyOnLoad(templateRoot);
            GameObject go;
            if (source.prefab != null)
            {
                go = UnityEngine.Object.Instantiate(source.prefab, templateRoot.transform);
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
            // Weapon.Rearm is a no-op for MountedTroops, so a rearmable mount only files
            // rearm requests (WeaponStation.LaunchMount) that can never refill it.
            mt.Rearmable = false;
            if (info != null) mt.info = info;

            go.SetActive(true);
            return go;
        }

        private static void InjectIntoHardpointSets(HardpointSet[] hardpointSets, WeaponMount troops)
        {
            for (int i = 0; i < hardpointSets.Length; i++)
            {
                HardpointSet set = hardpointSets[i];
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

        /// <summary>Searches the Encyclopedia list in its fixed order, so every peer clones the same source.</summary>
        private static WeaponMount FindIbisTroopsMount(List<WeaponMount> mounts)
        {
            WeaponMount anyTroops = null;
            for (int i = 0; i < mounts.Count; i++)
            {
                WeaponMount wm = mounts[i];
                if (wm == null) continue;

                if (string.Equals(wm.name, IbisTroopsMountForward, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(wm.jsonKey, IbisTroopsMountForward, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(wm.name, IbisTroopsMountReverse, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(wm.jsonKey, IbisTroopsMountReverse, StringComparison.OrdinalIgnoreCase))
                {
                    return wm;
                }

                if (anyTroops == null && (wm.Troops || (wm.info != null && wm.info.troops)))
                    anyTroops = wm;
            }

            return anyTroops;
        }
    }
}
