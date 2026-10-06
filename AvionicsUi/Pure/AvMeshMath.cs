using System;

namespace NOAvionics
{
    public struct AvV2
    {
        public float X, Y;
        public AvV2(float x, float y) { X = x; Y = y; }
    }

    /// <summary>Per-corner cut sizes of an FUI frame. Diagonal (top-right + bottom-left) is the console silhouette.</summary>
    public struct AvChamfer
    {
        public float TL, TR, BR, BL;
        public static AvChamfer All(float c) => new AvChamfer { TL = c, TR = c, BR = c, BL = c };
        public static AvChamfer Diagonal(float c) => new AvChamfer { TR = c, BL = c };
    }

    /// <summary>Engine-free geometry for the kit's mesh Graphics (Unity-UI-Extensions class, own code).</summary>
    public static class AvMeshMath
    {
        private const float Tan225 = 0.41421356f;

        public static AvV2[] ChamferPolygon(float x0, float y0, float x1, float y1, AvChamfer c)
        {
            float w = x1 - x0, h = y1 - y0, lim = Math.Max(0f, Math.Min(w, h) * 0.5f);
            float tl = Math.Clamp(c.TL, 0f, lim), tr = Math.Clamp(c.TR, 0f, lim), br = Math.Clamp(c.BR, 0f, lim), bl = Math.Clamp(c.BL, 0f, lim);
            return new[]
            {
                new AvV2(x0, y1 - tl), new AvV2(x0 + tl, y1),   // top-left
                new AvV2(x1 - tr, y1), new AvV2(x1, y1 - tr),   // top-right
                new AvV2(x1, y0 + br), new AvV2(x1 - br, y0),   // bottom-right
                new AvV2(x0 + bl, y0), new AvV2(x0, y0 + bl),   // bottom-left
            };
        }

        public static AvChamfer Inset(AvChamfer c, float stroke)
        {
            float d = stroke * Tan225;
            return new AvChamfer
            {
                TL = c.TL > 0 ? Math.Max(0f, c.TL - d) : 0f, TR = c.TR > 0 ? Math.Max(0f, c.TR - d) : 0f,
                BR = c.BR > 0 ? Math.Max(0f, c.BR - d) : 0f, BL = c.BL > 0 ? Math.Max(0f, c.BL - d) : 0f,
            };
        }

        public static int SegmentsLit(int segments, float value01)
        {
            if (segments <= 0 || float.IsNaN(value01)) return 0;
            float v = value01 < 0 ? 0 : value01 > 1 ? 1 : value01;
            return Math.Min(segments, (int)Math.Floor(v * segments + 1e-4f));
        }

        public static int ArcSteps(float sweepDegrees, float maxStepDegrees = 6f) =>
            Math.Max(1, (int)Math.Ceiling(Math.Abs(sweepDegrees) / Math.Max(0.5f, maxStepDegrees)));

        public static AvV2 ArcPoint(float cx, float cy, float r, float degrees)
        {
            double a = degrees * Math.PI / 180.0;
            return new AvV2(cx + (float)Math.Cos(a) * r, cy + (float)Math.Sin(a) * r);
        }
    }
}
