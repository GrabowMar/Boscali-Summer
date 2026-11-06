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
}

namespace NOAvionics
{
    /// <summary>Static console decoration: a ruler under the header and registration crosses at two corners. No motion.</summary>
    public sealed class AvDecorGraphic : MaskableGraphic
    {
        public float RulerTop = 32f;

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

    /// <summary>
    /// The console body's graticule: faint registration crosses on a fixed pitch (every fourth one larger) in whatever part of the
    /// body the current page leaves empty, so it reads as a powered display, not a gap. The grid is fixed to the body, so rows
    /// only appear or vanish as content grows; the mesh is rebuilt only when the body is resized or <see cref="Top"/> moves.
    /// </summary>
    public sealed class AvGroundGraphic : MaskableGraphic
    {
        private const float Pitch = 32f, Arm = 2.5f, Inset = 12f, Clear = 10f;
        private float top;

        /// <summary>Height of the current page's content from the body top; no cross is drawn above it (plus a small clearance).</summary>
        public float Top
        {
            get => top;
            set { if (Mathf.Abs(value - top) < 0.5f) return; top = value; SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            if (r.width < 2f * Inset || r.height < 2f * Inset) return;
            Color minor = color, major = color;
            major.a = Mathf.Min(1f, major.a * 1.8f);
            int cols = Mathf.FloorToInt((r.width - 2f * Inset) / Pitch), rows = Mathf.FloorToInt((r.height - 2f * Inset) / Pitch);
            float x0 = Mathf.Round(r.center.x - cols * Pitch * 0.5f), y0 = Mathf.Round(r.yMax - Inset), floor = r.yMax - top - Clear;
            for (int j = 0; j <= rows; j++)
                for (int i = 0; i <= cols && y0 - j * Pitch + 4f <= floor; i++)
                {
                    bool big = i % 4 == 0 && j % 4 == 0;
                    float a = big ? 4f : Arm, x = x0 + i * Pitch, y = y0 - j * Pitch;
                    Color c = big ? major : minor;
                    AvMesh.Quad(vh, x - a, y - 0.5f, x + a, y + 0.5f, c);
                    AvMesh.Quad(vh, x - 0.5f, y - a, x + 0.5f, y + a, c);
                }
        }
    }
}
