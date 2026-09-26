using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    internal enum StrikeKind : byte
    {
        /// <summary>Inside the cloud: a flash that lights the tower, no visible channel to ground.</summary>
        InCloud = 0,

        /// <summary>Cloud to ground: a visible branching bolt.</summary>
        Ground = 1,
    }

    internal struct Strike
    {
        public float Time;
        public StrikeKind Kind;
        public float X;
        public float Z;

        /// <summary>Height of the flash (in-cloud) or of the channel's top (ground), metres above sea.</summary>
        public float Height;
        public uint Seed;
        public float Intensity;
        public int CellSlot;
    }

    /// <summary>
    /// The deterministic strike schedule. Each alive cell rolls once per whole second of mission
    /// time; a roll under rate/60 is a strike, placed and typed by further hashes of the same
    /// second. Every peer therefore sees the same bolt in the same place without a message, and
    /// a frame that spans several seconds simply collects every second it crossed.
    /// About one strike in five reaches the ground; the rest are in-cloud flashes.
    /// </summary>
    internal static class LightningSchedule
    {
        public const float GroundShare = 0.2f;

        /// <summary>Collects strikes with time in (from, to]; returns how many were written.</summary>
        public static int Collect(WeatherField field, float from, float to, Strike[] buffer)
        {
            if (!field.IsBuilt || to <= from) return 0;
            int count = 0;
            long first = (long)Math.Floor(from) + 1;
            long last = (long)Math.Floor(to);
            // A hitch or a scrub never replays more than a minute of lightning.
            if (last - first > 60) first = last - 60;

            for (int c = 0; c < field.CellCount && count < buffer.Length; c++)
            {
                StormCell cell = field.Cell(c);
                for (long second = first; second <= last && count < buffer.Length; second++)
                {
                    if (TryStrike(cell, second, out Strike strike)) buffer[count++] = strike;
                }
            }
            return count;
        }

        public static bool TryStrike(StormCell cell, long second, out Strike strike)
        {
            strike = default;
            float age = (second - cell.BirthTime) / cell.Life;
            if (age <= 0f || age >= 1f) return false;

            StormCell then = cell;
            then.Age = age;
            float rate = then.LightningRate;
            if (rate <= 0f) return false;

            int s = unchecked((int)second);
            if (WeatherMath.Hash01(cell.Seed, s, 61) >= rate / 60f) return false;

            float elapsed = second - cell.BirthTime;
            float cx = cell.X - cell.VelocityX * (cell.Age * cell.Life - elapsed);
            float cz = cell.Z - cell.VelocityZ * (cell.Age * cell.Life - elapsed);
            float angle = WeatherMath.Hash01(cell.Seed, s, 62) * 6.2831853f;
            float radius = (float)Math.Sqrt(WeatherMath.Hash01(cell.Seed, s, 63)) * cell.Radius * 1.1f;
            bool ground = WeatherMath.Hash01(cell.Seed, s, 64) < GroundShare;

            strike.Time = second + WeatherMath.Hash01(cell.Seed, s, 65) * 0.9f;
            strike.Kind = ground ? StrikeKind.Ground : StrikeKind.InCloud;
            strike.X = cx + (float)Math.Cos(angle) * radius;
            strike.Z = cz + (float)Math.Sin(angle) * radius;
            strike.Height = ground
                ? cell.Base + 200f
                : cell.Base + (then.Top - cell.Base) * WeatherMath.HashRange(cell.Seed, s, 66, 0, 0.2f, 0.8f);
            strike.Seed = unchecked(cell.Seed ^ (uint)s * 2246822519u);
            strike.Intensity = WeatherMath.HashRange(cell.Seed, s, 67, 0, 0.6f, 1f) * (cell.Severe ? 1.3f : 1f);
            strike.CellSlot = cell.Slot;
            return true;
        }

        /// <summary>
        /// Whether a strike hits an aircraft deep in a mature core during this second. Rare on
        /// purpose — about once in three minutes spent in the very core of a mature cell.
        /// </summary>
        public static bool HazardStrike(float coreDepth, float lightningRate, long second, uint salt)
        {
            if (coreDepth < 0.6f || lightningRate <= 0f) return false;
            float perSecond = 0.0055f * WeatherMath.Smoothstep(0.6f, 0.95f, coreDepth) * Math.Min(lightningRate / 4f, 2f);
            return WeatherMath.Hash01(salt, unchecked((int)second), 71) < perSecond;
        }
    }

    internal struct BoltSegment
    {
        public float AX, AY, AZ;
        public float BX, BY, BZ;
        public float Width;
        public float Brightness;
    }

    /// <summary>
    /// A branching lightning channel by recursive midpoint displacement: each segment is split at
    /// a midpoint pushed sideways by a fraction of its length, and early splits may fork a
    /// branch that is shorter, thinner and dimmer. Deterministic from the strike seed.
    /// </summary>
    internal static class BoltGenerator
    {
        public const int MaxSegments = 192;
        private const int Levels = 6;
        private const float Jitter = 0.32f;
        private const float BranchChance = 0.3f;

        public static int Generate(uint seed, float ax, float ay, float az, float bx, float by, float bz, BoltSegment[] buffer)
        {
            int count = 0;
            uint state = seed | 1u;
            Split(ref state, ax, ay, az, bx, by, bz, Levels, 1f, 1f, buffer, ref count);
            return count;
        }

        private static void Split(
            ref uint state, float ax, float ay, float az, float bx, float by, float bz,
            int level, float width, float brightness, BoltSegment[] buffer, ref int count)
        {
            if (count >= buffer.Length || count >= MaxSegments) return;
            if (level == 0)
            {
                buffer[count++] = new BoltSegment
                {
                    AX = ax, AY = ay, AZ = az, BX = bx, BY = by, BZ = bz,
                    Width = width, Brightness = brightness,
                };
                return;
            }

            float dx = bx - ax, dy = by - ay, dz = bz - az;
            float length = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            float mx = (ax + bx) * 0.5f + (Next(ref state) - 0.5f) * 2f * Jitter * length;
            float my = (ay + by) * 0.5f + (Next(ref state) - 0.5f) * 0.6f * Jitter * length;
            float mz = (az + bz) * 0.5f + (Next(ref state) - 0.5f) * 2f * Jitter * length;

            Split(ref state, ax, ay, az, mx, my, mz, level - 1, width, brightness, buffer, ref count);

            if (level >= Levels - 2 && Next(ref state) < BranchChance)
            {
                // A fork heads roughly the same way, 30–60 % as long, drifting off to one side.
                float share = 0.3f + 0.3f * Next(ref state);
                float fx = mx + (bx - mx) * share + (Next(ref state) - 0.5f) * length * 0.6f;
                float fy = my + (by - my) * share;
                float fz = mz + (bz - mz) * share + (Next(ref state) - 0.5f) * length * 0.6f;
                Split(ref state, mx, my, mz, fx, fy, fz, level - 2, width * 0.5f, brightness * 0.5f, buffer, ref count);
            }

            Split(ref state, mx, my, mz, bx, by, bz, level - 1, width, brightness, buffer, ref count);
        }

        private static float Next(ref uint state)
        {
            unchecked
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return (state & 0x00ffffffu) / 16777216f;
            }
        }
    }
}
