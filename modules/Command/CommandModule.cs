using System;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using BoscaliSummer.Modules.Command.Patches;
using BoscaliSummer.Modules.Command.Networking;
using BoscaliSummer.Modules.Command.Presentation;
using BoscaliSummer.Modules.Command.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using NOAvionics;

namespace BoscaliSummer.Modules.Command
{
    internal sealed class CommandModule : IModule
    {
        private static readonly ModuleMetadata Module =
            new ModuleMetadata("command", "Avionics and tactical map");

        public ModuleMetadata Metadata => Module;

        public Type[] PatchTypes => new[]
        {
            typeof(FactionMfdMergePatch),
            typeof(MfdRailPatch),
            typeof(MfdReliefMapCursorPatch),
            typeof(MfdReliefMapBoundsPatch),
            typeof(MfdReliefGridLabelsPatch),
            typeof(MfdReliefUnitIconPatch),
            typeof(MfdReliefAirbaseIconPatch),
            typeof(MfdReliefObjectivePatch),
            typeof(MfdReliefSelectedInfoPatch),
            typeof(MfdScreenChromePatch),
            typeof(MfdSinglePanelPatch),
            typeof(DynamicMapMaximizePatch),
            typeof(DynamicMapMinimizePatch),
            typeof(HoloMapKeyScopePatch),
            typeof(HoloMapKeyPatch),
            typeof(MapControlsPanelGuardPatch),
            typeof(MapBoxIconClickGuardPatch),
            typeof(MapCursorPanelGuardPatch),
            typeof(GridLabelsPatch)
        };

        public void Install(ModuleContext context)
        {
            AvUiSound.Volume = context.Settings.Command.UiSoundVolume.Value;
            context.AddSceneService<AvionicsSettingsBridge>(48).Configure(context.Settings.Command);
            TargetPresetRuntime.Configure(context.Settings.Command);
            context.AddSceneService<TargetPresetHotkeys>(49).Configure(context.Settings.Command);
            context.AddService<IRadialMenuPage>(new TargetPresetRadialPage());
            context.AddService<IVisorTerrainSettings>(context.Settings.Command);
            context.AddService<ITacticalLog>(new MfdRadioLog());

            MissionMapCompatibilityEngine compat = context.AddSceneService<MissionMapCompatibilityEngine>(51);
            CommandManager manager = context.AddSceneService<CommandManager>(52);
            context.AddService<IFactionMoraleView>(manager);
            context.AddService<ITheaterCensusSource>(manager);
            FactionMoraleNet moraleNet = context.AddComponent<FactionMoraleNet>();
            moraleNet.Configure(manager);
            ComMapOverlay overlay = context.AddSceneService<ComMapOverlay>(53);
            // STR and the MAP bezel resolve this late; neither searches the scene.
            context.AddService<ComMapOverlay>(overlay);
            context.AddService<IMapProjection>(new MfdMapProjection());
            ThreatMapOverlay threats = context.AddSceneService<ThreatMapOverlay>(54);
            context.AddService<ThreatMapOverlay>(threats);
            RoadMapOverlay roads = context.AddSceneService<RoadMapOverlay>(54);
            roads.Configure(context.Settings.Command);
            context.AddService<RoadMapOverlay>(roads);
            LogisticsMapOverlay logistics = context.AddSceneService<LogisticsMapOverlay>(55);
            context.AddService<LogisticsMapOverlay>(logistics);
            logistics.Configure(context.Settings.Command);
            context.AddSceneService<MapUiManager>(57);
            context.AddSceneService<SettingsMfdPanel>(58)
                .Configure(context.Settings.Command, context.Logger, overlay,
                    context.HostSettings, context.ClientSettings);
            context.AddSceneService<MiscMfdPanel>(58).Configure(context.Settings, context.Logger);
            context.AddSceneService<TerrainGhostOverlay>(61).Configure(context.Settings.Command);
            context.AddSceneService<HoloMap>(62).Configure(context.Settings.Command);

            TerritoryControlView territory = context.AddSceneService<TerritoryControlView>(52);
            territory.Configure(compat, context.Settings.Command.GridCellSizeMetres.Value);
            context.AddService<ITerritoryIngress>(territory);
            manager.Configure(moraleNet, territory);
            overlay.Configure(context.Settings.Command, compat, territory);
            threats.Configure(context.Settings.Command);
        }
    }
}
