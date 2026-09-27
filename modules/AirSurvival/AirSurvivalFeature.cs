using System;
using BoscaliSummer.Features.AirSurvival.Patches;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.AirSurvival
{
    internal sealed class AirSurvivalFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("air-survival", "Mission AI air survival and stations", "theater-ops");

        public FeatureMetadata Metadata => Feature;
        public Type[] PatchTypes => new[] { typeof(AirMissionStationPatch), typeof(AirMissileFlarePatch) };
        public void Install(FeatureContext context) { }
    }
}
