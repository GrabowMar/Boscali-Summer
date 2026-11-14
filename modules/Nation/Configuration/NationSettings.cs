using BepInEx.Configuration;

namespace BoscaliSummer.Modules.Nation.Configuration
{
    /// <summary>Host knobs of the NATION layer. Everything else is balance and lives in <c>NationRules</c>.</summary>
    internal sealed class NationSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<float> StartingStock { get; }
        public ConfigEntry<int> FactoryCost { get; }
        public ConfigEntry<int> FocusSeconds { get; }

        public NationSettings(ConfigFile config)
        {
            const string section = "Nation";
            Enabled = config.Bind(section, "Enabled", true,
                "Run the nation layer in Escalation, Terminal Control and Domination: factories produce the stock, three policies and a focus tree per faction.");
            StartingStock = config.Bind(section, "StartingStock", 0.5f,
                new ConfigDescription("Share of the mission's own aircraft and vehicle stock each faction starts with. The rest has to be produced.",
                    new AcceptableValueRange<float>(0.25f, 1f)));
            FactoryCost = config.Bind(section, "FactoryCost", 4000,
                new ConfigDescription("Faction funds a new factory costs.", new AcceptableValueRange<int>(1000, 20000)));
            FocusSeconds = config.Bind(section, "FocusSeconds", 240,
                new ConfigDescription("Seconds one national focus takes.", new AcceptableValueRange<int>(120, 600)));
        }
    }
}
