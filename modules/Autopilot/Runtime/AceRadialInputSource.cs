using BoscaliSummer.Modules.Autopilot.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>
    /// One frame of menu input: keyboard and mouse from UnityEngine.Input (the Rewired keyboard and mouse are switched off while the menu is open,
    /// see <see cref="AceRadialInputGuard"/>), and the stick plus a press from the vanilla Rewired actions the native wheel uses
    /// ("Radial Menu Horizontal" / "Radial Menu Vertical" axes, the "Radial Menu" button). Stick convention is the vanilla wheel's own:
    /// x = Horizontal, and the Vertical axis already reads negative for stick-up (the shipped map inverts it), which is "up" in menu pixels (y down).
    /// Arrow-key repeat is 350 ms then 100 ms.
    /// </summary>
    internal sealed class AceRadialInputSource
    {
        public const string PadHorizontal = "Radial Menu Horizontal", PadVertical = "Radial Menu Vertical", PadPress = "Radial Menu";
        private const float RepeatDelay = 0.35f, RepeatEvery = 0.1f;

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
        private Vector2 lastMouse;
        private int skipDeltaFrames;

        /// <summary>Call on the frame the menu opens: the cursor may jump when the game frees it, so the first frames report no travel.</summary>
        public void Prime()
        {
            lastMouse = Input.mousePosition;
            skipDeltaFrames = 2;
            left.Reset(); right.Reset(); up.Reset(); down.Reset();
        }

        public MenuInput Sample(KeyCode openKey, bool canOpen, float now)
        {
            MenuInput i = MenuInput.Idle(now);
            i.CHeld = openKey != KeyCode.None && Input.GetKey(openKey);
            i.CanOpen = canOpen;

            Vector2 mouse = Input.mousePosition;
            i.PointerX = mouse.x; i.PointerY = Screen.height - mouse.y;
            if (skipDeltaFrames > 0) skipDeltaFrames--;
            else { i.DeltaX = mouse.x - lastMouse.x; i.DeltaY = -(mouse.y - lastMouse.y); }
            lastMouse = mouse;
            i.PointerMoved = i.DeltaX != 0f || i.DeltaY != 0f;

            for (int n = 0; n <= 9; n++)
            {
                KeyCode key = KeyCode.Alpha0 + n;
                if (Input.GetKeyDown(key)) i.Digit = n;
                if (Input.GetKey(key)) i.DigitHeld = n;
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

            Rewired.Player pad = GameManager.playerInput;
            if (pad != null)
            {
                i.StickX = pad.GetAxis(PadHorizontal); i.StickY = pad.GetAxis(PadVertical);
                i.PadAHeld = pad.GetButton(PadPress);
                if (pad.GetButtonDown(PadPress)) { i.Press = true; i.PressSource = PressSource.Pad; i.FromPad = true; }
            }
            return i;
        }

        /// <summary>True while any key or button the menu consumes is still down: the Rewired keyboard and mouse are only handed back after that, or the release would read as a fresh press.</summary>
        public static bool AnyMenuInputHeld(KeyCode openKey)
        {
            if (Input.GetMouseButton(0) || Input.GetMouseButton(1)) return true;
            if (openKey != KeyCode.None && Input.GetKey(openKey)) return true;
            foreach (KeyCode k in HeldKeys) if (Input.GetKey(k)) return true;
            for (int n = 0; n <= 9; n++) if (Input.GetKey(KeyCode.Alpha0 + n)) return true;
            return false;
        }

        private static readonly KeyCode[] HeldKeys =
        {
            KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Space, KeyCode.Backspace, KeyCode.Escape,
            KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.UpArrow, KeyCode.DownArrow,
        };
    }
}
