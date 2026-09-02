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
                "Log bounded runtime diagnostics and individual feature events.");
            BypassRequirements = config.Bind("Debug", "BypassRequirements", false,
                "Ignore rank, skill-point, prerequisite, and support-entitlement requirements. " +
                "Developer/testing aid; also removes support allocation cost.");
        }
    }
}
