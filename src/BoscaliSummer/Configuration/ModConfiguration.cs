using BepInEx.Configuration;
using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Features.FireAndDestruction.Configuration;
using BoscaliSummer.Features.Radio.Configuration;
using BoscaliSummer.Features.Progression.Configuration;
using BoscaliSummer.Features.Support.Configuration;
using BoscaliSummer.Features.UrbanCombat.Configuration;
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
        public SupportSettings Support { get; }
        public CommandSettings Command { get; }
        public DiagnosticSettings Diagnostics { get; }

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
                Support = new SupportSettings(config);
                Command = new CommandSettings(config);
                Diagnostics = new DiagnosticSettings(config);
                LegacyConfigMigration.RemoveEntries(config);
            }
            finally
            {
                config.SaveOnConfigSet = saveOnSet;
            }
            config.Save();
        }
    }
}
