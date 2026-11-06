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
        private bool placed;
        private AvSlot lastSlot;
        private float titleH = Line, captionH = Line, captionW;

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

        public void SetCaption(string c)
        {
            if (!AvText.Set(caption, c)) return;
            // The flow only re-places parts whose height changes; inline captions also change their column width.
            if (placed) { Measure(lastSlot.W); Place(lastSlot); }
            Changed();
        }

        public override float Measure(float width)
        {
            float inner = Mathf.Max(1f, width - 22f), titleW = AvText.Width(title);
            captionW = AvText.Width(caption);
            title.enableWordWrapping = titleW > inner || title.text.IndexOf('\n') >= 0;
            titleH = title.enableWordWrapping ? Mathf.Max(Line, AvText.Height(title, inner) + 4f) : Line;
            twoLines = caption.text.Length > 0 && (titleH > Line || caption.text.IndexOf('\n') >= 0 || 22f + titleW + 12f + captionW > width);
            caption.enableWordWrapping = twoLines;
            captionH = twoLines ? Mathf.Max(Line, AvText.Height(caption, inner) + 4f) : Line;
            return titleH + (twoLines ? captionH : 0f) + 1f;
        }

        public override void Place(AvSlot slot)
        {
            base.Place(slot);
            lastSlot = slot; placed = true;
            AvLay.Place(icon.rectTransform, 0f, 3f, 16f, 16f);
            float titleW = slot.W - 22f - (!twoLines && caption.text.Length > 0 ? captionW + 12f : 0f);
            AvLay.Place(title.rectTransform, 22f, 0f, Mathf.Max(0f, titleW), titleH);
            title.alignment = titleH > Line ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            caption.enableWordWrapping = twoLines;
            AvLay.Place(caption.rectTransform, twoLines ? 22f : slot.W - captionW, twoLines ? titleH : 0f,
                twoLines ? Mathf.Max(0f, slot.W - 22f) : captionW, twoLines ? captionH : Line);
            caption.alignment = twoLines ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineRight;
            AvLay.Place(rule.rectTransform, 0f, slot.H - 1f, slot.W, 1f);
            AvLay.Place(cap.rectTransform, 0f, slot.H - 1.5f, 22f, 1.5f);
        }

        public override void Restyle()
        {
            title.color = AvStyleHost.FuiInk("section-title", AvTheme.RailInfo);
            icon.color = AvStyleHost.FuiInk("section-icon", AvTheme.RailInfo);
            caption.color = AvStyleHost.FuiInk("section-caption", AvTheme.Disabled);
            rule.color = AvStyleHost.FuiBorder("section", AvTheme.Hairline);
            cap.color = title.color;
        }
    }
}
