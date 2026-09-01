using BepInEx.Configuration;

namespace BoscaliSummer.Features.Radio.Configuration
{
    internal sealed class RadioSettings
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<float> CrossfadeSeconds;
        public readonly ConfigEntry<bool> Shuffle;
        public readonly ConfigEntry<bool> RepeatTrack;

        public RadioSettings(ConfigFile config)
        {
            Enabled = config.Bind("Radio", "Enabled", true,
                "Enable the client-local Boscali radio and its map MFD panel.");
            CrossfadeSeconds = config.Bind("Radio", "CrossfadeSeconds", 1.5f,
                new ConfigDescription(
                    "Seconds used to blend between local tracks.",
                    new AcceptableValueRange<float>(0f, 8f)));
            Shuffle = config.Bind("Radio", "Shuffle", false,
                "Choose a different random track after each song.");
            RepeatTrack = config.Bind("Radio", "RepeatTrack", false,
                "Repeat the current track instead of advancing.");
        }
    }
}
