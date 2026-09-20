using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>The shared reading grid for the two full-screen OPS instruments.</summary>
    internal static class SupportOverlayUi
    {
        public static float FillPitch(float span, int count, float rowHeight) =>
            count <= 1 ? rowHeight : Mathf.Max(rowHeight, (span - rowHeight) / (count - 1));

        public static TMP_Text Value(RectTransform parent, Rect area) =>
            Fit(AvStyled.Label(parent, area, "—", "kv-value", align: TextAlignmentOptions.MidlineRight));

        public static TMP_Text Fit(TMP_Text label)
        {
            label.enableAutoSizing = true;
            label.fontSize = label.fontSizeMax = 14f;
            label.fontSizeMin = 12f;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        public static TMP_Text Detail(RectTransform parent, Rect area)
        {
            TMP_Text label = AvKit.Label(parent, "", area, AvTheme.Dim, 13f,
                FontStyles.Normal, TextAlignmentOptions.TopLeft, wrap: true);
            label.maxVisibleLines = 2;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        public static void Section(RectTransform parent, float x, ref float y, float width, string title)
        {
            AvKit.Label(parent, title, new Rect(x, y, width, 18f), AvTheme.TextPrimary, 13f, FontStyles.Bold);
            AvKit.Rule(parent, new Rect(x, y - 24f, width, 1f), AvTheme.Hairline);
            y -= 34f;
        }
    }
}
