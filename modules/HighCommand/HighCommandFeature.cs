using System;
using BoscaliSummer.Features.HighCommand.Networking;
using BoscaliSummer.Features.HighCommand.Patches;
using BoscaliSummer.Features.HighCommand.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.HighCommand
{
    internal sealed class HighCommandFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("high-command", "Chain of command and VIP staff");

        public FeatureMetadata Metadata => Feature;

        public Type[] PatchTypes => new[]
        {
            typeof(HighCommandDamagePatch)
        };

        public void Install(FeatureContext context)
        {
            HighCommandNet network = context.AddComponent<HighCommandNet>();
            HighCommandManager manager = context.AddSceneService<HighCommandManager>(46);
            manager.Configure(context.Settings.HighCommand, network, context.Logger);
            network.Configure(manager);
            context.AddService<IHighCommandView>(manager);
        }
    }
}
