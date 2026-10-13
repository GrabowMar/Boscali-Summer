using System;
using BoscaliSummer.Core.Game;

namespace BoscaliSummer.Core.Modules
{
    internal sealed class MissionProfileModule : IModule
    {
        public ModuleMetadata Metadata { get; } =
            new ModuleMetadata("mission-profile", "Mission start prompt");

        public Type[] PatchTypes => BoscaliMission.PatchTypes;

        public void Install(ModuleContext context) =>
            BoscaliMission.EnsureRegistered(context.Settings);
    }
}
