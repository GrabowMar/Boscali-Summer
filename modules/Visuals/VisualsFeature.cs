using System;
using BoscaliSummer.Features.Visuals.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Visuals
{
    internal sealed class VisualsFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("visuals", "Visuals");

        public FeatureMetadata Metadata => Feature;
        public Type[] PatchTypes => Type.EmptyTypes;

        public void Install(FeatureContext context)
        {
            VisualsManager visuals = context.AddSceneService<VisualsManager>(45);
            visuals.Configure(context.Settings.Visuals, context.Logger);
            context.AddService<IVisualEnhancements>(visuals);

            FoliageWindService wind = context.AddSceneService<FoliageWindService>(46);
            wind.Configure(context.Settings.Visuals, context.Logger);
        }
    }
}
