using System;
using BoscaliSummer.Features.Weather.Domain;

namespace BoscaliSummer.Features.Weather.Visuals
{
    /// <summary>The cloud renderer's weather maps for one settled state: a near map over the
    /// flight domain and a coarse far map (the level of detail out to the horizon), RGBA32
    /// bytes ready to upload. Pure: built on a worker, or once on the main thread to preload.
    /// Structure: sheet cover, front cover, cell shape, cloud top. Profiles: front base, front
    /// top, cloud base, rain.</summary>
    internal sealed class CloudMaps
    {
        internal const int NearSize = 256;
        internal const int FarSize = 128;
        internal const float VolumeTop = 16000f;

        internal readonly float SettledAt;
        internal readonly byte[] Near = new byte[NearSize * NearSize * 4];
        internal readonly byte[] NearProfiles = new byte[NearSize * NearSize * 4];
        internal readonly byte[] Far = new byte[FarSize * FarSize * 4];
        internal readonly byte[] FarProfiles = new byte[FarSize * FarSize * 4];
        internal float Bottom, Top, HorizonCover;
        internal int Revision;

        private CloudMaps(float settledAt) { SettledAt = settledAt; }

        internal static CloudMaps Build(WeatherKey key, float settledAt, float halfX, float halfZ, float hour,
            float nearHalf, float farHalf)
        {
            var snapshot = new WeatherField();
            snapshot.Build(key, settledAt, halfX, halfZ, hour);
            var result = new CloudMaps(settledAt);
            float bottom = VolumeTop, top = 0f;
            Fill(snapshot, nearHalf, NearSize, result.Near, result.NearProfiles, ref bottom, ref top);
            Fill(snapshot, farHalf, FarSize, result.Far, result.FarProfiles, ref bottom, ref top);
            result.HorizonCover = RingCover(result.Far, FarSize);
            result.Bottom = Math.Max(0f, bottom - 500f);
            result.Top = Math.Min(VolumeTop, Math.Max(bottom + 2000f, top + 1600f));
            return result;
        }

        private static void Fill(WeatherField snapshot, float half, int size, byte[] pixels, byte[] profiles,
            ref float bottom, ref float top)
        {
            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                float worldX = ((x + 0.5f) / size * 2f - 1f) * half;
                float worldZ = ((z + 0.5f) / size * 2f - 1f) * half;
                WeatherPoint p = snapshot.Sample(worldX, worldZ);
                int i = (z * size + x) * 4;
                pixels[i] = Byte(p.BackgroundCover);
                pixels[i + 1] = Byte(p.FrontCover);
                pixels[i + 2] = Byte(p.CellShape);
                pixels[i + 3] = Byte(p.CloudTop / VolumeTop);
                profiles[i] = Byte(p.FrontBase / VolumeTop);
                profiles[i + 1] = Byte(p.FrontTop / VolumeTop);
                profiles[i + 2] = Byte(p.CloudBase / VolumeTop);
                profiles[i + 3] = Byte(p.RainRate / 100f);
                if (p.Cover > 0.02f)
                {
                    bottom = Math.Min(bottom, p.FrontCover > 0.02f ? Math.Min(p.CloudBase, p.FrontBase) : p.CloudBase);
                    top = Math.Max(top, Math.Max(p.CloudTop, p.FrontTop));
                }
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
