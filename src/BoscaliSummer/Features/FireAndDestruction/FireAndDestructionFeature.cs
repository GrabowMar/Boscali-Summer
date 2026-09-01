using System;
using BoscaliSummer.Fire;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.FireAndDestruction
{
    internal sealed class FireAndDestructionFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("fire-and-destruction", "Fire and destruction", "networking");
        private static readonly Type[] Patches =
        {
            typeof(BulletImpactPatch),
            typeof(MissileImpactPatch),
            typeof(GroundVehicleDestructionPatch),
            typeof(MapBuildingDamagePatch)
        };

        public FeatureMetadata Metadata => Feature;
        public Type[] PatchTypes => Patches;

        public void Install(FeatureContext context)
        {
            ImpactFireManager fires = context.AddSceneService<ImpactFireManager>(10);
            RuinAftermathManager ruins = context.AddSceneService<RuinAftermathManager>(20);
            context.AddService(fires);
            context.AddService(ruins);
        }
    }
}
