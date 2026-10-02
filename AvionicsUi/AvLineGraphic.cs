using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>Polyline with optional gradient fill-under, one mesh (UILineRenderer-class, own code). Capped at 512 points.</summary>
    public sealed class AvLineGraphic : MaskableGraphic
    {
        public const int MaxPoints = 512;
        public float Thickness = 1.5f;
        public bool FillUnder = true;
        public Color LineColor = Color.white, FillTop = new Color(1f, 1f, 1f, 0.18f), FillBottom = new Color(1f, 1f, 1f, 0f);
        private readonly float[] xs = new float[MaxPoints], ys = new float[MaxPoints];
        private int count;

        public void SetPoints(float[] xs01, float[] ys01, int n)
        {
            n = Mathf.Clamp(n, 0, MaxPoints);
            if (xs01 == null || ys01 == null) n = 0;
            for (int i = 0; i < n; i++) { xs[i] = Mathf.Clamp01(xs01[i]); ys[i] = Mathf.Clamp01(ys01[i]); }
            count = n;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (count < 2) return;
            Rect r = GetPixelAdjustedRect();
            if (FillUnder)
            {
                for (int i = 1; i < count; i++)
                {
                    Vector2 a = P(r, i - 1), b = P(r, i);
                    int c = vh.currentVertCount;
                    vh.AddVert(new Vector3(a.x, r.yMin), FillBottom, Vector4.zero);
                    vh.AddVert(new Vector3(a.x, a.y), Color.Lerp(FillBottom, FillTop, ys[i - 1]), Vector4.zero);
                    vh.AddVert(new Vector3(b.x, b.y), Color.Lerp(FillBottom, FillTop, ys[i]), Vector4.zero);
                    vh.AddVert(new Vector3(b.x, r.yMin), FillBottom, Vector4.zero);
                    vh.AddTriangle(c, c + 1, c + 2); vh.AddTriangle(c, c + 2, c + 3);
                }
            }
            float h = Thickness * 0.5f;
            for (int i = 1; i < count; i++)
            {
                Vector2 a = P(r, i - 1), b = P(r, i);
                Vector2 d = (b - a).normalized, n = new Vector2(-d.y, d.x) * h;
                a -= d * h; b += d * h;  // overlap ends so joins have no cracks
                int c = vh.currentVertCount;
                vh.AddVert(a - n, LineColor, Vector4.zero); vh.AddVert(a + n, LineColor, Vector4.zero);
                vh.AddVert(b + n, LineColor, Vector4.zero); vh.AddVert(b - n, LineColor, Vector4.zero);
                vh.AddTriangle(c, c + 1, c + 2); vh.AddTriangle(c, c + 2, c + 3);
            }
        }

        private Vector2 P(Rect r, int i) => new Vector2(r.xMin + xs[i] * r.width, r.yMin + ys[i] * r.height);
    }
}
