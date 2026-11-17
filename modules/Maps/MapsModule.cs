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
    /// game's map lists and load like vanilla maps. Always installed: every peer must be able to load the host's map.</summary>
    internal sealed class MapsModule : IModule
    {
        public ModuleMetadata Metadata => new ModuleMetadata("maps", "Custom maps");
        public Type[] PatchTypes => new[] { typeof(MapLoaderListPatch), typeof(MapEnablePatch), typeof(NewMissionTilesPatch), typeof(MapLaunchPatch), typeof(MapGroundPatch) };

        private static string Entry(MapEntry entry) => entry.Manifest.prefabName + ":" + entry.Hash;

        public void Install(ModuleContext context)
        {
            var missions = new MapMissions();
            BoscaliMission.ExtraGroups += missions.Groups;
            MapLaunchPatch.Resolve = missions.Launch;
            MapBuilds.Catalog = () => string.Join(",", MapCatalog.All.Select(Entry));
            MapBuilds.Hosted = () =>
            {
                var key = GameAccess.NetworkManagerOrNull?.MapKey;
                MapEntry entry = key.HasValue ? MapCatalog.Find(key.Value.Path) : null;
                return entry != null ? Entry(entry) : "";
            };
            context.OnTeardown(() =>
            {
                BoscaliMission.ExtraGroups -= missions.Groups;
                MapLaunchPatch.Resolve = null;
                MapBuilds.Catalog = MapBuilds.Hosted = () => "";
            });
        }
    }
}
