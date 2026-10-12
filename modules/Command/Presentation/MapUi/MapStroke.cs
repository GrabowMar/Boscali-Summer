using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Anti-aliased vector strokes for map layers. uGUI geometry has no edge smoothing of its
    /// own, so every stroke carries a feather: a band across each edge where the ink fades to
    /// clear. A feather of one screen pixel gives a clean line; a wide one gives a glow.
    /// </summary>
    internal static class MapStroke
    {
        /// <summary>Vertices one polyline point costs in <see cref="Ribbon"/>.</summary>
        internal const int RibbonVertices = 4;
        /// <summary>Vertices one <see cref="Segment"/> costs.</summary>
        internal const int SegmentVertices = 8;

        /// <summary>One straight feathered stroke, in the graphic's own units.</summary>
        internal static void Segment(VertexHelper vh, Vector2 a, Vector2 b, float halfWidth, float feather, Color32 ink)
        {
            Vector2 delta = b - a;
            float length = delta.magnitude;
            if (length < .01f) return;
            Vector2 normal = new Vector2(-delta.y, delta.x) / length;
            Row(vh, a, normal, halfWidth, feather, ink);
            Row(vh, b, normal, halfWidth, feather, ink);
            Bridge(vh, vh.currentVertCount - RibbonVertices * 2);
        }

        /// <summary>
        /// One continuous stroke through <paramref name="points"/>[start, end) with mitred joints,
        /// so translucent ink never doubles up or gaps at a bend. Points and widths are in screen
        /// pixels; <paramref name="toLocal"/> converts them to the graphic's units.
        /// </summary>
        internal static void Ribbon(VertexHelper vh, Vector2[] points, int start, int end, float toLocal,
            float halfWidth, float feather, Color32 ink)
        {
            if (end - start < 2) return;
            halfWidth *= toLocal;
            feather *= toLocal;
            for (int i = start; i < end; i++)
            {
                Vector2 into = i > start ? (points[i] - points[i - 1]).normalized : Vector2.zero;
                Vector2 away = i + 1 < end ? (points[i + 1] - points[i]).normalized : Vector2.zero;
                Vector2 along = (into + away).normalized;
                if (along == Vector2.zero) along = into != Vector2.zero ? into : Vector2.right;
                Vector2 normal = new Vector2(-along.y, along.x);
                // Widen the joint so the stroke keeps its width through the bend, within reason.
                Vector2 leg = away != Vector2.zero ? away : into;
                float cosine = Mathf.Abs(normal.x * -leg.y + normal.y * leg.x);
                Row(vh, points[i] * toLocal, normal / Mathf.Max(.5f, cosine), halfWidth, feather, ink);
                if (i > start) Bridge(vh, vh.currentVertCount - RibbonVertices * 2);
            }
        }

        /// <summary>Clear, ink, ink, clear across the stroke at one point.</summary>
        private static void Row(VertexHelper vh, Vector2 at, Vector2 normal, float halfWidth, float feather, Color32 ink)
        {
            float inner = halfWidth - feather * .5f, outer = halfWidth + feather * .5f;
            if (inner < 0f)
            {
                // Thinner than its own feather: keep the same total ink in a fainter peak.
                ink.a = (byte)(ink.a * Mathf.Clamp01(halfWidth * 2f / feather + .5f));
                inner = 0f;
            }
            Color32 clear = ink;
            clear.a = 0;
            UIVertex vert = UIVertex.simpleVert;
            vert.color = clear; vert.position = at - normal * outer; vh.AddVert(vert);
            vert.color = ink; vert.position = at - normal * inner; vh.AddVert(vert);
            vert.position = at + normal * inner; vh.AddVert(vert);
            vert.color = clear; vert.position = at + normal * outer; vh.AddVert(vert);
        }

        /// <summary>Joins the two rows that start at <paramref name="first"/>.</summary>
        private static void Bridge(VertexHelper vh, int first)
        {
            for (int column = 0; column < RibbonVertices - 1; column++)
            {
                int a = first + column, b = a + RibbonVertices;
                vh.AddTriangle(a, a + 1, b + 1);
                vh.AddTriangle(a, b + 1, b);
            }
        }
    }
}
