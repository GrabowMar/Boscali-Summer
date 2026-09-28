using BepInEx.Configuration;

namespace BoscaliSummer
{
    internal static class LegacyConfigMigration
    {
        public static void RemoveEntries(ConfigFile config)
        {
            // Bind-then-remove consumes old values from BepInEx's orphan table as well as
            // active entries, so the next save leaves a clean compact config file.
            BindAndRemove(config, "Smoke", "Enabled", true);
            BindAndRemove(config, "Smoke", "Strength", 1f);
            BindAndRemove(config, "Smoke", "Ammo", 8);
            BindAndRemove(config, "Smoke", "CooldownSeconds", 1.25f);
            BindAndRemove(config, "Smoke", "PuffsPerSide", 5);
            BindAndRemove(config, "Smoke", "LifetimeSeconds", 16f);
            BindAndRemove(config, "Smoke", "MaximumRadius", 38f);
            BindAndRemove(config, "Smoke", "LogicalPuffLimit", 96);

            BindAndRemove(config, "Fires", "BulletChance", 0.00075f);
            BindAndRemove(config, "Fires", "ExplosiveChance", 0.025f);
            BindAndRemove(config, "Fires", "MaximumActiveSites", 24);
            BindAndRemove(config, "Fires", "LifetimeSeconds", 90f);
            BindAndRemove(config, "Fires", "MergeRadius", 72f);
            BindAndRemove(config, "Fires", "CellCooldownSeconds", 8f);
            BindAndRemove(config, "Fires", "ScorchRadius", 45f);
            BindAndRemove(config, "Fires", "ScorchRadiusScale", 0.72f);
            BindAndRemove(config, "Fires", "ForestIndexCellSize", 32f);
            BindAndRemove(config, "Fires", "SpreadEnabled", true);
            BindAndRemove(config, "Fires", "SpreadIntervalSeconds", 11f);
            BindAndRemove(config, "Fires", "SpreadDistance", 62f);
            BindAndRemove(config, "Fires", "SpreadGenerations", 2);

            // Replaced by BloomBoost, a multiplier on the game's own bloom (2026-09-24).
            BindAndRemove(config, "Visuals", "BloomIntensity", 0.4f);
            // The game's own renderer already runs SSAO; the duplicate switch was dropped.
            BindAndRemove(config, "Visuals", "AmbientOcclusionEnabled", false);
            // URP camera motion blur smears the whole view through the game's overlay post camera.
            BindAndRemove(config, "Visuals", "MotionBlurEnabled", true);
            // Runtime point lights never reached the cockpit shaders (tested 2026-09-25).
            BindAndRemove(config, "Immersion", "CockpitFloodLightEnabled", true);

            BindAndRemove(config, "Destruction", "MaximumPersistentRuins", 256);
            BindAndRemove(config, "Destruction", "MaximumRuinSmokeVisuals", 24);
            BindAndRemove(config, "Destruction", "MaximumCollapseBursts", 4);
            BindAndRemove(config, "Destruction", "HotRuinSeconds", 120f);
            BindAndRemove(config, "Buildings", "DamagedBelowHitPoints", 58f);
            BindAndRemove(config, "Garrisons", "MinimumPerZone", 2);
            BindAndRemove(config, "Garrisons", "MaximumPerZone", 4);
            BindAndRemove(config, "Garrisons", "DefenseDefinitionKey", string.Empty);
            BindAndRemove(config, "Garrisons", "DamageShaderEnabled", true);
            BindAndRemove(config, "Garrisons", "DamageHeatGlowEnabled", true);
            BindAndRemove(config, "Garrisons", "StrongholdHitPoints", 2500f);
            BindAndRemove(config, "Garrisons", "StrongholdPierceArmor", 25f);
            BindAndRemove(config, "Garrisons", "StrongholdBlastArmor", 50f);
            BindAndRemove(config, "Garrisons", "StrongholdDefenseType", "auto");
            BindAndRemove(config, "Air Assault", "InfantryPerFastRope", 8);
            BindAndRemove(config, "Radio", "Volume", 0.65f);

            // The sector grid moved from a fixed dimension count to cells on the map's own
            // base grid, so the old size knob no longer means anything.
            BindAndRemove(config, "Command", "GridResolution", 32);

            // Offensives stopped charging one price for the staff work and the first wave
            // together: the overhead and each wave slot are priced apart now. The old keys are
            // purged rather than reused so the new defaults are not read as 60 and 40.
            BindAndRemove(config, "TheaterOps", "OperationSetupCost", 60f);
            BindAndRemove(config, "TheaterOps", "OperationCommitCost", 40f);

            // Support costs stopped being hand-picked constants and became vanilla-value
            // derived. The old keys are purged rather than reused: an existing config would
            // otherwise keep charging 10 and 8 allocation against a four-figure balance.
            BindAndRemove(config, "Support", "FortificationCost", 10f);
            BindAndRemove(config, "Support", "ArtilleryCost", 8f);
            BindAndRemove(config, "Support", "VehicleAirdrops", true);
            BindAndRemove(config, "Support", "VehicleAirdropCost", 12f);
            BindAndRemove(config, "Support", "ArtilleryDefinitionKey", string.Empty);
            // Stations hold a fixed sector now, so there are no pass gaps left to scale.
            BindAndRemove(config, "Support", "OrbitGapScale", 1f);

            // Purge keys from the retired synoptic weather controller and radar.
            BindAndRemove(config, "Weather", "RainEffects", true);
            BindAndRemove(config, "Weather", "RainEffectDensity", 1f);
            BindAndRemove(config, "Weather", "RainAudio", true);
            // The SET effect pack was removed; keep obsolete values out of the live config.
            BindAndRemove(config, "Visuals", "Enabled", true);
            BindAndRemove(config, "Visuals", "CinematicPostFxEnabled", true);
            BindAndRemove(config, "Visuals", "BloomBoost", 1.35f);
            BindAndRemove(config, "Visuals", "SharpenEnabled", true);
            BindAndRemove(config, "Visuals", "SharpenStrength", 0.5f);
            BindAndRemove(config, "Visuals", "GForceEffectsEnabled", true);
            BindAndRemove(config, "Visuals", "FoliageDynamicsEnabled", true);
            BindAndRemove(config, "Visuals", "FoliageSwayStrength", 1f);
            BindAndRemove(config, "Visuals", "FxSpikeEnabled", true);
            BindAndRemove(config, "Visuals", "NvgGogglesEnabled", true);
            BindAndRemove(config, "Visuals", "NvgGain", 1f);
            BindAndRemove(config, "Visuals", "NvgTubes", 1);
            BindAndRemove(config, "Immersion", "Enabled", true);
            BindAndRemove(config, "Immersion", "HeadMotionEnabled", true);
            BindAndRemove(config, "Immersion", "HeadMotionStrength", 1f);
            BindAndRemove(config, "Immersion", "ExtraShakeEnabled", true);
            BindAndRemove(config, "Immersion", "ShakeStrength", 1f);
            BindAndRemove(config, "Immersion", "SunGlareEnabled", true);
            BindAndRemove(config, "Immersion", "MfdGlowEnabled", true);
            BindAndRemove(config, "Immersion", "AirframeAudioEnabled", true);
            BindAndRemove(config, "Immersion", "SurfaceImmersionEnabled", true);
            BindAndRemove(config, "Weather", "RainAudioEnabled", true);
            BindAndRemove(config, "Weather", "RainVolume", 0.75f);
            BindAndRemove(config, "Garrisons", "UrbanAmbience", true);
            BindAndRemove(config, "Garrisons", "UrbanAmbienceVolume", 0.7f);
            BindAndRemove(config, "Weather", "RainQuality", "High");
            BindAndRemove(config, "Weather", "RainOnCanopy", true);
            BindAndRemove(config, "Weather", "Hud", true);
            BindAndRemove(config, "Weather", "ForecastSteps", 8);
            BindAndRemove(config, "Weather", "ForecastStepMinutes", 5);
            BindAndRemove(config, "Weather", "DynamicWeather", true);
            BindAndRemove(config, "Weather", "StartRegime", 0);
            BindAndRemove(config, "Weather", "StormTurbulence", true);
            BindAndRemove(config, "Weather", "SensorEffects", true);
            BindAndRemove(config, "Weather", "LightningHazard", true);
            BindAndRemove(config, "Weather", "DebugControls", false);
            BindAndRemove(config, "Weather", "DebugRainPreview", -1f);
            BindAndRemove(config, "Weather", "Supercells", true);
            BindAndRemove(config, "Weather", "SupercellDetail", 0.6f);
            BindAndRemove(config, "Weather", "ReplaceVanillaClouds", false);
            BindAndRemove(config, "Weather", "CloudSortFudge", -100f);
            // The moving front/cell model gave way to static weather states.
            BindAndRemove(config, "Weather", "TransitionIntervalMinutes", 4f);
            BindAndRemove(config, "Weather", "TransitionDurationMinutes", 2f);
            BindAndRemove(config, "Weather", "RadarRangeKm", 40f);

            // The weather overhaul's own [WeatherFronts] section is gone with the module.
            BindAndRemove(config, "WeatherFronts", "Enabled", true);
            BindAndRemove(config, "WeatherFronts", "LocalRendering", true);
            BindAndRemove(config, "WeatherFronts", "Seed", 13u);
            BindAndRemove(config, "WeatherFronts", "Heading", 90f);
            BindAndRemove(config, "WeatherFronts", "Speed", 25f);
            BindAndRemove(config, "WeatherFronts", "Width", 12000f);
            BindAndRemove(config, "WeatherFronts", "Base", 2000f);
            BindAndRemove(config, "WeatherFronts", "Top", 6000f);
            BindAndRemove(config, "WeatherFronts", "Intensity", 1f);
            BindAndRemove(config, "WeatherFronts", "Quality", "standard");
            BindAndRemove(config, "WeatherFronts", "DebugActions", false);
        }

        private static void BindAndRemove<T>(ConfigFile config, string section, string key, T defaultValue)
        {
            config.Bind(section, key, defaultValue);
            config.Remove(new ConfigDefinition(section, key));
        }
    }
}
