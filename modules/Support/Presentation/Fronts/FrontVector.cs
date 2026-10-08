using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// One vertex-coloured vector layer for the front rooms: lines, dashes, rings, discs, polygons, all in panel pixels
    /// (origin top-left, y down) so a room draws its whole map (grid, ground tracks, footprints, glyphs) in a single mesh.
    /// Cosmetic geometry only; it never takes input.
    /// </summary>
    internal sealed class FrontVector : MaskableGraphic
    {
        private readonly List<Vector2> pos = new List<Vector2>(2048);
        private readonly List<Color32> col = new List<Color32>(2048);
        private readonly List<int> idx = new List<int>(4096);

        public static FrontVector Add(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<FrontVector>();
            g.raycastTarget = false;
            return g;
        }

        public void Clear() { pos.Clear(); col.Clear(); idx.Clear(); }

        /// <summary>Pushes the drawn geometry to the canvas.</summary>
        public void Flush() => SetVerticesDirty();

        private int V(Vector2 p, Color c) { pos.Add(p); col.Add(c); return pos.Count - 1; }

        public void Tri(Vector2 a, Vector2 b, Vector2 c, Color k)
        {
            int i = V(a, k); V(b, k); V(c, k);
            idx.Add(i); idx.Add(i + 1); idx.Add(i + 2);
        }

        public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color k) { Tri(a, b, c, k); Tri(a, c, d, k); }

        /// <summary>A quad with a colour per corner (top-left, top-right, bottom-right, bottom-left): soft vignettes and scrims.</summary>
        public void Grad(float x, float y, float w, float h, Color tl, Color tr, Color br, Color bl)
        {
            int i = V(new Vector2(x, y), tl); V(new Vector2(x + w, y), tr); V(new Vector2(x + w, y + h), br); V(new Vector2(x, y + h), bl);
            idx.Add(i); idx.Add(i + 1); idx.Add(i + 2); idx.Add(i); idx.Add(i + 2); idx.Add(i + 3);
        }

        public void Rect(float x, float y, float w, float h, Color k) =>
            Quad(new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h), k);

        public void Frame(float x, float y, float w, float h, float t, Color k)
        {
            Rect(x, y, w, t, k); Rect(x, y + h - t, w, t, k); Rect(x, y, t, h, k); Rect(x + w - t, y, t, h, k);
        }

        public void Line(Vector2 a, Vector2 b, float w, Color k)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            Vector2 n = new Vector2(-d.y, d.x).normalized * (w * 0.5f);
            Quad(a + n, b + n, b - n, a - n, k);
        }

        public void Dashed(Vector2 a, Vector2 b, float w, Color k, float dash, float gap)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1e-3f) return;
            Vector2 u = d / len;
            for (float s = 0f; s < len; s += dash + gap) Line(a + u * s, a + u * Mathf.Min(len, s + dash), w, k);
        }

        public void Polyline(IList<Vector2> pts, float w, Color k, bool closed = false)
        {
            for (int i = 1; i < pts.Count; i++) Line(pts[i - 1], pts[i], w, k);
            if (closed && pts.Count > 2) Line(pts[pts.Count - 1], pts[0], w, k);
        }

        public void Ring(Vector2 c, float r, float w, Color k, int seg = 56, float dash = 0f, float gap = 0f)
        {
            Vector2 prev = c + new Vector2(r, 0f);
            for (int i = 1; i <= seg; i++)
            {
                float a = 2f * Mathf.PI * i / seg;
                Vector2 next = c + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                if (dash <= 0f || (i / 2) % 2 == 0) Line(prev, next, w, k);
                prev = next;
            }
        }

        public void Disc(Vector2 c, float r, Color k, int seg = 40)
        {
            int hub = V(c, k);
            for (int i = 0; i <= seg; i++)
            {
                float a = 2f * Mathf.PI * i / seg;
                V(c + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r), k);
            }
            for (int i = 0; i < seg; i++) { idx.Add(hub); idx.Add(hub + 1 + i); idx.Add(hub + 2 + i); }
        }

        public void Diamond(Vector2 c, float r, Color k) =>
            Quad(c + new Vector2(0f, -r), c + new Vector2(r, 0f), c + new Vector2(0f, r), c + new Vector2(-r, 0f), k);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            for (int i = 0; i < pos.Count; i++)
                vh.AddVert(new Vector3(r.xMin + pos[i].x, r.yMax - pos[i].y, 0f), col[i], Vector4.zero);
            for (int i = 0; i + 2 < idx.Count; i += 3) vh.AddTriangle(idx[i], idx[i + 1], idx[i + 2]);
        }
    }
}
