using System;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Modules.SeaStructures.Runtime;
using HarmonyLib;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.SeaStructures
{
    /// <summary>Sea structures: armed oil platforms and sea fortresses that break apart section by section.</summary>
    internal sealed class SeaStructuresModule : IModule
    {
        private static readonly ModuleMetadata Module = new ModuleMetadata("sea", "Sea structures");

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => new[] { typeof(SeaPrefabPatch) };

        public void Install(ModuleContext context) =>
            context.AddSceneService<SeaStructureService>(79).Configure(context.Settings.SeaStructures);
    }

    /// <summary>Registers the structure prefabs at the moment the game registers its own, so a client that joins
    /// mid-mission can resolve structures that already exist.</summary>
    [HarmonyPatch(typeof(NetworkManagerNuclearOption), "RegisterPrefabs")]
    internal static class SeaPrefabPatch
    {
        private static void Postfix()
        {
            try { SeaTemplates.EnsureRegistered(force: true); }
            catch (Exception e) { PatchGuard.Report("SeaStructures.RegisterPrefabs", e); }
        }
    }
}
