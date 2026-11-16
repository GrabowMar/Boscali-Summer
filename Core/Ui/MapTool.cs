using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>
    /// What every armed map tool agrees on (rule B, 2026-10-29): the right button places, Esc cancels and never pauses (Command
    /// holds the pause key while any tool but WMC's is armed), one click tolerance, nothing while a text field has the keyboard,
    /// and a closed map disarms. Each tool keeps its own gesture; arbitration stays <see cref="NOAvionics.MapPicker"/>.
    /// </summary>
    internal static class MapTool
    {
        /// <summary>Release travel, in pixels, still read as a click rather than a drag.</summary>
        internal const float ClickSlop = 8f;

        /// <summary>The mouse button an armed tool places with: the right one.</summary>
        internal const int Button = 1;

        internal static bool Typing()
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.GetComponent<TMP_InputField>() != null;
        }

        /// <summary>Esc this frame, unless it went to a text field.</summary>
        internal static bool CancelPressed() => Input.GetKeyDown(KeyCode.Escape) && !Typing();

        internal static bool IsClick(Vector2 down, Vector2 up) => (up - down).sqrMagnitude <= ClickSlop * ClickSlop;
    }
}
