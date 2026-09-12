using BepInEx.Configuration;

namespace BoscaliSummer.Features.Trenches.Configuration
{
    internal sealed class TrenchesSettings
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<float> GrowthIntervalSeconds;
        public readonly ConfigEntry<int> MaxTrenchNetworks;
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

            MaxTrenchNetworks = config.Bind("Trenches", "MaxNetworks", 16,
                new ConfigDescription(
                    "Maximum concurrent trench networks per theater.",
                    new AcceptableValueRange<int>(1, 32)));

            LODDistanceNear = config.Bind("Trenches", "LODNearDistance", 250f,
                new ConfigDescription(
                    "Distance in meters for full-detail 3D trench geometry with colliders (LOD0).",
                    new AcceptableValueRange<float>(100f, 600f)));

            LODDistanceFar = config.Bind("Trenches", "LODFarDistance", 3500f,
                new ConfigDescription(
                    "Distance in meters before 3D trench geometry is culled from flight camera.",
                    new AcceptableValueRange<float>(1500f, 8000f)));

            ShowOnTacticalMap = config.Bind("Trenches", "ShowOnTacticalMap", true,
                "Display NATO APP-6 crenellated entrenchment marks and strongpoints on the theater map.");
        }
    }
}
