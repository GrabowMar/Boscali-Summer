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

        /// <summary>Overlap area of two boxes in square pixels; boxes that only touch overlap by 0.</summary>
        public static float Overlap(MetBox a, MetBox b)
        {
            float w = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X), h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
            return w > 0f && h > 0f ? w * h : 0f;
        }

        public static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

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
