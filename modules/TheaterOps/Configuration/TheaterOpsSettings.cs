using BepInEx.Configuration;

namespace BoscaliSummer.Modules.TheaterOps.Configuration
{
    /// <summary>
    /// Living Front gates. Vanilla supplies the vehicles; the optional
    /// frontline route gives eligible depot ground AI staged destinations through its normal
    /// objective query.
    /// </summary>
    internal sealed class TheaterOpsSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> FrontlineTacticsEnabled { get; }
        public ConfigEntry<bool> AiStaffConvoys { get; }

        public TheaterOpsSettings(ConfigFile config)
        {
            const string section = "TheaterOps";
            Enabled = config.Bind(section, "Enabled", true,
                "Run Living Front: one host-owned staff per faction offers bounded operations and " +
                "directs nearby autonomous forces. Native production, combat and player orders remain authoritative.");
            FrontlineTacticsEnabled = config.Bind(section, "FrontlineTacticsEnabled", true,
                "Let the staff steer AI ground vehicles: group them, stage them behind the line and advance them on the pilot's operation. " +
                "Off: they keep the game's own orders. Vanilla supply and player orders remain authoritative.");
            AiStaffConvoys = config.Bind(section, "AiStaffConvoys", false,
                "Host only. AI-only factions buy one combat convoy every 2 minutes while their operation runs, and only when their pool " +
                "holds at least twice the price. Off (default): AI factions get no staff convoys, so a pilot who ignores the staff is not out-reinforced.");
        }
    }
}
