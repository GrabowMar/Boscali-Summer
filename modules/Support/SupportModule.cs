using System;
using BoscaliSummer.Modules.Support.Configuration;
using BoscaliSummer.Modules.Support.Networking;
using BoscaliSummer.Modules.Support.Presentation;
using BoscaliSummer.Modules.Support.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Modules.Support
{
    internal sealed class SupportModule : IModule
    {
        private static readonly ModuleMetadata Module =
            new ModuleMetadata("support", "Support operations", "progression");

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => new[]
        {
            typeof(Patches.SupportMissileDetonatePatch),
            typeof(Patches.SupportMissileAuthorityPatch),
            typeof(Patches.SupportMissileDescentPatch),
            typeof(Patches.CreditRewardPatch)
        };

        public void Install(ModuleContext context)
        {
            IPlayerPerks perks = context.Services.GetRequired<IPlayerPerks>();
            context.Services.TryGet(out IZoneFortificationService fortifications);

            SupportManager manager = context.AddSceneService<SupportManager>(50);
            SupportNet network = context.AddComponent<SupportNet>();
            SupportHudLine hudLine = context.AddSceneService<SupportHudLine>(56);
            Visuals.SatelliteSky satellite = context.AddSceneService<Visuals.SatelliteSky>(58);

            CallsController calls = context.AddSceneService<CallsController>(54);
            context.Services.TryGet(out IObservationSource observations);

            network.Configure(manager);
            manager.Configure(context.Settings.Support, perks, fortifications, network, context.Logger);
            manager.ConfigureBypass(context.Settings.Diagnostics.BypassRequirements);
            manager.ConfigureDisableCooldowns(context.Settings.Diagnostics.DisableOpsCooldowns);
            context.AddService<ICameraTargetService>(manager);
            context.AddService<ITheaterStrikePicture>(manager);
            context.AddService<IGroundForceReadiness>(manager);
            calls.Configure(manager, context.Settings.Support, observations);
            manager.AttachCalls(calls);
            hudLine.Configure(manager, calls);
            satellite.Configure(manager);

            context.AddHostSettings(SupportHostSettings.Build(context.Settings.Support));
        }
    }
}
