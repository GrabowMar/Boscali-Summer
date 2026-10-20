using System.Collections.Generic;
using NOAvionics;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>
    /// The big-window input hold (the OPS front window's, shared since 2026-10-19 with the UAV terminal): the game's SelectionMenu
    /// cursor flag frees the cursor, the Rewired mouse goes off so a click cannot fire a weapon and comes back only once its buttons
    /// are up, Esc stops opening the pause menu. Keyboard and joysticks stay live: the pilot keeps flying. With the Rewired mouse off
    /// the event system hears nothing, so <see cref="Pointer"/> hit-tests the window's own kit controls.
    /// </summary>
    internal sealed class WindowInput
    {
        private readonly FeedInputLease lease = new FeedInputLease();
        private readonly List<AvHit> hits = new List<AvHit>(160);
        private AvHit hovered;
        private bool ownsCursorFlag, mouseDisabledByUs, pauseTouched, pauseWas;
        private float nextHits;

        public void Take()
        {
            ownsCursorFlag = !CursorManager.GetFlag(CursorFlags.SelectionMenu);
            if (ownsCursorFlag) CursorManager.SetFlag(CursorFlags.SelectionMenu, true);
            if (!pauseTouched)
            {
                pauseTouched = true;
                pauseWas = GameplayUI.AllowPauseKeybind;
                GameplayUI.AllowPauseKeybind = false; // Esc closes the window instead of opening the pause menu
            }
            Mouse mouse = TryMouse();
            if (lease.Acquire(mouse != null, mouse != null && mouse.enabled))
            {
                mouse.enabled = false;
                mouseDisabledByUs = true;
            }
            nextHits = 0f;
        }

        /// <summary>
        /// Every frame while open: takes back what another overlay handed back under the window. The C-menu that opens the UAV terminal
        /// holds the same cursor flag and the mouse, and releases both as it closes, one frame after the window took input.
        /// </summary>
        public void Keep()
        {
            if (!CursorManager.GetFlag(CursorFlags.SelectionMenu))
            {
                CursorManager.SetFlag(CursorFlags.SelectionMenu, true);
                ownsCursorFlag = true;
            }
            Mouse mouse = TryMouse();
            if (mouse == null || !mouse.enabled) return;
            if (lease.Acquire(true, true) || mouseDisabledByUs)
            {
                mouse.enabled = false;
                mouseDisabledByUs = true;
            }
        }

        public void GiveBack()
        {
            WindowPointer.Hover(ref hovered, null);
            ReleaseFlags();
            if (lease.Release(ButtonsDown())) RestoreMouse();
        }

        /// <summary>Every frame, open or not: a mouse still held at close is handed back the moment its buttons are up.</summary>
        public void Tick()
        {
            if (lease.Tick(ButtonsDown())) RestoreMouse();
        }

        /// <summary>Teardown or fault: everything back now, buttons or not.</summary>
        public void Force()
        {
            WindowPointer.Hover(ref hovered, null);
            if (lease.ForceRelease()) RestoreMouse();
            ReleaseFlags();
        }

        /// <summary>Hover and left-click for the controls under <paramref name="root"/> while the event system is deaf (the Rewired mouse is off).</summary>
        public void Pointer(RectTransform root)
        {
            Mouse device = TryMouse();
            if (!mouseDisabledByUs && (device == null || device.enabled)) return;
            Vector2 mouse = Input.mousePosition;
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
            if (under != null && Input.GetMouseButtonDown(0)) ((IPointerClickHandler)under).OnPointerClick(WindowPointer.Data());
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
            if (mouse != null) mouse.enabled = lease.RestoreMouseTo;
            mouseDisabledByUs = false;
        }
    }

    /// <summary>
    /// Who owns the mouse while a full-screen window or feed is open, as a pure state machine the runtime applies to Rewired. The mouse is
    /// disabled only when it was found enabled and is handed back to exactly that state, and only once its buttons are up, so the
    /// closing click cannot fire a weapon. The keyboard and joysticks are never touched: flight and weapon bindings stay live in the air.
    /// </summary>
    internal sealed class FeedInputLease
    {
        private bool mouseTouched, mouseWas, pending;

        public bool Held { get; private set; }
        /// <summary>The enabled state to give the mouse back; valid after Release/Tick/ForceRelease returned true.</summary>
        public bool RestoreMouseTo => mouseWas;

        /// <summary>True when the caller must now disable the mouse.</summary>
        public bool Acquire(bool mouseAvailable, bool mouseEnabled)
        {
            Held = true;
            if (pending) { pending = false; return false; } // reopened before the buttons were up: still ours, still disabled
            if (mouseTouched) return false;
            if (!mouseAvailable || !mouseEnabled) return false;
            mouseTouched = true;
            mouseWas = true;
            return true;
        }

        /// <summary>True when the caller must now re-enable the mouse; false either means nothing to give back or "wait".</summary>
        public bool Release(bool buttonsDown)
        {
            Held = false;
            if (!mouseTouched) return false;
            if (buttonsDown) { pending = true; return false; }
            return Finish();
        }

        public bool Tick(bool buttonsDown) => pending && !buttonsDown && Finish();

        /// <summary>Teardown, reset or exception: hand the mouse back now, buttons or not.</summary>
        public bool ForceRelease()
        {
            Held = false;
            return mouseTouched && Finish();
        }

        private bool Finish()
        {
            mouseTouched = false;
            pending = false;
            return true;
        }
    }
}
