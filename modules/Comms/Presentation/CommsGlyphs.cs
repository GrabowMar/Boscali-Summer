using System;
using System.Collections.Generic;

namespace BoscaliSummer.Features.Comms.Presentation
{
    /// <summary>
    /// Vector art for every ping, sticker and tool, as polylines in a unit box (−1…1, y up).
    /// No textures and no font glyphs: the game font has no emoji, and a mesh stays crisp at
    /// any map zoom. The map layer, the palette buttons and the cockpit markers all draw from
    /// this one table, so a sticker looks the same wherever it appears.
    ///
    /// <para>Pure data with no Unity types, so the geometry is checked by the test runner.</para>
    /// </summary>
    internal static class CommsGlyphs
    {
        private static readonly Dictionary<string, float[][]> cache = new Dictionary<string, float[][]>();

        /// <summary>The polylines for a glyph; an unknown key draws a plain ring rather than nothing.</summary>
        public static float[][] Get(string key)
        {
            if (string.IsNullOrEmpty(key)) key = "mark";
            if (cache.TryGetValue(key, out float[][] strokes)) return strokes;
            strokes = Build(key) ?? new[] { Circle(0f, 0f, 0.8f, 20) };
            cache[key] = strokes;
            return strokes;
        }

        /// <summary>Every key the table draws on purpose, for the geometry test.</summary>
        public static readonly string[] Keys =
        {
            "mark", "enemy", "sam", "attack", "defend", "rally", "help",
            "star", "heart", "smile", "skull", "flame", "crown", "bolt", "flag", "question", "exclaim", "eye", "mug",
            "pen", "line", "arrow", "circle", "box", "eraser", "measure", "text", "sticker", "ping", "off",
            "undo", "hunt", "guess", "dice", "rps", "poll", "comms", "trash",
        };

