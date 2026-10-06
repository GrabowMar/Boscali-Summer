using TMPro;
using UnityEngine;

namespace NOAvionics
{
    public sealed class AvGauge : AvPart
    {
        private readonly AvFrame frame;
        private readonly TMP_Text key, value;
        private readonly AvGaugeGraphic dial;
        private AvState state = AvState.Ready;

        public AvGauge(RectTransform parent, string keyText, AvGaugeShape shape, float diameter = 88f)
        {
            Rect = AvLay.Child(parent, "Gauge " + keyText);
            frame = AvFrame.Add(Rect, "Instrument", AvChamfer.Diagonal(2f));
            AvLay.Fill(frame.rectTransform); frame.Bracket = 3f;
            key = AvText.Make(Rect, "Key", AvTextRole.Micro, keyText);
            AvText.Fit(key, false);
            var go = new GameObject("Dial", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(Rect, false);
            dial = go.AddComponent<AvGaugeGraphic>();
            // All cockpit meters share a rectangular ladder, including legacy Ring/Arc callers.
            dial.Shape = AvGaugeShape.Segments; dial.Ticks = 0; dial.Segments = 10; dial.SegmentGap = 1.5f; dial.raycastTarget = false;
            value = AvText.Make(Rect, "Value", AvTextRole.DataStrong, "");
            AvText.Fit(value, false);
            Restyle();
        }

        /// <summary>Hover help shown while the pointer is anywhere over the instrument.</summary>
        public string Help
        {
            get => tip != null ? tip.Text : null;
            set { frame.raycastTarget = !string.IsNullOrEmpty(value); tip = AvHelpTip.Attach(frame.gameObject, value); }
        }
        private AvHelpTip tip;

        public void Set(float v01, string text, AvState st = AvState.Ready)
        {
            dial.Value = v01;
            if (value.text != text) value.text = text ?? "";
            if (st != state) { state = st; Restyle(); }
        }

        public override float Measure(float width) => 48f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(key.rectTransform, 6f, 3f, s.W - 12f, 15f);
            AvLay.Place(value.rectTransform, 6f, 18f, s.W - 12f, 19f);
            AvLay.Place((RectTransform)dial.transform, 6f, s.H - 8f, s.W - 12f, 4f);
        }

        public override void Restyle()
        {
            key.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("metric-key").Color, AvTheme.RailInfo);
            frame.Paint(AvStyleHost.FuiColor("surface-inert", AvTheme.SurfaceInert), AvStyleHost.FuiColor("hairline", AvTheme.Hairline));
            frame.BracketColor = AvStyleHost.FuiColor("ink-muted", AvTheme.Dim).WithAlpha(0.5f);
            frame.SetVerticesDirty();
            value.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("metric-value").Color, AvTheme.TextPrimary);
            dial.Track = AvStyleHost.Resolve(AvStyleHost.FuiStyle("gauge-track").Background, AvTheme.Hairline);
            dial.TickColor = AvStyleHost.Resolve(AvStyleHost.FuiStyle("chart-axis").Color, AvTheme.Hairline);
            Color fill = AvStyleHost.Resolve(AvStyleHost.FuiStyle("metric-fill " + AvStates.Class(state)).Background, AvTheme.Accent);
            dial.FillColor = fill; dial.FillEnd = Color.Lerp(fill, Color.white, 0.25f);
            dial.SetVerticesDirty();
        }
    }
}
