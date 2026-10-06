using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>
    /// Console footer: a fixed two-line strip of sentence-case status / help. Its height never changes (a
    /// hover hint must not make the page above it jump); longer text shrinks toward the 11 px floor.
    /// </summary>
    public sealed class AvFooter : AvPart
    {
        private readonly Image back;
        private readonly AvFrame tag;
        private readonly TMP_Text text, tagText;
        private AvState state;
        private string baseText = "", hint;

        public AvFooter(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Footer");
            back = AvLay.Solid(Rect, "Back", Color.clear); AvLay.Fill(back.rectTransform);
            AvSurfaceGradient.Apply(back, Color.white, new Color(.72f, .77f, .79f, 1f));
            // The badge sits in its own container so the frame is not read as the backing of the footer text beside it.
            RectTransform badge = AvLay.Child(Rect, "TipBadge");
            AvLay.Place(badge, AvGridTokens.Pad, 8f, TagW, 18f);
            tag = AvFrame.Add(badge, "TipTag", default(AvChamfer)); tag.Stroke = 0f; AvLay.Fill(tag.rectTransform);
            tagText = AvText.Make(badge, "TipWord", AvTextRole.Micro, "TIP", TextAlignmentOptions.Center);
            AvText.Fit(tagText, false);
            text = AvText.Make(Rect, "Text", AvTextRole.Prose, "", TextAlignmentOptions.TopLeft, true);
            AvText.Fit(text, true);
            AvLay.Fill(text.rectTransform, 0f); text.rectTransform.offsetMin = new Vector2(TagW + 2f * AvGridTokens.Pad, 6f);
            text.rectTransform.offsetMax = new Vector2(-AvGridTokens.Pad, -6f);
            AvLay.Fill(tagText.rectTransform);
            Restyle();
        }

        public void Set(string value, AvState s = AvState.Inert)
        {
            string composed = AvStates.Glyph(s) + (value ?? "");
            if (baseText == composed && state == s) return;
            baseText = composed; state = s;
            if (hint == null) text.text = composed;
            Restyle();
        }

        /// <summary>Hover help: temporarily show <paramref name="value"/>; null restores the status line.</summary>
        public void SetHint(string value)
        {
            hint = string.IsNullOrEmpty(value) ? null : value;
            text.text = hint ?? baseText;
            Restyle();
        }

        private const float TagW = 34f;

        public override float Measure(float width) => AvGridTokens.Footer;

        public override void Restyle()
        {
            AvStyle s = AvStyleHost.FuiStyle("footer " + AvStates.Class(state));
            back.color = AvStyleHost.Resolve(s.Background, AvTheme.SurfaceInert);
            text.color = AvStyleHost.Resolve(hint != null ? AvStyleHost.FuiStyle("title").Color : s.Color, AvTheme.Dim);
            tag.Paint(AvStyleHost.FuiFill("slab " + (hint != null || state == AvState.Inert ? "ready" : AvStates.Class(state)), AvTheme.Accent), Color.clear);
            tagText.color = AvStyleHost.FuiInk("slab", Color.black);
            tagText.text = hint != null ? "TIP" : "SYS";
        }
    }
}
