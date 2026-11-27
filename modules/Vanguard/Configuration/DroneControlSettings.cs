using BepInEx.Configuration;
using BoscaliSummer.Core.Modules;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Configuration
{
    internal sealed class DroneControlSettings
    {
        public ConfigEntry<KeyCode> PitchUp { get; }
        public ConfigEntry<KeyCode> PitchDown { get; }
        public ConfigEntry<KeyCode> RollLeft { get; }
        public ConfigEntry<KeyCode> RollRight { get; }
        public ConfigEntry<KeyCode> YawLeft { get; }
        public ConfigEntry<KeyCode> YawRight { get; }
        public ConfigEntry<KeyCode> ThrottleUp { get; }
        public ConfigEntry<KeyCode> ThrottleDown { get; }
        public ConfigEntry<KeyCode> Fire { get; }
        public ConfigEntry<KeyCode> CycleWeapon { get; }
        public ConfigEntry<KeyCode> View { get; }
        public ConfigEntry<KeyCode> Release { get; }
        public ConfigEntry<KeyCode> Orbit { get; }
        public ConfigEntry<KeyCode> Center { get; }
        public ConfigEntry<KeyCode> GunView { get; }
        public ConfigEntry<string> JoystickDevice { get; }
        public ConfigEntry<int> JoystickPitch { get; }
        public ConfigEntry<int> JoystickRoll { get; }
        public ConfigEntry<int> JoystickYaw { get; }
        public ConfigEntry<int> JoystickThrottle { get; }
        public ConfigEntry<int> JoystickFire { get; }

        internal DroneControlSettings(ConfigFile config)
        {
            PitchUp = config.Bind("DroneControls", "PitchUp", KeyCode.S, "Pitch the drone nose up.");
            PitchDown = config.Bind("DroneControls", "PitchDown", KeyCode.W, "Pitch the drone nose down.");
            RollLeft = config.Bind("DroneControls", "RollLeft", KeyCode.A, "Bank the drone left.");
            RollRight = config.Bind("DroneControls", "RollRight", KeyCode.D, "Bank the drone right.");
            YawLeft = config.Bind("DroneControls", "YawLeft", KeyCode.Q, "Yaw the drone left.");
            YawRight = config.Bind("DroneControls", "YawRight", KeyCode.E, "Yaw the drone right.");
            ThrottleUp = config.Bind("DroneControls", "ThrottleUp", KeyCode.LeftShift, "Increase the drone's throttle while held.");
            ThrottleDown = config.Bind("DroneControls", "ThrottleDown", KeyCode.LeftControl, "Decrease the drone's throttle while held.");
            Fire = config.Bind("DroneControls", "Fire", KeyCode.Mouse0, "Fire the drone's selected weapon.");
            CycleWeapon = config.Bind("DroneControls", "CycleWeapon", KeyCode.X, "Switch the selected drone weapon.");
            View = config.Bind("DroneControls", "View", KeyCode.End, "Switch between the drone's forward and chase views.");
            Release = config.Bind("DroneControls", "Release", KeyCode.Backspace, "Return to your aircraft and release the drone to its autopilot.");
            Orbit = config.Bind("DroneControls", "MouseControl", KeyCode.Mouse1, "Hold to steer the drone with the mouse in forward view or orbit in chase view.");
            Center = config.Bind("DroneControls", "CenterView", KeyCode.Home, "Center the drone's chase camera and reset its zoom.");
            GunView = config.Bind("DroneControls", "GunView", KeyCode.G, "Hold to sight along a drone's port cannon in forward view.");
            JoystickDevice = config.Bind("DroneControls", "JoystickDevice", "",
                "Optional raw joystick override. Use its exact device name from the game's controls menu. Leave empty to use the game's mapped controller actions.");
            JoystickPitch = config.Bind("DroneControls", "JoystickPitch", 0, "Drone pitch axis by one-based device axis number. Use a negative number to invert it or 0 to disable it.");
            JoystickRoll = config.Bind("DroneControls", "JoystickRoll", 0, "Drone roll axis by one-based device axis number. Use a negative number to invert it or 0 to disable it.");
            JoystickYaw = config.Bind("DroneControls", "JoystickYaw", 0, "Drone yaw axis by one-based device axis number. Use a negative number to invert it or 0 to disable it.");
            JoystickThrottle = config.Bind("DroneControls", "JoystickThrottle", 0, "Absolute drone throttle by one-based device axis number. Move the axis to take over. Use a negative number to invert it or 0 to disable it.");
            JoystickFire = config.Bind("DroneControls", "JoystickFire", 0, "Drone trigger by one-based device button number. Use 0 to disable it.");
        }

        internal void Register(ModuleContext context)
        {
            Add("PITCH UP", PitchUp);
            Add("PITCH DOWN", PitchDown);
            Add("ROLL LEFT", RollLeft);
            Add("ROLL RIGHT", RollRight);
            Add("YAW LEFT", YawLeft);
            Add("YAW RIGHT", YawRight);
            Add("THROTTLE UP", ThrottleUp);
            Add("THROTTLE DOWN", ThrottleDown);
            Add("FIRE", Fire);
            Add("CYCLE WEAPON", CycleWeapon);
            Add("SWITCH VIEW", View);
            Add("RETURN", Release);
            Add("MOUSE CONTROL", Orbit);
            Add("CENTER VIEW", Center);
            Add("GUN VIEW", GunView);

            void Add(string label, ConfigEntry<KeyCode> key) =>
                context.AddClientKey("DRONE " + label, key.Description.Description, key, onPrompt: false);
        }
    }
}
