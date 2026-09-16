using BepInEx.Configuration;

namespace BoscaliSummer.Features.TheaterOps.Configuration
{
    /// <summary>
    /// Theater priority gate. The module only biases where the vanilla AI already looks;
    /// it never spawns, retasks or moves a unit of its own, so there is nothing else to tune.
    /// </summary>
    internal sealed class TheaterOpsSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> MapMarkerEnabled { get; }

        public TheaterOpsSettings(ConfigFile config)
        {
            const string section = "TheaterOps";
            Enabled = config.Bind(section, "Enabled", true,
                "Run the theater priority: friendly AI reinforcement delivery and movement with no " +
                "better order favour the host-chosen objective.");
            MapMarkerEnabled = config.Bind(section, "MapMarkerEnabled", true,
                "Draw the local faction's main effort as a diamond on the vanilla map. Client-local presentation.");
        }
    }
}
