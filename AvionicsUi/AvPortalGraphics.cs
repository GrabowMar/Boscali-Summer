using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    internal static class AvMesh
    {
        public static void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color c)
        {
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), c, Vector4.zero); vh.AddVert(new Vector3(x0, y1), c, Vector4.zero);
            vh.AddVert(new Vector3(x1, y1), c, Vector4.zero); vh.AddVert(new Vector3(x1, y0), c, Vector4.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }

        public static void Fan(VertexHelper vh, AvV2[] p, Color c)
        {
            int i = vh.currentVertCount;
            for (int k = 0; k < p.Length; k++) vh.AddVert(new Vector3(p[k].X, p[k].Y), c, Vector4.zero);
            for (int k = 1; k + 1 < p.Length; k++) vh.AddTriangle(i, i + k, i + k + 1);
        }

        public static void Outline(VertexHelper vh, Rect r, Color c)
        {
            Quad(vh, r.xMin, r.yMin, r.xMax, r.yMin + 1f, c); Quad(vh, r.xMin, r.yMax - 1f, r.xMax, r.yMax, c);
            Quad(vh, r.xMin, r.yMin + 1f, r.xMin + 1f, r.yMax - 1f, c); Quad(vh, r.xMax - 1f, r.yMin + 1f, r.xMax, r.yMax - 1f, c);
        }
    }

    /// <summary>Framed bar filled with 45-degree hazard stripes up to Value (progress, cooldown, risk).</summary>
    public sealed class AvHazardGraphic : MaskableGraphic
    {
        public Color FillColor = Color.white, FrameColor = new Color(1f, 1f, 1f, 0.3f);
        public float Period = 8f;
        private float value;

        public float Value
        {
            get => value;
            set { float v = Mathf.Clamp01(float.IsNaN(value) ? 0f : value); if (Mathf.Abs(v - this.value) < 0.001f) return; this.value = v; SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            AvMesh.Outline(vh, r, FrameColor);
            float x0 = r.xMin + 2f, x1 = r.xMax - 2f, y0 = r.yMin + 2f, y1 = r.yMax - 2f;
            if (value <= 0f || x1 <= x0) return;
            foreach (AvV2[] s in AvPortalMath.HazardStripes(x0, y0, x0 + (x1 - x0) * value, y1, Period)) AvMesh.Fan(vh, s, FillColor);
        }
    }

    /// <summary>Bottom-aligned histogram; the tallest bar is lifted toward white as a peak marker.</summary>
    public sealed class AvEqualizerGraphic : MaskableGraphic
    {
        public Color FillColor = Color.white, BaseColor = new Color(1f, 1f, 1f, 0.3f);
        public float Gap = 1f;
        private float[] values = new float[0];

        public void SetValues(float[] v) { values = v ?? new float[0]; SetVerticesDirty(); }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            if (r.width < 2f || r.height < 2f) return;
            // Scope graticule, same four hairlines as a chart. An empty series stays an instrument.
            Color guide = BaseColor;
            guide.a *= 0.55f;
            for (int g = 0; g < 4; g++)
            {
                float y = r.yMin + (r.height - 1f) * g / 3f;
                AvMesh.Quad(vh, r.xMin, y, r.xMax, y + 1f, guide);
            }
            int n = values.Length;
            if (n == 0) return;
            float w = (r.width - Gap * (n - 1)) / n, top = r.height - 2f;
            int peak = 0;
            for (int i = 1; i < n; i++) if (values[i] > values[peak]) peak = i;
            for (int i = 0; i < n; i++)
            {
                float x = r.xMin + i * (w + Gap);
                AvMesh.Quad(vh, x, r.yMin, x + 1f, r.yMax, guide);
                float v = Mathf.Clamp01(values[i]);
                if (v < 0.02f) continue;
                float h = Mathf.Max(2f, v * top);
                AvMesh.Quad(vh, x, r.yMin + 2f, x + Mathf.Max(2f, w), r.yMin + 2f + h,
                    i == peak ? Color.Lerp(FillColor, Color.white, 0.6f) : FillColor);
            }
        }
    }

    /// <summary>Grid of cells whose brightness is a 0..1 intensity; one cell can be selected (full colour).</summary>
    public sealed class AvHeatGraphic : MaskableGraphic
    {
        public int Cols = 8, Selected = -1;
        public float CellH = 9f, Gap = 2f;
        public Color FillColor = Color.white;
        private float[] cells = new float[0];

        public void SetCells(float[] c, int selected) { cells = c ?? new float[0]; Selected = selected; SetVerticesDirty(); }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float cw = (r.width - Gap * (Cols - 1)) / Cols;
            if (cells.Length == 0)
            {
                // No series yet: outline the slot grid so the grown region is a board, not a hole.
                int rows = Mathf.Max(1, Mathf.RoundToInt((r.height + Gap) / Mathf.Max(1f, CellH + Gap)));
                Color edge = FillColor;
                edge.a *= 0.28f;
                Color fill = FillColor;
                fill.a *= 0.06f;
                for (int row = 0; row < rows; row++)
                    for (int col = 0; col < Cols; col++)
                    {
                        float x = r.xMin + col * (cw + Gap);
                        float y = r.yMin + row * (CellH + Gap);
                        var cell = new Rect(x, y, cw, CellH);
                        AvMesh.Quad(vh, cell.xMin, cell.yMin, cell.xMax, cell.yMax, fill);
                        if (cw > 6f && CellH > 6f) AvMesh.Outline(vh, cell, edge);
                    }
                return;
            }
            for (int i = 0; i < cells.Length; i++)
            {
                AvV2 o = AvPortalMath.HeatCell(i, Cols, cw, CellH, Gap, r.height);
                float v = Mathf.Clamp01(cells[i]);
                Color fill = FillColor, edge = FillColor;
                fill.a *= i == Selected ? 1f : 0.14f + 0.72f * v;
                edge.a *= i == Selected ? 1f : 0.35f + 0.65f * v;
                var cell = new Rect(r.xMin + o.X, r.yMin + o.Y, cw, CellH);
                AvMesh.Quad(vh, cell.xMin, cell.yMin, cell.xMax, cell.yMax, fill);
                if (cw > 6f && CellH > 6f) AvMesh.Outline(vh, cell, edge);   // tall cells read as tactical tiles, not slabs
            }
        }
    }
}

