using System;
using BoscaliSummer.Modules.Vanguard.Configuration;
using NuclearOption.MissionEditorScripts;
using Rewired;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>Samples the drone seat's own bindings without running any aircraft input readers.</summary>
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
        private float throttle, lastJoystickThrottle;
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

        internal bool ReturnPressed => Application.isFocused && Pressed(settings.Release.Value);

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
            if (lastJoystick != joystick)
            {
                lastJoystick = joystick;
                lastJoystickThrottle = float.NaN;
                fireArmed = false;
            }

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            bool mouseControl = Held(settings.Orbit.Value);
            Vector2 mouseDelta = mouseControl ? MouseDelta() : Vector2.zero;
            mouseStick = mouseControl && !thirdPerson
                ? Vector2.ClampMagnitude(mouseStick + mouseDelta / 35f, 1f)
                : Vector2.zero;

            float pitch = Steer(joystick, settings.JoystickPitch.Value, settings.PitchDown.Value, settings.PitchUp.Value, mouseStick.y);
            float yaw = Steer(joystick, settings.JoystickYaw.Value, settings.YawRight.Value, settings.YawLeft.Value);
            float roll = Steer(joystick, settings.JoystickRoll.Value, settings.RollRight.Value, settings.RollLeft.Value, mouseStick.x);
            UpdateThrottle(joystick, dt);

            int fireButton = settings.JoystickFire.Value;
            bool fireHeld = Held(settings.Fire.Value) || (joystick != null && fireButton > 0 &&
                fireButton <= joystick.buttonCount && joystick.GetButton(fireButton - 1));
            bool cycle = Pressed(settings.CycleWeapon.Value);
            if (cycle) fireArmed = false;
            if (!fireHeld) fireArmed = true;
            return new Frame
            {
                Enabled = true,
                Axes = new Vector3(pitch, yaw, roll),
                Throttle = throttle,
                Fire = fireHeld && fireArmed,
                CycleWeapon = cycle,
                ToggleView = Pressed(settings.View.Value),
                GunView = !thirdPerson && Held(settings.GunView.Value),
                LookDelta = thirdPerson ? mouseDelta : Vector2.zero,
                Center = Pressed(settings.Center.Value),
                Zoom = thirdPerson ? Mathf.Clamp(Input.mouseScrollDelta.y, -4f, 4f) : 0f
            };
        }

        private void Suspend()
        {
            lastJoystick = null;
            lastJoystickThrottle = float.NaN;
            mouseStick = Vector2.zero;
            fireArmed = false;
        }

        private void UpdateThrottle(Joystick joystick, float dt)
        {
            if (TryAxis(joystick, settings.JoystickThrottle.Value, out float analog))
            {
                // A lever left at the aircraft's setting must move before it takes over the drone throttle.
                if (float.IsNaN(lastJoystickThrottle)) lastJoystickThrottle = analog;
                else if (Mathf.Abs(analog - lastJoystickThrottle) >= 0.035f)
                {
                    throttle = (analog + 1f) * 0.5f;
                    lastJoystickThrottle = analog;
                }
            }
            else lastJoystickThrottle = float.NaN;

            throttle = Mathf.Clamp01(throttle + KeyAxis(settings.ThrottleUp.Value, settings.ThrottleDown.Value) * dt * 0.6f);
        }

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

        private static float Steer(Joystick stick, int axis, KeyCode positive, KeyCode negative, float mouse = 0f)
        {
            if (Held(positive) || Held(negative)) return KeyAxis(positive, negative);
            if (TryAxis(stick, axis, out float value) && Mathf.Abs(value) > 0.06f) return value;
            return mouse;
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
