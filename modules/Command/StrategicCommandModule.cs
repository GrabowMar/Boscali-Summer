using System;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Modules.Command.Presentation;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using BoscaliSummer.Modules.Command.Runtime;

namespace BoscaliSummer.Modules.Command
{
    /// <summary>The STR workspace installed only for the dedicated Boscali Summer mission.</summary>
    internal sealed class StrategicCommandModule : IModule
    {
        private static readonly ModuleMetadata Module =
            new ModuleMetadata("strategic-command", "Strategic command", "command");

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => Type.EmptyTypes;

        public void Install(ModuleContext context)
        {
            StrViewHub hub = context.AddSceneService<StrViewHub>(55);
            hub.Configure(context.Settings.Command);
            context.AddSceneService<StrMfdPanel>(56)
                .Configure(context.Settings.Command, hub, context.Logger);
            context.AddSceneService<FactionResourceRecorder>(59);
            context.AddSceneService<StrMapOverlay>(60);
        }
    }
}
