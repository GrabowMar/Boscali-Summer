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
        public ConfigEntry<bool> OnRadialBinding { get; }
        public ConfigEntry<UnityEngine.KeyCode> ChaffKey { get; }
        public ConfigEntry<bool> CinematicCamera { get; }

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
            // A new key name on purpose (GitHub issue 11): the old InteractionMenuReplacesRadial was on by default and is saved as on in every
            // config written since, and taking the game's wheel away breaks other mods that add to it.
            OnRadialBinding = config.Bind("Autopilot", "InteractionMenuOnRadialBinding", false,
                "Off (default): the game's Radial Menu binding opens the game's own wheel, and the interaction menu opens only on AceRadialKey. " +
                "On: the interaction menu takes that binding (HOTAS or gamepad button) and the native wheel stays closed; other mods that add " +
                "entries to the native wheel are then out of reach.");
            // The game ships no keyboard binding for Radial Menu (only a mouse button and the gamepad), so the keyboard needs this key of its own.
            AceRadialKey = config.Bind("Autopilot", "AceRadialKey", UnityEngine.KeyCode.C,
                "Keyboard key that opens the interaction menu at once (default C; None closes this way in). " +
                "Also set from SET > THIS PILOT > COCKPIT > KEYS or the mission prompt's CONTROLS card. " +
                "Tap it to keep the menu open for clicks, or hold it, point at a row and release.");
            ChaffKey = config.Bind("Autopilot", "ChaffKey", UnityEngine.KeyCode.None,
                "Keyboard key that pops one chaff salvo (default None: use the countermeasure button or the interaction menu).");
            CinematicCamera = config.Bind("Autopilot", "CinematicCamera", true,
                "On (default): Switch View goes cockpit, external, cinematic. Off: the cinematic camera is skipped and external goes straight back to the cockpit. " +
                "Also switched from the interaction menu (VIEW > CINEMATIC CAMERA).");
        }

        public float Slope => IlsGuidance.ClampSlope(GlideslopeDegrees.Value);
    }
}
