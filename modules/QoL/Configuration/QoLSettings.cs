using BepInEx.Configuration;
using BoscaliSummer.Modules.QoL.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.QoL.Configuration
{
    internal sealed class QoLSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> ObservationEnabled { get; }
        public ConfigEntry<KeyCode> MarkCameraKey { get; }
        public ConfigEntry<NvgLook> NightVisionLook { get; }
        public QoLSettings(ConfigFile config)
        {
            Enabled = config.Bind("QoL", "Enabled", true,
                "Enable local quality-of-life features at startup, independently of Support and Progression.");
            ObservationEnabled = config.Bind("QoL", "CameraMarks", true,
                "Mark the surface at the centre of the live native camera. One local point, expires after 120 seconds. No laser or tracking changes.");
            MarkCameraKey = config.Bind("QoL", "MarkCameraKey", KeyCode.F8,
                "Capture a camera observation; ignores text entry and pause. None disables the shortcut. Also available on TGT CAMERA.");
            NightVisionLook = config.Bind("QoL", "NightVisionLook", NvgLook.Fused,
                "How night vision looks. Vanilla: the game's own green picture. WhitePhosphor: a white tube whose gain follows what you look at and dips for bright sources. Fused: the same, with an orange glow on hot units.");

        }
    }
}
