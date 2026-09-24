using System;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Garrisons;
using BoscaliSummer.Features.UrbanCombat.Audio;
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
            typeof(ChimeraMountRegistrationPatch),
            typeof(UrbanDefensePatch),
            typeof(SiegeFloorPatch),
            typeof(UrbanArmorPatch),
            typeof(ShellDamagePatch),
            typeof(StrongpointDamagePatch),
            typeof(StrongpointClientGuardPatch),
            typeof(DestroyCommandGuardPatch),
            typeof(ShellDestructPatch)
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
            context.AddSceneService<UrbanAmbienceService>(33);
            context.AddSceneService<WarzoneDressingService>(34);
            context.AddService<IBuildingOccupancy>(garrisons);
            context.AddService<IZoneFortificationService>(garrisons);
            context.AddService<IBaseDefenseAlarmService>(alarm);

            // Whether occupied buildings defend a zone at all. How many buildings and how
            // large a stick are balance, and stay in the config file.
            context.AddHostSettings(new HostSettingsTable("URBAN COMBAT")
                .Toggle(1, context.Settings.UrbanCombat.GarrisonsEnabled, "ZONE GARRISONS",
                    "Occupied buildings near owned airbases become defensive positions; the Zone Fortification support action needs this on.")
                .Toggle(2, context.Settings.UrbanCombat.SiegeEnabled, "URBAN SIEGE",
                    "Cities resist capture with their size and rooftop nests; control holds at the strongpoint floor until the nests fall.")
                .Number(3, context.Settings.UrbanCombat.SiegeDefenseScale, "SIEGE STRENGTH",
                    "Overall urban siege strength; 0 is vanilla capture pacing everywhere.", 0.25f));
        }
    }
}
