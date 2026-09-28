using NOAvionics;
using TMPro;
using UnityEngine;

namespace NOAvionics.Ui
{
    /// <summary>The one hero reading of a page: mono value + unit + caption.</summary>
    public sealed class AvReadout : AvPart
    {
        private readonly TMP_Text value, unit, caption;

        public AvReadout(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Readout");
            value = AvText.Make(Rect, "Value", AvTextRole.Display);
            unit = AvText.Make(Rect, "Unit", AvTextRole.Label);
            caption = AvText.Make(Rect, "Caption", AvTextRole.Micro);
            Restyle();
        }

        public void Set(string v, string u, string c = null)
        {
            if (value.text != v) value.text = v ?? "";
            if (unit.text != (u ?? "")) unit.text = u ?? "";
            if (caption.text != (c ?? "")) caption.text = c ?? "";
        }

        public override float Measure(float width) => caption.text.Length > 0 ? 46f : 30f;

        public override void Place(AvSlot slot)
        {
            base.Place(slot);
            float vw = AvText.Width(value);
            AvLay.Place(value.rectTransform, 0f, 0f, vw + 2f, 30f);
            AvLay.Place(unit.rectTransform, vw + 6f, 8f, slot.W - vw - 6f, 20f);
            AvLay.Place(caption.rectTransform, 0f, 30f, slot.W, 16f);
        }

        public override void Restyle()
        {
            value.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("readout").Color, AvTheme.TextPrimary);
            unit.color = caption.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("readout-unit").Color, AvTheme.Dim);
        }
    }
}
