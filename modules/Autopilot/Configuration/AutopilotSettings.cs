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
        public ConfigEntry<bool> ReplaceNativeWheel { get; }
        public ConfigEntry<bool> ChaffOnCountermeasureKey { get; }
        public ConfigEntry<UnityEngine.KeyCode> ChaffKey { get; }

        public AutopilotSettings(ConfigFile config)
        {
            Enabled = config.Bind("Autopilot", "Enabled", true,
                "Local ownship autopilot landing from the native radial menu (Boscali Summer > " +
                "Autopilot). Client-local: flies only your aircraft with the game's own autopilot, " +
                "never commands other aircraft, and yields to manual stick input.");
            IlsHud = config.Bind("Autopilot", "IlsHud", true,
                "When landing gear is down near a field, show the APPROACH page (runway funnel, " +
                "localizer / glideslope, speed) on the HUD's TAC BOARD while you fly the approach by hand.");
            GlideslopeDegrees = config.Bind("Autopilot", "GlideslopeDegrees", IlsGuidance.DefaultSlope,
                new ConfigDescription(
                    "ILS glideslope angle in degrees for the hand-flown APPROACH page (the landing autopilot flies its own slope).",
                    new AcceptableValueRange<float>(IlsGuidance.MinSlope, IlsGuidance.MaxSlope)));
            ReplaceNativeWheel = config.Bind("Autopilot", "InteractionMenuReplacesRadial", true,
                "The interaction menu opens on the game's own Radial Menu binding (keyboard, HOTAS or gamepad) and " +
                "the native wheel stays closed. Hold the binding, point at a category and a row, release to run it; " +
                "release at the centre to keep the menu open for clicks. A quick tap is left to the game.");
            // The game ships no keyboard binding for Radial Menu (only a mouse button and the gamepad), so the keyboard needs this key of its own.
            AceRadialKey = config.Bind("Autopilot", "AceRadialKey", UnityEngine.KeyCode.C,
                "Keyboard key that opens the interaction menu at once (default C; None leaves only the Radial Menu binding). " +
                "Tap it to keep the menu open for clicks, or hold it, point at a row and release.");
            ChaffOnCountermeasureKey = config.Bind("Autopilot", "ChaffOnCountermeasureKey", true,
                "Your aircraft carries a chaff dispenser (single player and hosting). On: holding the game's countermeasure button " +
                "also dispenses chaff while a radar-guided missile is inbound. Off: chaff only from the interaction menu (DEFENCE > POP CHAFF) or ChaffKey. " +
                "Chaff dazzles a radar seeker most when you fly across its line of sight, and does nothing head-on.");
            ChaffKey = config.Bind("Autopilot", "ChaffKey", UnityEngine.KeyCode.None,
                "Keyboard key that pops one chaff salvo (default None: use the countermeasure button or the interaction menu).");
        }

        public float Slope => IlsGuidance.ClampSlope(GlideslopeDegrees.Value);
    }
}
