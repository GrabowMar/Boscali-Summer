using Rewired;

namespace BoscaliSummer.Features.Autopilot.Runtime
{
    /// <summary>
    /// Takes the mouse away from flying while the interaction menu is open, the way ACE3's
    /// cursor menu freezes the camera and blocks firing. The game's own cursor flag frees and
    /// centres the cursor and stops cockpit look; disabling the Rewired mouse stops
    /// mouse-aim steering, chase-camera pan and mouse-button weapons fire. Both are restored
    /// exactly as found.
    /// </summary>
    internal static class AceRadialInputGuard
    {
        private static bool held;
        private static bool ownsCursorFlag;
        private static bool mouseWasEnabled = true;

        public static bool Held => held;

        public static void Acquire()
        {
            if (held) return;
            held = true;
            ownsCursorFlag = !CursorManager.GetFlag(CursorFlags.SelectionMenu);
            if (ownsCursorFlag) CursorManager.SetFlag(CursorFlags.SelectionMenu, true);
            Mouse mouse = TryMouse();
            if (mouse == null) return;
            mouseWasEnabled = mouse.enabled;
            mouse.enabled = false;
        }

        public static void Release()
        {
            if (!held) return;
            held = false;
            Mouse mouse = TryMouse();
            if (mouse != null) mouse.enabled = mouseWasEnabled;
            if (ownsCursorFlag) CursorManager.SetFlag(CursorFlags.SelectionMenu, false);
            ownsCursorFlag = false;
        }

        private static Mouse TryMouse()
        {
            if (!ReInput.isReady || ReInput.controllers == null) return null;
            return ReInput.controllers.Mouse;
        }
    }
}
