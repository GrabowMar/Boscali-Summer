using System;
using System.Linq;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Modules.Maps.Patches;
using BoscaliSummer.Modules.Maps.Runtime;

namespace BoscaliSummer.Modules.Maps
{
    /// <summary>Custom maps: the mod's own map bundles (made by <c>nomod map gen</c> and <c>nomod map bake</c>) appear in the
    /// game's map lists and load like vanilla maps. Always installed: every peer must be able to load the host's map.
    /// It also serves the scenery kit (<c>Core.Contracts.MapKit</c>, on any map) and the climate of the loaded map
    /// (<c>Core.Contracts.MapClimate</c>, null on a vanilla map) to the other modules.</summary>
    internal sealed class MapsModule : IModule
    {
        public ModuleMetadata Metadata => new ModuleMetadata("maps", "Custom maps");
        public Type[] PatchTypes => new[] { typeof(MapLoaderListPatch), typeof(MapEnablePatch), typeof(NewMissionTilesPatch), typeof(MapLaunchPatch), typeof(MapGroundPatch) };

        private static string Entry(MapEntry entry) => entry.Manifest.prefabName + ":" + entry.Hash;

        public void Install(ModuleContext context)
        {
            var missions = new MapMissions();
            BoscaliMission.ExtraGroups += missions.Groups;
            BoscaliMission.Labels += missions.Label;
            MapLaunchPatch.Resolve = missions.Launch;
            MapBuilds.Catalog = () => string.Join(",", MapCatalog.All.Select(Entry));
            MapBuilds.Hosted = () =>
            {
                var key = GameAccess.NetworkManagerOrNull?.MapKey;
                MapEntry entry = key.HasValue ? MapCatalog.Find(key.Value.Path) : null;
                return entry != null ? Entry(entry) : "";
            };
            // A lookup, not a value: it follows the loaded map by itself, so nothing sets or resets it per mission.
            MapClimate.Current = () => MapCatalog.Loaded()?.Manifest.climate;
            MapKit.Prototype = MapKitCatalog.Prototype;
            MapKit.Kinds = MapKitCatalog.Kinds;
            context.OnTeardown(() =>
            {
                BoscaliMission.ExtraGroups -= missions.Groups;
                BoscaliMission.Labels -= missions.Label;
                MapLaunchPatch.Resolve = null;
                MapBuilds.Catalog = MapBuilds.Hosted = () => "";
                MapClimate.Current = () => null;
                MapKit.Prototype = (kind, variant) => null;
                MapKit.Kinds = () => new string[0];
                MapKitCatalog.Close();
            });
        }
    }
}
