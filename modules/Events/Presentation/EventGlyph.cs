using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Events.Presentation
{
    /// <summary>
    /// Category mark for a card's icon slot, drawn as a vector until a real PNG exists for
    /// the entry. Deliberately self-contained: the Command module's glyph widget is not a
    /// contract, and copying a whole glyph set for three shapes would not pay for itself.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class EventGlyph : MaskableGraphic
    {
        public const string Economic = "economic";
        public const string Political = "political";
        public const string Hazard = "hazard";

        private string kind = Economic;

        public void SetKind(string value)
        {
            string next = value == Political ? Political
                : value == Hazard ? Hazard
                : Economic;
            if (next == kind) return;
            kind = next;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            switch (kind)
            {
                case Political:
                    Path(mesh, .15f, .05f, .15f, .95f, .85f, .8f, .15f, .55f);
                    break;
                case Hazard:
                    Path(mesh, .5f, .95f, .97f, .15f, .03f, .15f, .5f, .95f);
                    Line(mesh, .5f, .3f, .5f, .62f);
                    Line(mesh, .5f, .2f, .5f, .24f);
                    break;
                default:
                    Ring(mesh, .5f, .5f, .34f, 16);
                    Line(mesh, .5f, .18f, .5f, .82f);
                    Line(mesh, .32f, .66f, .68f, .66f);
                    Line(mesh, .32f, .34f, .68f, .34f);
                    break;
            }
        }

        private void Path(VertexHelper mesh, params float[] points)
        {
            for (int i = 2; i < points.Length; i += 2)
                Line(mesh, points[i - 2], points[i - 1], points[i], points[i + 1]);
        }

        private void Ring(VertexHelper mesh, float cx, float cy, float radius, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                float b = (i + 1) / (float)segments * Mathf.PI * 2f;
                Line(mesh, cx + Mathf.Cos(a) * radius, cy + Mathf.Sin(a) * radius,
                           cx + Mathf.Cos(b) * radius, cy + Mathf.Sin(b) * radius);
            }
        }

        private void Line(VertexHelper mesh, float x1, float y1, float x2, float y2)
        {
            Rect r = rectTransform.rect;
            var a = new Vector2(r.x + x1 * r.width, r.y + y1 * r.height);
            var b = new Vector2(r.x + x2 * r.width, r.y + y2 * r.height);
            Vector2 d = (b - a).normalized;
            Vector2 n = new Vector2(-d.y, d.x) * .7f;
            int start = mesh.currentVertCount;
            mesh.AddVert(a - n, color, Vector2.zero); mesh.AddVert(a + n, color, Vector2.zero);
            mesh.AddVert(b + n, color, Vector2.zero); mesh.AddVert(b - n, color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
