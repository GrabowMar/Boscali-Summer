using TMPro;
using UnityEngine;

namespace NOAvionics
{
    /// <summary>The one hero reading of a page: mono value + unit + caption.</summary>
    public sealed class AvReadout : AvPart
    {
        private readonly TMP_Text value, unit, caption;
        private float placedW = -1f;

        public AvReadout(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Readout");
            value = AvText.Make(Rect, "Value", AvTextRole.Display);
            unit = AvText.Make(Rect, "Unit", AvTextRole.Label);
            AvText.Fit(value, false); AvText.Fit(unit, true);
            caption = AvText.Make(Rect, "Caption", AvTextRole.Micro, "", TextAlignmentOptions.MidlineLeft, true);
            Restyle();
        }

        public void Set(string v, string u, string c = null)
        {
            bool changed = AvText.Set(value, v);
            changed |= AvText.Set(unit, u);
            changed |= AvText.Set(caption, c);
            if (!changed) return;
            if (placedW > 0f) Arrange(placedW);
            Changed();
        }

        private bool StackUnit(float width) => unit.text.Length > 0 && AvText.Width(value) + 6f + AvText.Width(unit) > width;

        private float ReadingHeight(float width) => StackUnit(width) ? 32f + Mathf.Max(20f, AvText.Height(unit, width)) : 32f;

        public override float Measure(float width) => ReadingHeight(Mathf.Max(1f, width)) +
            (caption.text.Length > 0 ? Mathf.Max(16f, AvText.Height(caption, width)) : 0f);

        public override void Place(AvSlot slot)
        {
            base.Place(slot);
            placedW = slot.W;
            Arrange(slot.W);
        }

        private void Arrange(float w)
        {
            w = Mathf.Max(1f, w);
            bool stacked = StackUnit(w);
            float vw = stacked ? w : Mathf.Min(AvText.Width(value) + 2f, w);
            AvLay.Place(value.rectTransform, 0f, 0f, vw, 32f);
            AvLay.Place(unit.rectTransform, stacked ? 0f : vw + 4f, stacked ? 32f : 9f,
                stacked ? w : Mathf.Max(0f, w - vw - 4f), stacked ? Mathf.Max(20f, AvText.Height(unit, w)) : 20f);
            AvLay.Place(caption.rectTransform, 0f, ReadingHeight(w), w,
                caption.text.Length > 0 ? Mathf.Max(16f, AvText.Height(caption, w)) : 0f);
        }

        public override void Restyle()
        {
            value.color = AvStyleHost.FuiInk("readout", AvTheme.TextPrimary);
            unit.color = caption.color = AvStyleHost.FuiInk("readout-unit", AvTheme.Dim);
        }
    }
}
