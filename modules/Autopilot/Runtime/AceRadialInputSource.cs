using System.Collections.Generic;
using BoscaliSummer.Modules.Autopilot.Domain;
using Rewired;
using UnityEngine;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>
    /// One frame of menu input: keyboard and mouse from UnityEngine.Input (the Rewired keyboard and mouse are switched off while the menu is open,
    /// see <see cref="AceRadialInputGuard"/>), and the controller from the vanilla Rewired actions the native wheel uses: the "Radial Menu" button
    /// opens and confirms, "Radial Menu Horizontal" / "Radial Menu Vertical" steer. They follow the game's own Radial Control setting: a stick
    /// (x = Horizontal, and Vertical already reads negative for stick-up, which is "up" in menu pixels) or, in button mode, four hat steps.
    /// Because the Rewired keyboard is off, a keyboard or mouse binding of "Radial Menu" is read raw from its key code. Step repeat is 350 ms then 100 ms.
    /// </summary>
    internal sealed class AceRadialInputSource
    {
        public const string PadHorizontal = "Radial Menu Horizontal", PadVertical = "Radial Menu Vertical", PadPress = "Radial Menu";
        private const float RepeatDelay = 0.35f, RepeatEvery = 0.1f;
        private const int ButtonMode = 2; // PlayerSettings.radialControl: 0 stick, 1 mouse, 2 buttons

        private sealed class Repeater
        {
            private bool down;
            private float next;

            public bool Step(bool held, float now)
            {
                if (!held) { down = false; return false; }
                if (!down) { down = true; next = now + RepeatDelay; return true; }
                if (now < next) return false;
                next = now + RepeatEvery;
                return true;
            }

            public void Reset() => down = false;
        }

        private readonly Repeater left = new Repeater(), right = new Repeater(), up = new Repeater(), down = new Repeater();
        private readonly Repeater hatLeft = new Repeater(), hatRight = new Repeater(), hatUp = new Repeater(), hatDown = new Repeater();
        private readonly List<KeyCode> radialKeys = new List<KeyCode>(2);
        private readonly List<int> radialMouse = new List<int>(1);
        private Vector2 lastMouse;

        /// <summary>Call on the frame the menu opens.</summary>
        public void Prime()
        {
            lastMouse = Input.mousePosition;
            left.Reset(); right.Reset(); up.Reset(); down.Reset();
            hatLeft.Reset(); hatRight.Reset(); hatUp.Reset(); hatDown.Reset();
        }

        /// <summary>Reads which keys and mouse buttons the player bound to "Radial Menu"; call when that button goes down (bindings can change in the pause menu).</summary>
        public void ResolveRadialBinding()
        {
            radialKeys.Clear(); radialMouse.Clear();
            Player pad = GameManager.playerInput;
            if (pad == null) return;
            foreach (ActionElementMap map in pad.controllers.maps.ButtonMapsWithAction(ControllerType.Keyboard, PadPress, true))
                if (map.keyCode != KeyCode.None) radialKeys.Add(map.keyCode);
            foreach (ActionElementMap map in pad.controllers.maps.ButtonMapsWithAction(ControllerType.Mouse, PadPress, true))
                if (map.elementIdentifierId >= 3) radialMouse.Add(map.elementIdentifierId - 3); // Rewired mouse: 0-2 are the axes, 3 is the left button
        }

        private bool RadialRawHeld()
        {
            foreach (KeyCode k in radialKeys) if (Input.GetKey(k)) return true;
            foreach (int b in radialMouse) if (Input.GetMouseButton(b)) return true;
            return false;
        }

        /// <summary>The "Radial Menu" button is down on any device, whether or not the Rewired keyboard is switched off.</summary>
        public bool RadialHeld()
        {
            Player pad = GameManager.playerInput;
            return RadialRawHeld() || (pad != null && pad.GetButton(PadPress));
        }

        public bool OpenHeld(KeyCode openKey, bool radial) => (openKey != KeyCode.None && Input.GetKey(openKey)) || (radial && RadialHeld());

        public MenuInput Sample(KeyCode openKey, bool radial, bool canOpen, float now)
        {
            MenuInput i = MenuInput.Idle(now);
            bool keyHeld = openKey != KeyCode.None && Input.GetKey(openKey), rawHeld = radial && RadialRawHeld();
            i.CanOpen = canOpen;

            Vector2 mouse = Input.mousePosition;
            i.PointerX = mouse.x; i.PointerY = Screen.height - mouse.y;
            i.DeltaX = mouse.x - lastMouse.x; i.DeltaY = -(mouse.y - lastMouse.y);
            lastMouse = mouse;
            i.PointerMoved = i.DeltaX != 0f || i.DeltaY != 0f;

            for (int n = 0; n <= 9; n++)
            {
                KeyCode key = KeyCode.Alpha0 + n, numpad = KeyCode.Keypad0 + n; // the numpad counts too
                if (Input.GetKeyDown(key) || Input.GetKeyDown(numpad)) i.Digit = n;
                if (Input.GetKey(key) || Input.GetKey(numpad)) i.DigitHeld = n;
            }
            i.Left = left.Step(Input.GetKey(KeyCode.LeftArrow), now);
            i.Right = right.Step(Input.GetKey(KeyCode.RightArrow), now);
            i.Up = up.Step(Input.GetKey(KeyCode.UpArrow), now);
            i.Down = down.Step(Input.GetKey(KeyCode.DownArrow), now);

            i.EnterHeld = Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter);
            i.SpaceHeld = Input.GetKey(KeyCode.Space);
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { i.Press = true; i.PressSource = PressSource.Enter; }
            else if (Input.GetKeyDown(KeyCode.Space)) { i.Press = true; i.PressSource = PressSource.Space; }
            i.Back = Input.GetKeyDown(KeyCode.Backspace);
            i.Esc = Input.GetKeyDown(KeyCode.Escape);

            float wheel = Input.mouseScrollDelta.y;
            i.Wheel = wheel > 0f ? -1 : wheel < 0f ? 1 : 0;
            i.Rmb = Input.GetMouseButtonDown(1);
            i.Lmb = Input.GetMouseButtonDown(0);
            i.LmbHeld = Input.GetMouseButton(0);
            if (i.Lmb) i.PointerMoved = true;

            Player pad = GameManager.playerInput;
            bool padHeld = false;
            if (pad != null && radial)
            {
                // a keyboard or mouse binding also reads through Rewired until the guard takes those devices: only a button with no raw key behind it is a controller
                padHeld = !rawHeld && pad.GetButton(PadPress);
                i.PadAHeld = padHeld;
                if (padHeld && pad.GetButtonDown(PadPress)) { i.Press = true; i.PressSource = PressSource.Pad; i.FromPad = true; }
                if (PlayerSettings.radialControl >= ButtonMode)
                {
                    // the wheel's own button mode: positive Vertical is up, positive Horizontal is right
                    bool l = hatLeft.Step(pad.GetNegativeButton(PadHorizontal), now), r = hatRight.Step(pad.GetButton(PadHorizontal), now);
                    bool u = hatUp.Step(pad.GetButton(PadVertical), now), d = hatDown.Step(pad.GetNegativeButton(PadVertical), now);
                    if (l || r || u || d) { i.Left |= l; i.Right |= r; i.Up |= u; i.Down |= d; i.FromPad = true; }
                }
                else { i.StickX = pad.GetAxis(PadHorizontal); i.StickY = pad.GetAxis(PadVertical); }
            }
            i.CHeld = keyHeld || rawHeld || padHeld;
            if (padHeld && !keyHeld) i.FromPad = true;
            return i;
        }

        /// <summary>True while any key or button the menu consumes is still down: the Rewired keyboard and mouse are only handed back after that, or the release would read as a fresh press.</summary>
        public bool AnyMenuInputHeld(KeyCode openKey, bool radial)
        {
            if (Input.GetMouseButton(0) || Input.GetMouseButton(1)) return true;
            if (openKey != KeyCode.None && Input.GetKey(openKey)) return true;
            if (radial && RadialRawHeld()) return true;
            foreach (KeyCode k in HeldKeys) if (Input.GetKey(k)) return true;
            for (int n = 0; n <= 9; n++) if (Input.GetKey(KeyCode.Alpha0 + n) || Input.GetKey(KeyCode.Keypad0 + n)) return true;
            return false;
        }

        private static readonly KeyCode[] HeldKeys =
        {
            KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Space, KeyCode.Backspace, KeyCode.Escape,
            KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.UpArrow, KeyCode.DownArrow,
        };
    }
}
