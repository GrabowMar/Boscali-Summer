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
            typeof(AiTargetScoringPatch),
            typeof(MfdRailPatch),
            typeof(MfdScreenChromePatch),
            typeof(MfdSinglePanelPatch),
            typeof(DynamicMapMaximizePatch),
            typeof(DynamicMapMinimizePatch)
        };

        public void Install(FeatureContext context)
        {
            IProgressionView progression = context.Services.GetRequired<IProgressionView>();

            MissionMapCompatibilityEngine compat = context.AddSceneService<MissionMapCompatibilityEngine>(51);
            CommandManager manager = context.AddSceneService<CommandManager>(52);
            ComMapOverlay overlay = context.AddSceneService<ComMapOverlay>(53);
            StrMfdPanel strategic = context.AddSceneService<StrMfdPanel>(56);
            context.AddSceneService<MapUiManager>(57);
            context.AddSceneService<SettingsMfdPanel>(58).Configure(context.Settings.Command, context.Logger);

            compat.Configure(context.Logger);
            manager.Configure(progression, context.Logger);
            overlay.Configure(context.Settings.Command, manager, compat, context.Logger);
            strategic.Configure(context.Settings.Command, manager, overlay, context.Logger);
        }
    }
}
