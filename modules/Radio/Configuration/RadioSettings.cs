using BepInEx.Configuration;

namespace BoscaliSummer.Features.Radio.Configuration
{
    /// <summary>How much receiver character is baked onto the music itself.</summary>
    internal enum BroadcastFilterMode
    {
        Clean,
        Light,
        Broadcast
    }

    internal sealed class RadioSettings
    {
        public const int PresetSlots = 6;
        public const int NoPreset = -1;

        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<float> CrossfadeSeconds;
        public readonly ConfigEntry<bool> Shuffle;
        public readonly ConfigEntry<bool> RepeatTrack;
        public readonly ConfigEntry<BroadcastFilterMode> BroadcastFilter;
        public readonly ConfigEntry<float> Volume;
        public readonly ConfigEntry<bool> StationIdents;
        public readonly ConfigEntry<bool> CarrierNoise;
        public readonly ConfigEntry<float> ScanDwellSeconds;
        public readonly ConfigEntry<int>[] Presets;

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

            BroadcastFilter = config.Bind("Radio", "BroadcastFilter", BroadcastFilterMode.Broadcast,
                "Receiver character on the music: Clean passes it through untouched, Light " +
                "band-limits it, Broadcast narrows it to an AM speech band with a touch of crunch. " +
                "MW stations always keep the AM curve; this setting governs FM.");
            Volume = config.Bind("Radio", "Volume", 1f,
                new ConfigDescription(
                    "Receiver volume knob; scales music, carrier static and station idents. " +
                    "The game's music mixer sits underneath it.",
                    new AcceptableValueRange<float>(0f, 1f)));
            StationIdents = config.Bind("Radio", "StationIdents", true,
                "Play a short synthesized morse station ident when the radio locks onto a station.");
            CarrierNoise = config.Bind("Radio", "CarrierNoise", true,
                "Play a synthesized burst of carrier static while tuning between stations.");
            ScanDwellSeconds = config.Bind("Radio", "ScanDwellSeconds", 5f,
                new ConfigDescription(
                    "Seconds SCAN holds on each station before seeking the next.",
                    new AcceptableValueRange<float>(2f, 15f)));

            Presets = new ConfigEntry<int>[PresetSlots];
            for (int i = 0; i < PresetSlots; i++)
            {
                Presets[i] = config.Bind("Radio", "Preset" + (i + 1), NoPreset,
                    new ConfigDescription(
                        "Station stored on preset " + (i + 1) + " (-1 is empty). Set in the panel " +
                        "with SET, or edit here by station index.",
                        new AcceptableValueRange<int>(NoPreset, 31)));
            }
        }
    }
}
