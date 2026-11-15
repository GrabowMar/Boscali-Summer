using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>Pointer input for kit v2 parts. Not a Selectable, so flight sticks can never steer focus onto it (spec §6.4).</summary>
    public sealed class AvHit : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        public Action<PointerEventData> Click;
        public Action<bool> Hover;
        public Action<bool> Press;
        public bool Interactable = true;
        /// <summary>The cue a click plays once its handler has run (the handler may set it for that click, or play a cue of its own).</summary>
        public AvUiCue Cue = AvUiCue.Press;
        /// <summary>A click while not interactable answers with the soft refusal (disabled buttons).</summary>
        public bool Refuses;
        private bool hovered, pressed;

        public static AvHit On(Graphic g)
        {
            g.raycastTarget = true;
            AvHit hit = g.GetComponent<AvHit>();
            return hit != null ? hit : g.gameObject.AddComponent<AvHit>();
        }

        public void OnPointerEnter(PointerEventData e) { if (Interactable) { hovered = true; Hover?.Invoke(true); AvUiSound.Play(AvUiCue.Hover); } }
        public void OnPointerExit(PointerEventData e) => ClearPointer();
        // A window that hit-tests its own controls clicks on the press (a fresh event, eligibleForClick false); if the event system also hears that press it
        // clicks again on release, and a toggle (an armed map pick) undid itself: only held while the button was. The release click of that press is dropped.
        private int downFrame = -10, synthFrame = -10;

        public void OnPointerDown(PointerEventData e) { downFrame = Time.frameCount; if (Interactable) { pressed = true; Press?.Invoke(true); } }
        public void OnPointerUp(PointerEventData e) { if (pressed) { pressed = false; Press?.Invoke(false); } }
        private void OnDisable() => ClearPointer();

        private void ClearPointer()
        {
            if (hovered) { hovered = false; Hover?.Invoke(false); }
            if (pressed) { pressed = false; Press?.Invoke(false); }
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (!e.eligibleForClick) synthFrame = Time.frameCount;
            else if (Mathf.Abs(synthFrame - downFrame) <= 1) return;
            if (!Interactable) { if (Refuses) AvUiSound.Play(AvUiCue.Caution); return; }
            Click?.Invoke(e);
            AvUiSound.Play(Cue);
            AvInput.Deselect(gameObject);
        }
    }
}
