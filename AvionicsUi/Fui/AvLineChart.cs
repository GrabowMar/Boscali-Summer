using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics.Ui
{
    public sealed class AvLineChart : AvPart
    {
        private const float AxisW = 56f;
        private readonly float height;
        private readonly bool spark;
        private readonly AvLineGraphic line;
        private readonly Image grid0, grid1, cursor;
        private readonly TMP_Text max, min, last;
        private readonly float[] xs = new float[AvLineGraphic.MaxPoints], ys = new float[AvLineGraphic.MaxPoints];
        private int count;

        public AvLineChart(RectTransform parent, float h = 150f, bool sparkline = false)
        {
            height = sparkline ? 28f : h; spark = sparkline;
            Rect = AvLay.Child(parent, sparkline ? "Spark" : "Chart");
            grid0 = AvLay.Solid(Rect, "Grid0", Color.clear); grid1 = AvLay.Solid(Rect, "Grid1", Color.clear);
            var go = new GameObject("Line", typeof(RectTransform));
            go.transform.SetParent(Rect, false);
            line = go.AddComponent<AvLineGraphic>(); line.raycastTarget = false; line.FillUnder = !sparkline;
            cursor = AvLay.Solid(Rect, "Cursor", Color.clear);
            max = AvText.Make(Rect, "Max", AvTextRole.DataSmall, "", TextAlignmentOptions.TopRight);
            min = AvText.Make(Rect, "Min", AvTextRole.DataSmall, "", TextAlignmentOptions.BottomRight);
            last = AvText.Make(Rect, "Last", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
            max.gameObject.SetActive(!spark); min.gameObject.SetActive(!spark); grid0.enabled = grid1.enabled = !spark;
            Restyle();
        }

        public void SetSeries(float[] values, int n, string minLabel, string maxLabel, string lastLabel)
        {
            count = Mathf.Clamp(n, 0, AvLineGraphic.MaxPoints);
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < count; i++) { lo = Mathf.Min(lo, values[i]); hi = Mathf.Max(hi, values[i]); }
            float span = Mathf.Max(1e-6f, hi - lo);
            for (int i = 0; i < count; i++) { xs[i] = count == 1 ? 1f : i / (float)(count - 1); ys[i] = (values[i] - lo) / span; }
            line.SetPoints(xs, ys, count);
            min.text = minLabel ?? ""; max.text = maxLabel ?? ""; last.text = lastLabel ?? "";
            PlaceCursor();
        }

        public override float Measure(float width) => height;

        private RectTransform Plot => (RectTransform)line.transform;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float left = spark ? 0f : AxisW, right = spark ? 64f : 0f;
            AvLay.Place(Plot, left, 4f, s.W - left - right, s.H - 8f);
            AvLay.Place(grid0.rectTransform, left, 4f, s.W - left - right, 1f);
            AvLay.Place(grid1.rectTransform, left, s.H - 5f, s.W - left - right, 1f);
            AvLay.Place(max.rectTransform, 0f, 0f, AxisW - 6f, 16f);
            AvLay.Place(min.rectTransform, 0f, s.H - 16f, AxisW - 6f, 16f);
            PlaceCursor();
        }

        private void PlaceCursor()
        {
            if (count == 0) { cursor.enabled = false; last.text = ""; return; }
            Rect r = Plot.rect;
            Vector2 p = Plot.anchoredPosition;
            float x = p.x + r.width, y = -p.y + (1f - ys[count - 1]) * r.height;
            cursor.enabled = true;
            AvLay.Place(cursor.rectTransform, x - 3f, y - 3f, 6f, 6f);
            AvLay.Place(last.rectTransform, spark ? x + 6f : x - 90f, y - (spark ? 8f : 20f), spark ? 58f : 86f, 16f);
        }

        public override void Restyle()
        {
            Color c = AvStyleHost.Resolve(AvStyleHost.FuiStyle("chart-line").Color, AvTheme.Accent);
            line.LineColor = c; line.FillTop = c.WithAlpha(0.22f); line.FillBottom = c.WithAlpha(0f); line.SetVerticesDirty();
            cursor.color = c;
            Color grid = AvStyleHost.Resolve(AvStyleHost.FuiStyle("chart-grid").Background, AvTheme.Hairline);
            grid0.color = grid1.color = grid;
            max.color = min.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("chart-axis").Color, AvTheme.Disabled);
            last.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("readout").Color, AvTheme.TextPrimary);
        }
    }
}
