using System;
using BoscaliSummer.Modules.AirSurvival.Patches;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Modules.AirSurvival
{
    internal sealed class AirSurvivalModule : IModule
    {
        private static readonly ModuleMetadata Module =
            new ModuleMetadata("air-survival", "Mission AI air survival and stations", "theater-ops");

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => new[] { typeof(AirMissionStationPatch), typeof(AirMissileFlarePatch) };
        public void Install(ModuleContext context) { }
    }
}
