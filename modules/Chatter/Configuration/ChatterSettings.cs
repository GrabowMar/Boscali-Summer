using BepInEx.Configuration;
using BoscaliSummer.Core.Chatter;

namespace BoscaliSummer.Modules.Chatter.Configuration
{
    /// <summary>
    /// Client-local switches for which military radio channels play on the chatter net.
    /// Changes apply immediately; scripted mission dialogue always plays.
    /// </summary>
    internal sealed class ChatterSettings
    {
        public ConfigEntry<bool> AwacsEnabled { get; }
        public ConfigEntry<bool> GroundForcesEnabled { get; }
        public ConfigEntry<bool> NavalEnabled { get; }
        public ConfigEntry<bool> CommandEnabled { get; }
        public ConfigEntry<bool> TowerEnabled { get; }
        public ConfigEntry<bool> WingmenEnabled { get; }
        public ConfigEntry<bool> AlliedFlightsEnabled { get; }
        public ConfigEntry<bool> EnemySigintEnabled { get; }

        public ChatterSettings(ConfigFile config)
        {
            const string section = "Chatter";

            AwacsEnabled = config.Bind(section, "AwacsEnabled", true,
                "Play AWACS (SKYWATCH) traffic: radar contact, bandits, missiles, SAM launches, kills and fuel/gear reminders.");
            GroundForcesEnabled = config.Bind(section, "GroundForcesEnabled", true,
                "Play ground forces and paratrooper traffic: base captures, withdrawals and air assault insertions.");
            NavalEnabled = config.Bind(section, "NavalEnabled", true,
                "Play naval traffic: carrier evacuation orders and friendly ship loss reports.");
            CommandEnabled = config.Bind(section, "CommandEnabled", true,
                "Play theater command traffic: world events, operations, OPS perks, contracts, drones and support control.");
            TowerEnabled = config.Bind(section, "TowerEnabled", true,
                "Play airfield tower traffic: startup, taxi, landing clearances, takeoff calls and airfield attack alarms.");
            WingmenEnabled = config.Bind(section, "WingmenEnabled", true,
                "Play wingman traffic: order acknowledgements, weapon calls, kills, damage and flight banter.");
            AlliedFlightsEnabled = config.Bind(section, "AlliedFlightsEnabled", true,
                "Play allied AI flight traffic: nearby friendly kills, ejections and thanks.");
            EnemySigintEnabled = config.Bind(section, "EnemySigintEnabled", true,
                "Play intercepted enemy pilot transmissions (SIGINT).");
        }

        public bool Allows(ChatterChannel channel)
        {
            switch (channel)
            {
                case ChatterChannel.Awacs: return AwacsEnabled.Value;
                case ChatterChannel.Ground: return GroundForcesEnabled.Value;
                case ChatterChannel.Naval: return NavalEnabled.Value;
                case ChatterChannel.Command: return CommandEnabled.Value;
                case ChatterChannel.Tower: return TowerEnabled.Value;
                case ChatterChannel.Wing: return WingmenEnabled.Value;
                case ChatterChannel.Allied: return AlliedFlightsEnabled.Value;
                case ChatterChannel.Enemy: return EnemySigintEnabled.Value;
                default: return true;
            }
        }
    }
}
