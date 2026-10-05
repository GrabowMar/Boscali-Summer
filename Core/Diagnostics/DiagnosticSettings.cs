using BepInEx.Configuration;

namespace BoscaliSummer.Core.Diagnostics
{
    internal sealed class DiagnosticSettings
    {
        public readonly ConfigEntry<bool> VerboseLogging;
        public readonly ConfigEntry<bool> BypassRequirements;
        public readonly ConfigEntry<bool> DisableOpsCooldowns;
        public readonly ConfigEntry<bool> FootprintProfiler;

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
            DisableOpsCooldowns = config.Bind("Debug", "DisableOpsCooldowns", false,
                "DEBUG CHEAT: Disable loading times (cooldowns) between abilities in OPS, " +
                "allowing consecutive support requests without waiting. " +
                "Host-authoritative: on a server, only the host's value decides what is allowed.");
            FootprintProfiler = config.Bind("Debug", "FootprintProfiler", false,
                "DEVELOPER TOOL: measure what Boscali itself costs per frame, per module and per " +
                "method (read it with `nomod bridge footprint` or the BepInEx log). Wraps every " +
                "Boscali Update and patch while on, which itself costs a little; free when off. " +
                "Applies live. Client-local.");
            if (FootprintProfiler.Value) NOModKitTelemetry.StartWhenReady(deep: false);
            FootprintProfiler.SettingChanged += (_, __) =>
            {
                if (FootprintProfiler.Value) Diagnostics.FootprintProfiler.Start(deep: false);
                else Diagnostics.FootprintProfiler.Stop();
            };
        }
    }
}
