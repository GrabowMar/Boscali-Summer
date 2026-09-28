using System;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace NOAvionics.Ui
{
    public sealed class AvStepper : AvPart
    {
        private readonly TMP_Text label, value;
        private readonly AvControl minus, plus;
        public AvControl Minus => minus;
        public AvControl Plus => plus;
        private readonly Func<string> read;

        public AvStepper(RectTransform parent, string labelText, Func<string> valueText, Action onMinus, Action onPlus)
        {
            Rect = AvLay.Child(parent, "Stepper " + labelText);
            read = valueText;
            label = AvText.Make(Rect, "Label", AvTextRole.Label, labelText, TextAlignmentOptions.MidlineLeft, true);
            value = AvText.Make(Rect, "Value", AvTextRole.Data, "", TextAlignmentOptions.Center);
            AvText.Fit(value, true); // long values shrink, then wrap to two lines within the row
            minus = AvControl.Make(Rect, new AvControl.Spec("", () => { onMinus?.Invoke(); Refresh(); }, AvButtonStyle.Quiet, AvIcon.Minus));
            plus = AvControl.Make(Rect, new AvControl.Spec("", () => { onPlus?.Invoke(); Refresh(); }, AvButtonStyle.Quiet, AvIcon.Plus));
            Refresh();
        }

        public void Refresh() { string v = read?.Invoke() ?? ""; if (value.text != v) value.text = v; }

        public override float Measure(float width) => Mathf.Max(AvGridTokens.Row, AvText.Height(label, width - 170f) + 8f);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float y = (s.H - 26f) * 0.5f;
            AvLay.Place(label.rectTransform, 0f, 0f, s.W - 170f, s.H);
            AvLay.Place(minus.Rect, s.W - 162f, y, 30f, 26f);
            AvLay.Place(value.rectTransform, s.W - 128f, 0f, 90f, s.H);
            AvLay.Place(plus.Rect, s.W - 34f, y, 30f, 26f);
        }

        public override void Restyle()
        {
            label.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            value.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-value").Color, AvTheme.TextPrimary);
            minus.Restyle(); plus.Restyle();
        }
    }
}
