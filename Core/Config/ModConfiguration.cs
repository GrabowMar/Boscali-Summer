using BepInEx.Configuration;
using BoscaliSummer.Modules.Autopilot.Configuration;
using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Modules.Comms.Configuration;
using BoscaliSummer.Modules.DynamicOperations.Configuration;
using BoscaliSummer.Modules.Events.Configuration;
using BoscaliSummer.Modules.FireAndDestruction.Configuration;
using BoscaliSummer.Modules.HighCommand.Configuration;
using BoscaliSummer.Modules.Hud.Configuration;
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
using BoscaliSummer.Modules.Spooktober.Kaiju.Configuration;
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
        public ConfigEntry<bool> MissionAskAtStart { get; }
        public ConfigEntry<string> MissionDisabledModules { get; }
        public ConfigEntry<string> MissionRemembered { get; }
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
        public KaijuSettings Kaiju { get; }
        public BoscaliSummer.Modules.Spooktober.Configuration.SpooktoberSettings Spooktober { get; }
        public BoscaliSummer.Modules.Ares.Configuration.AresSettings Ares { get; }
        public EventsSettings Events { get; }
        public CommsSettings Comms { get; }
        public QoLSettings QoL { get; }
        public AutopilotSettings Autopilot { get; }
        public ImmersionSettings Immersion { get; }
        public HudSettings TacBoard { get; }
        public DiagnosticSettings Diagnostics { get; }
        public WeatherSettings Weather { get; }
        public PerformanceSettings Performance { get; }
        public CinematographySettings Cinematography { get; }
        public BoscaliSummer.Modules.Scenario.Configuration.ScenarioSettings Scenario { get; }
        public BoscaliSummer.Modules.Pmc.Configuration.PmcSettings Pmc { get; }
        public BoscaliSummer.Modules.Effects.Configuration.EffectsSettings Effects { get; }
        public BoscaliSummer.Modules.CombatAdjust.Configuration.CombatAdjustSettings CombatAdjust { get; }

        public ModConfiguration(ConfigFile config)
        {
            bool saveOnSet = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try
            {
                MissionAskAtStart = config.Bind("Mission", "AskAtStart", true,
                    "Ask at the start of each mission which Boscali Summer systems run in it. " +
                    "The prompt switches this off when you choose never to be asked.");
                MissionDisabledModules = config.Bind("Mission", "DisabledModules", "",
                    "Module ids switched off by the last mission-start answer, comma separated. Empty runs everything.");
                MissionRemembered = config.Bind("Mission", "RememberedMissions", "{}",
                    "Answers remembered per mission name (JSON: mission name to switched-off module ids). " +
                    "Set to {} to be asked on those missions again.");
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
                Kaiju = new KaijuSettings(config);
                Spooktober = new BoscaliSummer.Modules.Spooktober.Configuration.SpooktoberSettings(config);
                Ares = new BoscaliSummer.Modules.Ares.Configuration.AresSettings(config);
                Events = new EventsSettings(config);
                Comms = new CommsSettings(config);
                QoL = new QoLSettings(config);
                Autopilot = new AutopilotSettings(config);
                Immersion = new ImmersionSettings(config);
                TacBoard = new HudSettings(config);
                Diagnostics = new DiagnosticSettings(config);
                Weather = new WeatherSettings(config);
                Performance = new PerformanceSettings(config);
                Cinematography = new CinematographySettings(config);
                Scenario = new BoscaliSummer.Modules.Scenario.Configuration.ScenarioSettings(config);
                Pmc = new BoscaliSummer.Modules.Pmc.Configuration.PmcSettings(config);
                Effects = new BoscaliSummer.Modules.Effects.Configuration.EffectsSettings(config);
                CombatAdjust = new BoscaliSummer.Modules.CombatAdjust.Configuration.CombatAdjustSettings(config);
                LegacyConfigMigration.RemoveEntries(config);
                // Last, so every module's entries are present to be sorted into what the
                // F1 window lists and what stays in the config file only.
                ConfigMenu.Apply(config);
            }
            finally
            {
                config.SaveOnConfigSet = saveOnSet;
            }
            config.Save();
        }
    }
}
