using System;
using BoscaliSummer.Features.Command.Presentation.MapUi;
using BoscaliSummer.Features.Command.Patches;
using BoscaliSummer.Features.Command.Networking;
using BoscaliSummer.Features.Command.Presentation;
using BoscaliSummer.Features.Command.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using NOAvionics.Ui;

namespace BoscaliSummer.Features.Command
{
    internal sealed class CommandFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("command", "Tactical command and theater SA", "progression");

        public FeatureMetadata Metadata => Feature;

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
            typeof(MapControlsPanelGuardPatch),
            typeof(MapBoxIconClickGuardPatch),
            typeof(MapCursorPanelGuardPatch),
            typeof(GridLabelsPatch)
        };

        public void Install(FeatureContext context)
        {
            AvUiSound.Volume = context.Settings.Command.UiSoundVolume.Value;
            context.AddSceneService<AvionicsSettingsBridge>(48).Configure(context.Settings.Command);
            TargetPresetRuntime.Configure(context.Settings.Command);
            context.AddSceneService<TargetPresetHotkeys>(49).Configure(context.Settings.Command);
            context.AddService<IRadialMenuPage>(new TargetPresetRadialPage());

            MissionMapCompatibilityEngine compat = context.AddSceneService<MissionMapCompatibilityEngine>(51);
            CommandManager manager = context.AddSceneService<CommandManager>(52);
            context.AddService<IFactionMoraleView>(manager);
            FactionMoraleNet moraleNet = context.AddComponent<FactionMoraleNet>();
            moraleNet.Configure(manager);
            ComMapOverlay overlay = context.AddSceneService<ComMapOverlay>(53);
            // STR and the MAP bezel resolve this late; neither searches the scene.
            context.AddService<ComMapOverlay>(overlay);
            context.AddService<IMapProjection>(new MfdMapProjection());
            ThreatMapOverlay threats = context.AddSceneService<ThreatMapOverlay>(54);
            context.AddService<ThreatMapOverlay>(threats);
            StrMfdPanel strategic = context.AddSceneService<StrMfdPanel>(56);
            context.AddSceneService<MapUiManager>(57);
            context.AddSceneService<SettingsMfdPanel>(58)
                .Configure(context.Settings.Command, context.Logger, overlay,
                    context.HostSettings, context.ClientSettings);
            context.AddSceneService<FactionResourceRecorder>(59);

            TerritoryControlView territory = context.AddSceneService<TerritoryControlView>(52);
            territory.Configure(compat, context.Settings.Command.GridCellSizeMetres.Value);
            context.AddService<ITerritoryIngress>(territory);
            manager.Configure(context.Logger, moraleNet);
            overlay.Configure(context.Settings.Command, manager, compat, context.Logger, territory);
            threats.Configure(context.Settings.Command, context.Logger);
            strategic.Configure(context.Settings.Command, manager, overlay, context.Logger);
        }
    }
}
