using BoscaliSummer.Modules.Weather.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    /// <summary>
    /// A row of the TIME card: an optional label, one or more buttons (equal width, or a fixed width each) and a trailing readout. HOLD TAPE with its RUNNING // TAPE line is one row,
    /// FRONT IN with its four minute buttons and its S5 tag is another.
    /// </summary>
    internal sealed class MetLabeledRow : AvPart
    {
        private readonly TMP_Text label, trail;
        private readonly float labelW, controlW, trailW;
        public AvControl[] Controls { get; }

        /// <param name="controlWidth">Fixed width of each button, or 0 to share the room left by the label and the trailing readout equally.</param>
        public MetLabeledRow(RectTransform parent, string labelText, float labelWidth, AvControl.Spec[] specs, float controlWidth, float trailWidth)
        {
            Rect = AvLay.Child(parent, "Row " + labelText);
            labelW = labelWidth; controlW = controlWidth; trailW = trailWidth;
            if (!string.IsNullOrEmpty(labelText))
            {
                label = AvText.Make(Rect, "Label", AvTextRole.Label, labelText, TextAlignmentOptions.MidlineLeft);
                AvText.Fit(label, false);
            }
            Controls = new AvControl[specs.Length];
            for (int i = 0; i < specs.Length; i++) Controls[i] = AvControl.Make(Rect, specs[i]);
            trail = AvText.Make(Rect, "Trail", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineLeft);
            AvText.Fit(trail, false);
            Restyle();
        }

        public void SetTrail(string text, string inkRole)
        {
            AvText.Set(trail, text);
            trail.color = EnvInk.Role(inkRole);
        }

        public override float Measure(float width) => MetOfficeLayout.Row;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float y = (s.H - 26f) * 0.5f;
            if (label != null) AvLay.Place(label.rectTransform, 0f, 0f, labelW, s.H);
            float x = labelW, room = s.W - labelW - (trailW > 0f ? trailW + 6f : 0f);
            float w = controlW > 0f ? controlW : AvFlowMath.ColumnWidth(room, Controls.Length, 2f);
            for (int i = 0; i < Controls.Length; i++) { AvLay.Place(Controls[i].Rect, x, y, w, 26f); x += w + (controlW > 0f ? 4f : 2f); }
            AvLay.Place(trail.rectTransform, x + 4f, 0f, Mathf.Max(1f, s.W - x - 4f), s.H);
        }

        public override void Restyle()
        {
            if (label != null) label.color = EnvInk.Role("ink-dim");
            foreach (AvControl c in Controls) c.Restyle();
        }
    }

    /// <summary>The window's own input contract, carried over unchanged from the old console (modules/Weather/AGENTS.md): cursor flag, pause key, Rewired mouse and keyboard off while open, restored exactly as found, the mouse only once both buttons are up.</summary>
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
