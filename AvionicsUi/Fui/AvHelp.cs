using UnityEngine;
using UnityEngine.EventSystems;

namespace NOAvionics
{
    /// <summary>
    /// Hover help: while the pointer is over a control, its help sentence replaces the owning console's (or
    /// window's) footer line; leaving restores it. One footer per console, no floating tooltip windows.
    /// </summary>
    public sealed class AvHelpScope : MonoBehaviour
    {
        internal AvFooter Footer;
        public void Show(string text) => Footer?.SetHint(text);
        public void Clear() => Footer?.SetHint(null);
    }

    /// <summary>Attach to any raycast target; shows <see cref="Text"/> in the nearest <see cref="AvHelpScope"/>.</summary>
    public sealed class AvHelpTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private string text;
        private bool hovered;
        private AvHelpScope scope;

        /// <summary>Help sentence; changing it while hovered updates the footer at once.</summary>
        public string Text
        {
            get => text;
            set { text = value; if (hovered && scope != null) { if (string.IsNullOrEmpty(value)) scope.Clear(); else scope.Show(value); } }
        }

        public static AvHelpTip Attach(GameObject target, string text)
        {
            if (target == null) return null;
            AvHelpTip tip = target.GetComponent<AvHelpTip>();
            if (tip == null) tip = target.AddComponent<AvHelpTip>();
            tip.Text = text;
            return tip;
        }

        public void OnPointerEnter(PointerEventData e)
        {
            hovered = true;
            if (scope == null) scope = GetComponentInParent<AvHelpScope>();
            if (string.IsNullOrEmpty(text)) return;
            if (scope != null) scope.Show(text);
        }

        public void OnPointerExit(PointerEventData e)
        {
            hovered = false;
            if (scope != null) scope.Clear();
        }

        private void OnDisable()
        {
            hovered = false;
            if (scope != null) scope.Clear();
        }
    }
}
