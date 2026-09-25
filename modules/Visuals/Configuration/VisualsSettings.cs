using BepInEx.Configuration;

namespace BoscaliSummer.Features.Visuals.Configuration
{
    /// <summary>
    /// Client-side visual layer. Everything but <see cref="Enabled"/> is read every frame, so the
    /// MFD SET page can flip it mid-flight.
    /// </summary>
    internal sealed class VisualsSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> CinematicPostFxEnabled { get; }
        public ConfigEntry<float> BloomBoost { get; }
        public ConfigEntry<bool> SharpenEnabled { get; }
        public ConfigEntry<float> SharpenStrength { get; }
        public ConfigEntry<bool> GForceEffectsEnabled { get; }
        public ConfigEntry<bool> FoliageDynamicsEnabled { get; }
        public ConfigEntry<float> FoliageSwayStrength { get; }

        public VisualsSettings(ConfigFile config)
        {
            const string section = "Visuals";

            Enabled = config.Bind(section, "Enabled", true,
                "Master switch for Boscali's visual layer (colour grade, sharpening, G effects, tree and grass sway). Read at startup.");

            CinematicPostFxEnabled = config.Bind(section, "CinematicPostFxEnabled", true,
                "Filmic grade on top of the game's own tonemapping and auto-exposure: a touch more contrast, cool shadows and warm highlights, fine film grain and stronger bloom.");

            BloomBoost = config.Bind(section, "BloomBoost", 1.35f,
                new ConfigDescription(
                    "Multiplier on the game's own day/night bloom (afterburners, tracers, sun glint, explosions). 1 = vanilla.",
                    new AcceptableValueRange<float>(0.5f, 2.5f)));

            SharpenEnabled = config.Bind(section, "SharpenEnabled", true,
                "AMD FidelityFX contrast-adaptive sharpening (RCAS) at native resolution: crisper distant aircraft, terrain and cockpit text.");

            SharpenStrength = config.Bind(section, "SharpenStrength", 0.5f,
                new ConfigDescription("Sharpening strength, 0 (off) to 1 (strongest).",
                    new AcceptableValueRange<float>(0f, 1f)));

            GForceEffectsEnabled = config.Bind(section, "GForceEffectsEnabled", true,
                "Cockpit-only G effects added to the game's own G-LOC blackout: red-out under negative G and lens fringing under heavy positive G.");

            FoliageDynamicsEnabled = config.Bind(section, "FoliageDynamicsEnabled", true,
                "Trees sway and grass waves with the map's wind.");

            FoliageSwayStrength = config.Bind(section, "FoliageSwayStrength", 1.0f,
                new ConfigDescription("Strength of tree sway and grass wind.",
                    new AcceptableValueRange<float>(0.2f, 2.5f)));
        }
    }
}
