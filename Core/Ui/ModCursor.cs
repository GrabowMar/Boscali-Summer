using System.Collections.Generic;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>The mod's cursor glyphs. <see cref="None"/> is the OS arrow; <see cref="Blank"/> an empty image (a pad-driven menu).</summary>
    internal enum CursorShape { None, Blank, Arrow, Ground, Select, Drop, BoxAdd, BoxDrop, Grab, Orbit, Place, Move, Stack }

    /// <summary>
    /// The one writer of <c>Cursor.SetCursor</c>: every glyph is painted in code (outlined, theme-tinted, sized for the screen),
    /// cached, and written only when the shape changes. <see cref="Base"/> is what the cursor goes back to when a surface lets
    /// go of it: the themed arrow, or the OS arrow while the setting is off.
    /// </summary>
    internal static class ModCursor
    {
        private const float Unit = 32f;
        private static readonly Dictionary<CursorShape, Texture2D> cache = new Dictionary<CursorShape, Texture2D>();
        private static int cacheSize, cacheTheme = -1;

        internal static CursorShape Base = CursorShape.None;
        /// <summary>The shape a big window that holds the mouse claims (the OPS front window's placing reticle); None leaves the cursor to the map.</summary>
        internal static CursorShape Window = CursorShape.None;
        internal static CursorShape Current { get; private set; } = CursorShape.None;

        internal static void Restore() => Set(Base);

        internal static void Set(CursorShape shape)
        {
            int size = Screen.height >= 1600 ? 48 : 32;
            bool stale = size != cacheSize || AvStyleHost.FuiGeneration != cacheTheme;
            if (shape == Current && !stale) return;
            if (stale) Flush(size);
            Current = shape;
            if (shape == CursorShape.None) { Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); return; }
            if (!cache.TryGetValue(shape, out Texture2D texture)) cache[shape] = texture = Paint(shape, size);
            float k = size / Unit;
            Vector2 hot = shape == CursorShape.Arrow ? new Vector2(2f, 2f) * k : new Vector2(16f, 16f) * k;
            Cursor.SetCursor(texture, hot, CursorMode.Auto);
        }

        private static void Flush(int size)
        {
            foreach (Texture2D t in cache.Values) if (t != null) Object.Destroy(t);
            cache.Clear();
            cacheSize = size;
            cacheTheme = AvStyleHost.FuiGeneration;
        }

        // ---- painting: strokes and one filled polygon in a 32-unit box, y down, signed distance with a dark outline ----

        private struct Seg { public Vector2 A, B; public float W; }

        private static Texture2D Paint(CursorShape shape, int size)
        {
            var segs = new List<Seg>(48);
            Vector2[] poly = null;
            Color ink = AvTheme.Accent;
            Vector2 c = new Vector2(16f, 16f);
            switch (shape)
            {
                case CursorShape.Arrow:
                    poly = new[] { V(2, 2), V(2, 24), V(7.5f, 18.6f), V(11.8f, 27.6f), V(15.4f, 26f), V(11.2f, 17.2f), V(18.6f, 17.2f) };
                    break;
                case CursorShape.Ground:
                    Cross(segs, c, 4f, 11.5f, .85f);
                    Brackets(segs, c, 12.5f, 2.5f, .75f);
                    Dot(segs, c, 1.25f);
                    break;
                case CursorShape.Select:
                    Brackets(segs, c, 9.5f, 5f, .95f);
                    Dot(segs, c, 1.4f);
                    break;
                case CursorShape.Drop:
                    ink = AvTheme.Warning;
                    Brackets(segs, c, 9.5f, 5f, .95f);
                    Line(segs, V(11.5f, 16), V(20.5f, 16), 1.05f);
                    break;
                case CursorShape.BoxAdd:
                case CursorShape.BoxDrop:
                    if (shape == CursorShape.BoxDrop) ink = AvTheme.Warning;
                    Cross(segs, c, 2.5f, 8.5f, .9f);
                    Line(segs, V(21, 27), V(29, 27), .95f);
                    if (shape == CursorShape.BoxAdd) Line(segs, V(25, 23), V(25, 31), .95f);
                    break;
                case CursorShape.Grab:
                    Arc(segs, c, 3.2f, 0f, 360f, .85f);
                    Line(segs, V(16, 4), V(16, 12)); Line(segs, V(16, 20), V(16, 28));
                    Line(segs, V(4, 16), V(12, 16)); Line(segs, V(20, 16), V(28, 16));
                    Head(segs, V(16, 4), V(0, -1)); Head(segs, V(16, 28), V(0, 1));
                    Head(segs, V(4, 16), V(-1, 0)); Head(segs, V(28, 16), V(1, 0));
                    break;
                case CursorShape.Orbit:
                    Arc(segs, c, 9.5f, 25f, 160f, .95f);
                    Head(segs, c + Polar(160f, 9.5f), Polar(160f + 90f, 1f));
                    Arc(segs, c, 9.5f, 205f, 340f, .95f);
                    Head(segs, c + Polar(340f, 9.5f), Polar(340f + 90f, 1f));
                    Dot(segs, c, 1.4f);
                    break;
                case CursorShape.Place:
                    ink = AvTheme.Warning;
                    Arc(segs, c, 8.5f, 0f, 360f, .95f);
                    Line(segs, V(16, 2), V(16, 8.5f)); Line(segs, V(16, 23.5f), V(16, 30));
                    Line(segs, V(2, 16), V(8.5f, 16)); Line(segs, V(23.5f, 16), V(30, 16));
                    Dot(segs, c, 1.45f);
                    break;
                case CursorShape.Move:
                    ink = AvTheme.Friendly;
                    Line(segs, V(16, 5.5f), V(26.5f, 16)); Line(segs, V(26.5f, 16), V(16, 26.5f));
                    Line(segs, V(16, 26.5f), V(5.5f, 16)); Line(segs, V(5.5f, 16), V(16, 5.5f));
                    Line(segs, V(13, 17.5f), V(16, 13.5f), .85f); Line(segs, V(16, 13.5f), V(19, 17.5f), .85f);
                    break;
                case CursorShape.Stack:
                    Brackets(segs, c, 10.5f, 4.5f, .9f);
                    Line(segs, V(16, 10.5f), V(21.5f, 16), .85f); Line(segs, V(21.5f, 16), V(16, 21.5f), .85f);
                    Line(segs, V(16, 21.5f), V(10.5f, 16), .85f); Line(segs, V(10.5f, 16), V(16, 10.5f), .85f);
                    Dot(segs, c, 1.15f);
                    break;
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float k = size / Unit;
            Color edge = new Color(0f, 0f, 0f, .9f);
            ink.a = 1f;
            if (shape != CursorShape.Blank)
            {
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Distance(new Vector2((x + .5f) / k, (y + .5f) / k), segs, poly);
                    float inkA = Mathf.Clamp01(.5f - d * k);
                    float a = Mathf.Max(inkA, Mathf.Clamp01(.5f - (d - 1.35f) * k) * edge.a);
                    if (a <= 0f) continue;
                    Color col = Color.Lerp(edge, ink, inkA);
                    col.a = a;
                    pixels[(size - 1 - y) * size + x] = col; // texture rows run bottom-up
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>Signed distance to the ink: negative inside a stroke or the polygon.</summary>
        private static float Distance(Vector2 p, List<Seg> segs, Vector2[] poly)
        {
            float d = float.MaxValue;
            for (int i = 0; i < segs.Count; i++) d = Mathf.Min(d, SegDistance(p, segs[i].A, segs[i].B) - segs[i].W);
            if (poly == null) return d;
            float edge = float.MaxValue;
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                edge = Mathf.Min(edge, SegDistance(p, poly[j], poly[i]));
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
            }
            return Mathf.Min(d, inside ? -edge : edge);
        }

        private static float SegDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }

        private static Vector2 V(float x, float y) => new Vector2(x, y);
        private static Vector2 Polar(float degrees, float r) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad)) * r;
        private static void Line(List<Seg> s, Vector2 a, Vector2 b, float w = .9f) => s.Add(new Seg { A = a, B = b, W = w });
        private static void Dot(List<Seg> s, Vector2 c, float r = 1.3f) => Line(s, c, c, r);

        private static void Cross(List<Seg> s, Vector2 c, float gap, float reach, float w = .9f)
        {
            Line(s, c + V(0, -reach), c + V(0, -gap), w); Line(s, c + V(0, gap), c + V(0, reach), w);
            Line(s, c + V(-reach, 0), c + V(-gap, 0), w); Line(s, c + V(gap, 0), c + V(reach, 0), w);
        }

        private static void Brackets(List<Seg> s, Vector2 c, float r, float arm, float w = .9f)
        {
            for (int i = 0; i < 4; i++)
            {
                Vector2 dir = new Vector2(i % 2 == 0 ? -1 : 1, i < 2 ? -1 : 1);
                Vector2 corner = c + dir * r;
                Line(s, corner, corner - new Vector2(dir.x * arm, 0), w);
                Line(s, corner, corner - new Vector2(0, dir.y * arm), w);
            }
        }

        private static void Head(List<Seg> s, Vector2 tip, Vector2 dir)
        {
            dir.Normalize();
            Vector2 side = new Vector2(-dir.y, dir.x);
            Line(s, tip, tip - dir * 4f + side * 3.5f);
            Line(s, tip, tip - dir * 4f - side * 3.5f);
        }

        private static void Arc(List<Seg> s, Vector2 c, float r, float from, float to, float w = .9f)
        {
            const int Steps = 20;
            for (int i = 0; i < Steps; i++)
                Line(s, c + Polar(Mathf.Lerp(from, to, i / (float)Steps), r), c + Polar(Mathf.Lerp(from, to, (i + 1) / (float)Steps), r), w);
        }
    }
}
