using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    public enum AvGaugeShape { Bar, Segments, Arc, Ring }

    /// <summary>Bar / segmented bar / arc / ring gauge in one mesh (UICircle-class, own code).</summary>
    public sealed class AvGaugeGraphic : MaskableGraphic
    {
        public AvGaugeShape Shape;
        public int Segments = 10;
        public float StartDeg = 210f, SweepDeg = 240f, Thickness = 4f, SegmentGap = 2f;
        public Color Track = new Color(1f, 1f, 1f, 0.15f), FillColor = Color.white, FillEnd = Color.white, TickColor = new Color(1f, 1f, 1f, 0.3f);
        public int Ticks;
        private float value;

        public float Value
        {
            get => value;
            set
            {
                float v = Mathf.Clamp01(float.IsNaN(value) ? 0f : value);
                if (Mathf.Abs(v - this.value) < 0.001f) return;
                this.value = v;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            switch (Shape)
            {
                case AvGaugeShape.Bar:
                    Quad(vh, r.xMin, r.yMin, r.xMax, r.yMax, Track, Track);
                    if (value > 0f) Quad(vh, r.xMin, r.yMin, r.xMin + r.width * value, r.yMax, FillColor, Color.Lerp(FillColor, FillEnd, value));
                    break;
                case AvGaugeShape.Segments:
                    int n = Mathf.Max(1, Segments), lit = AvMeshMath.SegmentsLit(n, value);
                    float w = (r.width - SegmentGap * (n - 1)) / n;
                    for (int i = 0; i < n; i++)
                    {
                        float x = r.xMin + i * (w + SegmentGap);
                        Color c = i < lit ? Color.Lerp(FillColor, FillEnd, n == 1 ? 0f : i / (float)(n - 1)) : Track;
                        Quad(vh, x, r.yMin, x + w, r.yMax, c, c);
                    }
                    break;
                default:
                    float sweep = Shape == AvGaugeShape.Ring ? 360f : SweepDeg;
                    float rad = Mathf.Min(r.width, r.height) * 0.5f, cx = r.center.x, cy = r.center.y;
                    if (Ticks > 0) { TickRing(vh, cx, cy, rad, rad - 3f); rad -= 5f; }
                    Arc(vh, cx, cy, rad, rad - Thickness, StartDeg, -sweep, Track, Track);
                    if (value > 0f) Arc(vh, cx, cy, rad, rad - Thickness, StartDeg, -sweep * value, FillColor, FillEnd);
                    break;
            }
        }

        private void TickRing(VertexHelper vh, float cx, float cy, float r0, float r1)
        {
            for (int i = 0; i < Ticks; i++)
            {
                float deg = Shape == AvGaugeShape.Arc ? StartDeg - SweepDeg * i / Mathf.Max(1, Ticks - 1) : AvPortalMath.TickDegrees(i, Ticks);
                float long_ = i % 6 == 0 ? 2f : 0f;
                AvV2 o = AvMeshMath.ArcPoint(cx, cy, r0, deg), n = AvMeshMath.ArcPoint(cx, cy, r1 - long_, deg);
                float nx = -(n.Y - o.Y), ny = n.X - o.X, l = Mathf.Sqrt(nx * nx + ny * ny);
                if (l < 0.01f) continue;
                nx = nx / l * 0.5f; ny = ny / l * 0.5f;
                int c = vh.currentVertCount;
                vh.AddVert(new Vector3(o.X - nx, o.Y - ny), TickColor, Vector4.zero); vh.AddVert(new Vector3(o.X + nx, o.Y + ny), TickColor, Vector4.zero);
                vh.AddVert(new Vector3(n.X + nx, n.Y + ny), TickColor, Vector4.zero); vh.AddVert(new Vector3(n.X - nx, n.Y - ny), TickColor, Vector4.zero);
                vh.AddTriangle(c, c + 1, c + 2); vh.AddTriangle(c, c + 2, c + 3);
            }
        }

        private static void Arc(VertexHelper vh, float cx, float cy, float r0, float r1, float start, float sweep, Color a, Color b)
        {
            int steps = AvMeshMath.ArcSteps(sweep);
            int c = vh.currentVertCount;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps, deg = start + sweep * t;
                Color col = Color.Lerp(a, b, t);
                AvV2 o = AvMeshMath.ArcPoint(cx, cy, r0, deg), n = AvMeshMath.ArcPoint(cx, cy, r1, deg);
                vh.AddVert(new Vector3(o.X, o.Y), col, Vector4.zero);
                vh.AddVert(new Vector3(n.X, n.Y), col, Vector4.zero);
                if (i > 0) { int k = c + i * 2; vh.AddTriangle(k - 2, k, k + 1); vh.AddTriangle(k - 2, k + 1, k - 1); }
            }
        }

        private static void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color left, Color right)
        {
            int c = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), left, Vector4.zero); vh.AddVert(new Vector3(x0, y1), left, Vector4.zero);
            vh.AddVert(new Vector3(x1, y1), right, Vector4.zero); vh.AddVert(new Vector3(x1, y0), right, Vector4.zero);
            vh.AddTriangle(c, c + 1, c + 2); vh.AddTriangle(c, c + 2, c + 3);
        }
    }
}
