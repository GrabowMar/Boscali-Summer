using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Modules.Chatter.Runtime;

namespace BoscaliSummer.Modules.Chatter
{
    /// <summary>The radio: the chatter packs, the one channel every net shares, the HUD strip and the command net. Publishers reach it through <see cref="IChatter"/>.</summary>
    internal sealed class ChatterModule : IModule
    {
        private static readonly ModuleMetadata Module = new ModuleMetadata("chatter", "Chatter");

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => Type.EmptyTypes;

        public void Install(ModuleContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            ChatterHost host = context.AddSceneService<ChatterHost>(35);
            host.Configure(context.Settings.Wing, context.Logger);
            context.AddService<IChatter>(host);
        }
    }
}
