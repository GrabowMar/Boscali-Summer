using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics.Ui
{
    /// <summary>
    /// Console footer: a fixed two-line strip of sentence-case status / help. Its height never changes (a
    /// hover hint must not make the page above it jump); longer text shrinks toward the 11 px floor.
    /// </summary>
    public sealed class AvFooter : AvPart
    {
        private readonly Image back;
        private readonly TMP_Text text;
        private AvState state;
        private string baseText = "", hint;

        public AvFooter(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Footer");
            back = AvLay.Solid(Rect, "Back", Color.clear); AvLay.Fill(back.rectTransform);
            text = AvText.Make(Rect, "Text", AvTextRole.ProseSmall, "", TextAlignmentOptions.MidlineLeft, true);
            AvText.Fit(text, true);
            AvLay.Fill(text.rectTransform, 0f); text.rectTransform.offsetMin = new Vector2(AvGridTokens.Pad, 0f);
            text.rectTransform.offsetMax = new Vector2(-AvGridTokens.Pad, 0f);
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
        }

        public override float Measure(float width) => AvGridTokens.Footer;

        public override void Restyle()
        {
            AvStyle s = AvStyleHost.FuiStyle("footer " + AvStates.Class(state));
            back.color = AvStyleHost.Resolve(s.Background, AvTheme.SurfaceInert);
            text.color = AvStyleHost.Resolve(s.Color, AvTheme.Dim);
        }
    }
}
