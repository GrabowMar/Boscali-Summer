using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics.Ui
{
    /// <summary>Metric tile: key / mono value / unit / gauge. Lives on the console's live canvas.</summary>
    public sealed class AvMetric : AvPart
    {
        private readonly AvFrame frame;
        private readonly TMP_Text key, value, unit;
        private readonly AvGaugeGraphic gauge;
        private AvState state = AvState.Ready;
        private string shownValue, shownUnit;

        public AvMetric(RectTransform parent, string keyText, AvGaugeShape shape = AvGaugeShape.Bar)
        {
            Rect = AvLay.Child(parent, "Metric " + keyText);
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f)); AvLay.Fill(frame.rectTransform);
            key = AvText.Make(Rect, "Key", AvTextRole.Micro, keyText);
            value = AvText.Make(Rect, "Value", AvTextRole.Display);
            unit = AvText.Make(Rect, "Unit", AvTextRole.Micro, "", TextAlignmentOptions.MidlineRight);
            var go = new GameObject("Gauge", typeof(RectTransform));
            go.transform.SetParent(Rect, false);
            gauge = go.AddComponent<AvGaugeGraphic>();
            gauge.Shape = shape; gauge.raycastTarget = false;
            Restyle();
        }

        public override float Measure(float width) => AvGridTokens.Metric;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(key.rectTransform, 8f, 4f, s.W - 16f, 14f);
            AvLay.Place(value.rectTransform, 8f, 18f, s.W - 16f, 28f);
            AvLay.Place(unit.rectTransform, 8f, 44f, s.W - 16f, 12f);
            AvLay.Place((RectTransform)gauge.transform, 8f, s.H - 6f, s.W - 16f, 3f);
        }

        public void Set(string v, string u, float fill01, AvState st = AvState.Ready)
        {
            if (v != shownValue) { shownValue = v; value.text = v ?? ""; }
            string uu = AvStates.Glyph(st) + (u ?? "");
            if (uu != shownUnit) { shownUnit = uu; unit.text = uu; }
            gauge.Value = fill01;
            if (st != state) { state = st; Restyle(); }
        }

        public override void Restyle()
        {
            AvStyle m = AvStyleHost.FuiStyle("metric");
            frame.Paint(AvStyleHost.Resolve(m.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(m.Border, AvTheme.Hairline));
            key.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("metric-key").Color, AvTheme.RailInfo);
            value.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("metric-value").Color, AvTheme.TextPrimary);
            unit.color = state == AvState.Caution || state == AvState.Danger
                ? AvStyleHost.Resolve(AvStyleHost.FuiStyle("chip " + AvStates.Class(state)).Color, AvTheme.Warning)
                : AvStyleHost.Resolve(AvStyleHost.FuiStyle("metric-unit").Color, AvTheme.Dim);
            gauge.Track = AvStyleHost.Resolve(AvStyleHost.FuiStyle("metric-track").Background, AvTheme.Hairline);
            gauge.FillColor = gauge.FillEnd = AvStyleHost.Resolve(AvStyleHost.FuiStyle("metric-fill " + AvStates.Class(state)).Background, AvTheme.Accent);
            gauge.SetVerticesDirty();
        }
    }
}
