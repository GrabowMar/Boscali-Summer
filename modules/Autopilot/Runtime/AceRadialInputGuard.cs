using Rewired;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>
    /// Takes the mouse and the keyboard away from flying while the interaction menu is open, the way ACE3's cursor menu freezes the
    /// camera and blocks firing. The game's own cursor flag frees the cursor and stops cockpit look; disabling the Rewired mouse stops
    /// mouse-aim steering, chase-camera pan and mouse-button weapons fire; disabling the Rewired keyboard keeps the menu's own keys from
    /// reaching the game (the saved default map binds Enter to Select, Backspace to Cancel (deselects targets), Space to Fire, Tab, the up and
    /// down arrows and Esc; the digits and left / right arrows are unbound). The menu itself reads UnityEngine.Input, which Rewired's
    /// enable switch does not touch. Everything is restored exactly as found by <see cref="Release"/>.
    /// </summary>
    internal static class AceRadialInputGuard
    {
        private static bool held;
        private static bool ownsCursorFlag;
        private static bool mouseWasEnabled = true;
        private static bool keyboardWasEnabled = true;

        public static bool Held => held;

        public static void Acquire()
        {
            if (held) return;
            held = true;
            ownsCursorFlag = !CursorManager.GetFlag(CursorFlags.SelectionMenu);
            if (ownsCursorFlag) CursorManager.SetFlag(CursorFlags.SelectionMenu, true);
            Mouse mouse = TryMouse();
            if (mouse != null) { mouseWasEnabled = mouse.enabled; mouse.enabled = false; }
            Keyboard keyboard = TryKeyboard();
            if (keyboard != null) { keyboardWasEnabled = keyboard.enabled; keyboard.enabled = false; }
        }

        public static void Release()
        {
            if (!held) return;
            held = false;
            Mouse mouse = TryMouse();
            if (mouse != null) mouse.enabled = mouseWasEnabled;
            Keyboard keyboard = TryKeyboard();
            if (keyboard != null) keyboard.enabled = keyboardWasEnabled;
            if (ownsCursorFlag) CursorManager.SetFlag(CursorFlags.SelectionMenu, false);
            ownsCursorFlag = false;
            UnityEngine.Cursor.visible = CursorManager.Visible;
        }

        /// <summary>HOLD mode flicks with a drawn reticle, so the OS cursor hides; LATCHED shows it again. Only while the guard is held.</summary>
        public static void SetCursorVisible(bool visible)
        {
            if (held) UnityEngine.Cursor.visible = visible;
        }

        private static Mouse TryMouse()
        {
            if (!ReInput.isReady || ReInput.controllers == null) return null;
            return ReInput.controllers.Mouse;
        }

        private static Keyboard TryKeyboard()
        {
            if (!ReInput.isReady || ReInput.controllers == null) return null;
            return ReInput.controllers.Keyboard;
        }
    }

    /// <summary>
    /// Esc closes the menu and must not also pause the game: <c>GameplayUI.Update</c> pauses on the Pause action or a raw Escape press unless
    /// <c>GameplayUI.AllowPauseKeybind</c> is false. The flag is cleared while the menu is open and given back 0.25 s after it closes, so the
    /// Escape that closed it has passed through <c>GameplayUI.Update</c> first (the chat box does the same with 100 ms).
    /// </summary>
    internal static class AceRadialPauseGate
    {
        private const float RestoreDelay = 0.25f;
        private static bool holding;
        private static float restoreAt;

        public static void Hold()
        {
            holding = true; restoreAt = 0f;
            GameplayUI.AllowPauseKeybind = false;
        }

        public static void Release(float now)
        {
            if (holding && restoreAt <= 0f) restoreAt = now + RestoreDelay;
        }

        public static void Tick(float now)
        {
            if (holding && restoreAt > 0f && now >= restoreAt) ForceRelease();
        }

        public static void ForceRelease()
        {
            if (holding) GameplayUI.AllowPauseKeybind = true;
            holding = false; restoreAt = 0f;
        }
    }
}
