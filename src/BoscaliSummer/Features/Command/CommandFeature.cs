using System;
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
            typeof(DynamicMapMaximizePatch),
            typeof(DynamicMapMinimizePatch)
        };

        public void Install(FeatureContext context)
        {
            IProgressionView progression = context.Services.GetRequired<IProgressionView>();

            MissionMapCompatibilityEngine compat = context.AddSceneService<MissionMapCompatibilityEngine>(51);
            CommandManager manager = context.AddSceneService<CommandManager>(52);
            ComMapOverlay overlay = context.AddSceneService<ComMapOverlay>(53);
            ComMfdPanel mfd = context.AddSceneService<ComMfdPanel>(56);

            compat.Configure(context.Settings.Command, context.Logger);
            manager.Configure(context.Settings.Command, progression, context.Logger);
            overlay.Configure(context.Settings.Command, manager, compat, context.Logger);
            mfd.Configure(context.Settings.Command, manager, overlay, context.Logger);
            context.AddService<ITheaterPage>(mfd);
        }
    }
}
