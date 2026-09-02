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
        public Type[] PatchTypes => Array.Empty<Type>();

        public void Install(FeatureContext context)
        {
            IPlayerEntitlements entitlements = context.Services.GetRequired<IPlayerEntitlements>();
            IProgressionView progression = context.Services.GetRequired<IProgressionView>();
            context.Services.TryGet(out IZoneFortificationService fortifications);
            SupportManager manager = context.AddSceneService<SupportManager>(50);
            SupportNet network = context.AddComponent<SupportNet>();
            SupportPanel panel = context.AddSceneService<SupportPanel>(55);
            network.Configure(manager);
            manager.Configure(context.Settings.Support, entitlements, fortifications, network, context.Logger);
            panel.Configure(manager, progression, context.Logger);
            context.AddService(manager);
        }
    }
}
