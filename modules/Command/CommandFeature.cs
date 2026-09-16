using System;
using BoscaliSummer.Features.Command.Presentation.MapUi;
using BoscaliSummer.Features.Command.Patches;
using BoscaliSummer.Features.Command.Presentation;
using BoscaliSummer.Features.Command.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Command
{
    internal sealed class CommandFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("command", "Tactical command and theater SA", "progression");

        public FeatureMetadata Metadata => Feature;

        public Type[] PatchTypes => new[]
        {
            typeof(MfdRailPatch),
            typeof(MfdScreenChromePatch),
            typeof(MfdSinglePanelPatch),
            typeof(DynamicMapMaximizePatch),
            typeof(DynamicMapMinimizePatch),
            typeof(MapControlsPanelGuardPatch),
            typeof(MapCursorPanelGuardPatch),
            typeof(GridLabelsPatch)
        };

        public void Install(FeatureContext context)
        {
            TargetPresetRuntime.Configure(context.Settings.Command);
            context.AddSceneService<TargetPresetHotkeys>(49).Configure(context.Settings.Command);
            context.AddService<IRadialMenuPage>(new TargetPresetRadialPage());

            MissionMapCompatibilityEngine compat = context.AddSceneService<MissionMapCompatibilityEngine>(51);
            CommandManager manager = context.AddSceneService<CommandManager>(52);
            ComMapOverlay overlay = context.AddSceneService<ComMapOverlay>(53);
            // The MAP bezel resolves this late to switch the overlay's own layers; it never
            // reaches for the overlay by searching the scene.
            context.AddService<ComMapOverlay>(overlay);
            ThreatMapOverlay threats = context.AddSceneService<ThreatMapOverlay>(54);
            context.AddService<ThreatMapOverlay>(threats);
            StrMfdPanel strategic = context.AddSceneService<StrMfdPanel>(56);
            context.AddSceneService<MapUiManager>(57);
            context.AddSceneService<SettingsMfdPanel>(58)
                .Configure(context.Settings.Command, context.Logger, overlay, context.HostSettings);
            context.AddSceneService<FactionResourceRecorder>(59);

            TerritoryControlView territory = context.AddSceneService<TerritoryControlView>(52);
            territory.Configure(compat, context.Settings.Command.GridCellSizeMetres.Value);
            context.AddService<ITerritoryIngress>(territory);
            manager.Configure(context.Logger);
            overlay.Configure(context.Settings.Command, manager, compat, context.Logger, territory);
            threats.Configure(context.Settings.Command, context.Logger);
            strategic.Configure(context.Settings.Command, manager, overlay, context.Logger);
        }
    }
}
