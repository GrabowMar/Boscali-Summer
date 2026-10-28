using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Comms.Presentation
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
                case "spike":
                    // A radar beam fanning onto a small aircraft chevron: "they have me".
                    return new[]
                    {
                        Poly(0f, 0.95f, 0.45f, 0.35f, 0f, 0.5f, -0.45f, 0.35f),
                        Arc(0f, -0.9f, 0.55f, 40f, 140f, 8), Arc(0f, -0.9f, 0.95f, 50f, 130f, 10),
                        Circle(0f, -0.9f, 0.08f, 6),
                    };
                case "arrow":
                    return new[] { Line(-0.85f, -0.85f, 0.8f, 0.8f), Open(0.15f, 0.85f, 0.85f, 0.85f, 0.85f, 0.15f) };
                case "lz":
                    // The pilot's landing zone: a canopy on its rigging over the ground bar.
                    return new[]
                    {
                        Arc(0f, 0.05f, 0.8f, 0f, 180f, 16), Line(-0.8f, 0.05f, 0.8f, 0.05f),
                        Line(-0.8f, 0.05f, 0f, -0.6f), Line(0.8f, 0.05f, 0f, -0.6f),
                        Line(-0.6f, -0.9f, 0.6f, -0.9f),
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
    }
}
