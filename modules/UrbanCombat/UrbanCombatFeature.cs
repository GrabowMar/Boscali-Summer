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
            Plugin.Logger.LogInfo($"[Air Assault] Troop accounting capability: {AirAssaultController.TroopAccountingAvailable}");
            Plugin.Logger.LogInfo("[Urban Combat] Rooftop nests: native MG/AT/23mm AA; 49-position roof search with native collider fallback; local sandbags/flags; maximum 6 per zone, 96 total. Loaded definitions are required at spawn.");
            ZoneGarrisonManager garrisons = context.AddSceneService<ZoneGarrisonManager>(30);
            AirAssaultController assault = context.AddSceneService<AirAssaultController>(31);
            context.AddService<IAirAssaultObservation>(assault);
            BaseDefenseAlarmService alarm = context.AddSceneService<BaseDefenseAlarmService>(32);
            context.AddService<IBuildingOccupancy>(garrisons);
            context.AddService<IZoneFortificationService>(garrisons);
            context.AddService<IBaseDefenseAlarmService>(alarm);
        }
    }
}
