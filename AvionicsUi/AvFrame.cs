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

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            if (r.width <= 0f || r.height <= 0f) return;
            float s = StrokeColor.a > 0f ? Mathf.Clamp(Stroke, 0f, .5f * Mathf.Min(r.width, r.height)) : 0f;
            if (Fill && FillColor.a > 0f)
                AvMesh.Quad(vh, r.xMin + s, r.yMin + s, r.xMax - s, r.yMax - s, FillColor);
            if (s > 0f)
            {
                AvMesh.Quad(vh, r.xMin, r.yMin, r.xMax, r.yMin + s, StrokeColor);
                AvMesh.Quad(vh, r.xMin, r.yMax - s, r.xMax, r.yMax, StrokeColor);
                AvMesh.Quad(vh, r.xMin, r.yMin + s, r.xMin + s, r.yMax - s, StrokeColor);
                AvMesh.Quad(vh, r.xMax - s, r.yMin + s, r.xMax, r.yMax - s, StrokeColor);
            }
            if (Bracket > 0f && BracketColor.a > 0f)
            {
                // Registration ticks stay square and leave the top-right clear for controls.
                float t = Mathf.Max(1f, s), L = Mathf.Min(Bracket, Mathf.Min(r.width, r.height) * 0.4f);
                AvMesh.Quad(vh, r.xMin, r.yMax - t, r.xMin + L, r.yMax, BracketColor);
                AvMesh.Quad(vh, r.xMin, r.yMax - L, r.xMin + t, r.yMax - t, BracketColor);
                AvMesh.Quad(vh, r.xMax - L, r.yMin, r.xMax, r.yMin + t, BracketColor);
                AvMesh.Quad(vh, r.xMax - t, r.yMin + t, r.xMax, r.yMin + L, BracketColor);
            }
        }

    }
}
