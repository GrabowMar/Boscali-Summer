using System;
using BoscaliSummer.Features.Intel.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Intel
{
    /// <summary>
    /// Each faction's fog-of-war threat picture. No hard dependencies, no Harmony patches and
    /// no wire: it reads each faction's own vanilla tracking and publishes IThreatPicture.
    /// </summary>
    internal sealed class IntelFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("intel", "Fog-of-war threat picture");

        public FeatureMetadata Metadata => Feature;

        public Type[] PatchTypes => Array.Empty<Type>();

        public void Install(FeatureContext context)
        {
            // Resets before TheaterOps (50), whose director reads the picture.
            ThreatPictureService picture = context.AddSceneService<ThreatPictureService>(49);
            picture.Configure(context.Settings.Intel, context.Logger);
            context.AddService<IThreatPicture>(picture);
            context.Logger.LogInfo("Intel: threat picture installed (PreWarIntel=" +
                                   context.Settings.Intel.PreWarIntel.Value + ").");
        }
    }
}
