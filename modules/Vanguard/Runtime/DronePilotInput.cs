using System;
using BoscaliSummer.Modules.Vanguard.Configuration;
using NuclearOption.MissionEditorScripts;
using Rewired;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>Reads drone keys and the player's mapped hardware actions without running the aircraft pilot.</summary>
    internal sealed class DronePilotInput
    {
        internal struct Frame
        {
            public Vector3 Axes;
            public Vector2 LookDelta;
            public float Throttle, Zoom;
            public bool Enabled, Fire, CycleWeapon, ToggleView, GunView, Center;
        }

        private const CursorFlags PanelOwners = CursorFlags.GameMenu | CursorFlags.Map | CursorFlags.SelectionMenu |
            CursorFlags.Dialogue | CursorFlags.Chat | CursorFlags.Loading;
        private readonly DroneControlSettings settings;
        private Joystick lastJoystick;
        private Vector2 mouseStick;
        private float throttle, lastHardwareThrottle;
        private bool fireArmed;

        internal DronePilotInput(DroneControlSettings settings) => this.settings = settings;

        private static bool RadialOpen => SceneSingleton<RadialMenuMain>.i != null && RadialMenuMain.IsInUse();
        internal static bool CanReturn => !InputFieldChecker.InsideInputField && !RadialOpen;
        // The multiplayer scoreboard object stays active inside a hidden menu canvas.
        internal static bool GameplayOpen => CanReturn && !GameplayUI.GameIsPaused && !DynamicMap.mapMaximized;

        internal static bool PointerHeld => Input.GetKey(KeyCode.LeftAlt)
#if !PUBLIC_RELEASE
            || TestPointer
#endif
            ;
        internal static bool CanUseHudPointer => GameplayOpen && (CursorManager.GetFlags() & PanelOwners) == 0;
        internal static bool CanRead => Application.isFocused && !PointerHeld && !Cursor.visible && CanUseHudPointer;
#if !PUBLIC_RELEASE
        internal static bool TestPointer { get; set; }
#endif

        internal bool ReturnPressed => Application.isFocused && (Pressed(settings.Release.Value) ||
            CanRead && ButtonDown(MappedPlayer(), "Cancel"));

        internal void Reset(float initialThrottle)
        {
            throttle = Mathf.Clamp01(initialThrottle);
            Suspend();
        }

        internal Frame Tick(bool thirdPerson)
        {
            if (!CanRead)
            {
                Suspend();
                return new Frame { Throttle = throttle };
            }

            Joystick joystick = FindJoystick();
            Player mapped = MappedPlayer();
            if (lastJoystick != joystick)
            {
                lastJoystick = joystick;
                lastHardwareThrottle = float.NaN;
                fireArmed = false;
            }

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            bool mouseControl = Held(settings.Orbit.Value);
            Vector2 mouseDelta = mouseControl ? MouseDelta() : Vector2.zero;
            mouseStick = mouseControl && !thirdPerson
                ? Vector2.ClampMagnitude(mouseStick + mouseDelta / 35f, 1f)
                : Vector2.zero;

            float pitch = Steer(joystick, settings.JoystickPitch.Value, settings.PitchDown.Value, settings.PitchUp.Value,
                Axis(mapped, "Pitch"), mouseStick.y);
            float yaw = Steer(joystick, settings.JoystickYaw.Value, settings.YawRight.Value, settings.YawLeft.Value, Axis(mapped, "Yaw"));
            float roll = Steer(joystick, settings.JoystickRoll.Value, settings.RollRight.Value, settings.RollLeft.Value,
                Axis(mapped, "Roll"), mouseStick.x);
            UpdateThrottle(joystick, mapped, dt);

            int fireButton = settings.JoystickFire.Value;
            bool fireHeld = Held(settings.Fire.Value) || Button(mapped, "Fire") || (joystick != null && fireButton > 0 &&
                fireButton <= joystick.buttonCount && joystick.GetButton(fireButton - 1));
            bool cycle = Pressed(settings.CycleWeapon.Value) || ButtonDown(mapped, "Next Weapon") || ButtonDown(mapped, "Previous Weapon");
            if (cycle) fireArmed = false;
            if (!fireHeld) fireArmed = true;
            Vector2 orbit = Vector2.zero;
            float zoom = 0f;
            if (thirdPerson)
            {
                float tilt = PlayerSettings.viewInvertPitch ? -1f : 1f;
                orbit = mouseDelta + new Vector2(Axis(mapped, "Pan View"), Axis(mapped, "Tilt View") * tilt) * (90f * dt);
                zoom = Mathf.Clamp(Input.mouseScrollDelta.y, -4f, 4f) + Axis(mapped, "Zoom View") * (8f * dt);
            }
            return new Frame
            {
                Enabled = true,
                Axes = new Vector3(pitch, yaw, roll),
                Throttle = throttle,
                Fire = fireHeld && fireArmed,
                CycleWeapon = cycle,
                ToggleView = Pressed(settings.View.Value) || ButtonDown(mapped, "Switch View"),
                GunView = !thirdPerson && (Held(settings.GunView.Value) || Button(mapped, "Cycle Look At")),
                LookDelta = orbit,
                Center = Pressed(settings.Center.Value) || ButtonDown(mapped, "Center"),
                Zoom = zoom
            };
        }

        private void Suspend()
        {
            lastJoystick = null;
            lastHardwareThrottle = float.NaN;
            mouseStick = Vector2.zero;
            fireArmed = false;
        }

        private void UpdateThrottle(Joystick joystick, Player mapped, float dt)
        {
            float change = KeyAxis(settings.ThrottleUp.Value, settings.ThrottleDown.Value);
            if (TryAxis(joystick, settings.JoystickThrottle.Value, out float analog))
                TrackAbsoluteThrottle(analog, signed: true);
            else if (mapped != null && !mapped.GetButton("Axis Modifier"))
            {
                analog = mapped.GetAxisRaw("Throttle");
                bool fromHardware = ThrottleSource(mapped, out bool button);
                if (PlayerSettings.throttleUseRelative || button)
                {
                    lastHardwareThrottle = float.NaN;
                    if (fromHardware && Mathf.Abs(analog) > 0.1f) change += Mathf.Sign(analog);
                }
                // A centered lever may stop appearing as a current input source. Preserve the transition to zero.
                else if (fromHardware || analog == 0f) TrackAbsoluteThrottle(fromHardware ? analog : 0f, PlayerSettings.throttleUseNegative);
            }
            else lastHardwareThrottle = float.NaN;

            throttle = Mathf.Clamp01(throttle + Mathf.Clamp(change, -1f, 1f) * dt * 0.6f);
        }

        private void TrackAbsoluteThrottle(float analog, bool signed)
        {
            // A lever left at the aircraft's setting must move before taking over the drone throttle.
            if (float.IsNaN(lastHardwareThrottle)) lastHardwareThrottle = analog;
            else if (Mathf.Abs(analog - lastHardwareThrottle) >= 0.035f)
            {
                throttle = Mathf.Clamp01(signed ? (analog + 1f) * 0.5f : analog);
                lastHardwareThrottle = analog;
            }
        }

        private static Player MappedPlayer()
        {
            Player player = GameManager.playerInput;
            return player != null && ReInput.isReady && (player.controllers.joystickCount > 0 || player.controllers.customControllerCount > 0)
                ? player : null;
        }

        private static bool HardwareSource(Player player, string action) =>
            player.IsCurrentInputSource(action, ControllerType.Joystick) || player.IsCurrentInputSource(action, ControllerType.Custom);

        private static bool ThrottleSource(Player player, out bool button)
        {
            button = false;
            var sources = player.GetCurrentInputSources("Throttle");
            if (sources == null) return false;
            for (int i = 0; i < sources.Count; i++)
            {
                InputActionSourceData source = sources[i];
                if (source.controllerType != ControllerType.Joystick && source.controllerType != ControllerType.Custom) continue;
                button = source.actionElementMap.elementType == ControllerElementType.Button;
                return true;
            }
            return false;
        }

        private static float Axis(Player player, string action)
        {
            if (player == null) return 0f;
            float value = player.GetAxis(action);
            return Mathf.Abs(value) > 0.001f && HardwareSource(player, action) ? Mathf.Clamp(value, -1f, 1f) : 0f;
        }

        private static bool Button(Player player, string action) => player != null && player.GetButton(action) && HardwareSource(player, action);
        private static bool ButtonDown(Player player, string action) => player != null && player.GetButtonDown(action) && HardwareSource(player, action);

        private Joystick FindJoystick()
        {
            string name = settings.JoystickDevice.Value;
            if (string.IsNullOrWhiteSpace(name) || !ReInput.isReady || ReInput.controllers == null) return null;
            var sticks = ReInput.controllers.Joysticks;
            for (int i = 0; i < sticks.Count; i++)
            {
                Joystick stick = sticks[i];
                if (stick != null && stick.enabled && string.Equals(stick.name, name, StringComparison.OrdinalIgnoreCase)) return stick;
            }
            return null;
        }

        private static Vector2 MouseDelta()
        {
            Mouse mouse = ReInput.isReady && ReInput.controllers != null ? ReInput.controllers.Mouse : null;
            if (mouse == null || !mouse.enabled || mouse.axisCount < 2) return Vector2.zero;
            var delta = new Vector2(mouse.GetAxisRaw(0), -mouse.GetAxisRaw(1));
            return Vector2.ClampMagnitude(delta * 0.45f, 25f);
        }

        private static float Steer(Joystick stick, int axis, KeyCode positive, KeyCode negative, float mapped, float mouse = 0f)
        {
            if (Held(positive) || Held(negative)) return KeyAxis(positive, negative);
            if (TryAxis(stick, axis, out float value)) return Mathf.Abs(value) > 0.06f ? value : mouse;
            return mapped != 0f ? mapped : mouse;
        }

        private static bool TryAxis(Joystick joystick, int binding, out float value)
        {
            value = 0f;
            if (joystick == null || binding == 0 || binding < -joystick.axisCount || binding > joystick.axisCount) return false;
            value = joystick.GetAxis(Math.Abs(binding) - 1) * Math.Sign(binding);
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static int KeyAxis(KeyCode positive, KeyCode negative) => (Held(positive) ? 1 : 0) - (Held(negative) ? 1 : 0);
        private static bool Held(KeyCode key) => key != KeyCode.None && Input.GetKey(key);
        private static bool Pressed(KeyCode key) => key != KeyCode.None && Input.GetKeyDown(key);
    }
}