        private static float[][] Build(string key)
        {
            switch (key)
            {
                // ---- pings: NATO-flavoured, readable at 18 px
                case "mark":
                    return new[] { Poly(0f, 0.9f, 0.9f, 0f, 0f, -0.9f, -0.9f, 0f), Circle(0f, 0f, 0.18f, 10) };
                case "enemy":
                    return new[]
                    {
                        Poly(0f, 0.95f, 0.95f, 0f, 0f, -0.95f, -0.95f, 0f),
                        Line(-0.38f, 0.38f, 0.38f, -0.38f), Line(-0.38f, -0.38f, 0.38f, 0.38f),
                    };
                case "sam":
                    return new[]
                    {
                        Poly(0f, 0.95f, 0.22f, 0.45f, 0.22f, -0.45f, -0.22f, -0.45f, -0.22f, 0.45f),
                        Open(0.22f, -0.2f, 0.5f, -0.6f, 0.22f, -0.45f),
                        Open(-0.22f, -0.2f, -0.5f, -0.6f, -0.22f, -0.45f),
                        Line(-0.7f, -0.85f, 0.7f, -0.85f),
                    };
                case "attack":
                    return new[]
                    {
                        Circle(0f, 0f, 0.62f, 20),
                        Line(0f, 0.95f, 0f, 0.35f), Line(0f, -0.95f, 0f, -0.35f),
                        Line(0.95f, 0f, 0.35f, 0f), Line(-0.95f, 0f, -0.35f, 0f),
                    };
                case "defend":
                    return new[]
                    {
                        Poly(-0.75f, 0.8f, 0.75f, 0.8f, 0.75f, 0.1f, 0.4f, -0.5f, 0f, -0.9f, -0.4f, -0.5f, -0.75f, 0.1f),
                        Line(0f, 0.55f, 0f, -0.45f),
                    };
                case "rally":
                    return new[] { Pin(), Circle(0f, 0.35f, 0.2f, 10) };
                case "help":
                    return new[]
                    {
                        Circle(0f, 0f, 0.85f, 22),
                        Line(0f, 0.5f, 0f, -0.5f), Line(-0.5f, 0f, 0.5f, 0f),
                    };

                // ---- stickers: the fun ones
                case "star":
                    return new[] { Star(0f, 0f, 0.95f, 0.4f) };
                case "heart":
                    return new[] { Heart() };
                case "smile":
                    return new[]
                    {
                        Circle(0f, 0f, 0.9f, 24),
                        Circle(-0.32f, 0.28f, 0.1f, 8), Circle(0.32f, 0.28f, 0.1f, 8),
                        Arc(0f, 0.05f, 0.55f, 200f, 340f, 10),
                    };
                case "skull":
                    return new[]
                    {
                        Concat(Arc(0f, 0.15f, 0.8f, -20f, 200f, 18), Open(-0.75f, -0.13f, -0.45f, -0.35f, -0.45f, -0.8f,
                            0.45f, -0.8f, 0.45f, -0.35f, 0.75f, -0.13f)),
                        Circle(-0.32f, 0.12f, 0.2f, 10), Circle(0.32f, 0.12f, 0.2f, 10),
                        Poly(0f, -0.12f, 0.1f, -0.32f, -0.1f, -0.32f),
                        Line(-0.15f, -0.8f, -0.15f, -0.55f), Line(0.15f, -0.8f, 0.15f, -0.55f),
                    };
                case "flame":
                    return new[]
                    {
                        Poly(0f, 0.95f, 0.25f, 0.55f, 0.6f, 0.2f, 0.65f, -0.3f, 0.4f, -0.75f, 0f, -0.92f,
                            -0.4f, -0.75f, -0.65f, -0.3f, -0.55f, 0.1f, -0.3f, 0.35f, -0.15f, 0.1f),
                        Poly(0f, 0.2f, 0.25f, -0.25f, 0.2f, -0.6f, 0f, -0.72f, -0.2f, -0.6f, -0.25f, -0.3f),
                    };
                case "crown":
                    return new[]
                    {
                        Poly(-0.85f, -0.6f, 0.85f, -0.6f, 0.85f, 0.55f, 0.45f, 0.05f, 0f, 0.75f, -0.45f, 0.05f, -0.85f, 0.55f),
                        Line(-0.85f, -0.3f, 0.85f, -0.3f),
                        Circle(0f, 0.85f, 0.08f, 6), Circle(-0.85f, 0.65f, 0.08f, 6), Circle(0.85f, 0.65f, 0.08f, 6),
                    };
                case "bolt":
                    return new[] { Poly(0.25f, 0.95f, -0.5f, -0.05f, -0.05f, -0.05f, -0.25f, -0.95f, 0.5f, 0.1f, 0.05f, 0.1f) };
                case "flag":
                    return new[]
                    {
                        Line(-0.6f, -0.95f, -0.6f, 0.95f),
                        Open(-0.6f, 0.9f, -0.2f, 0.98f, 0.2f, 0.78f, 0.6f, 0.86f, 0.85f, 0.8f, 0.85f, 0.15f,
                            0.6f, 0.2f, 0.2f, 0.12f, -0.2f, 0.32f, -0.6f, 0.25f),
                    };
                case "question":
                    return new[]
                    {
                        Concat(Arc(0f, 0.45f, 0.45f, 160f, -60f, 12), Open(0.22f, 0.06f, 0f, -0.15f, 0f, -0.45f)),
                        Circle(0f, -0.8f, 0.1f, 8),
                    };
                case "exclaim":
                    return new[] { Poly(-0.12f, 0.95f, 0.12f, 0.95f, 0.06f, -0.45f, -0.06f, -0.45f), Circle(0f, -0.8f, 0.12f, 8) };
                case "eye":
                    return new[]
                    {
                        Concat(Arc(0f, -0.55f, 1.1f, 150f, 30f, 12), Arc(0f, 0.55f, 1.1f, -30f, -150f, 12)),
                        Circle(0f, 0f, 0.3f, 12), Circle(0f, 0f, 0.1f, 6),
                    };
                case "mug":
                    return new[]
                    {
                        Poly(-0.7f, 0.2f, 0.35f, 0.2f, 0.35f, -0.85f, -0.7f, -0.85f),
                        Arc(0.35f, -0.32f, 0.3f, 90f, -90f, 8),
                        Open(-0.45f, 0.35f, -0.35f, 0.55f, -0.45f, 0.75f, -0.35f, 0.95f),
                        Open(-0.1f, 0.35f, 0f, 0.55f, -0.1f, 0.75f, 0f, 0.95f),
                    };

                // ---- tools and panel icons
                case "pen":
                    return new[]
                    {
                        Poly(0.55f, 0.85f, 0.85f, 0.55f, -0.45f, -0.75f, -0.85f, -0.85f, -0.75f, -0.45f),
                        Line(0.35f, 0.65f, 0.65f, 0.35f),
                    };
                case "line":
                    return new[] { Line(-0.85f, -0.85f, 0.85f, 0.85f), Circle(-0.85f, -0.85f, 0.1f, 6), Circle(0.85f, 0.85f, 0.1f, 6) };
                case "arrow":
                    return new[] { Line(-0.85f, -0.85f, 0.8f, 0.8f), Open(0.15f, 0.85f, 0.85f, 0.85f, 0.85f, 0.15f) };
                case "circle":
                    return new[] { Circle(0f, 0f, 0.85f, 24), Line(0f, 0f, 0.6f, 0.6f) };
                case "box":
                    return new[] { Poly(-0.8f, -0.65f, 0.8f, -0.65f, 0.8f, 0.65f, -0.8f, 0.65f) };
                case "eraser":
                    return new[]
                    {
                        Poly(-0.9f, -0.2f, -0.2f, 0.5f, 0.55f, 0.1f, 0.9f, 0.45f, 0.2f, -0.85f, -0.4f, -0.85f),
                        Line(-0.45f, 0.05f, 0.05f, -0.45f), Line(-0.9f, -0.95f, 0.9f, -0.95f),
                    };
                case "measure":
                    return new[]
                    {
                        Poly(-0.95f, -0.3f, 0.95f, -0.3f, 0.95f, 0.3f, -0.95f, 0.3f),
                        Line(-0.6f, 0.3f, -0.6f, 0f), Line(-0.25f, 0.3f, -0.25f, 0.08f),
                        Line(0.1f, 0.3f, 0.1f, 0f), Line(0.45f, 0.3f, 0.45f, 0.08f),
                    };
                case "text":
                    return new[] { Line(-0.75f, 0.8f, 0.75f, 0.8f), Line(0f, 0.8f, 0f, -0.85f), Line(-0.3f, -0.85f, 0.3f, -0.85f) };
                case "sticker":
                    return new[] { Star(0f, 0f, 0.9f, 0.38f) };
                case "ping":
                    return new[] { Circle(0f, 0f, 0.25f, 10), Circle(0f, 0f, 0.6f, 16), Arc(0f, 0f, 0.95f, 30f, 150f, 8) };
                case "off":
                case "trash":
                    return key == "off"
                        ? new[] { Line(-0.7f, -0.7f, 0.7f, 0.7f), Line(-0.7f, 0.7f, 0.7f, -0.7f) }
                        : new[]
                        {
                            Poly(-0.6f, 0.55f, 0.6f, 0.55f, 0.45f, -0.9f, -0.45f, -0.9f),
                            Line(-0.85f, 0.7f, 0.85f, 0.7f), Open(-0.2f, 0.7f, -0.2f, 0.9f, 0.2f, 0.9f, 0.2f, 0.7f),
                            Line(0f, 0.3f, 0f, -0.65f),
                        };
                case "undo":
                    return new[] { Arc(0.1f, -0.1f, 0.7f, 180f, -90f, 14), Open(-0.9f, 0.2f, -0.6f, -0.2f, -0.25f, 0.15f) };
                case "hunt":
                    return new[]
                    {
                        Circle(0f, 0f, 0.8f, 22), Circle(0f, 0f, 0.45f, 16), Circle(0f, 0f, 0.1f, 8),
                        Line(0f, 0.98f, 0f, 0.62f), Line(0f, -0.98f, 0f, -0.62f),
                    };
                case "guess":
                    return new[] { Line(-0.6f, -0.6f, 0.6f, 0.6f), Line(-0.6f, 0.6f, 0.6f, -0.6f), Circle(0f, 0f, 0.9f, 16) };
                case "dice":
                    return new[]
                    {
                        Poly(-0.85f, -0.85f, 0.85f, -0.85f, 0.85f, 0.85f, -0.85f, 0.85f),
                        Circle(-0.45f, 0.45f, 0.12f, 6), Circle(0f, 0f, 0.12f, 6), Circle(0.45f, -0.45f, 0.12f, 6),
                    };
                case "rps":
                    return new[]
                    {
                        Circle(-0.55f, 0.35f, 0.35f, 12),
                        Poly(0.2f, 0.7f, 0.9f, 0.7f, 0.9f, 0f, 0.2f, 0f),
                        Line(-0.3f, -0.95f, 0.3f, -0.35f), Line(-0.3f, -0.35f, 0.3f, -0.95f),
                    };
                case "poll":
                    return new[]
                    {
                        Line(-0.9f, -0.85f, 0.9f, -0.85f),
                        Poly(-0.7f, -0.85f, -0.3f, -0.85f, -0.3f, 0.2f, -0.7f, 0.2f),
                        Poly(-0.2f, -0.85f, 0.2f, -0.85f, 0.2f, 0.85f, -0.2f, 0.85f),
                        Poly(0.3f, -0.85f, 0.7f, -0.85f, 0.7f, -0.2f, 0.3f, -0.2f),
                    };
                case "comms":
                    return new[]
                    {
                        Poly(-0.9f, 0.85f, 0.9f, 0.85f, 0.9f, -0.35f, -0.2f, -0.35f, -0.6f, -0.85f, -0.55f, -0.35f, -0.9f, -0.35f),
                        Circle(-0.4f, 0.25f, 0.08f, 6), Circle(0f, 0.25f, 0.08f, 6), Circle(0.4f, 0.25f, 0.08f, 6),
                    };
                default:
                    return null;
            }
        }

