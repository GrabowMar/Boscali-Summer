using System;
using BoscaliSummer.Features.Progression.Networking;
using BoscaliSummer.Features.Progression.Patches;
using BoscaliSummer.Features.Progression.Runtime;
using BoscaliSummer.Features.Progression.Presentation;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Progression
{
    internal sealed class ProgressionFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("progression", "Progression", "squad");
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
            ISquadView squad = context.Services.GetRequired<ISquadView>();
            manager.Configure(context.Settings.Progression, context.Logger, network, squad);
            manager.ConfigureBypass(context.Settings.Diagnostics.BypassRequirements);
            context.AddService<IPlayerPerks>(manager);
            context.AddService<IProgressionView>(manager);
            if (!UnityEngine.Application.isBatchMode)
            {
                context.AddSceneService<SqdMfdPanel>(54).Configure(manager, squad, context.Logger);
                context.AddSceneService<AceHuntHud>(54).Configure(squad);
            }
        }
    }
}
