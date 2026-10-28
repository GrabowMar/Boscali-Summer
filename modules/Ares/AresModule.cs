using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Modules.Ares.Presentation;
using BoscaliSummer.Modules.Ares.Runtime;

namespace BoscaliSummer.Modules.Ares
{
    /// <summary>ARES, the game-master console (locked behind the DEV ACCESS warning on MISC): SPAWN, POWERS and CHEATS tabs.</summary>
    internal sealed class AresModule : IModule
    {
        private static readonly ModuleMetadata Module = new ModuleMetadata("ares", "ARES game master");

        public ModuleMetadata Metadata => Module;

        public Type[] PatchTypes => new[]
        {
            typeof(AresUnitDamagePatch),
            typeof(AresAircraftDamagePatch),
            typeof(AresPartDamagePatch),
            typeof(AresShockwavePatch),
            typeof(AresGForceDamagePatch),
            typeof(AresWaterDamagePatch),
            typeof(AresBlackoutPatch),
            typeof(AresFuelPatch)
        };

        public void Install(ModuleContext context)
        {
            AresHost host = context.AddSceneService<AresHost>(76);
            host.Configure(context.Settings, context.Logger);
            context.AddService<IMiscTabs>(new AresTabs(host, context.Settings));
        }
    }
}
