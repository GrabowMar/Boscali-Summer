using NOAvionics;
using TMPro;
using UnityEngine;

namespace NOAvionics.Ui
{
    public static class AvText
    {
        public static TextMeshProUGUI Make(RectTransform parent, string name, AvTextRole role, string text = "",
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool wrap = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
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

        public static float Height(TMP_Text t, float width) =>
            t == null ? 0f : Mathf.Ceil(t.GetPreferredValues(t.text, Mathf.Max(1f, width), 0f).y);

        public static float Width(TMP_Text t) =>
            t == null ? 0f : Mathf.Ceil(t.GetPreferredValues(t.text, 100000f, 0f).x);
    }
}
