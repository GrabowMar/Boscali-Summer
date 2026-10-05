using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>The cloud renderer's weather maps for one settled state: a near map over the
    /// flight domain and a coarse far map (the level of detail out to the horizon), RGBA32
    /// bytes ready to upload. Pure: built on a worker, or once on the main thread to preload.
    /// Structure: sheet cover, front cover, cell shape, low/tower top. Profiles: front base, front
    /// top, cloud base, rain.</summary>
    internal sealed class CloudMaps
    {
        internal const int NearSize = 256;
        internal const int FarSize = 128;
        /// <summary>The empty-space map: half the near map's resolution over the same span.</summary>
        internal const int EnvelopeSize = 128;
        internal const float VolumeTop = 16000f;

        internal readonly float SettledAt;
        internal readonly byte[] Near = new byte[NearSize * NearSize * 4];
        internal readonly byte[] NearProfiles = new byte[NearSize * NearSize * 4];
        internal readonly byte[] Far = new byte[FarSize * FarSize * 4];
        internal readonly byte[] FarProfiles = new byte[FarSize * FarSize * 4];
        /// <summary>Conservative empty space over the near span: r the highest cover, g the
        /// highest cloud top, b the lowest cloud base within about 2.5 km (more than the shader's
        /// 1.4 km boundary warp and the bilinear footprint). Where r is zero, or a point lies
        /// outside [b, g] with margins, no cloud can be drawn and the march skips its density.</summary>
        internal readonly byte[] Envelope = new byte[EnvelopeSize * EnvelopeSize * 4];
        internal float Bottom, Top, HorizonCover;
        /// <summary>Maximum encoded precipitation alpha across both displayed map levels, 0..1.</summary>
        internal float RainMaximum;
        internal int Revision;

        private CloudMaps(float settledAt) { SettledAt = settledAt; }

        internal static CloudMaps Build(WeatherKey key, float settledAt, float halfX, float halfZ, float hour,
            float nearHalf, float farHalf)
        {
            var snapshot = new WeatherField();
            snapshot.Build(key, settledAt, halfX, halfZ, hour);
            var result = new CloudMaps(settledAt);
            float bottom = VolumeTop, top = 0f;
            Fill(snapshot, nearHalf, NearSize, result.Near, result.NearProfiles, ref bottom, ref top, ref result.RainMaximum);
            Fill(snapshot, farHalf, FarSize, result.Far, result.FarProfiles, ref bottom, ref top, ref result.RainMaximum);
            result.HorizonCover = RingCover(result.Far, FarSize);
            BuildEnvelope(result.Near, result.NearProfiles, result.Envelope);
            result.Bottom = Math.Max(-30f, bottom - 800f);
            result.Top = Math.Min(VolumeTop, Math.Max(bottom + 2000f, top + 1600f));
            return result;
        }

        private static void Fill(WeatherField snapshot, float half, int size, byte[] pixels, byte[] profiles,
            ref float bottom, ref float top, ref float rainMaximum)
        {
            CloudGenus genus = CloudShape.Resolve(snapshot.Params);
            float deckDepth = Math.Max(genus.PuffDepth, snapshot.Params.LayerDepth) + genus.BaseWobble * 0.5f;
            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                float worldX = ((x + 0.5f) / size * 2f - 1f) * half;
                float worldZ = ((z + 0.5f) / size * 2f - 1f) * half;
                WeatherPoint p = snapshot.Sample(worldX, worldZ);
                // The deck's geometry has its own thickness, independent of cell coverage.
                // Include it even at a thinly covered edge, or empty-space skipping cuts tops off.
                float visualTop = Math.Max(p.LowTop, p.CloudBase + deckDepth);
                int i = (z * size + x) * 4;
                pixels[i] = Byte(p.BackgroundCover);
                pixels[i + 1] = Byte(p.FrontCover);
                pixels[i + 2] = Byte(p.CellShape);
                pixels[i + 3] = Byte(visualTop / VolumeTop);
                profiles[i] = Byte(p.FrontBase / VolumeTop);
                profiles[i + 1] = Byte(p.FrontTop / VolumeTop);
                profiles[i + 2] = Byte(p.CloudBase / VolumeTop);
                profiles[i + 3] = Byte(p.RainRate / 100f);
                rainMaximum = Math.Max(rainMaximum, profiles[i + 3] / 255f);
                if (p.Cover > 0.02f)
                {
                    bottom = Math.Min(bottom, p.FrontCover > 0.02f ? Math.Min(p.CloudBase, p.FrontBase) : p.CloudBase);
                    top = Math.Max(top, Math.Max(visualTop, p.FrontTop));
                }
            }
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
                    byte c = Math.Max(near[i], Math.Max(near[i + 1], near[i + 2]));
                    if (c > cover) cover = c;
                    // Stored tops already include the genus. A few hundred metres of slack covers
                    // a puff dome above that top; the old base+1600 floor made every deck a slab.
                    int crown = Math.Max(near[i + 3], profiles[i + 1]) + 6;
                    top = (byte)Math.Max(top, Math.Min(255, crown));
                    bottom = Math.Min(bottom, Math.Min(profiles[i], profiles[i + 2]));
                }
                int o = (z * EnvelopeSize + x) * 4;
                envelope[o] = cover;
                envelope[o + 1] = cover > 0 ? top : (byte)0;
                envelope[o + 2] = cover > 0 ? bottom : (byte)255;
                envelope[o + 3] = 255;
            }
        }

        /// <summary>Per-texel union of two envelopes: what a crossfade between them can show.</summary>
        internal static void Union(byte[] a, byte[] b, byte[] into)
        {
            for (int i = 0; i < into.Length; i += 4)
            {
                into[i] = Math.Max(a[i], b[i]);
                into[i + 1] = Math.Max(a[i + 1], b[i + 1]);
                into[i + 2] = Math.Min(a[i + 2], b[i + 2]);
                into[i + 3] = 255;
            }
        }

        /// <summary>Mean sheet and front cover on the far map's outer ring: what the horizon deck
        /// continues with, so the two agree where they meet.</summary>
        internal static float RingCover(byte[] pixels, int size)
        {
            float sum = 0f;
            int n = 0;
            int ring = size / 10;
            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                if (x >= ring && x < size - ring && z >= ring && z < size - ring) continue;
                int i = (z * size + x) * 4;
                float sheet = pixels[i] / 255f, front = pixels[i + 1] / 255f;
                sum += 1f - (1f - sheet) * (1f - front);
                n++;
            }
            return n > 0 ? sum / n : 0f;
        }

        private static byte Byte(float v) => (byte)(Math.Max(0f, Math.Min(1f, v)) * 255f + 0.5f);
    }
}
