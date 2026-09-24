using System;
using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation.Viz
{
    /// <summary>
    /// Pointer behaviour for a control whose look belongs to the room that owns it: hover, press,
    /// enabled, latched, the hover tooltip, the click tick and the EventSystem release. It draws
    /// nothing; the room repaints its own skin from <see cref="Changed"/>. A right-click is passed
    /// up the hierarchy so the room (and then the window) can answer it.
    /// </summary>
    internal sealed class RoomControl : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler,
        IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private Action click;
        private string tooltip;

        public bool Hovered { get; private set; }
        public bool Pressed { get; private set; }
        public bool Enabled { get; private set; } = true;
        public bool Latched { get; private set; }

        /// <summary>Raised on every state change so the owner can repaint its own skin.</summary>
        public Action<RoomControl> Changed;

        public static RoomControl Create(RectTransform parent, Rect area, Action onClick, string name = "Control")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            AvKit.Place(rect, area);
            Image hit = go.GetComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;
            hit.canvasRenderer.cullTransparentMesh = true;
            RoomControl control = go.AddComponent<RoomControl>();
            control.click = onClick;
            return control;
        }

        public RectTransform Rect => (RectTransform)transform;

        public RoomControl WithTooltip(string text)
        {
            if (tooltip == text) return this;
            string previous = tooltip;
            tooltip = text;
            if (Hovered && AvButton.HoveredTooltip == previous)
                AvButton.PublishExternal(string.IsNullOrEmpty(text) ? previous : text, !string.IsNullOrEmpty(text));
            return this;
        }

        public void SetAction(Action onClick) => click = onClick;

        public void SetEnabled(bool on)
        {
            if (Enabled == on) return;
            Enabled = on;
            if (!on) Pressed = false;
            Changed?.Invoke(this);
        }

        public void SetLatched(bool on)
        {
            if (Latched == on) return;
            Latched = on;
            Changed?.Invoke(this);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.dragging) return;
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                if (transform.parent != null)
                    ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, eventData, ExecuteEvents.pointerClickHandler);
                return;
            }
            if (eventData.button != PointerEventData.InputButton.Left || !Enabled) return;
            AvInput.Deselect(gameObject);
            AvUiSound.Tick(0.3f);
            click?.Invoke();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            Hovered = true;
            AvButton.PublishExternal(tooltip, true);
            Changed?.Invoke(this);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Hovered = false;
            Pressed = false;
            AvButton.PublishExternal(tooltip, false);
            Changed?.Invoke(this);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !Enabled) return;
            Pressed = true;
            Changed?.Invoke(this);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!Pressed) return;
            Pressed = false;
            Changed?.Invoke(this);
        }

        private void OnDisable()
        {
            if (!Hovered && !Pressed) return;
            if (Hovered) AvButton.PublishExternal(tooltip, false);
            Hovered = false;
            Pressed = false;
        }
    }
}
