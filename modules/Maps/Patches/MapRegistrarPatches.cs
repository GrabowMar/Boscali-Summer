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

    /// <summary>Once the game has put a map in the scene, a mod map is noted as the live one (its climate is served from
    /// then on) and gets the game's grass, trees and blast scars.</summary>
    [HarmonyPatch(typeof(LevelInfo), nameof(LevelInfo.ApplyMapSettings))]
    internal static class MapGroundPatch
    {
        private static void Postfix(MapSettings mapSettings)
        {
            try { MapGround.Attach(mapSettings); }
            catch (Exception e) { PatchGuard.Report("Maps.Ground", e); }
        }
    }

    /// <summary>The new-mission screen has one tile per map and pairs tile i with map i: add a tile for each of ours
    /// before the screen wires them up.</summary>
    [HarmonyPatch(typeof(MissionEditorNewMenuV2), "Awake")]
    internal static class NewMissionTilesPatch
    {
        // The screen's private fields are read by name inside the guarded body, not bound as patch parameters: a field
        // the game renames then costs only the tiles, where a failed patch would take map registration down with it.
        private static void Prefix(MissionEditorNewMenuV2 __instance)
        {
            try
            {
                Traverse screen = Traverse.Create(__instance);
                var loader = screen.Field("mapLoader").GetValue<MapLoader>();
                var buttons = screen.Field("mapButtons").GetValue<NewMissionMapButton[]>();
                MapCatalog.EnsureListed(loader);
                if (loader == null || buttons == null || buttons.Length == 0) return;
                var prefab = screen.Field("mapButtonPrefab").GetValue<NewMissionMapButton>();
                var list = screen.Field("mapListParent").GetValue<RectTransform>();
                NewMissionMapButton source = prefab != null ? prefab : buttons[0];
                Transform parent = list != null ? list : buttons[0].transform.parent;
                var tiles = buttons.ToList();
                for (int i = tiles.Count; i < loader.Maps.Length; i++)
                {
                    NewMissionMapButton tile = UnityEngine.Object.Instantiate(source, parent);
                    tile.name = "MapTile_" + loader.Maps[i].PrefabName;
                    tile.SetMap(loader.Maps[i]);
                    tiles.Add(tile);
                }
                screen.Field("mapButtons").SetValue(tiles.ToArray());
            }
            catch (Exception e) { PatchGuard.Report("Maps.NewMissionTiles", e); }
        }
    }

    /// <summary>Missions made from the game's default mission (Free Flight, the map's own empty mission) carry overrides
    /// for Heartland's airfields. On one of our maps those are not in the scene and every load shows eight "not found in
    /// scene but is Override but not Attached" load errors; they are dropped here. Carriers (attached) and custom fields stay.</summary>
    [HarmonyPatch(typeof(Mission), "SetupAirbase")]
    internal static class MapForeignAirbasePatch
    {
        private static void Prefix(Mission __instance)
        {
            try
            {
                if (__instance?.airbases == null || MapCatalog.Loaded() == null) return;
                var lookup = FactionRegistry.airbaseLookup;
                __instance.airbases.RemoveAll(a => a != null && a.IsOverride && !a.IsAttached() && !lookup.ContainsKey(a.UniqueName));
            }
            catch (Exception e) { PatchGuard.Report("Maps.ForeignAirbases", e); }
        }
    }

    /// <summary>The <c>-mission</c> launch argument looks only in the game's own mission groups; a map's display name
    /// ("Marisol Islands") launches that map's empty mission. Every other name is left to the game.</summary>
    [HarmonyPatch(typeof(CommandLineArgParser), nameof(CommandLineArgParser.LoadMission))]
    internal static class MapLaunchPatch
    {
        internal static Func<string, Mission> Resolve;

        // After Scenario's LaunchMissionPatch and Gamemodes' GamemodeLaunchPatch: every prefix runs and the last __result writer wins.
        [HarmonyAfter("com.marci.boscalisummer.module.scenario", "com.marci.boscalisummer.module.gamemodes")]
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
