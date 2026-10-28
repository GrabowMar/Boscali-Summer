using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    /// <summary>The window's own input contract (modules/Weather/AGENTS.md): cursor flag, pause key, Rewired mouse and keyboard off while open, restored exactly as found, the mouse only once both buttons are up.</summary>
    internal sealed class MetOfficeInput
    {
        private bool taken, ownsCursorFlag, mouseTouched, mouseWas, keyboardTouched, keyboardWas, pauseWas, releasePending;

        /// <summary>True while the Rewired mouse is ours (off), so the window must hit-test by itself.</summary>
        public bool MouseIsOurs => mouseTouched;

        public void Take()
        {
            taken = true;
            ownsCursorFlag = !CursorManager.GetFlag(CursorFlags.SelectionMenu);
            if (ownsCursorFlag) CursorManager.SetFlag(CursorFlags.SelectionMenu, true);
            pauseWas = GameplayUI.AllowPauseKeybind;
            GameplayUI.AllowPauseKeybind = false;
            if (!Rewired.ReInput.isReady || Rewired.ReInput.controllers == null) return;
            if (!releasePending && Rewired.ReInput.controllers.Mouse != null)
            {
                mouseTouched = true;
                mouseWas = Rewired.ReInput.controllers.Mouse.enabled;
                Rewired.ReInput.controllers.Mouse.enabled = false;
            }
            releasePending = false;
            if (Rewired.ReInput.controllers.Keyboard != null)
            {
                keyboardTouched = true;
                keyboardWas = Rewired.ReInput.controllers.Keyboard.enabled;
                Rewired.ReInput.controllers.Keyboard.enabled = false;
            }
        }

        public void Give()
        {
            if (!taken) return;   // never taken (the offline harness): nothing to hand back
            taken = false;
            if (ownsCursorFlag) CursorManager.SetFlag(CursorFlags.SelectionMenu, false);
            ownsCursorFlag = false;
            GameplayUI.AllowPauseKeybind = pauseWas;
            if (keyboardTouched && Rewired.ReInput.isReady && Rewired.ReInput.controllers?.Keyboard != null)
                Rewired.ReInput.controllers.Keyboard.enabled = keyboardWas;
            keyboardTouched = false;
            // Hand the mouse back only once its buttons are up, or the closing click fires.
            if (mouseTouched) releasePending = true;
            if (releasePending && !Input.GetMouseButton(0) && !Input.GetMouseButton(1)) Restore();
        }

        /// <summary>Call every frame: a mouse still held at close is handed back the moment its buttons are up.</summary>
        public void Tick()
        {
            if (releasePending && !Input.GetMouseButton(0) && !Input.GetMouseButton(1)) Restore();
        }

        /// <summary>Teardown: a mouse still held is handed back at once.</summary>
        public void Teardown()
        {
            if (releasePending) Restore();
        }

        private void Restore()
        {
            releasePending = false;
            if (mouseTouched && Rewired.ReInput.isReady && Rewired.ReInput.controllers?.Mouse != null)
                Rewired.ReInput.controllers.Mouse.enabled = mouseWas;
            mouseTouched = false;
        }
    }
}
