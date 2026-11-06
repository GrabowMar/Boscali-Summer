using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>Status chip: 2 px state rail + word (with ▲/✕ glyph for caution/danger).</summary>
    public sealed class AvChip : AvPart
    {
        private readonly Image back, rail;
        private readonly TMP_Text text;
        private AvState state;
        private string body = "";

        public AvChip(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Chip");
            back = AvLay.Solid(Rect, "Back", Color.clear); AvLay.Fill(back.rectTransform);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            rail.rectTransform.anchorMin = Vector2.zero; rail.rectTransform.anchorMax = new Vector2(0f, 1f);
            rail.rectTransform.pivot = new Vector2(0f, 0.5f); rail.rectTransform.sizeDelta = new Vector2(2f, 0f);
            rail.rectTransform.anchoredPosition = Vector2.zero;
            text = AvText.Make(Rect, "Text", AvTextRole.Micro);
            AvText.Fit(text, false);
            AvLay.Fill(text.rectTransform); text.rectTransform.offsetMin = new Vector2(8f, 0f); text.rectTransform.offsetMax = new Vector2(-8f, 0f);
            Restyle();
        }

        public override float Measure(float width) => AvGridTokens.ChipStrip;

        public void Set(string value, AvState s)
        {
            string next = value ?? "";
            if (next == body && s == state) return;
            body = next; state = s; AvText.Set(text, AvStates.Glyph(s) + next);
            Restyle();
        }

        public override void Restyle()
        {
            AvStyle st = AvStyleHost.FuiStyle("chip " + AvStates.Class(state));
            back.color = AvStyleHost.Resolve(st.Background, AvTheme.SurfaceInert);
            rail.color = AvStyleHost.Resolve(st.Rail, AvTheme.RailInert);
            text.color = AvStyleHost.Resolve(st.Color, AvTheme.Dim);
        }
    }
}
