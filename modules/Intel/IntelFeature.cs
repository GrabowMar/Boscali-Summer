using System;
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
            // Installs ThreatPictureService from Task 6; the composition root registers it there.
        }
    }
}
