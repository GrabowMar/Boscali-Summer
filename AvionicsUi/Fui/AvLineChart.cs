using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    public sealed class AvLineChart : AvPart
    {
        private const float AxisW = 56f;
        private readonly float height;
        private readonly bool spark;
        private readonly AvLineGraphic line;
        private readonly ChartGrid grid;
        private readonly Image cursor;
        private readonly TMP_Text max, min, last, wait;
        private readonly float[] xs = new float[AvLineGraphic.MaxPoints], ys = new float[AvLineGraphic.MaxPoints];
        private int count;
        private AvSlot lastSlot;
        private bool placed;

        public AvLineChart(RectTransform parent, float h = 150f, bool sparkline = false)
        {
            height = sparkline ? 28f : h; spark = sparkline;
            Rect = AvLay.Child(parent, sparkline ? "Spark" : "Chart");
            var gridGo = new GameObject("Grid", typeof(RectTransform), typeof(CanvasRenderer));
            gridGo.transform.SetParent(Rect, false);
            grid = gridGo.AddComponent<ChartGrid>(); grid.raycastTarget = false;
            var go = new GameObject("Line", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(Rect, false);
            line = go.AddComponent<AvLineGraphic>(); line.raycastTarget = false; line.FillUnder = !sparkline;
            cursor = AvLay.Solid(Rect, "Cursor", Color.clear);
            max = AvText.Make(Rect, "Max", AvTextRole.DataSmall, "", TextAlignmentOptions.TopRight);
            min = AvText.Make(Rect, "Min", AvTextRole.DataSmall, "", TextAlignmentOptions.BottomRight);
            last = AvText.Make(Rect, "Last", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
            wait = AvText.Make(Rect, "Wait", AvTextRole.Micro, "AWAITING SAMPLES", TextAlignmentOptions.Center);
            AvText.Fit(wait, false); AvText.Fit(last, false); AvText.Fit(max, false); AvText.Fit(min, false);
            max.gameObject.SetActive(!spark); min.gameObject.SetActive(!spark); grid.gameObject.SetActive(!spark);
            wait.gameObject.SetActive(false);
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
            if (placed) Place(lastSlot);
            else PlaceCursor();
        }

        public override float Measure(float width) => height;

        private RectTransform Plot => (RectTransform)line.transform;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            lastSlot = s; placed = true;
            float left = spark ? 0f : AxisW, right = spark ? 64f : 0f, plotW = s.W - left - right, plotH = s.H - 8f;
            AvLay.Place(grid.rectTransform, left, 4f, plotW, plotH);
            AvLay.Place(Plot, left, 4f, plotW, plotH);
            bool waiting = !spark && count < 2;
            wait.gameObject.SetActive(waiting);
            if (waiting) AvLay.Place(wait.rectTransform, left, Mathf.Max(4f, (s.H - 16f) * 0.5f), plotW, 16f);
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
            // A trace that ends at its maximum would push the label above the part and into the
            // line above; clamp it to the plot instead.
            float ly = spark ? y - 8f : Mathf.Clamp(y - 20f, 2f, Mathf.Max(2f, lastSlot.H - 18f));
            AvLay.Place(last.rectTransform, spark ? x + 6f : x - 90f, ly, spark ? 58f : 86f, 16f);
        }

        public override void Restyle()
        {
            Color c = AvStyleHost.Resolve(AvStyleHost.FuiStyle("chart-line").Color, AvTheme.Accent);
            line.LineColor = Color.Lerp(c, Color.white, 0.2f); line.FillTop = c.WithAlpha(0.32f); line.FillBottom = c.WithAlpha(0.015f); line.SetVerticesDirty();
            cursor.color = c;
            grid.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("chart-grid").Background, AvTheme.Hairline);
            grid.SetVerticesDirty();
            Color axis = AvStyleHost.Resolve(AvStyleHost.FuiStyle("chart-axis").Color, AvTheme.Disabled);
            max.color = min.color = wait.color = axis;
            last.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("readout").Color, AvTheme.TextPrimary);
        }

        /// <summary>Four hairlines across the plot. UILineRenderer-class mesh, one draw, no hit target.</summary>
        private sealed class ChartGrid : MaskableGraphic
        {
            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                Rect r = GetPixelAdjustedRect();
                if (r.width < 1f || r.height < 1f) return;
                for (int i = 0; i < 4; i++)
                {
                    float y = r.yMin + r.height * i / 3f;
                    if (y > r.yMax - 1f) y = r.yMax - 1f;
                    Hairline(vh, r.xMin, y, r.width, color);
                }
                Color minor = color; minor.a *= 0.55f;
                for (int i = 0; i <= 8; i++)
                {
                    float x = Mathf.Min(r.xMax - 0.65f, r.xMin + r.width * i / 8f);
                    // Broken rules leave the trace readable at the smallest MFD size.
                    for (float y = r.yMin; y < r.yMax; y += 7f)
                        AvMesh.Quad(vh, x, y, x + 0.65f, Mathf.Min(y + 2f, r.yMax), minor);
                }
            }

            private static void Hairline(VertexHelper vh, float x, float y, float w, Color c)
            {
                int n = vh.currentVertCount;
                vh.AddVert(new Vector3(x, y), c, Vector2.zero);
                vh.AddVert(new Vector3(x, y + 1f), c, Vector2.zero);
                vh.AddVert(new Vector3(x + w, y + 1f), c, Vector2.zero);
                vh.AddVert(new Vector3(x + w, y), c, Vector2.zero);
                vh.AddTriangle(n, n + 1, n + 2);
                vh.AddTriangle(n, n + 2, n + 3);
            }
        }
    }
}
