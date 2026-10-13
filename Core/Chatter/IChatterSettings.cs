namespace BoscaliSummer.Core.Chatter
{
    /// <summary>
    /// What the radio reads of the pilot's settings, and where the pilot's voice packs live. The module that owns the config keys implements it and hands it to the radio, so
    /// the radio never names that module. Every property reads the live value.
    /// </summary>
    internal interface IChatterSettings
    {
        /// <summary>How much is said: Off, Essential (no ambient lines or exchanges) or Full.</summary>
        ChatterLevel Radio { get; }

        /// <summary>The recorded voice packs to use, as comma-separated folder names.</summary>
        string VoicePacks { get; }

        /// <summary>The pilot's own switches for pack lines ("profanity", "banter"), comma-separated.</summary>
        string PackConditions { get; }

        /// <summary>The volume of a recorded voice, 0 to 1.</summary>
        float VoicePackVolume { get; }

        /// <summary>The folder that holds the pilot's data files; voice packs are looked for in its <c>voicepacks</c> folder.</summary>
        string DataRoot { get; }
    }
}
