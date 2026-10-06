using TMPro;
using UnityEngine;

namespace NOAvionics
{
    /// <summary>
    /// A wrapped line of dim prose in a given type role: top-left aligned, never truncated, measured from its text. A state
    /// other than Inert tints it through the same row-value classes an <see cref="AvRow"/> uses; <c>emphasize</c> reads in
    /// the row-name ink instead of the dim row-sub one.
    /// </summary>
    public sealed class AvNote : AvPart
    {
        private readonly TMP_Text text;
        private readonly bool emphasize;
        private AvState state = AvState.Inert;

        /// <summary>The least height the note measures, however short its text.</summary>
        public float MinHeight;

        /// <summary>An empty note measures zero and leaves no gap in its flow.</summary>
        public bool CollapseEmpty;

        /// <summary>Stretch the text to the note's rect (centre pivot) instead of placing it top-left; the same box, a different transform.</summary>
        public bool StretchText;

        public AvNote(RectTransform parent, AvTextRole role = AvTextRole.ProseSmall, bool emphasize = false)
        {
            this.emphasize = emphasize;
            Rect = AvLay.Child(parent, "Note");
            text = AvText.Make(Rect, "Text", role, "", TextAlignmentOptions.TopLeft, true);
            Restyle();
        }

        public AvNote(RectTransform parent, string initial) : this(parent) => text.text = initial ?? "";

        public string Text
        {
            get => text.text;
            set => Set(value);
        }

        /// <summary>Overrides the ink until the next restyle.</summary>
        public Color Color { set => text.color = value; }

        public void Set(string body) => Set(body, AvState.Inert);

        public void Set(string body, AvState tint)
        {
            string next = body ?? "";
            if (text.text != next) { text.text = next; Changed(); }
            if (tint != state) { state = tint; Restyle(); }
        }

        public override float Measure(float width) =>
            CollapseEmpty && text.text.Length == 0 ? 0f : Mathf.Max(MinHeight, AvText.Height(text, width));

        public override void Place(AvSlot s)
        {
            base.Place(s);
            if (StretchText) AvLay.Fill(text.rectTransform);
            else AvLay.Place(text.rectTransform, 0f, 0f, s.W, s.H);
        }

        public override void Restyle()
        {
            text.color = state != AvState.Inert
                ? AvStyleHost.FuiInk("row-value " + AvStates.Class(state), AvTheme.TextPrimary)
                : emphasize
                    ? AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary)
                    : AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
        }
    }
}
