using TMPro;
using UnityEngine;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>
    /// A text label for the native cockpit HUD canvases, in the game's own font and material: one line, never wrapped,
    /// never a raycast target. The caller places it.
    /// </summary>
    internal static class HudText
    {
        public static TextMeshProUGUI Make(Transform parent, string name, TMP_FontAsset font, Material material,
            TextAlignmentOptions alignment, float fontSize = 0f, TextOverflowModes overflow = TextOverflowModes.Overflow)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSharedMaterial = material;
            if (fontSize > 0f) text.fontSize = fontSize;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            if (overflow != TextOverflowModes.Overflow) text.overflowMode = overflow;
            text.raycastTarget = false;
            return text;
        }
    }
}
