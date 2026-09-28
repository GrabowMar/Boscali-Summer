using System;
using BoscaliSummer.Features.Weather.Domain;

namespace BoscaliSummer.Features.Weather.Visuals
{
    /// <summary>
    /// CPU mirror of the volume shader's cloud density (FlightCloud.shader: Bodies, CoverMask,
    /// the low deck, fronts, towers and the middle layer; without detail erosion or the thin
    /// high layer). The shadow cookie and the in-cloud test use it, so each visible cloud
    /// casts its own shadow. Pure System.Math over the shared noise bytes, so it runs on a
    /// worker and in tests. Keep it in step with the shader.
    /// </summary>
    internal readonly struct CloudBodies
    {
        private readonly byte[] noise;
        private readonly int size;
        private readonly StateParams sky;
        private readonly float windX, windZ;
        private readonly SkySplit split;
        private readonly float fog;

        public CloudBodies(byte[] noise, int size, StateParams sky, float prevailingHeading = 0f, SkySplit split = default,
            float fog = 0f)
        {
            this.fog = fog;
            this.noise = noise;
            this.size = size;
            this.sky = sky;
            this.split = split;
            WeatherMath.HeadingToVector(prevailingHeading, out windX, out windZ);
        }

        /// <summary>Cloud density at a global position; <paramref name="shift"/> is the
        /// renderer's cloud-height shift, <paramref name="p"/> the weather at that column.</summary>
        public float Density(WeatherPoint p, float x, float y, float z, float shift)
        {
            float mid = Math.Max(MidLayer(x, y, z), FogBank(y));
            float layer = p.BackgroundCover, front = p.FrontCover, cell = p.CellShape;
            if (Math.Max(layer, Math.Max(front, cell)) < 0.025f) return mid;
            float baseY = p.CloudBase + shift;
            float topY = Math.Max(baseY + 1600f, p.CloudTop + shift);

            // The shader warps bodies by its broad noise so no grid shows at range.
            float broadR = Sample(x / 5800f, z / 5800f, y / 3900f, 0);
            float broadG = Sample(x / 5800f, z / 5800f, y / 3900f, 1);
            float wx = x + (broadR - 0.5f) * 2200f, wz = z + (broadG - 0.5f) * 2200f;

            // Low deck: separate domed bodies, or a smooth stratus/nimbostratus sheet.
            float body = Bodies(wx, y, wz, 4200f);
            float smooth = WeatherMath.Clamp01(sky.LayerSmooth);
            float deckBody = WeatherMath.Lerp(body, 0.72f + 0.28f * body, smooth);
            float mask = CoverMask(deckBody, WeatherMath.Clamp01(layer * 1.1f));
            float thick = Math.Max(300f, sky.LayerDepth) * (0.55f + 0.45f * WeatherMath.Clamp01(layer));
            float layerBase = baseY + (body - 0.5f) * WeatherMath.Lerp(160f, 60f, smooth);
            float layerTop = layerBase + thick *
                WeatherMath.Lerp(0.35f + 0.65f * mask, 0.88f + 0.12f * mask, smooth);
            float hl = WeatherMath.Clamp01((y - layerBase) / Math.Max(1f, layerTop - layerBase));
            float shape = WeatherMath.Clamp01(mask * 1.35f - hl * hl * 1.1f) *
                WeatherMath.Smoothstep(-60f, 120f, y - layerBase) * (y <= layerTop + 50f ? 1f : 0f);
            float layerDensity = WeatherMath.Clamp01((shape - 0.15f) / 0.70f) *
                WeatherMath.Smoothstep(0.02f, 0.10f, layer);

            // Fronts: denser bodies at a larger scale.
            float frontDensity = 0f;
            if (front > 0.02f)
            {
                float frontBase = p.FrontBase + shift;
                float frontCrown = Math.Max(frontBase + 600f, p.FrontTop + shift);
                float depth = frontCrown - frontBase;
                float fb = Bodies(wx + 5311f, y + 5311f, wz + 5311f, 5200f);
                float fm = CoverMask(fb, WeatherMath.Clamp01(front * 1.05f));
                float frontTop = frontCrown - (1f - fm) * Math.Min(1600f, depth * 0.35f);
                float profile = WeatherMath.Smoothstep(-120f, 220f, y - (frontBase + (fb - 0.5f) * 220f)) *
                    (1f - WeatherMath.Smoothstep(frontTop - Math.Min(600f, depth * 0.25f), frontTop + 250f, y));
                profile *= WeatherMath.Lerp(0.3f, 1f, WeatherMath.Smoothstep(900f, 2200f, depth));
                frontDensity = WeatherMath.Clamp01((fm * profile - 0.13f) / 0.74f) *
                    WeatherMath.Smoothstep(0.04f, 0.20f, front);
            }

            // Towers keep the weather field's shape.
            float h = WeatherMath.Clamp01((y - baseY) / Math.Max(1f, topY - baseY));
            float threshold = 0.25f + 0.43f * h * h;
            float tower = WeatherMath.Smoothstep(threshold - 0.16f, threshold + 0.16f, cell) *
                WeatherMath.Smoothstep(-100f, 170f, y - baseY) * (1f - WeatherMath.Smoothstep(0.84f, 1f, h)) *
                WeatherMath.Smoothstep(0.04f, 0.20f, cell);

            return Math.Max(mid, Math.Max(layerDensity * 0.52f, Math.Max(frontDensity * 0.58f, tower * 0.78f)));
        }

        /// <summary>The console fog bank (-30..330 m), near-continuous, for the in-cloud feel.</summary>
        public float FogBank(float y)
        {
            if (fog <= 0f || y <= -30f || y >= 330f) return 0f;
            float h = (y + 30f) / 360f;
            return fog * 0.55f * WeatherMath.Clamp01(4f * h * (1f - h) * 1.3f) * 0.85f;
        }

        /// <summary>The shader's middle layer (altocumulus .. altostratus), times its strength.</summary>
        public float MidLayer(float x, float y, float z)
        {
            float cover = sky.MidCover;
            if (cover <= 0.005f) return 0f;
            // Thins ahead of the frontal boundary, as the shader's middle layer does.
            if (split.Amount > 0f) cover *= WeatherMath.Lerp(1f - 0.75f * split.Amount, 1f, split.Share(x, z));
            float sheet = WeatherMath.Clamp01(sky.MidSheet);
            float y0 = WeatherMath.Lerp(4200f, 3300f, sheet);
            float y1 = y0 + WeatherMath.Lerp(400f, 1700f, sheet);
            return Slab(x, y, z, y0, y1, cover, WeatherMath.Smoothstep(0.4f, 1f, sheet), 2600f, 1.4f,
                0.7f * (1f - sheet)) * 0.42f;
        }

        private float Slab(float x, float y, float z, float y0, float y1, float cover, float sheet,
            float scale, float stretch, float ripple)
        {
            float h = (y - y0) / Math.Max(1f, y1 - y0);
            if (h <= 0f || h >= 1f) return 0f;
            float patch = Sample(x / 23000f + 0.61f, z / 23000f + 0.61f, 0.33f, 0);
            float px = x + (Sample(x / 31000f + 0.27f, z / 31000f + 0.27f, 0.71f, 0) - 0.5f) * scale * 1.6f;
            float pz = z + (Sample(x / 31000f + 0.27f, z / 31000f + 0.27f, 0.71f, 1) - 0.5f) * scale * 1.6f;
            float along = px * windX + pz * windZ, across = -px * windZ + pz * windX;
            float span = scale * stretch;
            float s1 = Sample(along / span, across / scale, y / (scale * 0.6f) + 0.17f, 0);
            float a2 = along * 0.982f - across * 0.191f, c2x = along * 0.191f + across * 0.982f;
            float s2 = Sample(a2 / (span * 1.73f) + 0.53f, c2x / (scale * 1.73f) + 0.29f, y / (scale * 1.1f) + 0.61f, 0);
            float streak = s1 * 0.6f + s2 * 0.4f;
            float cs = scale * 0.45f;
            float c1 = Sample(px / cs + 0.41f, pz / cs + 0.41f, y / (cs * 0.8f), 0);
            float c2 = Sample(pz / (cs * 2.37f) + 0.13f, -px / (cs * 2.37f) + 0.13f, y / (cs * 1.9f) + 0.77f, 0);
            float cells = c1 * 0.62f + c2 * 0.38f;
            float raw = WeatherMath.Lerp(streak, cells, ripple);
            float body = WeatherMath.Clamp01(0.5f + (raw - 0.52f) * 3.16f);
            body = WeatherMath.Lerp(body, 0.7f + 0.3f * body, sheet);
            float c = WeatherMath.Clamp01(cover * (0.55f + 0.9f * WeatherMath.Clamp01(0.5f + (patch - 0.52f) * 3.16f)));
            return CoverMask(body, c) * WeatherMath.Clamp01(4f * h * (1f - h) * 1.3f);
        }

        /// <summary>The shader's Bodies(): two non-integer scales, stretched to a near-uniform [0, 1].</summary>
        public float Bodies(float x, float y, float z, float scale)
        {
            float a = Sample(x / scale, z / scale, y / (scale * 0.9f), 0);
            float b = Sample(z / (scale * 2.73f) + 0.37f, -x / (scale * 2.73f) + 0.37f, y / (scale * 2.1f), 0);
            return WeatherMath.Clamp01(0.5f + (a * 0.62f + b * 0.38f - 0.52f) * 3.16f);
        }

        public static float CoverMask(float body, float cover)
        {
            float open = WeatherMath.Clamp01((body - (1f - cover)) / Math.Max(0.18f, cover));
            return WeatherMath.Lerp(open, Math.Max(open, 0.55f + body * 0.45f), WeatherMath.Smoothstep(0.82f, 1f, cover));
        }

        /// <summary>Trilinear, repeating read of one channel (0 = R, 1 = G), like the shader's tex3D.</summary>
        private float Sample(float u, float v, float w, int channel)
        {
            float fx = u * size - 0.5f, fy = v * size - 0.5f, fz = w * size - 0.5f;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy), z0 = (int)Math.Floor(fz);
            float tx = fx - x0, ty = fy - y0, tz = fz - z0;
            float c000 = C(x0, y0, z0, channel), c100 = C(x0 + 1, y0, z0, channel);
            float c010 = C(x0, y0 + 1, z0, channel), c110 = C(x0 + 1, y0 + 1, z0, channel);
            float c001 = C(x0, y0, z0 + 1, channel), c101 = C(x0 + 1, y0, z0 + 1, channel);
            float c011 = C(x0, y0 + 1, z0 + 1, channel), c111 = C(x0 + 1, y0 + 1, z0 + 1, channel);
            float x00 = c000 + (c100 - c000) * tx, x10 = c010 + (c110 - c010) * tx;
            float x01 = c001 + (c101 - c001) * tx, x11 = c011 + (c111 - c011) * tx;
            float y0v = x00 + (x10 - x00) * ty, y1v = x01 + (x11 - x01) * ty;
            return (y0v + (y1v - y0v) * tz) / 255f;
        }

        private float C(int x, int y, int z, int channel)
        {
            int m = size - 1; // size is a power of two
            return noise[(((z & m) * size + (y & m)) * size + (x & m)) * 4 + channel];
        }
    }
}
