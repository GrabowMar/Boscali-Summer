using BepInEx.Configuration;
using BoscaliSummer.Features.Autopilot.Configuration;
using BoscaliSummer.Features.Campaign.Configuration;
using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Features.Comms.Configuration;
using BoscaliSummer.Features.DynamicOperations.Configuration;
using BoscaliSummer.Features.Events.Configuration;
using BoscaliSummer.Features.FireAndDestruction.Configuration;
using BoscaliSummer.Features.HighCommand.Configuration;
using BoscaliSummer.Features.Hud.Configuration;
using BoscaliSummer.Features.Progression.Configuration;
using BoscaliSummer.Features.QoL.Configuration;
using BoscaliSummer.Features.Radio.Configuration;
using BoscaliSummer.Features.Support.Configuration;
using BoscaliSummer.Features.Squad.Configuration;
using BoscaliSummer.Features.TheaterOps.Configuration;
using BoscaliSummer.Features.Trenches.Configuration;
using BoscaliSummer.Features.UrbanCombat.Configuration;
using BoscaliSummer.Features.Visuals.Configuration;
using BoscaliSummer.Features.Immersion.Configuration;
using BoscaliSummer.Features.Weather.Configuration;
using BoscaliSummer.Infrastructure.Diagnostics;

namespace BoscaliSummer
{
    /// <summary>
    /// Composes module-owned settings. Features read their own settings object, not a
    /// flattened property list.
    /// </summary>
    internal sealed class ModConfiguration
    {
        public FireAndDestructionSettings FireAndDestruction { get; }
        public UrbanCombatSettings UrbanCombat { get; }
        public RadioSettings Radio { get; }
        public ProgressionSettings Progression { get; }
        public SquadSettings Squad { get; }
        public SupportSettings Support { get; }
        public CommandSettings Command { get; }
        public HighCommandSettings HighCommand { get; }
        public TheaterOpsSettings TheaterOps { get; }
        public DynamicOperationsSettings DynamicOperations { get; }
        public TrenchesSettings Trenches { get; }
        public EventsSettings Events { get; }
        public CommsSettings Comms { get; }
        public CampaignSettings Campaign { get; }
        public QoLSettings QoL { get; }
        public AutopilotSettings Autopilot { get; }
        public HudSettings Hud { get; }
        public DiagnosticSettings Diagnostics { get; }
        public WeatherSettings Weather { get; }
        public VisualsSettings Visuals { get; }
        public ImmersionSettings Immersion { get; }

        public ModConfiguration(ConfigFile config)
        {
            bool saveOnSet = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try
            {
                FireAndDestruction = new FireAndDestructionSettings(config);
                UrbanCombat = new UrbanCombatSettings(config);
                Radio = new RadioSettings(config);
                Progression = new ProgressionSettings(config);
                Squad = new SquadSettings(config);
                Support = new SupportSettings(config);
                Command = new CommandSettings(config);
                HighCommand = new HighCommandSettings(config);
                TheaterOps = new TheaterOpsSettings(config);
                DynamicOperations = new DynamicOperationsSettings(config);
                Trenches = new TrenchesSettings(config);
                Events = new EventsSettings(config);
                Comms = new CommsSettings(config);
                Campaign = new CampaignSettings(config);
                QoL = new QoLSettings(config);
                Autopilot = new AutopilotSettings(config);
                Hud = new HudSettings(config);
                Diagnostics = new DiagnosticSettings(config);
                Weather = new WeatherSettings(config);
                Visuals = new VisualsSettings(config);
                Immersion = new ImmersionSettings(config);
                LegacyConfigMigration.RemoveEntries(config);
                // Last, so every module's entries are present to be sorted into the
                // F1 window's plain and advanced halves.
                ConfigMenu.Apply(config, this);
            }
            finally
            {
                config.SaveOnConfigSet = saveOnSet;
            }
            config.Save();
        }
    }
}
