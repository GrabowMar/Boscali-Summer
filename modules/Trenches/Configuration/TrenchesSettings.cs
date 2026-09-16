using BepInEx.Configuration;

namespace BoscaliSummer.Features.Trenches.Configuration
{
    internal sealed class TrenchesSettings
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<float> GrowthIntervalSeconds;
        public readonly ConfigEntry<int> MaxTrenchPositions;
        public readonly ConfigEntry<float> LODDistanceNear;
        public readonly ConfigEntry<float> LODDistanceFar;
        public readonly ConfigEntry<bool> ShowOnTacticalMap;

        public TrenchesSettings(ConfigFile config)
        {
            Enabled = config.Bind("Trenches", "Enabled", true,
                "Enable autonomous, dynamic modular trench network growth and battlefield fortification.");

            GrowthIntervalSeconds = config.Bind("Trenches", "GrowthIntervalSeconds", 45f,
                new ConfigDescription(
                    "Interval in seconds between autonomous trench growth and fortification ticks.",
                    new AcceptableValueRange<float>(15f, 180f)));

            MaxTrenchPositions = config.Bind("Trenches", "MaxNetworks", 16,
                new ConfigDescription(
                    "Maximum concurrent trench positions per theater.",
                    new AcceptableValueRange<int>(1, 16)));

            LODDistanceNear = config.Bind("Trenches", "LODNearDistance", 600f,
                new ConfigDescription(
                    "Distance in meters for full-detail trench geometry: carved profile, wire belt and colliders (LOD0).",
                    new AcceptableValueRange<float>(100f, 2000f)));

            LODDistanceFar = config.Bind("Trenches", "LODFarDistance", 12000f,
                new ConfigDescription(
                    "Distance in meters before trench geometry is culled from the flight camera. The mid LODs keep real " +
                    "earthwork silhouettes, so a front still reads from cruise altitude.",
                    new AcceptableValueRange<float>(3000f, 24000f)));

            ShowOnTacticalMap = config.Bind("Trenches", "ShowOnTacticalMap", true,
                "Display NATO APP-6 crenellated entrenchment marks and strongpoints on the theater map.");
        }
    }
}
