using BepInEx.Configuration;
using UnityEngine;

namespace BoscaliSummer.Features.QoL.Configuration
{
    internal sealed class QoLSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> GunAimAssist { get; }
        public ConfigEntry<float> GunAimAssistStrength { get; }
        public ConfigEntry<bool> ObservationEnabled { get; }
        public ConfigEntry<KeyCode> MarkCameraKey { get; }
        public ConfigEntry<bool> ThirdPersonHudEnabled { get; }
        public ConfigEntry<KeyCode> ThirdPersonHudKey { get; }
        public ConfigEntry<bool> ThirdPersonHidePitchLadder { get; }
        public ConfigEntry<bool> ThirdPersonCameraEnabled { get; }
        public ConfigEntry<bool> ThirdPersonFlightCameraEnabled { get; }

        public QoLSettings(ConfigFile config)
        {
            Enabled = config.Bind("QoL", "Enabled", true,
                "Enable local quality-of-life features at startup, independently of Support and Progression.");
            GunAimAssist = config.Bind("QoL", "GunAimAssist", false,
                "Experimental subtle pitch/yaw help within 2.5 degrees of the native gun lead cue. Selected, freshly tracked enemies and fixed guns only; flight assist must be on. Yields to steering. Disabled pending flight testing.");
            GunAimAssistStrength = config.Bind("QoL", "GunAimAssistStrength", 0.04f,
                new ConfigDescription("Maximum additional control input before cone/input falloff. Default 0.04; zero disables correction.",
                    new AcceptableValueRange<float>(0f, 0.08f)));
            ObservationEnabled = config.Bind("QoL", "CameraMarks", true,
                "Mark the surface at the centre of the live native camera. One local point, expires after 120 seconds. No laser or tracking changes.");
            MarkCameraKey = config.Bind("QoL", "MarkCameraKey", KeyCode.F8,
                "Capture a camera observation; ignores text entry and pause. None disables the shortcut. Also available in OPS.");

            // Retain the existing keys so moving ownership does not reset user preferences.
            ThirdPersonHudEnabled = config.Bind("Avionics", "ThirdPersonHudEnabled", true,
                "Keep tactical flight HUD visible in external orbit and chase camera views.");
            ThirdPersonCameraEnabled = config.Bind("Avionics", "ThirdPersonCameraEnabled", true,
                "Show the native target camera feed only while targets are selected in third person; no additional world rendering.");
            ThirdPersonFlightCameraEnabled = config.Bind("Avionics", "ThirdPersonFlightCameraEnabled", true,
                "Smooth aircraft-relative orbit/rear chase framing with a steady horizon and room above the aircraft for aiming. Native zoom, look-at and other chase presets remain available.");
            ThirdPersonHudKey = config.Bind("Avionics", "ThirdPersonHudKey", KeyCode.F7,
                "Hotkey to toggle third-person HUD visibility on the fly.");
            ThirdPersonHidePitchLadder = config.Bind("Avionics", "ThirdPersonHidePitchLadder", true,
                "Declutter: hide the floating pitch ladder in third person while keeping reticle, ammo, and radar.");
        }
    }
}
