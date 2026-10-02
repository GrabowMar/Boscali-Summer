using System;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Modules.Wing
{
    internal sealed class WingModule : IModule
    {
        private static readonly ModuleMetadata Module =
            new ModuleMetadata("wing", "Wing");
        private static readonly Type[] Patches =
        {
            typeof(BoscaliSummer.Modules.Wing.Presentation.WingHudTint.UpdateColorPatch),
            typeof(BoscaliSummer.Modules.Wing.Presentation.WingMapTint.MapIconColorPatch),
            typeof(BoscaliSummer.Modules.Wing.Presentation.WingMapTint.ShowAirbasePatch),
            typeof(BoscaliSummer.Modules.Wing.Presentation.WingMenuActionPatches),
            typeof(BoscaliSummer.Modules.Wing.Presentation.WingRadialMenuPatches),
            typeof(BoscaliSummer.Modules.Wing.Presentation.WingRadialMenuPatches.AwakePatch),
            typeof(BoscaliSummer.Modules.Wing.Presentation.WmcMapControlsPatch),
            typeof(BoscaliSummer.Modules.Wing.Presentation.WmcMapSelection.ClickIconPatch),
            typeof(BoscaliSummer.Modules.Wing.Runtime.BounceGuard),
            typeof(BoscaliSummer.Modules.Wing.Runtime.EjectGuard),
            typeof(BoscaliSummer.Modules.Wing.Runtime.PlayerAutopilotPatches),
            typeof(BoscaliSummer.Modules.Wing.Runtime.RunwayLockPatch),
            typeof(BoscaliSummer.Modules.Wing.Runtime.SwitchStateGuard),
            typeof(BoscaliSummer.Modules.Wing.Runtime.WingEcmSpecialistPatch),
            typeof(BoscaliSummer.Modules.Wing.Runtime.WingFlareReflexPatch),
            typeof(BoscaliSummer.Modules.Wing.Runtime.WingKillMessagePatch),
            typeof(BoscaliSummer.Modules.Wing.Runtime.WingLuckPatch),
            typeof(BoscaliSummer.Modules.Wing.Runtime.WingPilotFatalDamagePatch),
            typeof(BoscaliSummer.Modules.Wing.Runtime.WingPilotKillerPatch),
            typeof(BoscaliSummer.Modules.Wing.Runtime.WingSquad.AceTargetPatch),
            typeof(BoscaliSummer.Modules.Wing.Runtime.WingSquad.SurvivorCapturePatch),
            typeof(BoscaliSummer.Modules.Wing.Runtime.WingSquad.SurvivorDisabledPatch),
            typeof(BoscaliSummer.Modules.Wing.Runtime.WingSquad.SurvivorSpawnPatch),
            typeof(BoscaliSummer.Modules.Wing.Runtime.WingSquad.SurvivorStatePatch),
            typeof(BoscaliSummer.Modules.Wing.Runtime.WingSurvivorCapturePatch),
            typeof(BoscaliSummer.Modules.Wing.Runtime.WingSurvivorDeathPatch),
            typeof(BoscaliSummer.Modules.Wing.Runtime.WingSurvivorReturnPatch),
            typeof(BoscaliSummer.Modules.Wing.Runtime.WingSurvivorSpawnPatch),
            typeof(BoscaliSummer.Modules.Wing.Runtime.WingTakeoverPatches),
            typeof(BoscaliSummer.Modules.Wing.Runtime.WingTargetPatch),
        };

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => Patches;

        public void Install(ModuleContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            WingLog.Init(context.Logger);
            WingSettings.Instance = context.Settings.Wing;
            WingLogExport.Start(context.Logger);
            GameAccess.Initialise();
            WingNet.Init();
            WingData.Load(context.Logger);

            var wing = new WingService();
            wing.RosterChanged += () => WingMembership.Publish(wing.Members);
            WingManager runtime = context.AddSceneService<WingManager>(60);
            runtime.Configure(context.Settings.Wing);
            runtime.Register(wing);
            runtime.Register(new WingPlans());
            runtime.Register(new DebriefService());
            runtime.Register(new RadioDirector());
            runtime.Register(new SpawnService());
            runtime.Register(new PlayerAutopilot());
            runtime.Register(new WingHotkeys());
            runtime.Register(new WingHudPanel());
            runtime.Register(new WmcPanel());
            runtime.Register(new DevService());
            context.AddComponent<BridgeState>();
            context.AddService<IWingSquad>(new WingSquadService());

            context.Logger.LogInfo($"Wing {WingLog.FullVersion} loaded.");
            context.Logger.LogInfo(new WingDiagnostic(WingDiagnosticEvent.PluginReady, 0));
            if (WingConfig.SimRun)
                context.Logger.LogInfo("Sim run: the player's records are left alone; this run's go to " + WingConfig.RecordsRoot);
            WingLog.Verbose($"Effective settings: Mode={context.Settings.Wing.Mode.Value} DevTools={context.Settings.Wing.DevTools.Value} " +
                $"DataRoot={WingConfig.DataRoot}");
        }
    }
}
