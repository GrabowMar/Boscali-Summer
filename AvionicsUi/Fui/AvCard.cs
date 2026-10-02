using NOAvionics;
using TMPro;
using UnityEngine;

namespace NOAvionics
{
    /// <summary>Framed group with corner brackets, an optional title and its own nested flow.</summary>
    public sealed class AvCard : AvPart
    {
        private const float Inset = 12f, TitleH = 22f;
        private readonly AvFrame frame;
        private readonly TMP_Text title;
        private readonly RectTransform body;
        private readonly string classes;

        public AvCard(RectTransform parent, AvTicker ticker, float width, string titleText = null, bool brackets = true, string variant = null)
        {
            Rect = AvLay.Child(parent, "Card " + titleText);
            classes = "card" + (variant != null ? " " + variant : "");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f)); AvLay.Fill(frame.rectTransform);
            frame.Bracket = brackets ? 8f : 0f;
            if (!string.IsNullOrEmpty(titleText)) title = AvText.Make(Rect, "Title", AvTextRole.Head, titleText);
            if (title != null) AvText.Fit(title, false);
            body = AvLay.Child(Rect, "Body");
            // The nested flow uses the card's inner width; it keeps its own pad = Inset and no gutter.
            Flow = new AvFlow(body, ticker, width, 0f);
            Flow.Host = this;
            Restyle();
        }

        public AvFlow Flow { get; }

        public override float Measure(float width)
        {
            Flow.Relayout();
            return (title != null ? TitleH : 0f) + Flow.ContentHeight;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float top = title != null ? TitleH : 0f;
            if (title != null) AvLay.Place(title.rectTransform, Inset, 4f, s.W - 2f * Inset, TitleH - 4f);
            AvLay.Place(body, 0f, top, s.W, s.H - top);
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle(classes);
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            frame.BracketColor = AvStyleHost.Resolve(AvStyleHost.FuiStyle("card-bracket").Background, AvTheme.Frame);
            frame.SetVerticesDirty();
            if (title != null) title.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("title").Color, AvTheme.TextPrimary);
        }
    }
}
