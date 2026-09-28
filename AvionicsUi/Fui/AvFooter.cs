using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics.Ui
{
    /// <summary>Console footer: one or two lines of sentence-case status / help.</summary>
    public sealed class AvFooter : AvPart
    {
        private readonly Image back;
        private readonly TMP_Text text;
        private AvState state;

        public AvFooter(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Footer");
            back = AvLay.Solid(Rect, "Back", Color.clear); AvLay.Fill(back.rectTransform);
            text = AvText.Make(Rect, "Text", AvTextRole.ProseSmall, "", TextAlignmentOptions.MidlineLeft, true);
            AvLay.Fill(text.rectTransform, 0f); text.rectTransform.offsetMin = new Vector2(AvGridTokens.Pad, 0f);
            text.rectTransform.offsetMax = new Vector2(-AvGridTokens.Pad, 0f);
            Restyle();
        }

        public void Set(string value, AvState s = AvState.Inert)
        {
            string composed = AvStates.Glyph(s) + (value ?? "");
            if (text.text == composed && state == s) return;
            text.text = composed; state = s; Restyle();
        }

        public override float Measure(float width) =>
            Mathf.Max(AvGridTokens.Footer, AvText.Height(text, width - 2f * AvGridTokens.Pad) + 8f);

        public override void Restyle()
        {
            AvStyle s = AvStyleHost.FuiStyle("footer " + AvStates.Class(state));
            back.color = AvStyleHost.Resolve(s.Background, AvTheme.SurfaceInert);
            text.color = AvStyleHost.Resolve(s.Color, AvTheme.Dim);
        }
    }
}
