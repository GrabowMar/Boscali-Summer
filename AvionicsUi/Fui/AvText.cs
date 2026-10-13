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

        /// <summary>Sets <paramref name="t"/>'s text only when it changed (TMP relays out on every assignment); true when it did.</summary>
        public static bool Set(TMP_Text t, string value)
        {
            string v = value ?? "";
            if (t == null || t.text == v) return false;
            t.text = v;
            return true;
        }

        // The flow's 2 Hz sweep and every Place re-measure each part; TMP's preferred-size pass is the expensive bit,
        // so the answer is kept per text object and reused while its text, box width, size, font and style match.
        private sealed class Measured { public string Text; public float W = -1f, Size, Value; public TMP_FontAsset Font; public FontStyles Style; public bool Wrap; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TMP_Text, Measured> heights = new System.Runtime.CompilerServices.ConditionalWeakTable<TMP_Text, Measured>();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TMP_Text, Measured> widths = new System.Runtime.CompilerServices.ConditionalWeakTable<TMP_Text, Measured>();

        private static float Cached(System.Runtime.CompilerServices.ConditionalWeakTable<TMP_Text, Measured> table, TMP_Text t, float box, bool wantWidth)
        {
            if (!table.TryGetValue(t, out Measured m)) { m = new Measured(); table.Add(t, m); }
            string text = t.text;
            if (m.W == box && m.Size == t.fontSize && m.Wrap == t.enableWordWrapping && m.Style == t.fontStyle &&
                ReferenceEquals(m.Font, t.font) && (ReferenceEquals(m.Text, text) || m.Text == text)) return m.Value;
            Vector2 pref = t.GetPreferredValues(text, box, 0f);
            if (!t.isActiveAndEnabled) return Mathf.Ceil(wantWidth ? pref.x : pref.y);
            m.Text = text; m.W = box; m.Size = t.fontSize; m.Wrap = t.enableWordWrapping; m.Style = t.fontStyle; m.Font = t.font;
            m.Value = Mathf.Ceil(wantWidth ? pref.x : pref.y);
            return m.Value;
        }

        public static float Height(TMP_Text t, float width) =>
            t == null ? 0f : Cached(heights, t, Mathf.Max(1f, width), false);

        public static float Width(TMP_Text t) =>
            t == null ? 0f : Cached(widths, t, 100000f, true);
    }
}
