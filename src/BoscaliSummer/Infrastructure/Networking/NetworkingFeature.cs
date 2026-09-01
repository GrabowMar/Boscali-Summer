using System;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Runtime;

namespace BoscaliSummer.Infrastructure.Networking
{
    internal sealed class NetworkingFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("networking", "Networking");

        public FeatureMetadata Metadata => Feature;
        public Type[] PatchTypes => Array.Empty<Type>();

        public void Install(FeatureContext context)
        {
            ModNet network = context.AddSceneService<ModNet>(100);
            context.AddService(network);
        }
    }
}
