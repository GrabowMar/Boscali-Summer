using TMPro;
using UnityEngine;

namespace NOAvionics
{
    /// <summary>Metric tile: key / mono value / unit / gauge. Lives on the console's live canvas.</summary>
    public sealed class AvMetric : AvPart
    {
        private readonly AvFrame frame;
        private readonly TMP_Text key, value, unit, symbol;
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
            symbol = AvIcons.Make(Rect, Symbol(keyText), 22f, Color.white);
            frame.Bracket = 3f;
            AvText.Fit(key, false); AvText.Fit(value, false); AvText.Fit(unit, false);
            var go = new GameObject("Gauge", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(Rect, false);
            gauge = go.AddComponent<AvGaugeGraphic>();
            gauge.Shape = shape; gauge.raycastTarget = false;
            Restyle();
        }

        public override float Measure(float width) => AvGridTokens.Metric;

        private AvSlot lastSlot;
        private bool placed, tipOwned;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            lastSlot = s; placed = true;
            // Boxes match each role's rendered line height (Display 24 -> 32, Micro 11 -> 15).
            // The unit shares the key line, or the value line, and is left off when neither has room:
            // the tile cannot grow, and a clipped unit fails the overflow gate.
            if (!unit.gameObject.activeSelf) unit.gameObject.SetActive(true);
            float avail = s.W - 12f, kw = AvText.Width(key) + 4f, uw = AvText.Width(unit) + 4f, vw = AvText.Width(value) + 6f;
            MetricLine line = AvFlowMath.PlaceMetric(avail, kw, uw, vw);
            unit.gameObject.SetActive(line.UnitShown);
            if (line.UnitOnTop)
            {
                float keyW = Mathf.Max(0f, avail - uw - 6f);
                AvLay.Place(key.rectTransform, 6f, 1f, keyW, 15f);
                AvLay.Place(unit.rectTransform, 6f + keyW + 6f, 1f, uw, 15f);
                AvLay.Place(value.rectTransform, 6f, 14f, avail, 32f);
            }
            else if (line.UnitByValue)
            {
                AvLay.Place(key.rectTransform, 6f, 1f, avail, 15f);
                AvLay.Place(value.rectTransform, 6f, 14f, Mathf.Max(0f, avail - uw - 6f), 32f);
                AvLay.Place(unit.rectTransform, 6f + avail - uw, 27f, uw, 15f);
            }
            else
            {
                AvLay.Place(key.rectTransform, 6f, 1f, avail, 15f);
                AvLay.Place(value.rectTransform, 6f, 14f, avail, 32f);
            }
            AvLay.Place((RectTransform)gauge.transform, 6f, s.H - 4f, s.W - 12f, 3f);
            // Decorative symbol uses only spare space; it never displaces a number or unit.
            symbol.gameObject.SetActive(!line.UnitByValue && vw + 28f < avail);
            AvLay.Place(symbol.rectTransform, s.W - 29f, 19f, 22f, 22f);
            SyncHelp(line.UnitShown);
        }

        /// <summary>A hidden unit still has to be readable on hover, unless the caller already wrote the tip.</summary>
        private void SyncHelp(bool unitShown)
        {
            AvHelpTip tip = Rect.GetComponent<AvHelpTip>();
            if (unitShown)
            {
                if (tipOwned && tip != null) tip.Text = null;
                tipOwned = false;
                return;
            }
            if (tip != null && !tipOwned && !string.IsNullOrEmpty(tip.Text)) return;
            AvHelpTip.Attach(Rect.gameObject, (key.text + " " + value.text + " " + (shownUnit ?? "")).Trim());
            tipOwned = true;
        }

        public void Set(string v, string u, float fill01, AvState st = AvState.Ready)
        {
            bool text = false;
            if (v != shownValue) { shownValue = v; value.text = v ?? ""; text = true; }
            string uu = AvStates.Glyph(st) + (u ?? "");
            if (uu != shownUnit) { shownUnit = uu; unit.text = uu; text = true; }
            if (text && placed) Place(lastSlot);   // the key/unit/value split depends on the text widths
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
            symbol.color = unit.color.WithAlpha(0.65f);
            frame.BracketColor = unit.color.WithAlpha(0.6f);
            gauge.Track = AvStyleHost.Resolve(AvStyleHost.FuiStyle("metric-track").Background, AvTheme.Hairline);
            gauge.FillColor = gauge.FillEnd = AvStyleHost.Resolve(AvStyleHost.FuiStyle("metric-fill " + AvStates.Class(state)).Background, AvTheme.Accent);
            gauge.SetVerticesDirty();
        }

        private static AvIcon Symbol(string label)
        {
            string k = (label ?? "").ToUpperInvariant();
            if (k.Contains("AIR") || k.Contains("SORTIE")) return AvIcon.Plane;
            if (k.Contains("FUND") || k.Contains("COST") || k.Contains("BUDGET")) return AvIcon.Coins;
            if (k.Contains("STAFF") || k.Contains("CREW") || k.Contains("PILOT")) return AvIcon.UsersGroup;
            if (k.Contains("RANK") || k.Contains("SCORE")) return AvIcon.Star;
            if (k.Contains("SECTOR") || k.Contains("FRONT")) return AvIcon.Map2;
            if (k.Contains("TIME") || k.Contains("COOL")) return AvIcon.Clock;
            if (k.Contains("LINK") || k.Contains("SIGNAL")) return AvIcon.Antenna;
            if (k.Contains("WIND")) return AvIcon.Wind;
            if (k.Contains("PICK")) return AvIcon.Stack2;
            return AvIcon.Activity;
        }
    }
}
