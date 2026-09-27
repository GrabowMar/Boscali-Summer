using System;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Visuals
{
    // Retired effect pack. Kept as an inert module boundary while the local
    // renderer-injection experiment remains in this folder.
    internal sealed class VisualsFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature = new FeatureMetadata("visuals", "Visuals");

        public FeatureMetadata Metadata => Feature;
        public Type[] PatchTypes => Type.EmptyTypes;
        public void Install(FeatureContext context) { }
    }
}
