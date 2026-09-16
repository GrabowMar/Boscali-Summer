using System;
using BoscaliSummer.Features.Campaign.Runtime;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Campaign
{
    /// <summary>
    /// Ships the authored Boscali Summer campaign mission into the game's user mission list.
    /// No Harmony patches and no scene service: the install is one bounded file write at
    /// startup, and a failure degrades to a log line instead of blocking the module.
    /// </summary>
    internal sealed class CampaignFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("campaign", "Boscali Summer campaign mission");

        public FeatureMetadata Metadata => Feature;

        public Type[] PatchTypes => Array.Empty<Type>();

        public void Install(FeatureContext context)
        {
            CampaignMissionInstaller.Install(context.Logger);
        }
    }
}
