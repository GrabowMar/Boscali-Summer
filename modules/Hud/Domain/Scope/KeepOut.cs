using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>Where a label may sit around its marker: the THREAT page's six spots or the SHOT page's eight (the prototype's <c>placeTag</c> / <c>place</c>).</summary>
    internal enum LabelSpots : byte { Threat, Shot }

    /// <summary>
    /// Keep-out placement for the scope's labels (HUD v8): every label takes the first of a few spots around its marker that does not sit on another
    /// label, a marker or a track line. Callers add what is already drawn with <see cref="Add"/>, then ask <see cref="Place"/> for each label (which
    /// registers it too). THREAT has one more rule (LAYOUT.md 12.2): the notch wedge's box (<see cref="AddWedge"/>) is dear to sit on and offers spots above and below it.
    /// Pure, allocation-free after construction.
    /// </summary>
    internal sealed class KeepOut
    {
        public const int Capacity = 48;

        private readonly float[] box = new float[Capacity * 4];
        private int count, wedge = -1;

        public int Count => count;

        public void Clear() { count = 0; wedge = -1; }

        /// <summary>The notch wedge's keep-out (its bounding box grown by 8): a THREAT label that overlaps it pays 4 per unit instead of 0.3 and may take a spot above or below it. At most one.</summary>
        public void AddWedge(float x, float y, float w, float h)
        {
            if (count >= Capacity) return;
            wedge = count;
            Add(x, y, w, h);
        }

        public void Add(float x, float y, float w, float h)
        {
            if (count >= Capacity) return;
            int o = count++ * 4;
            box[o] = x; box[o + 1] = y; box[o + 2] = w; box[o + 3] = h;
        }

        /// <summary>
        /// The best spot for a <paramref name="w"/> x <paramref name="h"/> label beside the marker at (<paramref name="px"/>, <paramref name="py"/>): the cheapest of the
        /// spots by overlap with what is already placed (30 % of the overlapping area, 35 % for SHOT; THREAT: 4 per unit for the wedge, and 0.3 per unit of
        /// the distance from the marker) and by sample points of <paramref name="segs"/> (x1, y1, x2, y2 lines) that fall inside it, clamped into the given limits.
        /// THREAT adds five spots above and five below the wedge's box when there is one. <paramref name="preferSide"/> (SHOT only): -1 left, +1 right, 0 either.
        /// The label is registered as placed.
        /// </summary>
        public void Place(LabelSpots spots, float px, float py, float w, float h, ReadOnlySpan<float> segs, float xMin, float xMax, float yMin, float yMax,
            out float x, out float y, int preferSide = 0)
        {
            bool shot = spots == LabelSpots.Shot;
            int n = shot ? 8 : wedge < 0 ? 6 : 16;
            float bestCost = float.MaxValue;
            x = y = 0f;
            for (int i = 0; i < n; i++)
            {
                float dx, dy;
                if (i < (shot ? 8 : 6)) Spot(shot, i, w, h, out dx, out dy);
                else
                {
                    // above the wedge's box, or well below it, centred on it and then 48 and 90 either side (LAYOUT.md 12.2)
                    int j = i - 6, side = (j >> 1) == 0 ? 0 : (j >> 1) == 1 ? -48 : (j >> 1) == 2 ? 48 : (j >> 1) == 3 ? -90 : 90;
                    float cx = box[wedge * 4] + box[wedge * 4 + 2] / 2f - w / 2f + side;
                    dx = cx - px;
                    dy = (j & 1) == 0 ? box[wedge * 4 + 1] - h - py : box[wedge * 4 + 1] + box[wedge * 4 + 3] + 22f - py;
                }
                float rx = (float)Math.Round(Clamp(px + dx, xMin, xMax - w)), ry = (float)Math.Round(Clamp(py + dy, yMin, yMax - h));
                float cost = i * (shot ? 0.4f : 0.5f) + (preferSide < 0 && dx > 0f ? 3f : preferSide > 0 && dx < 0f ? 3f : 0f);
                if (!shot) cost += 0.3f * (float)Math.Sqrt((rx + w / 2f - px) * (rx + w / 2f - px) + (ry + h / 2f - py) * (ry + h / 2f - py));
                for (int k = 0; k < count; k++) cost += Overlap(rx, ry, w, h, box[k * 4], box[k * 4 + 1], box[k * 4 + 2], box[k * 4 + 3]) * (shot ? 0.35f : k == wedge ? 4f : 0.3f);
                for (int s = 0; s + 3 < segs.Length; s += 4) cost += SegHits(rx, ry, w, h, segs[s], segs[s + 1], segs[s + 2], segs[s + 3], shot ? 14 : 12) * (shot ? 10f : 12f);
                if (cost < bestCost) { bestCost = cost; x = rx; y = ry; }
            }
            Add(x, y, w, h);
        }

        /// <summary>
        /// THREAT's leader test: whether the line from (<paramref name="x1"/>, <paramref name="y1"/>) to (<paramref name="x2"/>, <paramref name="y2"/>) runs through a label or box other than the wedge, the label just placed
        /// (the last one) and the marker's own box (centred within 14 of <paramref name="px"/>, <paramref name="py"/>): a leader never crosses another label.
        /// </summary>
        public bool LeaderCrosses(float x1, float y1, float x2, float y2, float px, float py) => LeaderCrosses(x1, y1, x2, y2, px, py, count - 1);

        /// <summary>
        /// <see cref="LeaderCrosses(float,float,float,float,float,float)"/> for a leader asked about after every label is placed: <paramref name="own"/> is the index (<see cref="Count"/> before the label was added) of the
        /// label the leader belongs to, which is not the last one any more. THREAT draws its radars' leaders after all the tags are down, so a tag placed later cannot end up under a leader drawn earlier.
        /// </summary>
        public bool LeaderCrosses(float x1, float y1, float x2, float y2, float px, float py, int own)
        {
            for (int k = 0; k < count; k++)
            {
                if (k == wedge || k == own) continue;
                float bx = box[k * 4], by = box[k * 4 + 1], bw = box[k * 4 + 2], bh = box[k * 4 + 3];
                if (Math.Abs(bx + bw / 2f - px) < 14f && Math.Abs(by + bh / 2f - py) < 14f) continue;
                if (SegHits(bx, by, bw, bh, x1, y1, x2, y2, 12) > 0) return true;
            }
            return false;
        }

        /// <summary>The air a THREAT radar tag keeps round itself (<see cref="OverlapCost"/>): two tags 4 apart read as two, two that touch read as one box (the corner brackets of MSV RADAR and T9K41 met in game).</summary>
        public const float Gap = 4f;

        /// <summary>The total area the rectangle, grown by <see cref="Gap"/> on every side, overlaps of everything placed so far (a label's cost where it sits).</summary>
        public float OverlapCost(float x, float y, float w, float h)
        {
            float area = 0f;
            for (int k = 0; k < count; k++) area += Overlap(x - Gap, y - Gap, w + 2f * Gap, h + 2f * Gap, box[k * 4], box[k * 4 + 1], box[k * 4 + 2], box[k * 4 + 3]);
            return area;
        }

        private const float DegToRad = (float)(Math.PI / 180.0);

        /// <summary>
        /// A radar's tag on THREAT's polar scope (centre <paramref name="cx"/>, <paramref name="cy"/>, rim <paramref name="r"/>): the cheapest of six spots (outside the rim at the spoke's bearing, nudged 14 and 28 degrees along the rim
        /// either way, then inside the rim; each costs for the distance the scope's limits moved it and for what it overlaps), then <see cref="Free"/> when that spot sits on another label or on a line of <paramref name="spokes"/>.
        /// Registers the tag; true when <see cref="Free"/> moved it off its own spot (the caller ties it to its spoke with a leader that crosses no label).
        /// </summary>
        public bool PlaceRadar(float cx, float cy, float r, float bearing, float w, float h, ReadOnlySpan<float> spokes, float xMin, float xMax, float yMin, float yMax, out float x, out float y)
        {
            float best = float.MaxValue;
            x = y = 0f;
            for (int i = 0; i < 6; i++)
            {
                float sx, sy, a = (i == 5 ? bearing : bearing + (i == 0 ? 0f : i == 1 ? 14f : i == 2 ? -14f : i == 3 ? 28f : -28f)) * DegToRad, sin = (float)Math.Sin(a), cos = (float)Math.Cos(a);
                if (i < 5) { sx = cx + sin * (r + 4f + w / 2f) - w / 2f; sy = cy - cos * (r + 4f + h / 2f) - h / 2f; }
                else { sx = cx + sin * (r - 22f) - w / 2f; sy = cy - cos * (r - 22f) - h / 2f; }
                float rx = (float)Math.Round(Clamp(sx, xMin, xMax - w)), ry = (float)Math.Round(Clamp(sy, yMin, yMax - h));
                float cost = i * 2f + (Math.Abs(rx - sx) + Math.Abs(ry - sy)) * 2f + OverlapCost(rx, ry, w, h) * 0.5f;
                if (cost < best) { best = cost; x = rx; y = ry; }
            }
            float fx = x, fy = y;
            Free(w, h, spokes, xMin, xMax, yMin, yMax, ref x, ref y);
            Add(x, y, w, h);
            return x != fx || y != fy;
        }

        /// <summary>
        /// For a label whose own few spots all sit on others (three long names at one bearing, in game) or on a line of <paramref name="segs"/> (x1, y1, x2, y2 each): leaves (<paramref name="x"/>, <paramref name="y"/>) alone when the
        /// <paramref name="w"/> x <paramref name="h"/> box there sits on nothing; else moves it to the spot of a grid over the area (half a label across and down) that sits on the least: first by overlapped area with what is placed, then
        /// by sample points on the lines, then the nearest to where it was. Registers nothing: the caller adds the label.
        /// </summary>
        public void Free(float w, float h, ReadOnlySpan<float> segs, float xMin, float xMax, float yMin, float yMax, ref float x, ref float y)
        {
            float own = OverlapCost(x, y, w, h) * Dear + Hits(x, y, w, h, segs) * OnLine;
            if (own <= 0f) return;
            float stepX = Math.Max(4f, w / 2f), stepY = Math.Max(4f, h / 2f), best = own, bx = x, by = y;
            int cols = (int)Math.Ceiling(Math.Max(0f, xMax - w - xMin) / stepX) + 1, rows = (int)Math.Ceiling(Math.Max(0f, yMax - h - yMin) / stepY) + 1;
            for (int j = 0; j < rows; j++)
            {
                float gy = (float)Math.Round(Clamp(yMin + j * stepY, yMin, Math.Max(yMin, yMax - h)));
                for (int i = 0; i < cols; i++)
                {
                    float gx = (float)Math.Round(Clamp(xMin + i * stepX, xMin, Math.Max(xMin, xMax - w)));
                    float cost = OverlapCost(gx, gy, w, h) * Dear + Hits(gx, gy, w, h, segs) * OnLine + Math.Abs(gx - x) + Math.Abs(gy - y);
                    if (cost < best) { best = cost; bx = gx; by = gy; }
                }
            }
            x = bx;
            y = by;
        }

        /// <summary>
        /// One unit of overlapped area costs ten sample points on a line, and a sample point costs more than any distance on the scope (<see cref="Free"/>, which is what its summary says: overlap first, then lines, then the
        /// nearest): a spoke through a tag's words reads as a letter struck out, while a tag that has moved only needs a leader. At 12 a point cost less than the move off a spoke, so a tag standing on its own spoke stayed
        /// and the spoke ran through the first letter of HYPERION in game.
        /// </summary>
        private const float Dear = 10000f, OnLine = 1000f;

        /// <summary>Sample points of the lines inside the box: 25 a line, the lines whose own box misses it left out first (seven samples let a spoke that clipped a tag's corner by 8 units through, and the grid asks it of every cell).</summary>
        private static int Hits(float x, float y, float w, float h, ReadOnlySpan<float> segs)
        {
            int hits = 0;
            for (int s = 0; s + 3 < segs.Length; s += 4)
            {
                if (Math.Max(segs[s], segs[s + 2]) < x - 1f || Math.Min(segs[s], segs[s + 2]) > x + w + 1f || Math.Max(segs[s + 1], segs[s + 3]) < y - 1f || Math.Min(segs[s + 1], segs[s + 3]) > y + h + 1f) continue;
                hits += SegHits(x, y, w, h, segs[s], segs[s + 1], segs[s + 2], segs[s + 3], 24);
            }
            return hits;
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
