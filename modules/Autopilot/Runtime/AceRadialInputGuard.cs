using Rewired;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>
    /// Takes the mouse and the keyboard away from flying while the interaction menu is open, the way ACE3's cursor menu freezes the
    /// camera and blocks firing. The game's own cursor flag frees the cursor and stops cockpit look; disabling the Rewired mouse stops
    /// mouse-aim steering, chase-camera pan and mouse-button weapons fire; disabling the Rewired keyboard keeps the menu's own keys from
    /// reaching the game (the saved default map binds Enter to Select, Backspace to Cancel (deselects targets), Space to Fire, Tab, the up and
    /// down arrows and Esc; the digits and left / right arrows are unbound). The menu itself reads UnityEngine.Input, which Rewired's
    /// enable switch does not touch. <see cref="Release"/> switches back on only the devices this guard switched off (the repo's lease rule: another
    /// window that already took one keeps it), so nothing is restored from a stale snapshot.
    /// </summary>
    internal static class AceRadialInputGuard
    {
        private static bool held;
        private static bool ownsCursorFlag;
        private static bool mouseTaken, keyboardTaken, cursorBlank;
        private static UnityEngine.Texture2D blank;

        public static bool Held => held;

        public static void Acquire()
        {
            if (held) return;
            held = true;
            ownsCursorFlag = !CursorManager.GetFlag(CursorFlags.SelectionMenu);
            if (ownsCursorFlag) CursorManager.SetFlag(CursorFlags.SelectionMenu, true);
            Mouse mouse = TryMouse();
            if (mouse != null && mouse.enabled) { mouse.enabled = false; mouseTaken = true; }
        }

        /// <summary>Switching the keyboard off reads as a key release to the game: with the open key still down and younger than the game's click delay
        /// that would be a tap (the default Radial Menu key is also Next Countermeasure on a tap). The host calls this once the key is up or too old to be one.</summary>
        public static void TakeKeyboard()
        {
            if (!held || keyboardTaken) return;
            Keyboard keyboard = TryKeyboard();
            if (keyboard != null && keyboard.enabled) { keyboard.enabled = false; keyboardTaken = true; }
        }

        public static void Release()
        {
            if (!held) return;
            held = false;
            if (mouseTaken) { Mouse mouse = TryMouse(); if (mouse != null) mouse.enabled = true; mouseTaken = false; }
            if (keyboardTaken) { Keyboard keyboard = TryKeyboard(); if (keyboard != null) keyboard.enabled = true; keyboardTaken = false; }
            if (ownsCursorFlag) CursorManager.SetFlag(CursorFlags.SelectionMenu, false);
            ownsCursorFlag = false;
            if (cursorBlank) { UnityEngine.Cursor.SetCursor(null, UnityEngine.Vector2.zero, UnityEngine.CursorMode.Auto); cursorBlank = false; }
            UnityEngine.Cursor.visible = CursorManager.Visible;
            UnityEngine.Cursor.lockState = CursorManager.Visible ? UnityEngine.CursorLockMode.None : UnityEngine.CursorLockMode.Locked; // CursorManager's own rule
        }

        /// <summary>The mouse points with the OS cursor in both modes; a controller steers a drawn reticle, so the cursor is blanked and stays inside the
        /// window. It is never hidden: the game's cockpit, chase and orbit cameras stop looking around only while <c>Cursor.visible</c> is true, and the
        /// view axes sit on the same stick that steers the menu (the game sets no cursor image of its own, so null restores it). Only while the guard is held.</summary>
        public static void SetCursorVisible(bool visible)
        {
            if (!held) return;
            if (!UnityEngine.Cursor.visible) UnityEngine.Cursor.visible = true;
            if (cursorBlank == visible)
            {
                if (blank == null)
                {
                    blank = new UnityEngine.Texture2D(32, 32, UnityEngine.TextureFormat.RGBA32, false) { hideFlags = UnityEngine.HideFlags.HideAndDontSave };
                    blank.SetPixels32(new UnityEngine.Color32[32 * 32]);
                    blank.Apply();
                }
                UnityEngine.Cursor.SetCursor(visible ? null : blank, UnityEngine.Vector2.zero, UnityEngine.CursorMode.Auto);
                cursorBlank = !visible;
            }
            UnityEngine.CursorLockMode mode = visible ? UnityEngine.CursorLockMode.None : UnityEngine.CursorLockMode.Confined;
            if (UnityEngine.Cursor.lockState != mode) UnityEngine.Cursor.lockState = mode; // every frame: write only on a change
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
