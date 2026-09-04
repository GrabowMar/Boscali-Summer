using System;
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
            typeof(Patches.ThirdPersonHudPatches)
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
            ThirdPersonHudController hudController = context.AddSceneService<ThirdPersonHudController>(52);
            SupportPanel panel = context.AddSceneService<SupportPanel>(55);

            hudController.Configure(context.Settings.Support);
            network.Configure(manager);
            manager.Configure(context.Settings.Support, perks, fortifications, network, context.Logger, fireSuppression);
            manager.ConfigureBypass(context.Settings.Diagnostics.BypassRequirements);
            manager.ConfigureDisableCooldowns(context.Settings.Diagnostics.DisableOpsCooldowns);
            panel.Configure(manager, progression, context.Logger, baseAlarm);
        }
    }
}
