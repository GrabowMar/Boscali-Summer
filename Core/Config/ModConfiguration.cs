using BepInEx.Configuration;
using BoscaliSummer.Modules.Autopilot.Configuration;
using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Modules.Comms.Configuration;
using BoscaliSummer.Modules.DynamicOperations.Configuration;
using BoscaliSummer.Modules.Events.Configuration;
using BoscaliSummer.Modules.FireAndDestruction.Configuration;
using BoscaliSummer.Modules.HighCommand.Configuration;
using BoscaliSummer.Modules.Immersion.Configuration;
using BoscaliSummer.Modules.Intel.Configuration;
using BoscaliSummer.Modules.Progression.Configuration;
using BoscaliSummer.Modules.Performance.Configuration;
using BoscaliSummer.Modules.QoL.Configuration;
using BoscaliSummer.Modules.Radio.Configuration;
using BoscaliSummer.Modules.Support.Configuration;
using BoscaliSummer.Modules.Squad.Configuration;
using BoscaliSummer.Modules.TheaterOps.Configuration;
using BoscaliSummer.Modules.Trenches.Configuration;
using BoscaliSummer.Modules.UrbanCombat.Configuration;
using BoscaliSummer.Modules.Weather.Configuration;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Modules.Cinematography.Configuration;

namespace BoscaliSummer.Core.Config
{
    /// <summary>
    /// Composes module-owned settings. Modules read their own settings object, not a
    /// flattened property list.
    /// </summary>
    internal sealed class ModConfiguration
    {
        public FireAndDestructionSettings FireAndDestruction { get; }
        public UrbanCombatSettings UrbanCombat { get; }
        public RadioSettings Radio { get; }
        public WingConfig Wing { get; }
        public ProgressionSettings Progression { get; }
        public SquadSettings Squad { get; }
        public SupportSettings Support { get; }
        public CommandSettings Command { get; }
        public HighCommandSettings HighCommand { get; }
        public IntelSettings Intel { get; }
        public TheaterOpsSettings TheaterOps { get; }
        public DynamicOperationsSettings DynamicOperations { get; }
        public TrenchesSettings Trenches { get; }
        public EventsSettings Events { get; }
        public CommsSettings Comms { get; }
        public QoLSettings QoL { get; }
        public AutopilotSettings Autopilot { get; }
        public ImmersionSettings Immersion { get; }
        public DiagnosticSettings Diagnostics { get; }
        public WeatherSettings Weather { get; }
        public PerformanceSettings Performance { get; }
        public CinematographySettings Cinematography { get; }

        public ModConfiguration(ConfigFile config)
        {
            bool saveOnSet = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try
            {
                FireAndDestruction = new FireAndDestructionSettings(config);
                UrbanCombat = new UrbanCombatSettings(config);
                Radio = new RadioSettings(config);
                Wing = new WingConfig(config);
                Progression = new ProgressionSettings(config);
                Squad = new SquadSettings(config);
                Support = new SupportSettings(config);
                Command = new CommandSettings(config);
                HighCommand = new HighCommandSettings(config);
                Intel = new IntelSettings(config);
                TheaterOps = new TheaterOpsSettings(config);
                DynamicOperations = new DynamicOperationsSettings(config);
                Trenches = new TrenchesSettings(config);
                Events = new EventsSettings(config);
                Comms = new CommsSettings(config);
                QoL = new QoLSettings(config);
                Autopilot = new AutopilotSettings(config);
                Immersion = new ImmersionSettings(config);
                Diagnostics = new DiagnosticSettings(config);
                Weather = new WeatherSettings(config);
                Performance = new PerformanceSettings(config);
                Cinematography = new CinematographySettings(config);
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