        // ---- builders ---------------------------------------------------------------------

        /// <summary>An open polyline through the given x, y pairs.</summary>
        private static float[] Open(params float[] points) => points;

        /// <summary>A closed polygon: the first point is repeated at the end.</summary>
        private static float[] Poly(params float[] points)
        {
            var closed = new float[points.Length + 2];
            Array.Copy(points, closed, points.Length);
            closed[points.Length] = points[0];
            closed[points.Length + 1] = points[1];
            return closed;
        }

        private static float[] Line(float x1, float y1, float x2, float y2) => new[] { x1, y1, x2, y2 };

        private static float[] Circle(float cx, float cy, float r, int segments) => Arc(cx, cy, r, 0f, 360f, segments);

        /// <summary>An arc from one angle to another, in degrees, counter-clockwise when to &gt; from.</summary>
        private static float[] Arc(float cx, float cy, float r, float fromDeg, float toDeg, int segments)
        {
            var points = new float[(segments + 1) * 2];
            for (int i = 0; i <= segments; i++)
            {
                double a = (fromDeg + (toDeg - fromDeg) * i / segments) * Math.PI / 180.0;
                points[i * 2] = cx + (float)Math.Cos(a) * r;
                points[i * 2 + 1] = cy + (float)Math.Sin(a) * r;
            }
            return points;
        }

