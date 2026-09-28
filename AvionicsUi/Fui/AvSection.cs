using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics.Ui
{
    /// <summary>Section header: icon + TITLE ···· caption over a hairline. The caption drops to a second line rather than clip.</summary>
    public sealed class AvSection : AvPart
    {
        private const float Line = 22f;
        private readonly TMP_Text icon, title, caption;
        private readonly Image rule;
        private bool twoLines;

        public AvSection(RectTransform parent, AvIcon glyph, string titleText, string captionText)
        {
            Rect = AvLay.Child(parent, "Section " + titleText);
            icon = AvIcons.Make(Rect, glyph, AvGridTokens.IconHead, Color.white);
            title = AvText.Make(Rect, "Title", AvTextRole.Head, titleText);
            AvText.Fit(title, false);
            caption = AvText.Make(Rect, "Caption", AvTextRole.Micro, captionText ?? "", TextAlignmentOptions.MidlineRight);
            rule = AvLay.Solid(Rect, "Rule", Color.clear);
            Restyle();
        }

        public void SetCaption(string c) { caption.text = c ?? ""; }

        public override float Measure(float width)
        {
            float need = 22f + AvText.Width(title) + 12f + AvText.Width(caption);
            twoLines = caption.text.Length > 0 && need > width;
            return (twoLines ? Line * 2f : Line) + 1f;
        }

        public override void Place(AvSlot slot)
        {
            base.Place(slot);
            AvLay.Place(icon.rectTransform, 0f, 3f, 16f, 16f);
            AvLay.Place(title.rectTransform, 22f, 0f, slot.W - 22f, Line);
            AvLay.Place(caption.rectTransform, twoLines ? 22f : 0f, twoLines ? Line : 0f, twoLines ? slot.W - 22f : slot.W, Line);
            caption.alignment = twoLines ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight;
            AvLay.Place(rule.rectTransform, 0f, slot.H - 1f, slot.W, 1f);
        }

        public override void Restyle()
        {
            title.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-title").Color, AvTheme.RailInfo);
            icon.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-icon").Color, AvTheme.RailInfo);
            caption.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-caption").Color, AvTheme.Disabled);
            rule.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section").Border, AvTheme.Hairline);
        }
    }
}
