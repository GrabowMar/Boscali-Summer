using BepInEx.Configuration;

namespace BoscaliSummer.Infrastructure.Diagnostics
{
    internal sealed class DiagnosticSettings
    {
        public readonly ConfigEntry<bool> VerboseLogging;
        public readonly ConfigEntry<bool> BypassRequirements;

        public DiagnosticSettings(ConfigFile config)
        {
            VerboseLogging = config.Bind("Debug", "VerboseLogging", false,
                "Log bounded runtime diagnostics and individual feature events. " +
                "Client-local: affects this machine's log only.");
            BypassRequirements = config.Bind("Debug", "BypassRequirements", false,
                "TESTING AID, NOT A PLAY MODE. Grants every perk for free, authorises every " +
                "support action, and charges no allocation, so the perk board shows FREE and no " +
                "point is ever spent. Leave this false for normal play. " +
                "Host-authoritative: on a server, only the host's value decides what is allowed.");
        }
    }
}
