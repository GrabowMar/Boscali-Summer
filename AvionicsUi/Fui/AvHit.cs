using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NOAvionics.Ui
{
    /// <summary>Pointer input for kit v2 parts. Not a Selectable, so flight sticks can never steer focus onto it (spec §6.4).</summary>
    public sealed class AvHit : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        public Action<PointerEventData> Click;
        public Action<bool> Hover;
        public Action<bool> Press;
        public bool Interactable = true;

        public static AvHit On(Graphic g)
        {
            g.raycastTarget = true;
            AvHit hit = g.GetComponent<AvHit>();
            return hit != null ? hit : g.gameObject.AddComponent<AvHit>();
        }

        public void OnPointerEnter(PointerEventData e) { if (Interactable) { Hover?.Invoke(true); AvUiSound.Play(AvUiCue.Hover); } }
        public void OnPointerExit(PointerEventData e) { Hover?.Invoke(false); Press?.Invoke(false); }
        public void OnPointerDown(PointerEventData e) { if (Interactable) Press?.Invoke(true); }
        public void OnPointerUp(PointerEventData e) { Press?.Invoke(false); }

        public void OnPointerClick(PointerEventData e)
        {
            if (!Interactable) return;
            AvUiSound.Play(AvUiCue.Press);
            Click?.Invoke(e);
            AvInput.Deselect(gameObject);
        }
    }
}