namespace NOAvionics
{
    /// <summary>Static console decoration: a ruler under the header and registration crosses at two corners. No motion.</summary>
    public sealed class AvDecorGraphic : MaskableGraphic
    {
        public float RulerTop = 32f, RulerFrom = 66f, RulerPad = 10f, Step = 6f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            Color c = color;
            float y = r.yMax - RulerTop;
            AvMesh.Quad(vh, r.xMin + 8f, y, r.xMax - 8f, y + 0.65f, c);
            AvMesh.Quad(vh, r.xMin + 59f, r.yMax - RulerTop + 7f, r.xMin + 59.65f, r.yMax - 7f, c);
            float footer = r.yMin + NOAvionics.AvGridTokens.Footer;
            AvMesh.Quad(vh, r.xMin + 8f, footer, r.xMax - 8f, footer + 0.65f, c);
            // Edge calibrations stay in the reserved margins; they do not imply live measurements.
            for (float tick = footer + 24f; tick < y - 12f; tick += 48f)
            {
                AvMesh.Quad(vh, r.xMin + 1f, tick, r.xMin + 4f, tick + 0.65f, c);
                AvMesh.Quad(vh, r.xMax - 4f, tick, r.xMax - 1f, tick + 0.65f, c);
            }
            Cross(vh, r.xMin + 5f, footer, c);
            Cross(vh, r.xMax - 5f, footer, c);
            Cross(vh, r.xMin + 5f, r.yMin + 5f, c);
            Cross(vh, r.xMax - 5f, r.yMin + 5f, c);
        }

        private static void Cross(VertexHelper vh, float cx, float cy, Color c)
        {
            AvMesh.Quad(vh, cx - 4f, cy - 0.5f, cx + 4f, cy + 0.5f, c);
            AvMesh.Quad(vh, cx - 0.5f, cy - 4f, cx + 0.5f, cy + 4f, c);
        }
    }
}
