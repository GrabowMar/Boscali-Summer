using System;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.PlayerSpawnPriority
{
    internal sealed class PlayerSpawnPriorityFeature : IModFeature
    {
        public FeatureMetadata Metadata { get; } =
            new FeatureMetadata("player-spawn-priority", "Player spawn priority");

        public Type[] PatchTypes => new[] { typeof(PlayerSpawnPriorityPatch) };

        public void Install(FeatureContext context) =>
            context.Logger.LogInfo("Player spawn priority=server hangar AI eviction");
    }
}
