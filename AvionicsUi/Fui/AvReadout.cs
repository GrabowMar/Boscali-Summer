using NOAvionics;
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
            AvText.Fit(value, false); AvText.Fit(unit, false); // long readings shrink, never spill past the unit
            caption = AvText.Make(Rect, "Caption", AvTextRole.Micro);
            Restyle();
        }

        public void Set(string v, string u, string c = null)
        {
            bool hadCaption = caption.text.Length > 0;
            bool moved = false;
            if (value.text != (v ?? "")) { value.text = v ?? ""; moved = true; }
            if (unit.text != (u ?? "")) unit.text = u ?? "";
            if (caption.text != (c ?? "")) caption.text = c ?? "";
            // The unit sits right after the value, so a new value re-places it (it used to keep the
            // layout-time position and overlap a longer value).
            if (moved && placedW > 0f) Arrange(placedW);
            if (hadCaption != caption.text.Length > 0) Changed();
        }

        public override float Measure(float width) => caption.text.Length > 0 ? 48f : 32f;

        public override void Place(AvSlot slot)
        {
            base.Place(slot);
            placedW = slot.W;
            Arrange(slot.W);
        }

        private void Arrange(float w)
        {
            float vw = Mathf.Min(AvText.Width(value), w - 40f);
            AvLay.Place(value.rectTransform, 0f, 0f, vw + 2f, 32f);
            AvLay.Place(unit.rectTransform, vw + 6f, 9f, Mathf.Max(0f, w - vw - 6f), 20f);
            AvLay.Place(caption.rectTransform, 0f, 32f, w, 16f);
        }

        public override void Restyle()
        {
            value.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("readout").Color, AvTheme.TextPrimary);
            unit.color = caption.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("readout-unit").Color, AvTheme.Dim);
        }
    }
}
