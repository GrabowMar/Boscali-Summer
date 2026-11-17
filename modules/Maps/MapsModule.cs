using System;
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
        public Type[] PatchTypes => new[] { typeof(MapLoaderListPatch), typeof(MapEnablePatch), typeof(NewMissionTilesPatch), typeof(MapLaunchPatch) };

        public void Install(ModuleContext context)
        {
            var missions = new MapMissions();
            BoscaliMission.ExtraGroups += missions.Groups;
            MapLaunchPatch.Resolve = missions.Launch;
            context.OnTeardown(() =>
            {
                BoscaliMission.ExtraGroups -= missions.Groups;
                MapLaunchPatch.Resolve = null;
            });
        }
    }
}
