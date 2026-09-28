using NOAvionics;
using TMPro;
using UnityEngine;

namespace NOAvionics.Ui
{
    public sealed class AvGauge : AvPart
    {
        private readonly float size;
        private readonly TMP_Text key, value;
        private readonly AvGaugeGraphic dial;
        private AvState state = AvState.Ready;

        public AvGauge(RectTransform parent, string keyText, AvGaugeShape shape, float diameter = 88f)
        {
            size = diameter;
            Rect = AvLay.Child(parent, "Gauge " + keyText);
            key = AvText.Make(Rect, "Key", AvTextRole.Micro, keyText, TextAlignmentOptions.Center);
            var go = new GameObject("Dial", typeof(RectTransform));
            go.transform.SetParent(Rect, false);
            dial = go.AddComponent<AvGaugeGraphic>();
            dial.Shape = shape; dial.Thickness = 6f; dial.Segments = 12; dial.raycastTarget = false;
            value = AvText.Make(Rect, "Value", AvTextRole.DataStrong, "", TextAlignmentOptions.Center);
            Restyle();
        }

        public void Set(float v01, string text, AvState st = AvState.Ready)
        {
            dial.Value = v01;
            if (value.text != text) value.text = text ?? "";
            if (st != state) { state = st; Restyle(); }
        }

        public override float Measure(float width) => size + 18f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float x = (s.W - size) * 0.5f;
            AvLay.Place(key.rectTransform, 0f, 0f, s.W, 16f);
            AvLay.Place((RectTransform)dial.transform, x, 18f, size, size);
            AvLay.Place(value.rectTransform, x, 18f + size * 0.5f - 10f, size, 20f);
        }

        public override void Restyle()
        {
            key.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("metric-key").Color, AvTheme.RailInfo);
            value.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("metric-value").Color, AvTheme.TextPrimary);
            dial.Track = AvStyleHost.Resolve(AvStyleHost.FuiStyle("gauge-track").Background, AvTheme.Hairline);
            Color fill = AvStyleHost.Resolve(AvStyleHost.FuiStyle("metric-fill " + AvStates.Class(state)).Background, AvTheme.Accent);
            dial.FillColor = fill; dial.FillEnd = Color.Lerp(fill, Color.white, 0.25f);
            dial.SetVerticesDirty();
        }
    }
}
