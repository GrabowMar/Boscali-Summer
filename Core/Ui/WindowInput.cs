using System.Collections.Generic;
using NOAvionics;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>
    /// Input hold for full-screen windows: the game's SelectionMenu
    /// cursor flag frees the cursor, the Rewired mouse goes off so a click cannot fire a weapon and comes back only once its buttons
    /// are up, Esc stops opening the pause menu. Keyboard and joysticks stay live: the pilot keeps flying. With the Rewired mouse off
    /// the event system hears nothing, so <see cref="Pointer"/> hit-tests the window's own kit controls.
    /// </summary>
    internal sealed class WindowInput
    {
        private readonly List<AvHit> hits = new List<AvHit>(160);
        private AvHit hovered;
        private bool ownsCursorFlag, mouseDisabledByUs, restoreWhenReleased, pauseTouched, pauseWas;
        private float nextHits;

        public AvHit Hovered => hovered;

        public void Take()
        {
            restoreWhenReleased = false; // Reopening while a mouse button is held keeps our input ownership.
            Keep();
            if (!pauseTouched)
            {
                pauseTouched = true;
                pauseWas = GameplayUI.AllowPauseKeybind;
                GameplayUI.AllowPauseKeybind = false; // Esc closes the window instead of opening the pause menu
            }
            nextHits = 0f;
        }

        /// <summary>
        /// Reacquire the cursor and mouse after an overlay beneath this window releases them.
        /// </summary>
        public void Keep()
        {
            if (!CursorManager.GetFlag(CursorFlags.SelectionMenu))
            {
                CursorManager.SetFlag(CursorFlags.SelectionMenu, true);
                ownsCursorFlag = true;
            }
            Mouse mouse = TryMouse();
            if (mouse != null && mouse.enabled)
            {
                mouse.enabled = false;
                mouseDisabledByUs = true;
            }
        }

        public void GiveBack()
        {
            ReleaseFlags();
            if (mouseDisabledByUs)
            {
                restoreWhenReleased = ButtonsDown();
                if (!restoreWhenReleased) RestoreMouse();
            }
            WindowPointer.Hover(ref hovered, null);
        }

        /// <summary>Every frame, open or not: a mouse still held at close is handed back the moment its buttons are up.</summary>
        public void Tick()
        {
            if (restoreWhenReleased && !ButtonsDown()) RestoreMouse();
        }

        /// <summary>Teardown or fault: everything back now, buttons or not.</summary>
        public void Force()
        {
            if (mouseDisabledByUs) RestoreMouse();
            ReleaseFlags();
            WindowPointer.Hover(ref hovered, null);
        }

        /// <summary>Hover and left-click for the controls under <paramref name="root"/> while the event system is deaf (the Rewired mouse is off).</summary>
        public void Pointer(RectTransform root, Vector2 mouse, bool clicked)
        {
            Mouse device = TryMouse();
            if (!mouseDisabledByUs && (device == null || device.enabled)) return;
            if (Time.unscaledTime >= nextHits)
            {
                nextHits = Time.unscaledTime + 0.1f;
                hits.Clear();
                root.GetComponentsInChildren(false, hits);
            }
            AvHit under = null;
            for (int i = hits.Count - 1; i >= 0; i--) // later children draw on top
            {
                AvHit h = hits[i];
                if (h != null && h.isActiveAndEnabled && h.Interactable &&
                    RectTransformUtility.RectangleContainsScreenPoint((RectTransform)h.transform, mouse, null)) { under = h; break; }
            }
            WindowPointer.Hover(ref hovered, under);
            if (under != null && clicked)
            {
                ((IPointerClickHandler)under).OnPointerClick(WindowPointer.Data());
                nextHits = 0f; // A click can reveal new controls before the next periodic refresh.
            }
        }

        private static bool ButtonsDown() => Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);

        private static Mouse TryMouse() => ReInput.isReady && ReInput.controllers != null ? ReInput.controllers.Mouse : null;

        private void ReleaseFlags()
        {
            if (ownsCursorFlag) CursorManager.SetFlag(CursorFlags.SelectionMenu, false);
            ownsCursorFlag = false;
            if (pauseTouched) GameplayUI.AllowPauseKeybind = pauseWas;
            pauseTouched = false;
        }

        private void RestoreMouse()
        {
            Mouse mouse = TryMouse();
            if (mouse != null) mouse.enabled = true;
            mouseDisabledByUs = restoreWhenReleased = false;
        }
    }
}
