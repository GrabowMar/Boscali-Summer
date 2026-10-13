using System;
using BoscaliSummer.Core.Game;

namespace BoscaliSummer.Core.Modules
{
    internal sealed class MissionProfileModule : IModule
    {
        public ModuleMetadata Metadata { get; } =
            new ModuleMetadata("mission-profile", "Dedicated Boscali Summer mission");

        public Type[] PatchTypes => BoscaliMission.PatchTypes;

        public void Install(ModuleContext context) => BoscaliMission.EnsureRegistered(context.Logger);
    }
}
