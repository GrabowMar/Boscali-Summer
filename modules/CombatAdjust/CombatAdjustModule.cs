using System;
using BepInEx.Bootstrap;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Modules.CombatAdjust.Configuration;
using BoscaliSummer.Modules.CombatAdjust.Runtime;

namespace BoscaliSummer.Modules.CombatAdjust
{
    /// <summary>Combat adjustments: link discipline, drifting stale tracks, fallible missiles, armour smoke, ship decoys, SAM EMCON.</summary>
    internal sealed class CombatAdjustModule : IModule
    {
        internal const string ExternalCombatGuid = "com.cnoevil.nuclearoption.overhaul";
        private static readonly ModuleMetadata Module = new ModuleMetadata("combat", "Combat adjustments");

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => new[]
        {
            typeof(Patches.CombatDetectScopePatch),
            typeof(Patches.CombatShareGatePatch),
            typeof(Patches.CombatFogPatch),
            typeof(Patches.CombatAccuracyScopePatch),
            typeof(Patches.CombatMissileStartPatch),
            typeof(Patches.CombatMissileLocalStartPatch),
            typeof(Patches.CombatProxyFusePatch),
            typeof(Patches.CombatAimPatch),
            typeof(Patches.CombatSmokeLosPatch)
        };

        public void Install(ModuleContext context)
        {
            CombatAdjustSettings settings = context.Settings.CombatAdjust;
            bool external = Chainloader.PluginInfos.ContainsKey(ExternalCombatGuid);
            if (external) context.Logger.LogInfo("External combat overhaul loaded: combat link, fog, expiry and missile faults stand down; smoke, decoys and EMCON stay.");
            context.AddSceneService<CombatAdjustService>(60).Configure(settings, external);
            context.AddHostSettings(CombatAdjustHostSettings.Build(settings, () => external));
        }
    }
}
