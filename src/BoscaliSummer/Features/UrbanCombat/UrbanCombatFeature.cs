using System;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Garrisons;

namespace BoscaliSummer.Features.UrbanCombat
{
    internal sealed class UrbanCombatFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("urban-combat", "Urban combat", "networking");
        private static readonly Type[] Patches =
        {
            typeof(AirbaseCapturePatch),
            typeof(GarrisonClientVisualPatch)
        };

        public FeatureMetadata Metadata => Feature;
        public Type[] PatchTypes => Patches;

        public void Install(FeatureContext context)
        {
            ZoneGarrisonManager garrisons = context.AddSceneService<ZoneGarrisonManager>(30);
            context.AddService(garrisons);
        }
    }
}
