using TMPro;
using UnityEngine;

namespace NOAvionics
{
    /// <summary>Chrome icons as glyphs of the bundled Tabler SDF font: crisp at any size, tinted by vertex colour, batched with text.</summary>
    public static class AvIcons
    {
        public static bool Available => AvType.Face(AvFace.Icons) != null && AvBundle.Font(AvTypeScale.AssetName(AvFace.Icons)) != null;

        public static string Glyph(AvIcon icon)
        {
            char c = AvIconTable.Char(icon);
            return c == '\0' ? "" : c.ToString();
        }

        public static TMP_Text Make(RectTransform parent, AvIcon icon, float size, Color color)
        {
            var go = new GameObject("Icon " + icon, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            t.color = color;
            t.raycastTarget = false;
            Set(t, icon, size);
            return t;
        }

        /// <summary>Without the icon font, show a neutral bullet rather than a tofu box (spec §7.4 fallback).</summary>
        public static void Set(TMP_Text text, AvIcon icon, float size)
        {
            if (text == null) return;
            if (Available)
            {
                TMP_FontAsset f = AvBundle.Font(AvTypeScale.AssetName(AvFace.Icons));
                if (text.font != f) text.font = f;
                text.fontStyle = FontStyles.Normal;
                text.fontSize = size;
                text.text = Glyph(icon);
            }
            else
            {
                AvType.Apply(text, AvTextRole.Micro);
                text.fontSize = size * 0.7f;
                text.text = icon == AvIcon.None ? "" : "·";
            }
        }
    }
}
