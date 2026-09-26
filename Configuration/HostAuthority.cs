using BepInEx.Configuration;

namespace BoscaliSummer
{
    /// <summary>
    /// The settings whose host value is the one that counts. A client joining a server takes
    /// these values from the host for the length of the session, so its prices, gates, timers
    /// and panels match what the host enforces; everything else (presentation, sound, keys,
    /// the HUD, the look of the map) stays the player's own.
    ///
    /// <para>Named through the settings objects, like the master switches in
    /// <see cref="ConfigMenu"/>, so a renamed key cannot quietly drop out of the list. Left out
    /// on purpose: the master switches (read once at startup, so a session value would change
    /// nothing), <c>Command.GridCellSizeMetres</c> (built once per peer, and only draws the map
    /// on a client) and the free-text keys the host alone reads.</para>
    /// </summary>
    internal static class HostAuthority
    {
        public static ConfigEntryBase[] Entries(ModConfiguration s) => new ConfigEntryBase[]
        {
            s.Comms.PingSeconds, s.Comms.StickerSeconds, s.Comms.DrawingSeconds,
            s.Comms.AllowAllChannel, s.Comms.AllowDrawing, s.Comms.AllowGames,

            s.DynamicOperations.RewardMultiplier,

            s.Events.RotationGapMinSeconds, s.Events.RotationGapMaxSeconds,
            s.Events.EffectStrength, s.Events.SuperEventsEnabled,

            s.FireAndDestruction.FiresEnabled, s.FireAndDestruction.FireIntensity,
            s.FireAndDestruction.DemolishUnoccupiedBuildings,

            s.HighCommand.EconomyEnabled, s.HighCommand.StipendIntervalSeconds,
            s.HighCommand.StipendBaseAmount, s.HighCommand.MaximumStipends,
            s.HighCommand.BountyBaseFunds, s.HighCommand.BountyComponentFunds,
            s.HighCommand.BountyTheaterFunds, s.HighCommand.TransfersEnabled,
            s.HighCommand.TransferMinSeconds, s.HighCommand.TransferMaxSeconds,
            s.HighCommand.DisruptionSeconds, s.HighCommand.PostRespawnSeconds,

            s.Progression.ScorePerPoint, s.Progression.MaximumPoints, s.Progression.PerkStrength,

            s.Squad.PilotLives, s.Squad.EnemyAceHunts, s.Squad.DamageThreshold, s.Squad.HuntCooldown,

            s.Support.ReconEnabled, s.Support.FortifyEnabled, s.Support.ArtilleryEnabled,
            s.Support.EmpEnabled, s.Support.ElintEnabled, s.Support.MtiEnabled,
            s.Support.FlareBarrageEnabled, s.Support.CyberEnabled, s.Support.EwEnabled,
            s.Support.SpecOpsEnabled, s.Support.PlatformCostScale, s.Support.PlatformJettisonRefund,
            s.Support.PlatformInsertionSeconds, s.Support.PlatformDockingSeconds,
            s.Support.PlatformDebrisEvents, s.Support.SarSceneRadius, s.Support.ElintCost,
            s.Support.ElintRadius, s.Support.MtiCost, s.Support.CostMultiplier, s.Support.ReconCost,
            s.Support.FortifyCost, s.Support.ArtilleryCost, s.Support.EmpCost, s.Support.EmpRadius,
            s.Support.FlareBarrageCost, s.Support.FlareBarrageRadius, s.Support.FlareBarrageCount,
            s.Support.FlareBarrageDuration, s.Support.MaximumRange, s.Support.RequestCooldown,
            s.Support.CyberUpgradeCostScale, s.Support.CyberCampaignIntensity, s.Support.CyberReach,
            s.Support.SpecOpsCostScale,

            s.TheaterOps.FrontlineTacticsEnabled, s.TheaterOps.OperationOverheadCost,
            s.TheaterOps.OperationWaveBudget, s.TheaterOps.OperationMusterSeconds,
            s.TheaterOps.OperationPlanSeconds, s.TheaterOps.OperationLaunchDelaySeconds,
            s.TheaterOps.OperationWaveSeconds, s.TheaterOps.OperationWaveRetrySeconds,
            s.TheaterOps.OperationHoldSeconds, s.TheaterOps.OperationAssaultSeconds,

            s.Trenches.GrowthIntervalSeconds, s.Trenches.MaxTrenchPositions,

            s.UrbanCombat.GarrisonsEnabled, s.UrbanCombat.GarrisonsPerZone,
            s.UrbanCombat.TroopsPerDeploy, s.UrbanCombat.SiegeEnabled,
            s.UrbanCombat.SiegeDefenseScale, s.UrbanCombat.SiegeArmorBonus,

            s.Weather.DynamicWeatherEnabled, s.Weather.TransitionIntervalMinutes,
            s.Weather.TransitionDurationMinutes, s.Weather.MinConditions, s.Weather.MaxConditions,
            s.Weather.WindVariability, s.Weather.TurbulenceMultiplier,

            // The host's bypasses decide what it accepts, so a client must predict with them.
            s.Diagnostics.BypassRequirements, s.Diagnostics.DisableOpsCooldowns,
        };

        /// <summary>
        /// Master switches of the modules the host runs for everyone. They are read at startup,
        /// so a session cannot change them; the handshake only compares them, so a client whose
        /// switches differ from the host's is told which ones, instead of meeting empty panels.
        /// </summary>
        public static ConfigEntryBase[] Switches(ModConfiguration s) => new ConfigEntryBase[]
        {
            s.Progression.Enabled, s.Support.Enabled, s.Command.Enabled, s.HighCommand.Enabled,
            s.TheaterOps.Enabled, s.DynamicOperations.Enabled, s.Events.Enabled, s.Comms.Enabled,
            s.Trenches.Enabled, s.Weather.Enabled,
        };

        /// <summary>The name one entry travels under: section and key, which both peers bind alike.</summary>
        public static string Key(ConfigEntryBase entry) =>
            entry.Definition.Section + "/" + entry.Definition.Key;
    }
}
