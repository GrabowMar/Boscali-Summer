using BepInEx.Configuration;
using BoscaliSummer.Features.Autopilot.Domain;

namespace BoscaliSummer.Features.Autopilot.Configuration
{
    internal sealed class AutopilotSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> IlsHud { get; }
        public ConfigEntry<float> GlideslopeDegrees { get; }
        public ConfigEntry<UnityEngine.KeyCode> AceRadialKey { get; }

        public AutopilotSettings(ConfigFile config)
        {
            Enabled = config.Bind("Autopilot", "Enabled", true,
                "Local ownship autopilot landing from the native radial menu (Boscali Summer > " +
                "Autopilot). Client-local: flies only your aircraft with the game's own autopilot, " +
                "never commands other aircraft, and yields to manual stick input.");
            IlsHud = config.Bind("Autopilot", "IlsHud", true,
                "When landing gear is down, show localizer / glideslope and the field safe ring " +
                "as a line on the common HUD element.");
            GlideslopeDegrees = config.Bind("Autopilot", "GlideslopeDegrees", IlsGuidance.DefaultSlope,
                new ConfigDescription(
                    "ILS glideslope angle in degrees for the gear-down HUD and the landing autopilot.",
                    new AcceptableValueRange<float>(IlsGuidance.MinSlope, IlsGuidance.MaxSlope)));
            AceRadialKey = config.Bind("Autopilot", "AceRadialKey", UnityEngine.KeyCode.C,
                "Key to open the ACE3 radial interaction menu (default: C). " +
                "Hold and release to execute hovered command, or tap to open and mouse click.");
        }

        public float Slope => IlsGuidance.ClampSlope(GlideslopeDegrees.Value);
    }
}
