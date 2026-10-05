using NOAvionics;
using TMPro;
using UnityEngine;

namespace NOAvionics
{
    public static class AvText
    {
        public static TextMeshProUGUI Make(RectTransform parent, string name, AvTextRole role, string text = "",
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool wrap = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            AvType.Apply(t, role);
            t.alignment = align;
            t.enableWordWrapping = wrap;
            t.overflowMode = TextOverflowModes.Overflow;   // never '…' — the layout grows instead (spec §2 item 3)
            t.isTextObjectScaleStatic = true;
            t.text = text ?? "";
            return t;
        }

        /// <summary>
        /// Fixed-size chrome (button/tab labels, chips, titles, metric fields): shrink toward the 11 px floor
        /// instead of spilling out of the box, optionally wrapping to a second line first. Call after Make.
        /// </summary>
        public static void Fit(TMP_Text t, bool wrap)
        {
            if (t == null) return;
            t.fontSizeMax = t.fontSize;
            t.fontSizeMin = AvTypeScale.Floor;
            t.enableAutoSizing = true;
            t.enableWordWrapping = wrap;
        }

        /// <summary>Fixed point size for a part that fits its text by truncation instead of shrinking (never below the 10 px micro floor).</summary>
        public static void Size(TMP_Text t, float size)
        {
            if (t != null) t.fontSize = Mathf.Max(AvTokens.FontMicro, size);
        }

        public static float Height(TMP_Text t, float width) =>
            t == null ? 0f : Mathf.Ceil(t.GetPreferredValues(t.text, Mathf.Max(1f, width), 0f).y);

        public static float Width(TMP_Text t) =>
            t == null ? 0f : Mathf.Ceil(t.GetPreferredValues(t.text, 100000f, 0f).x);
    }
}
