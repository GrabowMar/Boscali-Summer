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

            context.AddHostSettings(new HostSettingsTable("PROGRESSION")
                .Number(1, context.Settings.Progression.ScorePerPoint, "SCORE PER GRADE",
                    "Score for the first qualification grade; grade n costs n x this, so grades get longer as they get better.",
                    50)
                .Number(2, context.Settings.Progression.MaximumPoints, "MAXIMUM PICKS",
                    "Most picks one player can earn in a mission. Score pays the five grades; ace bonus picks go on top.",
                    1)
                .Number(3, context.Settings.Progression.PerkStrength, "PERK STRENGTH",
                    "Scales every passive perk. Support authorisations are on or off and are unaffected.",
                    0.05f, v => v.ToString("P0")));

            if (!UnityEngine.Application.isBatchMode)
            {
                context.AddSceneService<SqdMfdPanel>(54).Configure(
                    manager, squad, context.Settings.Progression, context.Logger);
                context.AddSceneService<AceHuntHud>(54).Configure(squad);
            }
        }
    }
}
