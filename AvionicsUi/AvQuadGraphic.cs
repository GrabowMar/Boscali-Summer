using System;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>
    /// Filled rectangles, flat or gradient, in the owner's top-left pixel space ((0,0) is the rect's top-left, y grows
    /// down), batched into one mesh. <see cref="Begin"/>, <see cref="Add(float, float, float, float)"/> any number of
    /// times, then <see cref="End"/> once.
    /// </summary>
    public sealed class AvQuadGraphic : MaskableGraphic
    {
        /// <summary>The most quads one graphic draws; further <c>Add</c> calls are ignored.</summary>
        public const int MaxQuads = 640;

        private struct Quad
        {
            public float X, Y, W, H;
            public Color A, B;
            public bool Horizontal, Tinted;
        }

        private Quad[] quads = new Quad[32];
        private int count;

        public void Begin() => count = 0;

        /// <summary>A rectangle in the graphic's own <see cref="Graphic.color"/>.</summary>
        public void Add(float x, float y, float w, float h) => Push(new Quad { X = x, Y = y, W = w, H = h, Tinted = true });

        public void Add(float x, float y, float w, float h, Color c) => Add(x, y, w, h, c, c, false);

        /// <summary>A gradient from <paramref name="a"/> (top, or left when horizontal) to <paramref name="b"/>.</summary>
        public void Add(float x, float y, float w, float h, Color a, Color b, bool horizontal) =>
            Push(new Quad { X = x, Y = y, W = w, H = h, A = a, B = b, Horizontal = horizontal });

        public void End() => SetVerticesDirty();

        private void Push(Quad q)
        {
            if (q.W <= 0f || q.H <= 0f) return;
            if (count == quads.Length)
            {
                if (count >= MaxQuads) return;
                Array.Resize(ref quads, Mathf.Min(count * 2, MaxQuads));
            }
            quads[count++] = q;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            for (int i = 0; i < count; i++)
            {
                Quad q = quads[i];
                if (q.Tinted) Emit(vh, r, q.X, q.Y, q.W, q.H, color, color, color, color);
                else if (q.Horizontal) Emit(vh, r, q.X, q.Y, q.W, q.H, q.A, q.B, q.B, q.A);
                else Emit(vh, r, q.X, q.Y, q.W, q.H, q.A, q.A, q.B, q.B);
            }
        }

        /// <summary>One rectangle at (<paramref name="x"/>, <paramref name="y"/>) from <paramref name="r"/>'s top-left, one colour per corner.</summary>
        public static void Emit(VertexHelper vh, Rect r, float x, float y, float w, float h,
            Color topLeft, Color topRight, Color bottomRight, Color bottomLeft)
        {
            float x0 = r.xMin + x, x1 = x0 + w, y1 = r.yMax - y, y0 = y1 - h;
            int c = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), bottomLeft, Vector4.zero);
            vh.AddVert(new Vector3(x0, y1), topLeft, Vector4.zero);
            vh.AddVert(new Vector3(x1, y1), topRight, Vector4.zero);
            vh.AddVert(new Vector3(x1, y0), bottomRight, Vector4.zero);
            vh.AddTriangle(c, c + 1, c + 2);
            vh.AddTriangle(c, c + 2, c + 3);
        }
    }
}
