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

        public TheaterOpsSettings(ConfigFile config)
        {
            const string section = "TheaterOps";
            Enabled = config.Bind(section, "Enabled", true,
                "Run Living Front: one host-owned staff per faction offers bounded operations and " +
                "directs nearby autonomous forces. Native production, combat and player orders remain authoritative.");
            FrontlineTacticsEnabled = config.Bind(section, "FrontlineTacticsEnabled", true,
                "Stage newly depot-spawned AI ground vehicles at the front, spread them into a line, " +
                "and let offensive groups advance after assembling. Vanilla supply and player orders remain authoritative.");
        }
    }
}
