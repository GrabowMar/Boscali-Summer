using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Comms.Presentation
{
    /// <summary>
    /// A section header that also carries its controls: icon, title, caption, then a few icon keys on
    /// the right (paging arrows, a send key). Replaces "section + pager row + button row" with one 28 px
    /// line. Kit gap: the kit's <see cref="AvSection"/> has no trailing controls; this is the module-private
    /// composition until the kit grows one.
    /// </summary>
    internal sealed class CommsHeaderPart : AvPart
    {
        private const float ButtonW = 30f, ButtonGap = 3f;
        private readonly TMP_Text icon, title, caption;
        private readonly Image rule, cap;
        private readonly AvControl[] buttons;
        private float placedW = -1f, placedH;

        public AvControl this[int index] => buttons[index];

        public CommsHeaderPart(RectTransform parent, AvIcon glyph, string titleText, params AvControl.Spec[] specs)
        {
            Rect = AvLay.Child(parent, "Header " + titleText);
            icon = AvIcons.Make(Rect, glyph, AvGridTokens.IconHead, Color.white);
            title = AvText.Make(Rect, "Title", AvTextRole.Head, titleText);
            AvText.Fit(title, false);
            caption = AvText.Make(Rect, "Caption", AvTextRole.Micro, string.Empty, TextAlignmentOptions.MidlineRight);
            rule = AvLay.Solid(Rect, "Rule", Color.clear);
            cap = AvLay.Solid(Rect, "Cap", Color.clear);
            buttons = new AvControl[specs.Length];
            for (int i = 0; i < specs.Length; i++) buttons[i] = AvControl.Make(Rect, specs[i]);
            Restyle();
        }

        public void SetTitle(string text)
        {
            if (title.text == (text ?? string.Empty)) return;
            title.text = text ?? string.Empty;
            Arrange();
        }

        public void SetCaption(string text)
        {
            if (caption.text == (text ?? string.Empty)) return;
            caption.text = text ?? string.Empty;
            Arrange();
        }

        public override float Measure(float width) => 28f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            placedW = s.W;
            placedH = s.H;
            Arrange();
        }

        private void Arrange()
        {
            if (placedW < 0f) return;
            float w = placedW;
            float strip = buttons.Length == 0 ? 0f : buttons.Length * (ButtonW + ButtonGap) - ButtonGap;
            float left = w - strip;
            for (int i = 0; i < buttons.Length; i++)
                AvLay.Place(buttons[i].Rect, left + i * (ButtonW + ButtonGap), 1f, ButtonW, 25f);
            float captionW = Mathf.Min(AvText.Width(caption) + 2f, Mathf.Max(0f, left - 90f));
            float captionX = left - (buttons.Length == 0 ? 0f : 8f) - captionW;
            AvLay.Place(icon.rectTransform, 0f, 5f, 16f, 16f);
            AvLay.Place(title.rectTransform, 22f, 0f, Mathf.Max(30f, captionX - 30f), 26f);
            AvLay.Place(caption.rectTransform, captionX, 0f, captionW, 26f);
            AvLay.Place(rule.rectTransform, 0f, placedH - 1f, w, 1f);
            AvLay.Place(cap.rectTransform, 0f, placedH - 3f, 28f, 3f);
        }

        public override void Restyle()
        {
            title.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-title").Color, AvTheme.RailInfo);
            icon.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-icon").Color, AvTheme.RailInfo);
            caption.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-caption").Color, AvTheme.Disabled);
            rule.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section").Border, AvTheme.Hairline);
            cap.color = title.color;
            foreach (AvControl b in buttons) b.Restyle();
        }
    }
}
