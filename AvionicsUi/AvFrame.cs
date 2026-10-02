using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>
    /// The FUI frame (samolevsky vocabulary): chamfered fill + 1 px stroke + optional corner brackets,
    /// all in ONE mesh and one draw. Colours are vertex colours so a state change dirties only this
    /// Graphic's vertices; the Graphic's own colour stays white so it batches with its siblings.
    /// </summary>
    public sealed class AvFrame : MaskableGraphic
    {
        public AvChamfer Chamfer;
        public float Stroke = 0.65f;
        public float Bracket;
        public bool Fill = true;
        public Color FillColor = new Color(0f, 0f, 0f, 0.5f);
        public Color StrokeColor = Color.white;
        public Color BracketColor = Color.white;

        public static AvFrame Add(RectTransform parent, string name, AvChamfer chamfer)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var f = go.AddComponent<AvFrame>();
            f.Chamfer = chamfer;
            f.raycastTarget = false;
            return f;
        }

        public void Paint(Color fill, Color stroke)
        {
            if (fill == FillColor && stroke == StrokeColor) return;
            FillColor = fill; StrokeColor = stroke;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            AvV2[] outer = AvMeshMath.ChamferPolygon(r.xMin, r.yMin, r.xMax, r.yMax, Chamfer);
            float s = Mathf.Max(0f, Stroke);
            AvV2[] inner = AvMeshMath.ChamferPolygon(r.xMin + s, r.yMin + s, r.xMax - s, r.yMax - s, AvMeshMath.Inset(Chamfer, s));

            if (Fill && FillColor.a > 0f)
            {
                int c = vh.currentVertCount;
                vh.AddVert(new Vector3(r.center.x, r.center.y), SurfaceColor(r, r.center.y), Vector4.zero);
                for (int i = 0; i < 8; i++) vh.AddVert(new Vector3(inner[i].X, inner[i].Y), SurfaceColor(r, inner[i].Y), Vector4.zero);
                for (int i = 0; i < 8; i++) vh.AddTriangle(c, c + 1 + i, c + 1 + (i + 1) % 8);
                // Inset light and shadow stay in this mesh: no extra materials or animated overlays.
                if (r.width > 24f && r.height > 20f)
                {
                    float inset = Mathf.Max(6f, s + 2f);
                    AvMesh.Quad(vh, r.xMin + inset, r.yMax - 2f, r.xMax - inset, r.yMax - 1.25f,
                        new Color(.64f, .77f, .79f, .075f * FillColor.a));
                    AvMesh.Quad(vh, r.xMin + inset, r.yMin + 1f, r.xMax - inset, r.yMin + 3f,
                        new Color(0f, 0f, 0f, .28f * FillColor.a));
                }
            }
            if (s > 0f && StrokeColor.a > 0f)
            {
                int c = vh.currentVertCount;
                for (int i = 0; i < 8; i++)
                {
                    vh.AddVert(new Vector3(outer[i].X, outer[i].Y), StrokeColor, Vector4.zero);
                    vh.AddVert(new Vector3(inner[i].X, inner[i].Y), StrokeColor, Vector4.zero);
                }
                for (int i = 0; i < 8; i++)
                {
                    int a = c + i * 2, b = c + ((i + 1) % 8) * 2;
                    vh.AddTriangle(a, b, b + 1);
                    vh.AddTriangle(a, b + 1, a + 1);
                }
            }
            if (Bracket > 0f && BracketColor.a > 0f)
            {
                float t = Mathf.Max(0.65f, s), L = Bracket;
                Quad(vh, r.xMin, r.yMax - t, r.xMin + L, r.yMax); Quad(vh, r.xMin, r.yMax - L, r.xMin + t, r.yMax);
                Quad(vh, r.xMax - L, r.yMin, r.xMax, r.yMin + t); Quad(vh, r.xMax - t, r.yMin, r.xMax, r.yMin + L);
            }
        }

        // A quiet top light gives the glass depth without a texture, material instance or extra draw.
        private Color SurfaceColor(Rect r, float y)
        {
            float t = Mathf.InverseLerp(r.yMin, r.yMax, y);
            Color c = Color.Lerp(FillColor * 0.68f, FillColor, t);
            c.a = FillColor.a;
            return c;
        }

        private void Quad(VertexHelper vh, float x0, float y0, float x1, float y1)
        {
            int c = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), BracketColor, Vector4.zero);
            vh.AddVert(new Vector3(x0, y1), BracketColor, Vector4.zero);
            vh.AddVert(new Vector3(x1, y1), BracketColor, Vector4.zero);
            vh.AddVert(new Vector3(x1, y0), BracketColor, Vector4.zero);
            vh.AddTriangle(c, c + 1, c + 2); vh.AddTriangle(c, c + 2, c + 3);
        }
    }
}
