using System;
using BepInEx.Bootstrap;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Modules.Vanguard.Runtime;

namespace BoscaliSummer.Modules.Vanguard
{
    /// <summary>
    /// VANGUARD near-future aircraft weapons. Content ships as an embedded Blueprinter bundle
    /// (Assets/Vanguard.nobp); without Blueprinter the weapons do not exist, so the module stays out.
    /// </summary>
    internal sealed class VanguardModule : IModule
    {
        public const string BlueprinterGuid = "com.nikkorap.blueprinter";
        private static readonly ModuleMetadata Module = new ModuleMetadata("vanguard", "Vanguard weapons");

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => new[]
        {
            typeof(Patches.VanguardInitializePatch),
            typeof(Patches.VanguardSeekPatch),
            typeof(Patches.VanguardSlowChecksPatch),
            typeof(Patches.AegisLockPatch),
            typeof(Patches.TorpedoCollisionsPatch)
        };

        public static bool Available => Chainloader.PluginInfos.ContainsKey(BlueprinterGuid);

        public void Install(ModuleContext context)
        {
            context.AddSceneService<AegisService>(60);
            context.AddSceneService<PayloadLifetime>(60);
            context.AddService<IDroneCommand>(context.AddSceneService<Networking.VanguardNet>(60));
        }
    }
}
