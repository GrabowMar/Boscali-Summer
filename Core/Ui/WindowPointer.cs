using NOAvionics;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>Pointer events for windows that hit-test their own kit controls because the Rewired mouse is off and the event system hears nothing.</summary>
    internal static class WindowPointer
    {
        /// <summary>A left-button event at the current mouse position.</summary>
        internal static PointerEventData Data() =>
            new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = Input.mousePosition };

        /// <summary>Moves the hover from <paramref name="hovered"/> to <paramref name="next"/>: exit goes to the old control and its help tip, enter to the new one's. No-op when they are the same.</summary>
        internal static void Hover(ref AvHit hovered, AvHit next)
        {
            if (hovered == next) return;
            if (hovered != null)
            {
                ((IPointerExitHandler)hovered).OnPointerExit(Data());
                hovered.GetComponent<AvHelpTip>()?.OnPointerExit(Data());
            }
            hovered = next;
            if (hovered != null)
            {
                ((IPointerEnterHandler)hovered).OnPointerEnter(Data());
                hovered.GetComponent<AvHelpTip>()?.OnPointerEnter(Data());
            }
        }
    }
}
