using BepInEx.Configuration;

namespace BoscaliSummer.Features.Immersion.Configuration
{
    /// <summary>
    /// Client-side cockpit feel. Everything but <see cref="Enabled"/> is read every frame, so the
    /// MFD SET page can change it mid-flight.
    /// </summary>
    internal sealed class ImmersionSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> HeadMotionEnabled { get; }
        public ConfigEntry<float> HeadMotionStrength { get; }
        public ConfigEntry<bool> ExtraShakeEnabled { get; }
        public ConfigEntry<float> ShakeStrength { get; }
        public ConfigEntry<bool> SunGlareEnabled { get; }

        public ImmersionSettings(ConfigFile config)
        {
            const string section = "Immersion";

            Enabled = config.Bind(section, "Enabled", true,
                "Master switch for cockpit feel: head movement under G, extra camera shake and sun glare. Read at startup.");

            HeadMotionEnabled = config.Bind(section, "HeadMotionEnabled", true,
                "Cockpit view: the head dips under G, leans with side force and leads a little into rolls and turns. Adds to the game's own head bob.");

            HeadMotionStrength = config.Bind(section, "HeadMotionStrength", 1f,
                new ConfigDescription("Strength of head movement under G.",
                    new AcceptableValueRange<float>(0.2f, 2f)));

            ExtraShakeEnabled = config.Bind(section, "ExtraShakeEnabled", true,
                "Cockpit view: your own guns, touchdowns and the runway roll shake the camera, through the game's own shake and rattle.");

            ShakeStrength = config.Bind(section, "ShakeStrength", 1f,
                new ConfigDescription("Strength of the extra camera shake.",
                    new AcceptableValueRange<float>(0.2f, 2f)));

            SunGlareEnabled = config.Bind(section, "SunGlareEnabled", true,
                "Lens flare and glare when looking towards the sun; hidden behind terrain and cloud.");
        }
    }
}
