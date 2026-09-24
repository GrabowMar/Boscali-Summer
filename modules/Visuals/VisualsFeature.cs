using System;
using BoscaliSummer.Features.Visuals.Patches;
using BoscaliSummer.Features.Visuals.Presentation;
using BoscaliSummer.Features.Visuals.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Visuals
{
    internal sealed class VisualsFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("visuals", "Visuals");

        private static readonly Type[] Patches =
        {
            typeof(GraphicsMenuStartPatch),
            typeof(GraphicsMenuRefreshPatch)
        };

        public FeatureMetadata Metadata => Feature;
        public Type[] PatchTypes => Patches;

        public void Install(FeatureContext context)
        {
            GraphicsMenuInjector.SetSettings(context.Settings.Visuals);

            VisualsManager visuals = context.AddSceneService<VisualsManager>(45);
            visuals.Configure(context.Settings.Visuals, context.Logger);
            context.AddService<IVisualEnhancements>(visuals);

            FoliageWindService wind = context.AddSceneService<FoliageWindService>(46);
            wind.Configure(context.Settings.Visuals, context.Logger);

            context.Logger.LogInfo("[Visuals] Visuals feature initialized with native URP post-processing, flight dynamics, and in-game settings hooks.");
        }
    }
}
