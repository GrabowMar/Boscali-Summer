using System;
using BoscaliSummer.Features.Immersion.Patches;
using BoscaliSummer.Features.Immersion.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Immersion
{
    internal sealed class ImmersionFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("immersion", "Immersion");

        private static readonly Type[] Patches =
        {
            typeof(CockpitHeadRotationPatch),
            typeof(CockpitHeadResetPatch),
            typeof(GunShotShakePatch)
        };

        public FeatureMetadata Metadata => Feature;
        public Type[] PatchTypes => Patches;

        public void Install(FeatureContext context)
        {
            ImmersionManager immersion = context.AddSceneService<ImmersionManager>(47);
            immersion.Configure(context.Settings.Immersion);
            context.AddService<IImmersionSettings>(immersion);
        }
    }
}
