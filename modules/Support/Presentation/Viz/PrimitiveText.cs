using BoscaliSummer.Features.Support.Domain.Layout;
using BoscaliSummer.Features.Support.Presentation.Window;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation.Viz
{
    internal static class PrimitiveText
    {
        public static TMP_Text Label(RectTransform parent, Rect area, Skin skin, TextAlignmentOptions align, float size = 0f)
        {
            TMP_Text label = AvKit.Label(parent, "", area, skin.Ink, Mathf.Max(10f, size > 0f ? size : skin.FontSize), skin.Style, align);
            label.characterSpacing = skin.Tracking;
            label.richText = true;
            return label;
        }

        public static bool Write(TMP_Text label, Skin skin, string value, ref string shown)
        {
            if (value == null) value = "";
            if (shown == value) return false;
            shown = value;
            label.text = skin.Text(value);
            return true;
        }
    }
}
