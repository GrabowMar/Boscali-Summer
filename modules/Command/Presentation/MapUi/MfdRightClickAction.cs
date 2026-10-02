using NOAvionics;
using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>Restores the native filter's right-click isolation action.</summary>
    internal sealed class MfdRightClickAction : MonoBehaviour, IPointerClickHandler
    {
        private Action action;

        public void Configure(Action onRightClick) => action = onRightClick;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Right) return;
            AvInput.Deselect(gameObject);
            action?.Invoke();
        }
    }
}
