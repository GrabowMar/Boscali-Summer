using BepInEx.Configuration;

namespace BoscaliSummer.Features.Radio.Configuration
{
    internal enum BroadcastFilterMode
    {
        Clean,
        Light,
        Broadcast
    }

    /// <summary>What the receiver demodulates: the band's own scheme, or a forced one.</summary>
    internal enum ReceiverMode
    {
        Auto,
        Fm,
        Am
    }

    internal sealed class RadioSettings
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<float> CrossfadeSeconds;
        public readonly ConfigEntry<bool> Shuffle;
        public readonly ConfigEntry<bool> RepeatTrack;
        public readonly ConfigEntry<BroadcastFilterMode> BroadcastFilter;
        public readonly ConfigEntry<ReceiverMode> Mode;
        public readonly ConfigEntry<float> Squelch;
        public readonly ConfigEntry<bool> NarrowBandwidth;
        public readonly ConfigEntry<bool> FineTuning;
        public readonly ConfigEntry<float> Volume;
        public readonly ConfigEntry<bool> StationIdents;
        public readonly ConfigEntry<bool> CarrierNoise;
        public readonly ConfigEntry<float> ScanDwellSeconds;

        public RadioSettings(ConfigFile config)
        {
            Enabled = config.Bind("Radio", "Enabled", true,
                "Enable the Boscali radio and its map MFD panels. " +
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
                "band-limits it, Broadcast narrows it to a speech band with a touch of crunch. " +
                "AM bands always keep the AM curve; this setting governs FM.");
            Mode = config.Bind("Radio", "Mode", ReceiverMode.Auto,
                "Receiver demodulator. Auto follows the band (FM broadcast is FM, VHF air and MW " +
                "are AM). Forcing the wrong mode on a station garbles it, as a real set would.");
            Squelch = config.Bind("Radio", "Squelch", 0.15f,
                new ConfigDescription(
                    "Signal floor, 0-1, below which the channel is muted and only the set's own " +
                    "hiss is heard. The SQ keys on the tuner move it while flying.",
                    new AcceptableValueRange<float>(0f, 0.95f)));
            NarrowBandwidth = config.Bind("Radio", "NarrowBandwidth", false,
                "NARROW cuts the passband: less noise on a weak AM signal, duller audio on FM.");
            FineTuning = config.Bind("Radio", "FineTuning", false,
                "FINE halves the tuning step five ways, so the dial can sit between channels. " +
                "Off-grid positions are dead air, exactly as they are on a real set.");
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
        }
    }
}
