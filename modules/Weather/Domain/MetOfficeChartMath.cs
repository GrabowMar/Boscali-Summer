using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>A rectangle in chart pixels (x right, y down), for the label placer.</summary>
    internal readonly struct MetBox
    {
        public readonly float X, Y, W, H;
        public MetBox(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
        public float Right => X + W;
        public float Bottom => Y + H;
    }

    /// <summary>The geometry of the synoptic chart, with no engine in it: world kilometres to chart pixels, the nearest point of a front, the wind barb and the order of the next-fronts card.</summary>
    internal static class MetChartMath
    {
        /// <summary>The chart shows ±150 km around the theater centre, north up.</summary>
        public const float RangeKm = 150f;

        /// <summary>NaN and infinity become 0 (the chart centre): a non-finite coordinate must never reach a vertex or a rect transform.</summary>
        public static float Fin(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;

        /// <summary>Chart x for a map x in km (east is right) on a square of <paramref name="size"/> pixels.</summary>
        public static float PxX(float xKm, float size) => (Fin(xKm) + RangeKm) / (2f * RangeKm) * size;

        /// <summary>Chart y (down) for a map z in km (north is up).</summary>
        public static float PxY(float zKm, float size) => (RangeKm - Fin(zKm)) / (2f * RangeKm) * size;

        public static bool Inside(float xKm, float zKm) => Math.Abs(xKm) <= RangeKm && Math.Abs(zKm) <= RangeKm;

        /// <summary>Clamps a point onto the chart edge so a label for an off-chart thing still has a place; a non-finite value sits at the centre.</summary>
        public static float Clamp(float km) { km = Fin(km); return km < -RangeKm ? -RangeKm : km > RangeKm ? RangeKm : km; }

        /// <summary>Wind barb of a wind in knots, rounded to the nearest 5 kt: 50 kt pennants, 10 kt full barbs, one 5 kt half barb.</summary>
        public static void Barb(float kts, out int pennants, out int full, out bool half)
        {
            if (!(kts > 0f) || float.IsInfinity(kts)) { pennants = full = 0; half = false; return; }   // calm, negative, NaN or infinite
            int five = (int)Math.Round(kts / 5f, MidpointRounding.AwayFromZero);
            pennants = five / 10;
            full = five % 10 / 2;
            half = five % 2 == 1;
        }

        /// <summary>
        /// Chart direction (x right, y down, unit length) of the wind barb's shaft from the station circle: toward the compass heading the wind blows FROM, as on a weather chart,
        /// so a wind from 270 points west and a wind from 090 points east. The feathers sit on the clockwise side of the shaft: (-dy, dx).
        /// </summary>
        public static void BarbAxis(int fromDeg, out float dx, out float dy)
        {
            double a = ((fromDeg % 360 + 360) % 360) * Math.PI / 180.0;
            dx = (float)Math.Sin(a); dy = -(float)Math.Cos(a);
        }

        /// <summary>Distance in km and compass bearing from the camera to the nearest point of a front's polyline. False for a line with fewer than two points.</summary>
        public static bool Nearest(float camX, float camZ, MetFront front, out float distKm, out float bearingDeg)
        {
            distKm = float.MaxValue; bearingDeg = 0f;
            if (front == null || front.Count < 2) return false;
            camX = Fin(camX); camZ = Fin(camZ);
            float bx = camX, bz = camZ;
            for (int i = 0; i + 1 < front.Count; i++)
            {
                float ax = front.X[i], az = front.Z[i], dx = front.X[i + 1] - ax, dz = front.Z[i + 1] - az;
                float len2 = dx * dx + dz * dz;
                float t = len2 < 1e-9f ? 0f : ((camX - ax) * dx + (camZ - az) * dz) / len2;
                t = t < 0f ? 0f : t > 1f ? 1f : t;
                float px = ax + dx * t, pz = az + dz * t;
                float d = (float)Math.Sqrt((px - camX) * (px - camX) + (pz - camZ) * (pz - camZ));
                if (d < distKm) { distKm = d; bx = px; bz = pz; }
            }
            bearingDeg = distKm < 1e-4f ? 0f : WeatherMath.VectorToHeading(bx - camX, bz - camZ);
            return true;
        }

        /// <summary>Fills <paramref name="order"/> with the indices of the fronts, nearest first (by distance from the camera); returns how many.</summary>
        public static int NextFronts(MetOfficeModel m, int[] order, float[] distances)
        {
            int n = 0;
            for (int i = 0; i < m.FrontCount && n < order.Length; i++)
            {
                if (!Nearest(m.CamX, m.CamZ, m.Fronts[i], out float d, out _)) continue;
                int at = n++;
                while (at > 0 && distances[at - 1] > d) { order[at] = order[at - 1]; distances[at] = distances[at - 1]; at--; }
                order[at] = i; distances[at] = d;
            }
            return n;
        }

        /// <summary>A label box in chart pixels (y down).</summary>
        public static MetBox Box(float x, float y, float w, float h) => new MetBox(x, y, w, h);

        /// <summary>Overlap area of two boxes in square pixels; boxes that only touch overlap by 0.</summary>
        public static float Overlap(MetBox a, MetBox b)
        {
            float w = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X), h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
            return w > 0f && h > 0f ? w * h : 0f;
        }

        private static readonly float[] RowsAbove = { -20f, 6f, -36f, 22f, -52f, 38f, 54f, -68f, 70f, -84f, 86f, -100f, 102f, -116f, 118f, 134f, 150f, 166f, 182f, 198f, 214f };
        private static readonly float[] RowsBelow = { 6f, -20f, 22f, -36f, 38f, -52f, 54f, -68f, 70f, -84f, 86f, -100f, 102f, -116f, 118f, 134f, 150f, 166f, 182f, 198f, 214f };
        private static readonly float[] Shifts = { 0f, 22f, 44f, 70f, 100f, 140f };
        private const int NearRows = 9;

        public static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static float Fit(float v, float lo, float hi) => hi < lo ? lo : v < lo ? lo : v > hi ? hi : v;

        /// <summary>One candidate: true when it touches nothing (and is then the answer), otherwise remembered when it is the least bad so far (the earlier candidate wins a tie).</summary>
        private static bool Consider(float px, float py, float w, float h, float shift, float dy, int side, MetBox bounds, MetBox[] taken, int takenCount, ref MetBox best, ref float bestCost)
        {
            float x = side == 0 ? px + 10f + shift : px - 10f - shift - w;
            var c = new MetBox(Fit(x, bounds.X, bounds.Right - w), Fit(py + dy, bounds.Y, bounds.Bottom - h), w, h);
            float cost = 0f;
            for (int i = 0; i < takenCount && cost < bestCost; i++) cost += Overlap(c, taken[i]);
            if (cost >= bestCost) return false;
            bestCost = cost; best = c;
            return cost <= 0f;
        }

        /// <summary>
        /// Places a label of <paramref name="w"/> x <paramref name="h"/> pixels next to the marker at (<paramref name="px"/>, <paramref name="py"/>), inside <paramref name="bounds"/> and clear of the
        /// first <paramref name="takenCount"/> boxes of <paramref name="taken"/> (the other labels and everything the chart draws as mesh). Candidates, nearest first: the rows above (or below, when
        /// <paramref name="below"/>) the marker, right side then left side, at growing horizontal gaps from the marker, then rows further up and down the plot. The first box that touches nothing wins;
        /// if none is free it returns the one with the least overlap. The caller appends the result to <paramref name="taken"/>, so the labels of one chart never share a pixel while there is room.
        /// </summary>
        public static MetBox Place(float px, float py, float w, float h, bool below, MetBox bounds, MetBox[] taken, int takenCount)
        {
            px = Fin(px); py = Fin(py);
            float[] rows = below ? RowsBelow : RowsAbove;
            MetBox best = default;
            float bestCost = float.MaxValue;
            for (int s = 0; s < Shifts.Length; s++)
                for (int r = 0; r < NearRows; r++)
                    for (int side = 0; side < 2; side++)
                        if (Consider(px, py, w, h, Shifts[s], rows[r], side, bounds, taken, takenCount, ref best, ref bestCost)) return best;
            // No coarse candidate is free (a corridor between two fronts is narrower than the gaps tried): on every row of the plot slide the label to sit flush against an obstacle's edge,
            // and take the free spot nearest to the marker (the shortest leader).
            MetBox pick = default;
            float pickDist = float.MaxValue;
            for (float dy = -140f; dy <= 220f; dy += 8f)
            {
                float y = Fit(py + dy, bounds.Y, bounds.Bottom - h);
                for (int k = 0; k < takenCount; k++)
                {
                    MetBox o = taken[k];
                    if (o.Bottom <= y || o.Y >= y + h) continue;
                    for (int side = 0; side < 2; side++)
                    {
                        float x = Fit(side == 0 ? o.Right + 0.5f : o.X - w - 0.5f, bounds.X, bounds.Right - w);
                        float gx = Math.Max(Math.Max(x - px, px - (x + w)), 0f), gy = Math.Max(Math.Max(y - py, py - (y + h)), 0f);
                        float d = gx * gx + gy * gy;
                        if (d >= pickDist) continue;
                        var c = new MetBox(x, y, w, h);
                        float cost = 0f;
                        for (int i = 0; i < takenCount && cost <= 0f; i++) cost += Overlap(c, taken[i]);
                        if (cost <= 0f) { pick = c; pickDist = d; }
                    }
                }
            }
            if (pickDist < float.MaxValue) return pick;
            for (int s = 0; s < 3; s++)
                for (int r = NearRows; r < rows.Length; r++)
                    for (int side = 0; side < 2; side++)
                        if (Consider(px, py, w, h, Shifts[s], rows[r], side, bounds, taken, takenCount, ref best, ref bestCost)) return best;
            return best;
        }

        /// <summary>A front's symbols along its polyline: the points (km) at every <paramref name="spacingKm"/> of arc length, with the unit tangent there. Returns the count written.</summary>
        public static int Symbols(MetFront f, float spacingKm, float[] x, float[] z, float[] tx, float[] tz)
        {
            int n = 0;
            if (f == null || f.Count < 2 || spacingKm <= 0f) return 0;
            float carry = spacingKm * 0.5f;
            for (int i = 0; i + 1 < f.Count && n < x.Length; i++)
            {
                float dx = f.X[i + 1] - f.X[i], dz = f.Z[i + 1] - f.Z[i];
                float len = (float)Math.Sqrt(dx * dx + dz * dz);
                if (len < 1e-6f) continue;
                float at = carry;
                while (at <= len && n < x.Length)
                {
                    x[n] = f.X[i] + dx * at / len; z[n] = f.Z[i] + dz * at / len; tx[n] = dx / len; tz[n] = dz / len; n++;
                    at += spacingKm;
                }
                carry = at - len;
            }
            return n;
        }
    }
}
