using System;
using BepInEx.Configuration;

namespace BoscaliSummer.Features.Visuals.Configuration
{
    /// <summary>
    /// Configuration settings for the Visuals module, cinematic post-processing,
    /// dynamic G-force effects, transonic motion blur, and foliage dynamics.
    /// </summary>
    internal sealed class VisualsSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> CinematicPostFxEnabled { get; }
        public ConfigEntry<bool> GForceEffectsEnabled { get; }
        public ConfigEntry<bool> MotionBlurEnabled { get; }
        public ConfigEntry<bool> FoliageDynamicsEnabled { get; }
        public ConfigEntry<float> BloomIntensity { get; }
        public ConfigEntry<float> FoliageSwayStrength { get; }

        public event Action OnSettingsChanged;

        public VisualsSettings(ConfigFile config)
        {
            const string section = "Visuals";

            Enabled = config.Bind(section, "Enabled", true,
                "Master switch for Boscali Visual Enhancements, including cinematic post-processing and flight visual feedback.");

            CinematicPostFxEnabled = config.Bind(section, "CinematicPostFxEnabled", true,
                "Enable cinematic URP post-processing (ACES tonemapping, HDR bloom, calibrated color grading, subtle film grain).");

            GForceEffectsEnabled = config.Bind(section, "GForceEffectsEnabled", true,
                "Enable pilot physiological G-force visual effects (tunnel vision / blackout on high positive G, redout on negative G).");

            MotionBlurEnabled = config.Bind(section, "MotionBlurEnabled", true,
                "Enable dynamic transonic camera motion blur at high speeds (Mach 0.85+) and high roll rates.");

            FoliageDynamicsEnabled = config.Bind(section, "FoliageDynamicsEnabled", true,
                "Enable enhanced wind sway and ambient dynamics on terrain foliage.");

            BloomIntensity = config.Bind(section, "BloomIntensity", 0.40f,
                new ConfigDescription(
                    "Intensity of HDR bloom on afterburners, tracer rounds, solar glint, and explosions.",
                    new AcceptableValueRange<float>(0.0f, 1.0f)));

            FoliageSwayStrength = config.Bind(section, "FoliageSwayStrength", 1.0f,
                new ConfigDescription(
                    "Strength multiplier for terrain vegetation wind response.",
                    new AcceptableValueRange<float>(0.1f, 2.5f)));

            Enabled.SettingChanged += (_, _) => NotifyChanged();
            CinematicPostFxEnabled.SettingChanged += (_, _) => NotifyChanged();
            GForceEffectsEnabled.SettingChanged += (_, _) => NotifyChanged();
            MotionBlurEnabled.SettingChanged += (_, _) => NotifyChanged();
            FoliageDynamicsEnabled.SettingChanged += (_, _) => NotifyChanged();
            BloomIntensity.SettingChanged += (_, _) => NotifyChanged();
            FoliageSwayStrength.SettingChanged += (_, _) => NotifyChanged();
        }

        public void NotifyChanged()
        {
            OnSettingsChanged?.Invoke();
        }
    }
}
