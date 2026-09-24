using BepInEx.Configuration;
using UnityEngine;

namespace BoscaliSummer.Features.QoL.Configuration
{
    internal sealed class QoLSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> ObservationEnabled { get; }
        public ConfigEntry<KeyCode> MarkCameraKey { get; }
        public QoLSettings(ConfigFile config)
        {
            Enabled = config.Bind("QoL", "Enabled", true,
                "Enable local quality-of-life features at startup, independently of Support and Progression.");
            ObservationEnabled = config.Bind("QoL", "CameraMarks", true,
                "Mark the surface at the centre of the live native camera. One local point, expires after 120 seconds. No laser or tracking changes.");
            MarkCameraKey = config.Bind("QoL", "MarkCameraKey", KeyCode.F8,
                "Capture a camera observation; ignores text entry and pause. None disables the shortcut. Also available on TGT CAMERA.");


        }
    }
}
