using System;
using BoscaliSummer.Features.Progression.Networking;
using BoscaliSummer.Features.Progression.Patches;
using BoscaliSummer.Features.Progression.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Progression
{
    internal sealed class ProgressionFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("progression", "Progression");
        private static readonly Type[] Patches =
        {
            typeof(AircraftFuelUsePatch),
            typeof(RewardAllocationPatch)
        };

        public FeatureMetadata Metadata => Feature;
        public Type[] PatchTypes => Patches;

        public void Install(FeatureContext context)
        {
            ProgressionManager manager = context.AddSceneService<ProgressionManager>(45);
            ProgressionNet network = context.AddComponent<ProgressionNet>();
            network.Configure(manager);
            manager.Configure(context.Settings.Progression, context.Logger, network);
            manager.ConfigureBypass(context.Settings.BypassRequirements);
            context.AddService<IPlayerPerks>(manager);
            context.AddService<IProgressionView>(manager);
        }
    }
}
