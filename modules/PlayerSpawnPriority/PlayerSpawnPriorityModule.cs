using System;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Modules.PlayerSpawnPriority
{
    internal sealed class PlayerSpawnPriorityModule : IModule
    {
        public ModuleMetadata Metadata { get; } =
            new ModuleMetadata("player-spawn-priority", "Player spawn priority");

        public Type[] PatchTypes => new[] { typeof(PlayerSpawnPriorityPatch) };

        public void Install(ModuleContext context) =>
            context.Logger.LogInfo("Player spawn priority=server hangar AI eviction");
    }
}
