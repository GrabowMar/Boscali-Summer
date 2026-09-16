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
            context.AddSceneService<ImpactScorchManager>(15);
            context.AddSceneService<RuinAftermathManager>(20);
            context.AddSceneService<ModNet>(100);
            context.AddService<IFireSuppressionService>(fires);

            context.AddHostSettings(new HostSettingsTable("FIRE AND DESTRUCTION")
                .Toggle(1, context.Settings.FireAndDestruction.FiresEnabled, "FIRE IGNITION",
                    "Impacts on forests and buildings can start fires that then spread on their own.")
                .Number(2, context.Settings.FireAndDestruction.FireIntensity, "FIRE INTENSITY",
                    "Ignition chance, spread and visual intensity. Performance budgets stay bounded at any value.",
                    0.05f, v => v.ToString("P0"))
                .Toggle(3, context.Settings.FireAndDestruction.DemolishUnoccupiedBuildings, "BURN DOWN SHELLS",
                    "Demolish a civilian building after its fire burns out, unless a faction owns it."));
        }
    }
}
