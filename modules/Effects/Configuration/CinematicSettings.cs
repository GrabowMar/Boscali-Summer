using BepInEx.Configuration;

namespace BoscaliSummer.Modules.Effects.Configuration
{
    internal sealed class CinematicSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<float> Strength { get; }
        public ConfigEntry<float> Bloom { get; }

        public CinematicSettings(ConfigFile config)
        {
            Enabled = config.Bind("Effects", "CinematicEnabled", false,
                "Optional cinematic combat look: deeper cool shadows, warm highlights, selective bloom and subtle grain. Preserves automatic exposure and G-LOC; bypassed during night vision. Applies live.");
            Strength = config.Bind("Effects", "CinematicStrength", 0.65f,
                new ConfigDescription("Cinematic look strength, 0 to 1. Zero restores the normal picture.",
                    new AcceptableValueRange<float>(0f, 1f)));
            Bloom = config.Bind("Effects", "CinematicBloom", 0.35f,
                new ConfigDescription("Extra glow around bright lights and afterburners, 0 to 1. Tracks automatic exposure to keep clouds and the sky clear.",
                    new AcceptableValueRange<float>(0f, 1f)));
        }
    }
}
