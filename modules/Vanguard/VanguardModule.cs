using System;
using System.Linq;
using BepInEx.Bootstrap;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Modules.Vanguard.Runtime;

namespace BoscaliSummer.Modules.Vanguard
{
    /// <summary>
    /// VANGUARD near-future aircraft weapons. Content ships as an embedded Blueprinter bundle
    /// (assets/BoscaliSummer.nobp); without Blueprinter the weapons do not exist, so the module stays out.
    /// </summary>
    internal sealed class VanguardModule : IModule
    {
        public const string BlueprinterGuid = "com.nikkorap.blueprinter";
        private static readonly ModuleMetadata Module = new ModuleMetadata("vanguard", "Vanguard weapons");

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => CorePatchTypes.Concat(TorchBoost.PatchTypes).Concat(SunspearPod.PatchTypes)
            .Concat(StormcellPod.PatchTypes).Concat(WatchtowerPod.PatchTypes).Concat(MantaStore.PatchTypes).ToArray();

        private static readonly Type[] CorePatchTypes =
        {
            typeof(Patches.VanguardArticulationPatch),
            typeof(Patches.MaldjShadowPatch),
            typeof(Patches.VanguardInitializePatch),
            typeof(Patches.VanguardSeekPatch),
            typeof(Patches.HawcxGlowPatch),
            typeof(Patches.VanguardSlowChecksPatch),
            typeof(Patches.AegisSnapPatch),
            typeof(Patches.AegisDoorsPatch),
            typeof(Patches.AegisFinsPatch),
            typeof(Patches.TorpedoCollisionsPatch),
            typeof(Patches.LanceFirePatch),
            typeof(Patches.LanceAttachPatch),
            typeof(Patches.LanceRemoteShotPatch),
            typeof(Patches.LanceArmorPenetratePatch),
            typeof(Patches.LanceTurbineThrottlePatch),
            typeof(Patches.SkywellLaunchPatch),
            typeof(Patches.SkywellFirePatch),
            typeof(Patches.SkywellHoldPatch),
            typeof(Patches.SkywellFilterPatch),
            typeof(Patches.AlexPressPatch),
            typeof(Patches.AlexLaunchPatch),
            typeof(Patches.AlexDeflectArhPatch),
            typeof(Patches.AlexDeflectSarhPatch),
            typeof(Patches.AlexSuppressPatch),
            typeof(Patches.HiddenPodRegisterPatch),
            typeof(Patches.AiLoadoutFilterPatch)
        };

        public static bool Available => Chainloader.PluginInfos.ContainsKey(BlueprinterGuid);

        public void Install(ModuleContext context)
        {
            // Runtime stores. Registration order is the network index order on every peer: append, never reorder.
            TorchBoost.Register(context);      // SRB-2 TORCH strap-on boost rockets
            SunspearPod.Register(context);     // HEL-60 SUNSPEAR laser pod
            StormcellPod.Register(context);    // PCU-9 STORMCELL capacitor pod
            WatchtowerPod.Register(context);   // ASQ-X WATCHTOWER recon pod
            MantaStore.Register(context);      // XQ-90 MANTA heavy escort drone (needs the bundle's REMORA, so it runs last)
            context.AddSceneService<RuntimeStoreService>(55);
            context.AddSceneService<VanguardPricing>(60);
            context.AddSceneService<Presentation.VanguardHudFeeds>(60);
            context.AddSceneService<AegisService>(60);
            context.AddSceneService<Networking.AegisNet>(60);
            context.AddSceneService<Presentation.AegisHud>(60);
            context.AddSceneService<Networking.GlaiveNet>(60);
            context.AddSceneService<Networking.KestrelNet>(60);
            context.AddSceneService<Networking.OrcaNet>(60);
            context.AddSceneService<Networking.SentinelNet>(60);
            context.AddSceneService<LanceService>(60);
            context.AddSceneService<MaldxService>(60);
            context.AddSceneService<Networking.MaldxNet>(60);
            context.AddSceneService<Networking.LanceNet>(60);
            context.AddSceneService<Presentation.LanceFxPlayer>(60);
            context.AddSceneService<LanceEngineDraw>(60);
            context.AddSceneService<Presentation.TowCable>(60);
            context.AddSceneService<Presentation.MaldjHud>(60);
            context.AddSceneService<SkywellService>(60);
            context.AddSceneService<Networking.SkywellNet>(60);
            context.AddSceneService<Presentation.SkywellVisuals>(60);
            context.AddService<ISkywellControl>(context.AddSceneService<Presentation.SkywellHud>(60));
            context.AddSceneService<Networking.RemoraNet>(60);
            context.AddSceneService<Presentation.RemoraHud>(60);
            context.AddSceneService<AlexService>(60);
            context.AddSceneService<Networking.AlexNet>(60);
            context.AddSceneService<Presentation.AlexHud>(60);
            context.AddSceneService<Presentation.SkywellScopeFeed>(60);
            Networking.VanguardNet net = context.AddSceneService<Networking.VanguardNet>(60);
            Presentation.DroneLinkService drones = context.AddSceneService<Presentation.DroneLinkService>(60);
            drones.Configure(net);
            context.AddService<IDroneCommand>(drones);
            context.AddSceneService<Presentation.DroneFeedHud>(60).Configure(drones);       // the HUD feed card
            context.AddSceneService<Presentation.DroneMfdPanel>(65).Configure(drones, context.Logger); // UAV on the MFD
            var scopeGauges = new VanguardScopeGauges(drones);
            scopeGauges.Register();      // the TAC BOARD's LANCE, AEGIS and REMORA gauges
            context.OnTeardown(scopeGauges.Unregister);
            context.AddSceneService<HamilcarService>(60);
        }
    }
}
