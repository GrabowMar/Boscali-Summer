using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>Where a label may sit around its marker: the THREAT page's six spots or the SHOT page's eight (the prototype's <c>placeTag</c> / <c>place</c>).</summary>
    internal enum LabelSpots : byte { Threat, Shot }

    /// <summary>
    /// Keep-out placement for the scope's labels (HUD v8): every label takes the first of a few spots around its marker that does not sit on another
    /// label, a marker or a track line. Callers add what is already drawn with <see cref="Add"/>, then ask <see cref="Place"/> for each label (which
    /// registers it too). Pure, allocation-free after construction.
    /// </summary>
    internal sealed class KeepOut
    {
        public const int Capacity = 48;

        private readonly float[] box = new float[Capacity * 4];
        private int count;

        public int Count => count;

        public void Clear() => count = 0;

        public void Add(float x, float y, float w, float h)
        {
            if (count >= Capacity) return;
            int o = count++ * 4;
            box[o] = x; box[o + 1] = y; box[o + 2] = w; box[o + 3] = h;
        }

        /// <summary>
        /// The best spot for a <paramref name="w"/> x <paramref name="h"/> label beside the marker at (<paramref name="px"/>, <paramref name="py"/>): the cheapest of the
        /// spots by overlap with what is already placed (30 % of the overlapping area, 35 % for SHOT) and by sample points of <paramref name="segs"/>
        /// (x1, y1, x2, y2 lines) that fall inside it, clamped into the given limits. <paramref name="preferSide"/> (SHOT only): -1 left, +1 right, 0 either.
        /// The label is registered as placed.
        /// </summary>
        public void Place(LabelSpots spots, float px, float py, float w, float h, ReadOnlySpan<float> segs, float xMin, float xMax, float yMin, float yMax,
            out float x, out float y, int preferSide = 0)
        {
            bool shot = spots == LabelSpots.Shot;
            int n = shot ? 8 : 6;
            float bestCost = float.MaxValue;
            x = y = 0f;
            for (int i = 0; i < n; i++)
            {
                Spot(shot, i, w, h, out float dx, out float dy);
                float rx = (float)Math.Round(Clamp(px + dx, xMin, xMax - w)), ry = (float)Math.Round(Clamp(py + dy, yMin, yMax - h));
                float cost = i * (shot ? 0.4f : 0.5f) + (preferSide < 0 && dx > 0f ? 3f : preferSide > 0 && dx < 0f ? 3f : 0f);
                for (int k = 0; k < count; k++) cost += Overlap(rx, ry, w, h, box[k * 4], box[k * 4 + 1], box[k * 4 + 2], box[k * 4 + 3]) * (shot ? 0.35f : 0.3f);
                for (int s = 0; s + 3 < segs.Length; s += 4) cost += SegHits(rx, ry, w, h, segs[s], segs[s + 1], segs[s + 2], segs[s + 3], shot ? 14 : 12) * (shot ? 10f : 12f);
                if (cost < bestCost) { bestCost = cost; x = rx; y = ry; }
            }
            Add(x, y, w, h);
        }

        private static void Spot(bool shot, int i, float w, float h, out float dx, out float dy)
        {
            if (shot)
                switch (i)
                {
                    case 0: dx = 11f; dy = -h / 2f; return;
                    case 1: dx = 11f; dy = -h - 6f; return;
                    case 2: dx = 11f; dy = 6f; return;
                    case 3: dx = -11f - w; dy = -h / 2f; return;
                    case 4: dx = -11f - w; dy = -h - 6f; return;
                    case 5: dx = -11f - w; dy = 6f; return;
                    case 6: dx = -w / 2f; dy = -h - 12f; return;
                    default: dx = -w / 2f; dy = 12f; return;
                }
            switch (i)
            {
                case 0: dx = 12f; dy = -h - 4f; return;
                case 1: dx = 12f; dy = 4f; return;
                case 2: dx = -12f - w; dy = -h - 4f; return;
                case 3: dx = -12f - w; dy = 4f; return;
                case 4: dx = -w / 2f; dy = -h - 13f; return;
                default: dx = -w / 2f; dy = 13f; return;
            }
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        public static float Overlap(float ax, float ay, float aw, float ah, float bx, float by, float bw, float bh)
        {
            float w = Math.Min(ax + aw, bx + bw) - Math.Max(ax, bx), h = Math.Min(ay + ah, by + bh) - Math.Max(ay, by);
            return w > 0f && h > 0f ? w * h : 0f;
        }

        /// <summary>How many of <paramref name="samples"/> + 1 evenly spaced points of the line fall inside the rectangle (grown by a pixel).</summary>
        public static int SegHits(float rx, float ry, float rw, float rh, float x1, float y1, float x2, float y2, int samples)
        {
            int hits = 0;
            for (int i = 0; i <= samples; i++)
            {
                float u = (float)i / samples, x = x1 + (x2 - x1) * u, y = y1 + (y2 - y1) * u;
                if (x >= rx - 1f && x <= rx + rw + 1f && y >= ry - 1f && y <= ry + rh + 1f) hits++;
            }
            return hits;
        }
    }
}
