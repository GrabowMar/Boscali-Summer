using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>The cloud renderer's weather maps for one tape and frame: a near map over the
    /// flight domain and a coarse far map (the level of detail out to the horizon), RGBA32
    /// bytes ready to upload. Pure: built on a worker, or once on the main thread to preload.
    /// Structure: sheet cover, (unused), cell shape, low/tower top. Profiles: (unused), (unused),
    /// cloud base, rain.</summary>
    internal sealed class CloudMaps
    {
        internal const int NearSize = 256;
        internal const int FarSize = 128;
        /// <summary>The empty-space map: half the near map's resolution over the same span.</summary>
        internal const int EnvelopeSize = 128;
        internal const float VolumeTop = 16000f;
        /// <summary>The cloud-base byte spans 0-4096 m (16 m steps, spec 6): the base is clamped to 250-3600 m. The structure tops keep <see cref="VolumeTop"/>.</summary>
        internal const float BaseRange = 4096f;

        internal readonly byte[] Near = new byte[NearSize * NearSize * 4];
        internal readonly byte[] NearProfiles = new byte[NearSize * NearSize * 4];
        internal readonly byte[] Far = new byte[FarSize * FarSize * 4];
        internal readonly byte[] FarProfiles = new byte[FarSize * FarSize * 4];
        /// <summary>Conservative empty space over the near span: r the highest cover, g the
        /// highest cloud top, b the lowest cloud base within about 2.5 km (more than the shader's
        /// 1.4 km boundary warp and the bilinear footprint). Where r is zero, or a point lies
        /// outside [b, g] with margins, no cloud can be drawn and the march skips its density.</summary>
        internal readonly byte[] Envelope = new byte[EnvelopeSize * EnvelopeSize * 4];
        /// <summary>Maximum encoded precipitation alpha across both displayed map levels, 0..1.</summary>
        internal float RainMaximum;
        internal int Revision;

        internal static CloudMaps Build(WeatherKey key, float time, float halfX, float halfZ, float hour,
            float nearHalf, float farHalf, TapeFixture fixture = null)
        {
            var snapshot = new WeatherField();
            snapshot.Build(key, time, halfX, halfZ, hour);
            if (fixture != null) snapshot.UseFixtureTape(fixture.Sampler, fixture.Frame);
            var result = new CloudMaps();
            Fill(snapshot, nearHalf, NearSize, result.Near, result.NearProfiles, ref result.RainMaximum);
            Fill(snapshot, farHalf, FarSize, result.Far, result.FarProfiles, ref result.RainMaximum, areaMean: true);
            BuildEnvelope(result.Near, result.NearProfiles, result.Envelope);
            return result;
        }

        private static void Fill(WeatherField snapshot, float half, int size, byte[] pixels, byte[] profiles,
            ref float rainMaximum, bool areaMean = false)
        {
            float third = 2f * half / size / 3f;
            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                float worldX = ((x + 0.5f) / size * 2f - 1f) * half;
                float worldZ = ((z + 0.5f) / size * 2f - 1f) * half;
                WeatherPoint p = snapshot.Sample(worldX, worldZ);
                if (areaMean) p = AreaMean(snapshot, worldX, worldZ, third, p);
                // The deck's geometry has its own thickness, independent of cell coverage; the local tape row gives it
                // (DeckTop). Include it even at a thinly covered edge, or empty-space skipping cuts tops off.
                float visualTop = Math.Max(p.LowTop, p.DeckTop) + p.BaseVariation;
                int i = (z * size + x) * 4;
                pixels[i] = Byte(p.BackgroundCover);
                pixels[i + 1] = 0;                              // the frontal shield's channel is free (spec 5.3)
                pixels[i + 2] = Byte(p.CellShape);
                pixels[i + 3] = Byte(visualTop / VolumeTop);
                profiles[i] = 0;
                profiles[i + 1] = 0;
                profiles[i + 2] = Byte(p.CloudBase / BaseRange);
                profiles[i + 3] = Byte(p.RainRate / 100f);
                rainMaximum = Math.Max(rainMaximum, profiles[i + 3] / 255f);
            }
        }

        /// <summary>
        /// The far window's texel as the mean of 3 x 3 samples over its area (spec 6), centred on <paramref name="centre"/>'s sample:
        /// a far texel is ~5 km wide and the smallest group 2 km, so one sample aliases the groups. The covers are means; the heights, the
        /// rain and the cover that sets the window bounds are the largest of the nine (a mean height would cut a neighbour's cloud top off);
        /// the base stays the centre's.
        /// </summary>
        private static WeatherPoint AreaMean(WeatherField snapshot, float x, float z, float third, WeatherPoint centre)
        {
            WeatherPoint mean = centre;
            float background = centre.BackgroundCover, shape = centre.CellShape;
            for (int k = 0; k < 9; k++)
            {
                if (k == 4) continue;
                WeatherPoint s = snapshot.Sample(x + (k % 3 - 1) * third, z + (k / 3 - 1) * third);
                background += s.BackgroundCover; shape += s.CellShape;
                mean.LowTop = Math.Max(mean.LowTop, s.LowTop);
                mean.DeckTop = Math.Max(mean.DeckTop, s.DeckTop);
                mean.RainRate = Math.Max(mean.RainRate, s.RainRate);
                mean.LowCover = Math.Max(mean.LowCover, s.LowCover);
            }
            mean.BackgroundCover = background / 9f;
            mean.CellShape = shape / 9f;
            return mean;
        }

        private static void BuildEnvelope(byte[] near, byte[] profiles, byte[] envelope)
        {
            const int reach = 3;
            int scale = NearSize / EnvelopeSize;
            for (int z = 0; z < EnvelopeSize; z++)
            for (int x = 0; x < EnvelopeSize; x++)
            {
                byte cover = 0, top = 0, bottom = 255;
                int x0 = Math.Max(0, x * scale - reach), x1 = Math.Min(NearSize - 1, x * scale + scale - 1 + reach);
                int z0 = Math.Max(0, z * scale - reach), z1 = Math.Min(NearSize - 1, z * scale + scale - 1 + reach);
                for (int zz = z0; zz <= z1; zz++)
                for (int xx = x0; xx <= x1; xx++)
                {
                    // Heights of every texel in reach, cloudy or not: bilinear filtering
                    // blends an empty texel's heights into its cloudy neighbour's.
                    int i = (zz * NearSize + xx) * 4;
                    byte c = Math.Max(near[i], near[i + 2]);
                    if (c > cover) cover = c;
                    // Stored tops already include the genus. A few hundred metres of slack covers
                    // a puff dome above that top; the old base+1600 floor made every deck a slab.
                    int crown = near[i + 3] + 6;
                    top = (byte)Math.Max(top, Math.Min(255, crown));
                    bottom = Math.Min(bottom, BaseToMapUnits(profiles[i + 2]));
                }
                int o = (z * EnvelopeSize + x) * 4;
                envelope[o] = cover;
                envelope[o + 1] = cover > 0 ? top : (byte)0;
                envelope[o + 2] = cover > 0 ? bottom : (byte)255;
                envelope[o + 3] = 255;
            }
        }

        /// <summary>A cloud-base byte (metres / <see cref="BaseRange"/>) in the units of the 16 km structure tops, rounded down so the envelope stays conservative.</summary>
        private static byte BaseToMapUnits(byte b) => (byte)(b * BaseRange / VolumeTop);

        private static byte Byte(float v) => (byte)(Math.Max(0f, Math.Min(1f, v)) * 255f + 0.5f);
    }
}
