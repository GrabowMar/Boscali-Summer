using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>Section header: icon + TITLE ···· caption over a hairline. The caption drops to a second line rather than clip.</summary>
    public sealed class AvSection : AvPart
    {
        private const float Line = 22f;
        private readonly TMP_Text icon, title, caption;
        private readonly Image rule, cap;
        private bool twoLines;
        private float captionH = Line;

        public AvSection(RectTransform parent, AvIcon glyph, string titleText, string captionText = null)
        {
            Rect = AvLay.Child(parent, "Section " + titleText);
            icon = AvIcons.Make(Rect, glyph, AvGridTokens.IconHead, Color.white);
            title = AvText.Make(Rect, "Title", AvTextRole.Head, titleText);
            title.font = AvType.Face(AvFace.Cond);
            title.characterSpacing = 8f;
            AvText.Fit(title, false);
            caption = AvText.Make(Rect, "Caption", AvTextRole.Micro, captionText ?? "", TextAlignmentOptions.MidlineRight);
            rule = AvLay.Solid(Rect, "Rule", Color.clear);
            cap = AvLay.Solid(Rect, "Cap", Color.clear);
            Restyle();
        }

        public void SetCaption(string c) { if (caption.text == (c ?? "")) return; caption.text = c ?? ""; Changed(); }

        public override float Measure(float width)
        {
            float need = 22f + AvText.Width(title) + 12f + AvText.Width(caption);
            twoLines = caption.text.Length > 0 && need > width;
            if (!twoLines) return Line + 1f;
            // A second-line caption wraps under the title instead of running past the panel edge.
            caption.enableWordWrapping = true;
            captionH = Mathf.Max(Line, AvText.Height(caption, width - 22f) + 4f);
            return Line + captionH + 1f;
        }

        public override void Place(AvSlot slot)
        {
            base.Place(slot);
            AvLay.Place(icon.rectTransform, 0f, 3f, 16f, 16f);
            AvLay.Place(title.rectTransform, 22f, 0f, slot.W - 22f, Line);
            caption.enableWordWrapping = twoLines;
            AvLay.Place(caption.rectTransform, twoLines ? 22f : 0f, twoLines ? Line : 0f, twoLines ? slot.W - 22f : slot.W, twoLines ? captionH : Line);
            caption.alignment = twoLines ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineRight;
            AvLay.Place(rule.rectTransform, 0f, slot.H - 1f, slot.W, 1f);
            AvLay.Place(cap.rectTransform, 0f, slot.H - 1.5f, 22f, 1.5f);
        }

        public override void Restyle()
        {
            title.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-title").Color, AvTheme.RailInfo);
            icon.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-icon").Color, AvTheme.RailInfo);
            caption.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-caption").Color, AvTheme.Disabled);
            rule.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section").Border, AvTheme.Hairline);
            cap.color = title.color;
        }
    }
}
