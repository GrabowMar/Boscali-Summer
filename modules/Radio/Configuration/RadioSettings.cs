using BepInEx.Configuration;

namespace BoscaliSummer.Modules.Radio.Configuration
{
    internal sealed class RadioSettings
    {
        public readonly ConfigEntry<bool> Enabled, Shuffle, RepeatTrack, BroadcastTalk, ReceiveMilitary;
        public readonly ConfigEntry<float> CrossfadeSeconds, Volume, MilitaryVolume;

        public RadioSettings(ConfigFile config)
        {
            Enabled = config.Bind("Radio", "Enabled", true,
                "Enable local radio stations, music deck and RAD controls. Military receive is independent. Client-local.");
            CrossfadeSeconds = config.Bind("Radio", "CrossfadeSeconds", 1.2f,
                new ConfigDescription("Music transition time. Spoken station items always finish before the next item.",
                    new AcceptableValueRange<float>(0f, 4f)));
            Shuffle = config.Bind("Radio", "Shuffle", false, "Shuffle the local music deck.");
            RepeatTrack = config.Bind("Radio", "RepeatTrack", false, "Repeat the local music deck's current track.");
            Volume = config.Bind("Radio", "Volume", 0.8f,
                new ConfigDescription("Station and local music volume; military speech temporarily ducks it.",
                    new AcceptableValueRange<float>(0f, 1f)));
            BroadcastTalk = config.Bind("Radio", "BroadcastTalk", true,
                "Play station adverts and news between music. Base Broadcast is the same mercenary station for every faction.");
            ReceiveMilitary = config.Bind("Radio", "ReceiveMilitary", true,
                "Receive military communications: command, AWACS, tower and battlefield traffic. Works while station music is stopped.");
            MilitaryVolume = config.Bind("Radio", "MilitaryVolume", 0.9f,
                new ConfigDescription("Military receiver volume, independent of station music volume.",
                    new AcceptableValueRange<float>(0f, 1f)));
        }
    }
}
