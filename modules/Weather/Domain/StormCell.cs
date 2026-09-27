using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    internal enum StormStage : byte
    {
        Towering = 0,
        Mature = 1,
        Dissipating = 2,
    }

    /// <summary>
    /// One convective cell resolved at one instant. Everything but <see cref="Age"/> and the
    /// position is fixed at birth, so a cell drifts and evolves smoothly and never pops.
    /// </summary>
    internal struct StormCell
    {
        public int Slot;
        public int Cluster;
        public int Generation;
        public uint Seed;

        /// <summary>Centre now (metres, map frame: x east, z north).</summary>
        public float X;
        public float Z;
        public float VelocityX;
        public float VelocityZ;

        public float BirthTime;
        public float Life;

        /// <summary>0..1 through the lifecycle.</summary>
        public float Age;

        public float Radius;
        public float PeakRain;
        public float Base;
        public float TopMax;
        public float LightningPeak;
        public bool Severe;
        public bool Hail;
        public bool OnFront;

        public StormStage Stage => Age < 0.3f ? StormStage.Towering : Age < 0.7f ? StormStage.Mature : StormStage.Dissipating;

        /// <summary>Cloud presence: builds first, lingers as an anvil after the rain.</summary>
        public float CloudLevel => WeatherMath.Envelope(Age, 0f, 0.2f, 0.85f, 1f);

        /// <summary>Rain intensity: starts at 20 % of life, peaks through maturity.</summary>
        public float RainLevel => WeatherMath.Envelope(Age, 0.2f, 0.4f, 0.65f, 0.95f);

        public float LightningLevel => WeatherMath.Envelope(Age, 0.3f, 0.45f, 0.65f, 0.8f);

        public float UpdraftLevel => WeatherMath.Envelope(Age, 0.05f, 0.2f, 0.3f, 0.45f);

        public float DowndraftLevel => WeatherMath.Envelope(Age, 0.35f, 0.5f, 0.7f, 0.9f);

        /// <summary>Tower top now: rises through the towering stage.</summary>
        public float Top => Base + (TopMax - Base) * WeatherMath.Smoothstep(0f, 0.35f, Age);

        /// <summary>Strikes per minute now.</summary>
        public float LightningRate => LightningPeak * LightningLevel;

        public float RainAt(float x, float z)
        {
            float level = RainLevel;
            if (level <= 0f) return 0f;
            float core = Elliptic(x - X, z - Z, Radius);
            // A light-rain halo trailing downwind of the core.
            float speed = (float)Math.Sqrt(VelocityX * VelocityX + VelocityZ * VelocityZ);
            float hx = X, hz = Z;
            if (speed > 0.1f)
            {
                hx -= VelocityX / speed * Radius * 1.5f;
                hz -= VelocityZ / speed * Radius * 1.5f;
            }
            float halo = Gauss2(x - hx, z - hz, Radius * 2.5f);
            return PeakRain * level * (core + 0.12f * halo);
        }

        public float CoverAt(float x, float z)
        {
            float level = CloudLevel;
            if (level <= 0f) return 0f;
            float dx = x - X, dz = z - Z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            return level * (1f - WeatherMath.Smoothstep(Radius * 1.0f, Radius * 2.2f, d));
        }

        /// <summary>0..1 how deep a point is in the core (rain-weighted), for HUD and hazard.</summary>
        public float CoreAt(float x, float z) => RainLevel * Elliptic(x - X, z - Z, Radius);

        /// <summary>Horizontal outflow (gust front) and vertical draft at a point.</summary>
        public void WindAt(float x, float z, out float outX, out float outZ, out float vertical, out float turbulence)
        {
            float dx = x - X, dz = z - Z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            float down = DowndraftLevel;
            float up = UpdraftLevel;

            vertical = 6f * up * Gauss(d, Radius) - 8f * down * Gauss(d, Radius * 0.8f);

            // The gust front spreads out from the core as the downdraft matures.
            float ring = Radius * (1.0f + 2.0f * WeatherMath.Clamp01((Age - 0.35f) / 0.4f));
            float gust = 15f * down * Gauss(d - ring, Radius * 0.6f) + 6f * down * Gauss(d, Radius * 0.7f);
            if (d > 1f)
            {
                outX = dx / d * gust;
                outZ = dz / d * gust;
            }
            else
            {
                outX = 0f;
                outZ = 0f;
            }

            turbulence = (0.25f * up + 0.55f * down + 0.2f * CloudLevel) * Gauss(d, Radius * 2f);
        }

        private float Elliptic(float dx, float dz, float radius)
        {
            // Stretched 1.3× along the direction of travel.
            float speed = (float)Math.Sqrt(VelocityX * VelocityX + VelocityZ * VelocityZ);
            if (speed < 0.1f) return Gauss2(dx, dz, radius);
            float ax = VelocityX / speed, az = VelocityZ / speed;
            float along = dx * ax + dz * az;
            float across = -dx * az + dz * ax;
            float u = along / (radius * 1.3f);
            float v = across / radius;
            return (float)Math.Exp(-(u * u + v * v));
        }

        private static float Gauss2(float dx, float dz, float width)
        {
            float d2 = (dx * dx + dz * dz) / (width * width);
            return (float)Math.Exp(-d2);
        }

        private static float Gauss(float x, float width)
        {
            float u = x / width;
            return (float)Math.Exp(-u * u);
        }
    }

    /// <summary>
    /// Four deterministic clusters of three cells. Members share a birth region and steering
    /// wind, but grow at staggered times. Frontal clusters form behind the boundary; free
    /// clusters form in the convective air mass.
    /// </summary>
    internal static class StormCells
    {
        public const int MaxCells = 12;
        public const int CellsPerCluster = 3;
        public const float MinPeriod = 24f * 60f;
        public const float MaxPeriod = 36f * 60f;
        public const float MinLife = 18f * 60f;
        public const float MaxLife = 30f * 60f;

        /// <summary>Fills <paramref name="cells"/> with the cells alive at <paramref name="time"/>; returns the count.</summary>
        public static int Fill(StormCell[] cells, WeatherKey key, float time, float halfX, float halfZ)
        {
            int count = 0;
            for (int slot = 0; slot < MaxCells && count < cells.Length; slot++)
            {
                if (TryResolve(key, slot, time, halfX, halfZ, out StormCell cell)) cells[count++] = cell;
            }
            return count;
        }

        public static bool TryResolve(WeatherKey key, int slot, float time, float halfX, float halfZ, out StormCell cell)
        {
            cell = default;
            uint seed = key.Seed;
            int cluster = slot / CellsPerCluster;
            int member = slot % CellsPerCluster;
            float period = WeatherMath.HashRange(seed, cluster, 21, 0, MinPeriod, MaxPeriod);
            float phase = WeatherMath.Hash01(seed, cluster, 22) * period;
            int generation = (int)Math.Floor((time - key.Epoch - phase) / period);
            float clusterBirth = key.Epoch + phase + generation * period;
            float stagger = member * 85f + WeatherMath.HashRange(seed, slot, generation, 26, 0f, 65f);
            float birth = clusterBirth + stagger;
            float life = Math.Min(WeatherMath.HashRange(seed, slot, generation, 24, MinLife, MaxLife),
                period - stagger - 60f);
            float age = (time - birth) / life;
            if (age < 0f || age >= 1f) return false;

            RegimeState sky = RegimeSchedule.Evaluate(key, clusterBirth);
            float roll = WeatherMath.Hash01(seed, cluster, generation, 23);
            if (roll >= sky.Params.Convective) return false;

            uint clusterSeed = unchecked((uint)(cluster * 73856093) ^ (uint)(generation * 19349663) ^ seed);
            uint cellSeed = unchecked((uint)(slot * 73856093) ^ (uint)(generation * 19349663) ^ seed);
            bool severe = WeatherMath.Hash01(clusterSeed, 1, 0) < sky.Params.Severity;

            cell.Slot = slot;
            cell.Cluster = cluster;
            cell.Generation = generation;
            cell.Seed = cellSeed;
            cell.BirthTime = birth;
            cell.Life = life;
            cell.Age = age;
            cell.Severe = severe;
            cell.Radius = severe
                ? WeatherMath.HashRange(cellSeed, 2, 0, 0, 2500f, 5000f)
                : WeatherMath.HashRange(cellSeed, 2, 0, 0, 1500f, 4000f) * (0.7f + 0.3f * sky.Params.Convective);
            cell.PeakRain = severe
                ? WeatherMath.HashRange(cellSeed, 3, 0, 0, 60f, 120f)
                : WeatherMath.HashRange(cellSeed, 3, 0, 0, 15f, 80f);
            cell.Hail = severe && cell.PeakRain > 70f && WeatherMath.Hash01(cellSeed, 4, 0) < 0.6f;
            cell.Base = WeatherMath.Clamp(sky.Params.CloudBase, 400f, 2500f);
            cell.TopMax = WeatherMath.HashRange(cellSeed, 5, 0, 0, 7000f, 11000f) + (severe ? 2000f : 0f);
            cell.LightningPeak = WeatherMath.HashRange(cellSeed, 6, 0, 0, 2f, 6f) * (severe ? 3f : 1f);

            // Anchor to the strongest front on the map at birth, if the roll says so.
            int best = -1;
            float bestStrength = 0.3f;
            for (int i = 0; i < sky.FrontCount; i++)
            {
                FrontSource source = sky.GetFront(i);
                if (source.Strength > bestStrength)
                {
                    bestStrength = source.Strength;
                    best = i;
                }
            }

            float x0, z0, vx, vz;
            if (best >= 0 && WeatherMath.Hash01(clusterSeed, 7, 0) < 0.6f * bestStrength + 0.2f)
            {
                FrontState front = WeatherFronts.Resolve(sky.GetFront(best), seed, clusterBirth);
                float along = ((cluster - 1.5f) * 0.28f +
                    WeatherMath.HashRange(clusterSeed, 8, 0, 0, -0.07f, 0.07f)) * Math.Max(halfX, halfZ) * 2f;
                along += WeatherMath.HashRange(cellSeed, 8, 1, 0, -4500f, 4500f);
                // Just behind the line (the side it has already swept), riding with it.
                float behind = WeatherMath.HashRange(cellSeed, 9, 0, 0, 500f, 3500f);
                float lx = front.OffsetAtAlong(along) - behind;
                x0 = front.NormalX * lx - front.NormalZ * along;
                z0 = front.NormalZ * lx + front.NormalX * along;
                vx = front.NormalX * front.Speed;
                vz = front.NormalZ * front.Speed;
                cell.OnFront = true;
            }
            else
            {
                x0 = WeatherMath.HashRange(clusterSeed, 10, 0, 0, -0.38f, 0.38f) * halfX * 2f +
                    WeatherMath.HashRange(cellSeed, 10, 1, 0, -5000f, 5000f);
                z0 = WeatherMath.HashRange(clusterSeed, 11, 0, 0, -0.38f, 0.38f) * halfZ * 2f +
                    WeatherMath.HashRange(cellSeed, 11, 1, 0, -5000f, 5000f);
                WeatherField.PrevailingWind(seed, sky.Params.WindSpeed, birth, out float wx, out float wz);
                vx = 0.8f * wx + WeatherMath.HashRange(cellSeed, 12, 0, 0, -2.5f, 2.5f);
                vz = 0.8f * wz + WeatherMath.HashRange(cellSeed, 13, 0, 0, -2.5f, 2.5f);
            }

            float elapsed = time - birth;
            cell.X = x0 + vx * elapsed;
            cell.Z = z0 + vz * elapsed;
            cell.VelocityX = vx;
            cell.VelocityZ = vz;
            return true;
        }
    }
}
