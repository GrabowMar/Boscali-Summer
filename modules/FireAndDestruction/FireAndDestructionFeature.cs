using System;
using BoscaliSummer.Fire;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Runtime;

namespace BoscaliSummer.Features.FireAndDestruction
{
    internal sealed class FireAndDestructionFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("fire-and-destruction", "Fire and destruction");
        private static readonly Type[] Patches =
        {
            typeof(BulletImpactPatch),
            typeof(MissileImpactPatch),
            typeof(GroundVehicleDestructionPatch),
            typeof(MapBuildingRuinPatch),
            typeof(AircraftWreckPersistencePatch)
        };

        public FeatureMetadata Metadata => Feature;
        public Type[] PatchTypes => Patches;

        public void Install(FeatureContext context)
        {
            ImpactFireManager fires = context.AddSceneService<ImpactFireManager>(10);
            fires.Configure(context.Services);
            ImpactScorchManager scorch =
                context.AddSceneService<ImpactScorchManager>(15);
            RuinAftermathManager ruins = context.AddSceneService<RuinAftermathManager>(20);
            ModNet network = context.AddSceneService<ModNet>(100);
            context.AddService(fires);
            context.AddService<IFireSuppressionService>(fires);
            context.AddService(scorch);
            context.AddService(ruins);
            context.AddService(network);
        }
    }
}
