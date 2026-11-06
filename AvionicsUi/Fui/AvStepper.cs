using System;
using TMPro;
using UnityEngine;

namespace NOAvionics
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
            Restyle();
        }

        public void Refresh() { if (AvText.Set(value, read?.Invoke())) Changed(); }

        private bool Stacked(float width) => label.text.Length > 0 && AvText.Width(label) + 166f > width;

        private static float ValueWidth(float width) => Mathf.Min(90f, Mathf.Max(1f, width - 68f));
        private float ControlsHeight(float width) => Mathf.Max(AvGridTokens.Row, AvText.Height(value, ValueWidth(width)) + 8f);

        public override float Measure(float width)
        {
            if (Stacked(width)) return AvText.Height(label, width) + 4f + ControlsHeight(width);
            return Mathf.Max(ControlsHeight(width), AvText.Height(label, Mathf.Max(1f, width - 166f)) + 8f);
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            bool stacked = Stacked(s.W);
            float height = ControlsHeight(s.W), labelH = stacked ? AvText.Height(label, s.W) : s.H;
            float y = stacked ? labelH + 4f : (s.H - height) * 0.5f;
            AvLay.Place(label.rectTransform, 0f, 0f, stacked ? s.W : Mathf.Max(0f, s.W - 166f), labelH);
            float groupX = Mathf.Max(0f, s.W - 158f);
            AvLay.Place(minus.Rect, groupX, y + (height - AvGridTokens.Row) * 0.5f, 30f, AvGridTokens.Row);
            AvLay.Place(value.rectTransform, groupX + 34f, y, ValueWidth(s.W), height);
            AvLay.Place(plus.Rect, s.W - 30f, y + (height - AvGridTokens.Row) * 0.5f, 30f, AvGridTokens.Row);
        }

        public override void Restyle()
        {
            label.color = AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary);
            value.color = AvStyleHost.FuiInk("row-value", AvTheme.TextPrimary);
            minus.Restyle(); plus.Restyle();
        }
    }
}
