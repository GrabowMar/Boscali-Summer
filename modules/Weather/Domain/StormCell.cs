using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    internal enum StormStage : byte
    {
        Towering = 0,
        Mature = 1,
        Dissipating = 2,
    }

    /// <summary>
    /// One convective cell at a fixed site of the static layout. <see cref="Age"/> is its
    /// activation by the weather state (0 = gone, 0.5 = mature), so a fade grows the tower,
    /// then the rain, then the lightning, in place. Velocity only orients the rain halo.
    /// </summary>
    internal struct StormCell
    {
        public int Slot;
        public int Cluster;
        public uint Seed;

        /// <summary>Centre now (metres, map frame: x east, z north).</summary>
        public float X;
        public float Z;
        public float VelocityX;
        public float VelocityZ;

        /// <summary>0..1 through the lifecycle.</summary>
        public float Age;

        public float Radius;
        public float PeakRain;
        public float Base;
        public float TopMax;
        public float LightningPeak;
        public bool Severe;
        public bool Hail;

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
            float d = EllipticDistance(dx, dz);
            return level * (1f - WeatherMath.Smoothstep(Radius * 1.0f, Radius * 2.2f, d));
        }

        /// <summary>Continuous horizontal buoyant core for the cloud crown; unlike
        /// cover, it has no flat centre that would make a cylindrical tower.</summary>
        public float ShapeAt(float x, float z)
        {
            float dx = x - X, dz = z - Z;
            return CloudLevel * Elliptic(dx, dz, Radius * 1.4f);
        }

        /// <summary>Metres from the buoyant core in its fixed wind-oriented footprint.
        /// Rain, ordinary cloud and the crown use the same ellipse.</summary>
        public float EllipticDistance(float dx, float dz)
        {
            float speed = (float)Math.Sqrt(VelocityX * VelocityX + VelocityZ * VelocityZ);
            if (speed < 0.1f) return (float)Math.Sqrt(dx * dx + dz * dz);
            float ax = VelocityX / speed, az = VelocityZ / speed;
            float along = (dx * ax + dz * az) / 1.3f;
            float across = -dx * az + dz * ax;
            return (float)Math.Sqrt(along * along + across * across);
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
            float ring = Radius * (1.0f + 2.0f * Scalar.Clamp01((Age - 0.35f) / 0.4f));
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
    /// Four clusters of three cell sites per layout. Sites are fixed; each has a rank, and the
    /// state's convective level decides how many are active and how deep they grow.
    /// </summary>
    internal static class StormCells
    {
        public const int MaxCells = 12;
        public const int CellsPerCluster = 3;

        public static int Fill(StormCell[] cells, uint layout, StateParams sky, float halfX, float halfZ,
            float driftX, float driftZ, SkySplit spine = default)
        {
            int count = 0;
            float convective = Scalar.Clamp01(sky.Convective);
            if (convective <= 0f) return 0;
            for (int slot = 0; slot < MaxCells && count < cells.Length; slot++)
            {
                int cluster = slot / CellsPerCluster;
                uint clusterSeed = unchecked(layout ^ (uint)(cluster * 73856093) ^ 0x51ed27u);
                uint cellSeed = unchecked(layout ^ (uint)(slot * 19349663) ^ 0x2c1b3au);
                // Rank orders sites by how early they fire as convection deepens.
                float rank = (slot + WeatherMath.Hash01(cellSeed, 1, 0)) / MaxCells;
                float activation = WeatherMath.Smoothstep(rank * 0.7f, rank * 0.7f + 0.4f, convective);
                if (activation <= 0f) continue;

                // Severity blends each cluster toward its severe form rather than flipping it,
                // so a fade into STORM swells cells instead of popping them.
                float threshold = WeatherMath.Hash01(clusterSeed, 1, 0);
                float severe = WeatherMath.Smoothstep(threshold, threshold + 0.15f, sky.Severity);
                var cell = new StormCell
                {
                    Slot = slot,
                    Cluster = cluster,
                    Seed = cellSeed,
                    Age = 0.5f * activation,
                    Severe = severe > 0.5f,
                    X = WeatherMath.HashRange(clusterSeed, 10, 0, 0, -0.38f, 0.38f) * halfX * 2f +
                        WeatherMath.HashRange(cellSeed, 10, 1, 0, -5000f, 5000f),
                    Z = WeatherMath.HashRange(clusterSeed, 11, 0, 0, -0.38f, 0.38f) * halfZ * 2f +
                        WeatherMath.HashRange(cellSeed, 11, 1, 0, -5000f, 5000f),
                    VelocityX = driftX,
                    VelocityZ = driftZ,
                };
                if (cluster < 2 && spine.MeanderWavelength > 0f)
                {
                    // Two families bead the leading edge; two remain isolated. The curve,
                    // sibling spacing and side of the line are layout properties, never
                    // functions of convection, severity, radius or activation.
                    float span = Math.Min(halfX, halfZ);
                    float along = (cluster == 0 ? -0.42f : 0.42f) * span +
                        WeatherMath.HashRange(clusterSeed, 12, 0, 0, -0.12f, 0.12f) * span +
                        (slot % CellsPerCluster - 1) * 5000f +
                        WeatherMath.HashRange(cellSeed, 12, 1, 0, -1200f, 1200f);
                    float tx = -spine.NormalZ * along, tz = spine.NormalX * along;
                    float offset = spine.SignedDistance(tx, tz) - 2000f +
                        WeatherMath.HashRange(cellSeed, 13, 1, 0, -1600f, 1600f);
                    cell.X = Scalar.Clamp(tx + spine.NormalX * offset, -halfX * 0.96f, halfX * 0.96f);
                    cell.Z = Scalar.Clamp(tz + spine.NormalZ * offset, -halfZ * 0.96f, halfZ * 0.96f);
                }
                cell.Radius = WeatherMath.Lerp(
                    WeatherMath.HashRange(cellSeed, 2, 0, 0, 1500f, 4000f) * (0.7f + 0.3f * convective),
                    WeatherMath.HashRange(cellSeed, 2, 0, 0, 2500f, 5000f), severe);
                float peakRain = 2f + 78f * convective * convective * convective;
                cell.PeakRain = WeatherMath.Lerp(
                    WeatherMath.HashRange(cellSeed, 3, 0, 0, peakRain * 0.36f, peakRain),
                    WeatherMath.HashRange(cellSeed, 3, 0, 0, 60f, 120f), severe);
                cell.Hail = severe > 0.5f && cell.PeakRain > 70f && WeatherMath.Hash01(cellSeed, 4, 0) < 0.6f;
                cell.Base = Scalar.Clamp(sky.CloudBase, 400f, 2500f);
                // A fair-weather thermal condenses into shallow cumulus; deep moist
                // convection has the buoyancy for a tall rain cell.
                float depth = 1200f + 4600f * convective +
                    WeatherMath.HashRange(cellSeed, 5, 0, 0, 0f, 800f + 1800f * convective);
                cell.TopMax = cell.Base + depth + 3000f * severe;
                float lightning = convective < 0.55f ? 0f :
                    WeatherMath.HashRange(cellSeed, 6, 0, 0, 0.3f, 4f) * (convective - 0.55f) / 0.45f;
                cell.LightningPeak = WeatherMath.Lerp(lightning, WeatherMath.HashRange(cellSeed, 6, 0, 0, 6f, 18f), severe);
                cells[count++] = cell;
            }
            return count;
        }
    }
}
