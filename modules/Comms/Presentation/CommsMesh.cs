using BoscaliSummer.Features.Comms.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Comms.Presentation
{
    /// <summary>
    /// Line-art primitives for the comms graphics: quads for segments, polylines with a dark
    /// under-stroke so ink reads over any terrain, rings and <see cref="CommsGlyphs"/> glyphs.
    /// Every size is given in the caller's local units; the caller converts pixels.
    /// </summary>
    internal static class CommsMesh
    {
        public static readonly Color32 Under = new Color32(8, 10, 14, 170);

        public static Color32 Tone(CommsTone tone, byte alpha = 255)
        {
            switch (tone)
            {
                case CommsTone.Danger: return new Color32(255, 86, 72, alpha);
                case CommsTone.Caution: return new Color32(255, 190, 64, alpha);
                case CommsTone.Friendly: return new Color32(84, 196, 255, alpha);
                case CommsTone.Fun: return new Color32(255, 128, 214, alpha);
                default: return new Color32(214, 236, 246, alpha);
            }
        }

        public static Color32 Ink(int pen, byte alpha = 255)
        {
            PenInk ink = CommsCatalog.Pens[pen < 0 || pen >= CommsCatalog.Pens.Length ? 0 : pen];
            return new Color32(ink.R, ink.G, ink.B, alpha);
        }

        public static Color32 Fade(Color32 colour, float alpha)
        {
            colour.a = (byte)Mathf.Clamp(Mathf.RoundToInt(colour.a * Mathf.Clamp01(alpha)), 0, 255);
            return colour;
        }

        public static void Segment(VertexHelper vh, Vector2 a, Vector2 b, float halfWidth, Color32 ink)
        {
            Vector2 delta = b - a;
            float length = delta.magnitude;
            if (length < 1e-5f) return;
            Vector2 side = new Vector2(-delta.y, delta.x) / length * halfWidth;
            // Extend each end by half a width so consecutive segments meet without a notch.
            Vector2 along = delta / length * (halfWidth * 0.5f);
            Quad(vh, a - along - side, a - along + side, b + along + side, b + along - side, ink);
        }

        public static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 ink)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = ink;
            int index = vh.currentVertCount;
            vertex.position = a;
            vh.AddVert(vertex);
            vertex.position = b;
            vh.AddVert(vertex);
            vertex.position = c;
            vh.AddVert(vertex);
            vertex.position = d;
            vh.AddVert(vertex);
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }

        /// <summary>A filled disc, fanned from its centre.</summary>
        public static void Disc(VertexHelper vh, Vector2 centre, float radius, Color32 ink, int segments = 12)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = ink;
            int start = vh.currentVertCount;
            vertex.position = centre;
            vh.AddVert(vertex);
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                vertex.position = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                vh.AddVert(vertex);
            }
            for (int i = 0; i < segments; i++) vh.AddTriangle(start, start + 1 + i, start + 2 + i);
        }

        public static void Ring(VertexHelper vh, Vector2 centre, float radius, float halfWidth, Color32 ink, int segments = 32)
        {
            Vector2 previous = centre + new Vector2(radius, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector2 next = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Segment(vh, previous, next, halfWidth, ink);
                previous = next;
            }
        }

        /// <summary>A dashed straight line, dash and gap in local units.</summary>
        public static void Dashed(VertexHelper vh, Vector2 a, Vector2 b, float halfWidth, float dash, float gap, Color32 ink)
        {
            Vector2 delta = b - a;
            float length = delta.magnitude;
            if (length < 1e-5f || dash <= 0f) return;
            Vector2 unit = delta / length;
            int budget = 256;
            for (float t = 0f; t < length && budget-- > 0; t += dash + gap)
                Segment(vh, a + unit * t, a + unit * Mathf.Min(length, t + dash), halfWidth, ink);
        }

        /// <summary>
        /// Draw a glyph centred on <paramref name="centre"/>, <paramref name="half"/> local units
        /// from the centre to the edge of its box, with an under-stroke when one is asked for.
        /// </summary>
        public static void Glyph(VertexHelper vh, string key, Vector2 centre, float half, float halfWidth,
            Color32 ink, bool under, float rotationDegrees = 0f)
        {
            float[][] strokes = CommsGlyphs.Get(key);
            float cos = Mathf.Cos(rotationDegrees * Mathf.Deg2Rad), sin = Mathf.Sin(rotationDegrees * Mathf.Deg2Rad);
            if (under) Pass(vh, strokes, centre, half, halfWidth * 1.9f + 0.6f, Under, cos, sin);
            Pass(vh, strokes, centre, half, halfWidth, ink, cos, sin);
        }

        private static void Pass(VertexHelper vh, float[][] strokes, Vector2 centre, float half, float halfWidth,
            Color32 ink, float cos, float sin)
        {
            for (int s = 0; s < strokes.Length; s++)
            {
                float[] p = strokes[s];
                for (int i = 2; i + 1 < p.Length; i += 2)
                {
                    Vector2 a = centre + Rotate(p[i - 2], p[i - 1], cos, sin) * half;
                    Vector2 b = centre + Rotate(p[i], p[i + 1], cos, sin) * half;
                    Segment(vh, a, b, halfWidth, ink);
                }
            }
        }

        private static Vector2 Rotate(float x, float y, float cos, float sin) =>
            new Vector2(x * cos - y * sin, x * sin + y * cos);
    }

    /// <summary>
    /// One glyph in a UI rect: the palette buttons, the log rails and the cockpit markers all
    /// use it. Redraws only when its key, tint or rotation changes.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class CommsGlyphGraphic : MaskableGraphic
    {
        private string key = "mark";
        private float rotation;
        private float thickness = 1.1f;
        private bool under;

        public void Set(string glyph, Color tint, float stroke = 1.1f, bool underStroke = false)
        {
            bool dirty = glyph != key || !Mathf.Approximately(stroke, thickness) || under != underStroke;
            key = glyph;
            thickness = stroke;
            under = underStroke;
            if (color != tint) color = tint;
            if (dirty) SetVerticesDirty();
        }

        public void SetRotation(float degrees)
        {
            if (Mathf.Abs(degrees - rotation) < 0.5f) return;
            rotation = degrees;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            float half = Mathf.Min(r.width, r.height) * 0.5f - thickness;
            if (half <= 0.5f) return;
            CommsMesh.Glyph(vh, key, r.center, half, thickness, color, under, rotation);
        }
    }
}
