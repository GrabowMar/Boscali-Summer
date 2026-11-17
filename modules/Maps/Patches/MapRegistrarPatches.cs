using System;
using System.Linq;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Modules.Maps.Runtime;
using HarmonyLib;
using NuclearOption.BuildScripts;
using NuclearOption.MissionEditorScripts;
using NuclearOption.SavedMission;
using NuclearOption.SceneLoading;
using UnityEngine;

namespace BoscaliSummer.Modules.Maps.Patches
{
    /// <summary>The game asks its map list four ways; each one first makes sure the mod's maps are on the list.</summary>
    [HarmonyPatch(typeof(MapLoader))]
    internal static class MapLoaderListPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(MapLoader.CanLoad))]
        [HarmonyPatch(nameof(MapLoader.TryGetMapName))]
        [HarmonyPatch(nameof(MapLoader.TryResolveKey))]
        [HarmonyPatch(nameof(MapLoader.Load))]
        private static void Prefix(MapLoader __instance)
        {
            try { MapCatalog.EnsureListed(__instance); }
            catch (Exception e) { PatchGuard.Report("Maps.List", e); }
        }
    }

    /// <summary>Builds the map's prefab the first time the game wants to switch to it.</summary>
    [HarmonyPatch(typeof(MapSettingsManager), nameof(MapSettingsManager.EnableMap))]
    internal static class MapEnablePatch
    {
        private static void Prefix(MapSettingsManager __instance, string mapName)
        {
            try { MapCatalog.EnsureEnabled(__instance, mapName); }
            catch (Exception e) { PatchGuard.Report("Maps.Enable", e); }
        }
    }

    /// <summary>The new-mission screen has one tile per map and pairs tile i with map i: add a tile for each of ours
    /// before the screen wires them up.</summary>
    [HarmonyPatch(typeof(MissionEditorNewMenuV2), "Awake")]
    internal static class NewMissionTilesPatch
    {
        private static void Prefix(MissionEditorNewMenuV2 __instance, MapLoader ___mapLoader, RectTransform ___mapListParent,
            NewMissionMapButton ___mapButtonPrefab, ref NewMissionMapButton[] ___mapButtons)
        {
            try
            {
                MapCatalog.EnsureListed(___mapLoader);
                if (___mapLoader == null || ___mapButtons == null || ___mapButtons.Length == 0) return;
                NewMissionMapButton source = ___mapButtonPrefab != null ? ___mapButtonPrefab : ___mapButtons[0];
                Transform parent = ___mapListParent != null ? ___mapListParent : ___mapButtons[0].transform.parent;
                var tiles = ___mapButtons.ToList();
                for (int i = tiles.Count; i < ___mapLoader.Maps.Length; i++)
                {
                    NewMissionMapButton tile = UnityEngine.Object.Instantiate(source, parent);
                    tile.name = "MapTile_" + ___mapLoader.Maps[i].PrefabName;
                    tile.SetMap(___mapLoader.Maps[i]);
                    tiles.Add(tile);
                }
                ___mapButtons = tiles.ToArray();
            }
            catch (Exception e) { PatchGuard.Report("Maps.NewMissionTiles", e); }
        }
    }

    /// <summary>The <c>-mission</c> launch argument looks only in the game's own mission groups; a map's display name
    /// ("Marisol Shoals") launches that map's empty mission. Every other name is left to the game.</summary>
    [HarmonyPatch(typeof(CommandLineArgParser), nameof(CommandLineArgParser.LoadMission))]
    internal static class MapLaunchPatch
    {
        internal static Func<string, Mission> Resolve;

        private static bool Prefix(string missionName, ref Mission __result)
        {
            if (Resolve == null || string.IsNullOrEmpty(missionName)) return true;
            try
            {
                Mission mission = Resolve(missionName);
                if (mission == null) return true;
                __result = mission;
                return false;
            }
            catch (Exception e)
            {
                PatchGuard.Report("Maps.Launch", e);
                return true;
            }
        }
    }
}
