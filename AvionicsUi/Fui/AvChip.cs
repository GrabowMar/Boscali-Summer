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
            AvLay.Place(rail.rectTransform, 0f, 0f, 2f, AvGridTokens.ChipStrip);
            text = AvText.Make(Rect, "Text", AvTextRole.Micro);
            AvText.Fit(text, false);
            AvLay.Fill(text.rectTransform); text.rectTransform.offsetMin = new Vector2(8f, 0f);
            Restyle();
        }

        public override float Measure(float width) => AvGridTokens.ChipStrip;

        public void Set(string value, AvState s)
        {
            string composed = AvStates.Glyph(s) + (value ?? "");
            if (composed == body && s == state) return;
            body = composed; state = s; text.text = composed;
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
