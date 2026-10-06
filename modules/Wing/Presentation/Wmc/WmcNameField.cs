using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

using BoscaliSummer.Modules.Wing.Domain;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>The bezel's one text field (LOADOUT's NAME, STUDIO's CALLSIGN / NAME / BIO; critique C18): a kit v2 <see cref="AvField"/>
    /// with an optional key on its left and an optional button on its right, on one flow line. The keyboard is the field's only while
    /// it is focused: <see cref="AvField"/> holds the toolkit's keyboard guard itself, and this adds the game's pause key (Esc cancels
    /// the edit instead of pausing) and <see cref="Typing"/> (WC's own hotkeys and the map's Esc stand aside). Enter (or a click away)
    /// commits to the id that was being edited when the field took focus (a tile click or pilot pick meanwhile cannot redirect it), then
    /// the field lets go on the next tick. A field that is switched off (a tab or sub-page change, the panel hiding) lets go by itself.
    /// <see cref="BlurAny"/> releases everything even when the field was disabled or destroyed — called on a tab switch, the panel
    /// hiding, a reset and the room opening.</summary>
    internal sealed class WmcNameField : AvPart
    {
        private const float ButtonW = 92f;
        private static WmcNameField focusedField;
        private static int typingUntilFrame = -1;

        private readonly TMP_Text key;
        private readonly AvField inner;
        private readonly TMP_InputField input;
        private readonly Action<string, string> commit;
        private readonly float height, keyWidth;
        private string idAtFocus, shown;
        private bool focused, blurPending;

        /// <summary>A field has the keyboard now, or let go this frame or the last (the key that ended the edit acts nowhere else).</summary>
        public static bool Typing => focusedField != null || Time.frameCount <= typingUntilFrame;

        /// <summary><paramref name="keyText"/> null: no key column. <paramref name="onCommit"/> gets the id the field was editing
        /// (<see cref="EditingId"/> when it took focus) and the text. <paramref name="multiline"/> wraps and top-aligns (the bio).</summary>
        public WmcNameField(RectTransform parent, string keyText, int limit, Action<string, string> onCommit, string tip,
            string placeholder = "NAME", bool multiline = false, float height = AvGridTokens.Row, float keyWidth = 56f,
            AvControl.Spec? button = null)
        {
            commit = onCommit;
            this.height = height;
            this.keyWidth = keyWidth;
            Rect = AvLay.Child(parent, "NameField " + (keyText ?? placeholder));
            if (keyText != null)
            {
                key = AvText.Make(Rect, "Key", AvTextRole.Label, keyText, TextAlignmentOptions.MidlineLeft);
                AvText.Fit(key, false);
            }
            inner = new AvField(Rect, placeholder, limit, null);
            input = inner.Rect.GetComponent<TMP_InputField>();
            input.richText = false;
            input.restoreOriginalTextOnEscape = true;
            if (multiline)
            {
                input.lineType = TMP_InputField.LineType.MultiLineNewline;
                foreach (TMP_Text t in inner.Rect.GetComponentsInChildren<TMP_Text>(true))
                {
                    t.enableWordWrapping = true;
                    t.alignment = TextAlignmentOptions.TopLeft;
                }
            }
            input.onEndEdit.AddListener(OnEndEdit);
            input.onSelect.AddListener(_ => OnFocus());
            input.onDeselect.AddListener(_ => OnBlur());
            AvHelpTip.Attach(inner.Rect.gameObject, tip);
            inner.Rect.gameObject.AddComponent<BlurOnDisable>().Owner = this;
            if (button.HasValue) Button = AvControl.Make(Rect, button.Value);
            Restyle();
        }

        /// <summary>The trailing button, or null.</summary>
        public AvControl Button { get; }

        /// <summary>The id the page is editing; the field remembers it when it takes focus.</summary>
        public string EditingId { get; set; }

        public bool Focused => focused;

        /// <summary>The field holds text not yet saved (the build card's EDITED).</summary>
        public bool Dirty => focused && input != null && input.text != shown;

        /// <summary>What the field holds now, typed or not (SAVE reads the fields first: a click straight from a field is not lost).</summary>
        public string Text => input != null ? input.text : "";

        public override float Measure(float width) => height;

        public override void Place(AvSlot slot)
        {
            base.Place(slot);
            float x = key != null ? keyWidth : 0f, right = Button != null ? ButtonW + AvGridTokens.Gap : 0f;
            if (key != null) AvLay.Place(key.rectTransform, 0f, 0f, keyWidth - 6f, slot.H);
            inner.Place(new AvSlot(x, 0f, slot.W - x - right, slot.H));
            if (Button != null) AvLay.Place(Button.Rect, slot.W - ButtonW, 0f, ButtonW, AvGridTokens.Row);
        }

        public override void Restyle()
        {
            if (key != null) key.color = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
            inner.Restyle();
            Button?.Restyle();
        }

        /// <summary>The saved text, shown while the field is not being typed in.</summary>
        public void SetText(string text)
        {
            if (focused || input == null) return;
            shown = text ?? "";
            if (input.text != shown) input.SetTextWithoutNotify(shown);
        }

        public void SetInteractable(bool on)
        {
            if (input == null || input.interactable == on) return;
            if (!on) Blur();
            input.interactable = on;
        }

        /// <summary>Automation: focus the field as a click would.</summary>
        public void Focus()
        {
            if (input == null || !input.interactable || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(input.gameObject);
            input.ActivateInputField();
        }

        /// <summary>Automation: commit what is typed, as Enter would.</summary>
        public void Submit(string text)
        {
            if (input == null) return;
            if (!focused) idAtFocus = EditingId;
            OnEndEdit(text ?? input.text);
        }

        private void OnFocus()
        {
            // A blur pending from an edit that ended some other way (a click away, a tab switch) must not end this one.
            blurPending = false;
            focused = true;
            idAtFocus = EditingId;
            focusedField = this;
            PauseKeyHold.Set(KeyHold.Field, true);
        }

        private void OnEndEdit(string text)
        {
            if (idAtFocus != null) commit?.Invoke(idAtFocus, text);
            blurPending = true;
        }

        private void OnBlur()
        {
            blurPending = false;
            focused = false;
            if (ReferenceEquals(focusedField, this)) focusedField = null;
            typingUntilFrame = Time.frameCount + 1;
            // The Esc that ended the edit must not pause the game later this frame: the pause key comes back next frame.
            PauseKeyHold.Set(KeyHold.Field, false);
        }

        /// <summary>Lets go: the field is deselected (<see cref="AvField"/> releases the keyboard); a field that still thinks it is
        /// focused (disabled or destroyed) is released by force.</summary>
        public void Blur()
        {
            blurPending = false;
            EventSystem es = EventSystem.current;
            if (input != null && es != null && es.currentSelectedGameObject == input.gameObject) es.SetSelectedGameObject(null);
            if (!focused) return;
            focused = false;
            if (ReferenceEquals(focusedField, this)) focusedField = null;
            PauseKeyHold.Set(KeyHold.Field, false, now: true);
        }

        /// <summary>Lets any field go now, the pause key included (the room opening records the pause key as the player had it, so a
        /// release a frame later must not turn it on under the room and leave it off after).</summary>
        public static void BlurAny()
        {
            focusedField?.Blur();
            if (focusedField == null) PauseKeyHold.Set(KeyHold.Field, false, now: true);
        }

        /// <summary>Every frame (WmcPanel.Tick): a pending blur after Enter, and the pause key back one frame after the edit ended.</summary>
        public static void TickAll()
        {
            if (focusedField != null && focusedField.blurPending) focusedField.Blur();
            PauseKeyHold.Tick();
        }

        private sealed class BlurOnDisable : MonoBehaviour
        {
            public WmcNameField Owner;

            internal void OnDisable() => Owner?.Blur();
        }
    }
}
