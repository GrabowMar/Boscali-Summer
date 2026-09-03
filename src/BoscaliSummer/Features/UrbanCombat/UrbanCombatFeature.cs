using System;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Garrisons;

namespace BoscaliSummer.Features.UrbanCombat
{
    internal sealed class UrbanCombatFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("urban-combat", "Urban combat");
        private static readonly Type[] Patches =
        {
            typeof(AirbaseCapturePatch),
            typeof(GarrisonClientVisualPatch),
            typeof(MountedTroopsFirePatch),
            typeof(ChimeraLoadoutAssignAircraftPatch),
            typeof(ChimeraWeaponManagerInitPatch),
            typeof(ChimeraWeaponSelectorPopulatePatch),
            typeof(ChimeraWeaponCheckerAvailablePatch)
        };

        public FeatureMetadata Metadata => Feature;
        public Type[] PatchTypes => Patches;

        public void Install(FeatureContext context)
        {
            ZoneGarrisonManager garrisons = context.AddSceneService<ZoneGarrisonManager>(30);
            context.AddSceneService<AirAssaultController>(31);
            context.AddService<IBuildingOccupancy>(garrisons);
            context.AddService<IZoneFortificationService>(garrisons);
        }
    }
}