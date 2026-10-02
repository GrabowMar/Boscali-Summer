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
            typeof(Patches.SupportMissileDescentPatch)


        };

        public void Install(ModuleContext context)
        {
            IPlayerPerks perks = context.Services.GetRequired<IPlayerPerks>();
            IProgressionView progression = context.Services.GetRequired<IProgressionView>();
            context.Services.TryGet(out IZoneFortificationService fortifications);
            context.Services.TryGet(out IBaseDefenseAlarmService baseAlarm);

            SupportManager manager = context.AddSceneService<SupportManager>(50);
            SupportNet network = context.AddComponent<SupportNet>();
            SupportPanel panel = context.AddSceneService<SupportPanel>(55);
            Visuals.PlatformSky sky = context.AddSceneService<Visuals.PlatformSky>(59);
            SupportHudLine hudLine = context.AddSceneService<SupportHudLine>(56);
            Visuals.WindowMapStrip strip = context.AddSceneService<Visuals.WindowMapStrip>(57);
            Visuals.SatelliteSky satellite = context.AddSceneService<Visuals.SatelliteSky>(58);

            network.Configure(manager);
            manager.Configure(context.Settings.Support, perks, fortifications, network, context.Logger);
            manager.ConfigureBypass(context.Settings.Diagnostics.BypassRequirements);
            manager.ConfigureDisableCooldowns(context.Settings.Diagnostics.DisableOpsCooldowns);
            context.AddService<ICameraTargetService>(manager);
            context.AddService<ITheaterStrikePicture>(manager);
            context.AddService<IGroundForceReadiness>(manager);
            panel.Configure(manager, progression, context.Logger, baseAlarm);
            sky.Configure(manager);
            hudLine.Configure(manager);
            strip.Configure(manager);
            satellite.Configure(manager);

            context.AddHostSettings(SupportHostSettings.Build(context.Settings.Support));
        }
    }
}
