using BepInEx.Configuration;

namespace BoscaliSummer.Modules.Compatibility.Configuration
{
    internal enum RainOwner { Boscali, NoAtmoFx }

    internal enum OverlapOwner { Boscali, Kelly }

    /// <summary>How Boscali shares the game with NoAtmoFX and Kelly's Client Effects (SET > COMPAT).</summary>
    internal sealed class CompatibilitySettings
    {
        public ConfigEntry<bool> NoAtmoFxLink { get; }
        public ConfigEntry<bool> NoAtmoFxSky { get; }
        public ConfigEntry<bool> WeatherLink { get; }
        public ConfigEntry<RainOwner> Rain { get; }
        public ConfigEntry<bool> KellyLink { get; }
        public ConfigEntry<OverlapOwner> Overlaps { get; }
        public ConfigEntry<bool> MoveKeys { get; }
        public ConfigEntry<bool> Expansion2082Link { get; }
        public ConfigEntry<bool> ShipHaze { get; }
        public ConfigEntry<bool> ShipAaRules { get; }

        public CompatibilitySettings(ConfigFile config)
        {
            const string section = "Compatibility";
            NoAtmoFxLink = config.Bind(section, "NoAtmoFxLink", true,
                "When NoAtmoFX is installed, share the sky with it (the switches below). Off: Boscali ignores it.");
            NoAtmoFxSky = config.Bind(section, "NoAtmoFxSky", true,
                "NoAtmoFX draws the clouds; Boscali's volumetric clouds, sun shafts, lightning and thunder stand down while NoAtmoFX's clouds are on.");
            WeatherLink = config.Bind(section, "WeatherLink", true,
                "Drive NoAtmoFX's weather (its ConditionsOverride) from Boscali's weather at the camera, so its clouds follow fronts and storms. " +
                "Put back to the mission's own (-1) when the link stops.");
            Rain = config.Bind(section, "Rain", RainOwner.Boscali,
                "Who draws rain and canopy rain. Boscali: NoAtmoFX's near-camera rain and rain on the glass are switched off (its cloud mist and frost stay). " +
                "NoAtmoFx: Boscali's rain visuals stand down.");
            KellyLink = config.Bind(section, "KellyLink", true,
                "When the parts of Kelly's Client Effects that run beside Boscali are installed (KROP effects, airfield lighting, missile boom), share the overlaps.");
            Overlaps = config.Bind(section, "Overlaps", OverlapOwner.Boscali,
                "Who draws the effects both packs have (contrails, dust wake, bullet cracks, airfield lights, missile boom); the other side's copy is switched off.");
            MoveKeys = config.Bind(section, "MoveKeys", true,
                "Move another mod's plain key that clashes with a Boscali key to a Ctrl chord (Alt for F8, which Kelly's panel takes with Ctrl), once.");
            Expansion2082Link = config.Bind(section, "Expansion2082Link", true,
                "When the Expansion2082 ship pack is installed, drive its switches below from SET. Off: Boscali leaves its config alone.");
            ShipHaze = config.Bind(section, "ShipHaze", true,
                "Expansion2082's engine haze on its ships (Haze/Sweat).");
            ShipAaRules = config.Bind(section, "ShipAaRules", true,
                "Expansion2082's ship missile-defence rules: one interceptor per inbound missile, ships mind only their own missiles, gun shells do not count as missile attacks.");
        }
    }
}
