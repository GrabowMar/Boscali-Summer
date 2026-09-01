using BepInEx.Configuration;

namespace BoscaliSummer.Infrastructure.Diagnostics
{
    internal sealed class DiagnosticSettings
    {
        public readonly ConfigEntry<bool> VerboseLogging;

        public DiagnosticSettings(ConfigFile config)
        {
            VerboseLogging = config.Bind("Debug", "VerboseLogging", false,
                "Log bounded runtime diagnostics and individual feature events.");
        }
    }
}
