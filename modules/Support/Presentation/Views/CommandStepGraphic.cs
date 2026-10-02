using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Views
{
    /// <summary>Six-sided command key with a recessed lead-in and a forward point.</summary>
    internal sealed class CommandStepGraphic : MaskableGraphic
    {
        private Color fill;
        public Color FillColor { get => fill; set { if (fill == value) return; fill = value; SetVerticesDirty(); } }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect r = rectTransform.rect;
            float tip = Mathf.Min(18f, r.height * 0.27f);
            Vector2[] points = { new Vector2(r.xMin, r.yMax), new Vector2(r.xMax - tip, r.yMax),
                new Vector2(r.xMax, r.center.y), new Vector2(r.xMax - tip, r.yMin),
                new Vector2(r.xMin, r.yMin), new Vector2(r.xMin + tip, r.center.y) };
            mesh.AddVert(r.center, fill, Vector2.zero);
            for (int i = 0; i < points.Length; i++) mesh.AddVert(points[i], fill, Vector2.zero);
            for (int i = 0; i < points.Length; i++) mesh.AddTriangle(0, i + 1, (i + 1) % points.Length + 1);
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 a = points[i], b = points[(i + 1) % points.Length];
                Vector2 d = (b - a).normalized, n = new Vector2(-d.y, d.x) * 0.5f;
                int start = mesh.currentVertCount;
                mesh.AddVert(a + n, color, Vector2.zero); mesh.AddVert(b + n, color, Vector2.zero);
                mesh.AddVert(b - n, color, Vector2.zero); mesh.AddVert(a - n, color, Vector2.zero);
                mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
            }
        }
    }
}
