using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>
    /// The FUI frame: square fill + fine stroke + optional corner brackets,
    /// all in ONE mesh and one draw. Colours are vertex colours so a state change dirties only this
    /// Graphic's vertices; the Graphic's own colour stays white so it batches with its siblings.
    /// </summary>
    public sealed class AvFrame : MaskableGraphic
    {
        // Kept for existing module builders; frames now have one standardized rectangular silhouette.
        public AvChamfer Chamfer;
        public float Stroke = 0.8f;
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

        /// <summary>Paints the card style <paramref name="classes"/> from the sheet, the given tokens standing in where it names none.</summary>
        public void PaintCard(string classes, Color fill, Color stroke)
        {
            AvStyle c = AvStyleHost.FuiStyle(classes);
            Paint(AvStyleHost.Resolve(c.Background, fill), AvStyleHost.Resolve(c.Border, stroke));
        }

        // Every button, row, card and chip is an AvFrame and its mesh rebuilds on each hover, press and restyle: shared scratch, no garbage.
        private static readonly AvV2[] outerScratch = new AvV2[8], innerScratch = new AvV2[8];

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            AvV2[] outer = AvMeshMath.ChamferPolygon(r.xMin, r.yMin, r.xMax, r.yMax, default, outerScratch);
            float s = Mathf.Max(0f, Stroke);
            AvV2[] inner = AvMeshMath.ChamferPolygon(r.xMin + s, r.yMin + s, r.xMax - s, r.yMax - s, default, innerScratch);

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
                        new Color(.78f, .88f, .92f, .08f * FillColor.a));
                    AvMesh.Quad(vh, r.xMin + inset, r.yMin + 1f, r.xMax - inset, r.yMin + 2f,
                        new Color(0f, 0f, 0f, .12f * FillColor.a));
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
                // Registration ticks stay square and leave the top-right clear for controls.
                float t = Mathf.Max(1f, s), L = Mathf.Min(Bracket, Mathf.Min(r.width, r.height) * 0.4f);
                Quad(vh, r.xMin, r.yMax - t, r.xMin + L, r.yMax); Quad(vh, r.xMin, r.yMax - L, r.xMin + t, r.yMax);
                Quad(vh, r.xMax - L, r.yMin, r.xMax, r.yMin + t); Quad(vh, r.xMax - t, r.yMin, r.xMax, r.yMin + L);
            }
        }

        // Diffuse top light and a darker lower plane give even dark fills depth. Keep alpha intact:
        // opaque reading planes stay opaque, and selection washes retain their semantic tint.
        private Color SurfaceColor(Rect r, float y)
        {
            float t = Mathf.InverseLerp(r.yMin, r.yMax, y);
            Color c = Color.Lerp(FillColor * 0.80f, FillColor, t);
            c = Color.Lerp(c, new Color(.78f, .88f, .92f, FillColor.a), .035f * t);
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
