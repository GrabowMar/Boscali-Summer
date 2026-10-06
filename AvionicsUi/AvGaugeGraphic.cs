using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    public enum AvGaugeShape { Bar, Segments }

    /// <summary>Bar / segmented bar gauge in one mesh (UICircle-class, own code).</summary>
    public sealed class AvGaugeGraphic : MaskableGraphic
    {
        public AvGaugeShape Shape;
        public int Segments = 10;
        public float SegmentGap = 2f;
        public Color Track = new Color(1f, 1f, 1f, 0.15f), FillColor = Color.white, FillEnd = Color.white;
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
