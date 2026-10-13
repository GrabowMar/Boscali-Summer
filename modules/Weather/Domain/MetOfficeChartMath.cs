using System;

namespace BoscaliSummer.Modules.Weather.Domain
{

    /// <summary>The geometry of the synoptic chart, with no engine in it: world kilometres to chart pixels, the nearest point of a front, the wind barb and the order of the next-fronts card.</summary>
    internal static class MetChartMath
    {
        /// <summary>The chart shows ±150 km around the theater centre, north up.</summary>
        public const float RangeKm = 150f;

        /// <summary>NaN and infinity become 0 (the chart centre): a non-finite coordinate must never reach a vertex or a rect transform.</summary>
        public static float Fin(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;

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
    }
}
