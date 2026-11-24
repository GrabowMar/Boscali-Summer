using System;
using BoscaliSummer.Fire;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.FireAndDestruction.Configuration;
using BoscaliSummer.Modules.FireAndDestruction.Networking;

namespace BoscaliSummer.Modules.FireAndDestruction
{
    internal sealed class FireAndDestructionModule : IModule
    {
        private static readonly ModuleMetadata Module =
            new ModuleMetadata("fire-and-destruction", "Fire and destruction");
        private static readonly Type[] Patches =
        {
            typeof(BulletImpactPatch),
            typeof(MissileImpactPatch),
            typeof(GroundVehicleDestructionPatch),
            typeof(BuildingHitPatch),
            typeof(BuildingDestructPatch),
            typeof(BuildingDestructPatch.NativeRubbleCapture),
            typeof(AircraftWreckPersistencePatch)
        };

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => Patches;

        public void Install(ModuleContext context)
        {
            FireAndDestructionSettings fire = context.Settings.FireAndDestruction;
            ScorchDecalMaterialResolver.Diagnostics = context.Settings.Diagnostics;
            context.OnTeardown(() => ScorchDecalMaterialResolver.Diagnostics = null);
            ImpactFireManager fires = context.AddSceneService<ImpactFireManager>(10);
            fires.Configure(context.Services, fire, context.Settings.Diagnostics);
            context.AddSceneService<ImpactScorchManager>(15).Configure(fire, context.Settings.Diagnostics);
            context.AddClientSetting("WORLD FX", "IMPACT SCORCH",
                "Scorch marks and dust where explosive hits land on building walls. Client-local cosmetic; applies to new hits.",
                context.Settings.FireAndDestruction.ImpactScorchEnabled, ClientPreset.Light);
            context.AddSceneService<BuildingCarver>(16).Configure(fire);
            context.AddSceneService<BuildingHitLedger>(17).Configure(fire);
            context.AddSceneService<RuinAftermathManager>(20).Configure(fire);
            context.AddSceneService<ModNet>(100);
            context.AddService<IFireSuppressionService>(fires);
            context.AddService<IFoliageCover>(fires);
            context.AddService<IFireStarter>(fires);

            context.AddHostSettings(new HostSettingsTable("FIRE AND DESTRUCTION", HostSettingsPage.Effects)
                .Toggle(1, context.Settings.FireAndDestruction.FiresEnabled, "FIRE IGNITION",
                    "Impacts on forests and buildings can start fires that then spread on their own.")
                .Number(2, context.Settings.FireAndDestruction.FireIntensity, "FIRE INTENSITY",
                    "Ignition chance, spread and visual intensity. Performance budgets stay bounded at any value.",
                    0.05f, v => v.ToString("P0"))
                .Number(4, context.Settings.FireAndDestruction.ActiveFireLimit, "MAX FIRE SITES",
                    "Load limit for new fire sites; current fires expire normally. Lower values reduce fire simulation and effects.", 4)
                .Toggle(3, context.Settings.FireAndDestruction.DemolishUnoccupiedBuildings, "BURN DOWN SHELLS",
                    "Demolish a civilian building after its fire burns out, unless a faction owns it."));
        }
    }
}
