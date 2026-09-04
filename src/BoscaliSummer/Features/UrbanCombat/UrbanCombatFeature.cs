using System;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Garrisons;
using BoscaliSummer.Features.UrbanCombat.Runtime;

namespace BoscaliSummer.Features.UrbanCombat
{
    internal sealed class UrbanCombatFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("urban-combat", "Urban combat");
        private static readonly Type[] Patches =
        {
            typeof(AirbaseCapturePatch),
            typeof(GarrisonClientVisualPatch)
        };

        public FeatureMetadata Metadata => Feature;
        public Type[] PatchTypes => Patches;

        public void Install(FeatureContext context)
        {
            ZoneGarrisonManager garrisons = context.AddSceneService<ZoneGarrisonManager>(30);
            BaseDefenseAlarmService alarm = context.AddSceneService<BaseDefenseAlarmService>(32);
            context.AddService<IBuildingOccupancy>(garrisons);
            context.AddService<IZoneFortificationService>(garrisons);
            context.AddService<IBaseDefenseAlarmService>(alarm);
        }
    }
}
