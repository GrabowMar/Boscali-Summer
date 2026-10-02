using BepInEx.Configuration;
using BoscaliSummer.Modules.Autopilot.Domain;

namespace BoscaliSummer.Modules.Autopilot.Configuration
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
                "Key for the ACE3-style interaction menu (default C; None disables it). Hold it, " +
                "move the cursor onto an option (branches open when you rest on them) and release " +
                "to run it. A quick tap keeps the menu open for mouse clicks; right-click closes.");
        }

        public float Slope => IlsGuidance.ClampSlope(GlideslopeDegrees.Value);
    }
}
