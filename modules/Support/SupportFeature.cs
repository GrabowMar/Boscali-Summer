using System;
using BoscaliSummer.Features.Support.Configuration;
using BoscaliSummer.Features.Support.Networking;
using BoscaliSummer.Features.Support.Presentation;
using BoscaliSummer.Features.Support.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Support
{
    internal sealed class SupportFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("support", "Support operations", "progression");

        public FeatureMetadata Metadata => Feature;
        public Type[] PatchTypes => new[]
        {
            typeof(Patches.SupportMissileDetonatePatch),
            typeof(Patches.SupportMissileAuthorityPatch),
            typeof(Patches.SupportMissileDescentPatch),
            typeof(Patches.UplinkMapControlsGuardPatch),
            typeof(Patches.UplinkMapCursorGuardPatch)
        };

        public void Install(FeatureContext context)
        {
            IPlayerPerks perks = context.Services.GetRequired<IPlayerPerks>();
            IProgressionView progression = context.Services.GetRequired<IProgressionView>();
            context.Services.TryGet(out IZoneFortificationService fortifications);
            context.Services.TryGet(out IFireSuppressionService fireSuppression);
            context.Services.TryGet(out IBaseDefenseAlarmService baseAlarm);

            SupportManager manager = context.AddSceneService<SupportManager>(50);
            SupportNet network = context.AddComponent<SupportNet>();
            SupportPanel panel = context.AddSceneService<SupportPanel>(55);
            SupportMapOverlay mapOverlay = context.AddSceneService<SupportMapOverlay>(58);
            Visuals.PlatformSky sky = context.AddSceneService<Visuals.PlatformSky>(59);

            network.Configure(manager);
            manager.Configure(context.Settings.Support, perks, fortifications, network, context.Logger, fireSuppression);
            manager.ConfigureBypass(context.Settings.Diagnostics.BypassRequirements);
            manager.ConfigureDisableCooldowns(context.Settings.Diagnostics.DisableOpsCooldowns);
            context.AddService<ICameraTargetService>(manager);
            context.AddService<IGroundForceReadiness>(manager);
            panel.Configure(manager, progression, context.Logger, baseAlarm);
            mapOverlay.Configure(context.Settings.Support, manager, context.Logger);
            sky.Configure(manager);

            context.AddHostSettings(SupportHostSettings.Build(context.Settings.Support));
        }
    }
}
