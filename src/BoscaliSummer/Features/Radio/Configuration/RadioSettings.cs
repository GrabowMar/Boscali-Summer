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
            // The radio never touches the network, so every value here is yours alone.
            Enabled = config.Bind("Radio", "Enabled", true,
                "Enable the Boscali radio and its map MFD panel. " +
                "Client-local: this setting affects only your own game, never other players.");
            CrossfadeSeconds = config.Bind("Radio", "CrossfadeSeconds", 1.5f,
                new ConfigDescription(
                    "Seconds used to blend between tracks. 0 cuts straight from one to the next.",
                    new AcceptableValueRange<float>(0f, 8f)));
            Shuffle = config.Bind("Radio", "Shuffle", false,
                "Choose a different random track after each song.");
            RepeatTrack = config.Bind("Radio", "RepeatTrack", false,
                "Repeat the current track instead of advancing.");
        }
    }
}