        private static float[] Star(float cx, float cy, float outer, float inner)
        {
            var points = new float[22];
            for (int i = 0; i <= 10; i++)
            {
                double a = (90.0 + i * 36.0) * Math.PI / 180.0;
                float r = i % 2 == 0 ? outer : inner;
                points[i * 2] = cx + (float)Math.Cos(a) * r;
                points[i * 2 + 1] = cy + (float)Math.Sin(a) * r;
            }
            return points;
        }

        /// <summary>The classic parametric heart, fitted to the unit box.</summary>
        private static float[] Heart()
        {
            const int segments = 32;
            var points = new float[(segments + 1) * 2];
            for (int i = 0; i <= segments; i++)
            {
                double t = i * Math.PI * 2.0 / segments;
                double x = 16.0 * Math.Pow(Math.Sin(t), 3);
                double y = 13.0 * Math.Cos(t) - 5.0 * Math.Cos(2 * t) - 2.0 * Math.Cos(3 * t) - Math.Cos(4 * t);
                points[i * 2] = (float)(x / 17.0);
                points[i * 2 + 1] = (float)((y + 2.5) / 15.5);
            }
            return points;
        }

        /// <summary>A map pin: round head, pointed foot.</summary>
        private static float[] Pin()
        {
            float[] head = Arc(0f, 0.35f, 0.55f, -35f, 215f, 16);
            var points = new float[head.Length + 4];
            Array.Copy(head, points, head.Length);
            points[head.Length] = 0f;
            points[head.Length + 1] = -0.95f;
            points[head.Length + 2] = head[0];
            points[head.Length + 3] = head[1];
            return points;
        }

        private static float[] Concat(float[] a, float[] b)
        {
            var points = new float[a.Length + b.Length];
            Array.Copy(a, points, a.Length);
            Array.Copy(b, 0, points, a.Length, b.Length);
            return points;
        }
    }
}
