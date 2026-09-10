using System;
using BoscaliSummer.Features.Radio.Patches;
using BoscaliSummer.Features.Radio.Runtime;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Radio
{
    internal sealed class RadioFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("radio", "Radio");
        private static readonly Type[] Patches =
        {
            typeof(VanillaPlayMusicPatch),
            typeof(VanillaCrossFadeMusicPatch),
            typeof(VanillaQueueMusicPatch)
        };

        public FeatureMetadata Metadata => Feature;
        public Type[] PatchTypes => Patches;

        public void Install(FeatureContext context)
        {
            RadioManager radio = context.AddSceneService<RadioManager>(40);
            radio.Configure(context.Settings.Radio, context.Logger);
            context.AddService(radio);
        }
    }
}
