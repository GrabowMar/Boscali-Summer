using System;

namespace BoscaliSummer.Features.Support.Domain.Orbital
{
    /// <summary>Scene geometry for one SAR collect, in theatre metres relative to the scene centre.</summary>
    internal readonly struct SarGeometry
    {
        /// <summary>Incidence on level ground, radians from vertical.</summary>
        public readonly double Incidence;

        /// <summary>Horizontal unit vector from the scene toward the sensor (the look direction reversed).</summary>
        public readonly double RangeX, RangeZ;

        public readonly double SlantRange;
        public readonly double PlatformSpeed;

        public SarGeometry(double incidence, double rangeX, double rangeZ, double slantRange, double platformSpeed)
        {
            Incidence = OrbitMath.Clamp(incidence, 5.0 * OrbitMath.Deg, 80.0 * OrbitMath.Deg);
            double length = Math.Sqrt(rangeX * rangeX + rangeZ * rangeZ);
            RangeX = length > 1e-9 ? rangeX / length : 0.0;
            RangeZ = length > 1e-9 ? rangeZ / length : 1.0;
            SlantRange = Math.Max(1.0, slantRange);
            PlatformSpeed = Math.Max(1.0, platformSpeed);
        }

        /// <summary>Along-track axis: range rotated a quarter turn.</summary>
        public double AzimuthX => -RangeZ;
        public double AzimuthZ => RangeX;

        /// <summary>Unit line of sight from the scene up to the sensor.</summary>
        public double LosX => RangeX * Math.Sin(Incidence);
        public double LosY => Math.Cos(Incidence);
        public double LosZ => RangeZ * Math.Sin(Incidence);
    }

    /// <summary>
    /// Forms a ground-range-detected SAR image from scatter samples. A sample is projected the
    /// way a radar measures it — by slant range and Doppler — so a tall point lays over toward
    /// the sensor by h·cot(incidence), a target with radial velocity is displaced in azimuth by
    /// v·R/V, ground the rays never reached stays in shadow, and bright point targets spread
    /// sidelobes. Two-look speckle, a noise floor and a percentile dB stretch finish the image.
    /// Pure arithmetic and a seeded generator: the same samples always form the same bytes.
    /// </summary>
    internal sealed class SarImageFormer
    {
        public const double PointTargetSigma = 5.0;
        private const double MinimumDb = -60.0;
        private const double MaximumDb = 30.0;
        private const int HistogramBins = 512;

        private static readonly float[] Sidelobes = { 0.12f, 0.05f, 0.02f };

        private readonly float[] intensity;
        private readonly double[] db;
        private readonly int[] histogram = new int[HistogramBins];
        private readonly byte[] bytes;
        private readonly SarGeometry geometry;
        private readonly double cotIncidence;
        private readonly int seed;

        public SarImageFormer(int width, int height, double halfSize, in SarGeometry geometry, int seed)
        {
            Width = Math.Max(8, Math.Min(512, width));
            Height = Math.Max(8, Math.Min(512, height));
            HalfSize = Math.Max(50.0, halfSize);
            this.geometry = geometry;
            cotIncidence = 1.0 / Math.Tan(geometry.Incidence);
            this.seed = seed == 0 ? 0x2545F491 : seed;
            intensity = new float[Width * Height];
            db = new double[Width * Height];
            bytes = new byte[Width * Height];
        }

        public int Width { get; }
        public int Height { get; }
        public double HalfSize { get; }
        public SarGeometry Geometry => geometry;
        public int Accepted { get; private set; }
        public int Dropped { get; private set; }

        public double PixelRange => 2.0 * HalfSize / Width;
        public double PixelAzimuth => 2.0 * HalfSize / Height;

        /// <summary>Image cell a scatterer lands in. Column 0 is near range (sensor side).</summary>
        public bool Project(double x, double y, double z, double radialVelocity, out int column, out int row)
        {
            double ground = x * geometry.RangeX + z * geometry.RangeZ + y * cotIncidence;
            double azimuth = x * geometry.AzimuthX + z * geometry.AzimuthZ +
                             radialVelocity * geometry.SlantRange / geometry.PlatformSpeed;
            double c = (HalfSize - ground) / PixelRange;
            double r = (HalfSize - azimuth) / PixelAzimuth;
            column = (int)Math.Floor(c);
            row = (int)Math.Floor(r);
            return column >= 0 && column < Width && row >= 0 && row < Height &&
                   !double.IsNaN(c) && !double.IsNaN(r);
        }

        public void Add(double x, double y, double z, double sigma, double radialVelocity)
        {
            if (double.IsNaN(sigma) || sigma <= 0.0) return;
            if (!Project(x, y, z, radialVelocity, out int column, out int row))
            {
                Dropped++;
                return;
            }
            Accepted++;
            float value = (float)Math.Min(sigma, 1e6);
            intensity[row * Width + column] += value;
            if (sigma < PointTargetSigma) return;
            for (int k = 0; k < Sidelobes.Length; k++)
            {
                float lobe = value * Sidelobes[k];
                int d = k + 1;
                Deposit(column - d, row, lobe);
                Deposit(column + d, row, lobe);
                Deposit(column, row - d, lobe);
                Deposit(column, row + d, lobe);
            }
        }

        /// <summary>Ground point on the flat reference plane for one planned ray, jittered inside
        /// its cell. Rays run row by row (azimuth) and across range within a row.</summary>
        public void PlanRay(int index, int rangeSamples, out double x, out double z)
        {
            int perRow = Math.Max(1, rangeSamples);
            int row = index / perRow;
            int column = index % perRow;
            uint hash = BoscaliSummer.Core.Deterministic.Hash(seed, index, 0x5a2, 7);
            double jitterU = BoscaliSummer.Core.Deterministic.UnitFloat(hash) - 0.5;
            double jitterV = BoscaliSummer.Core.Deterministic.UnitFloat(hash * 2654435761u) - 0.5;
            double ground = HalfSize - (column + 0.5 + jitterU) / perRow * 2.0 * HalfSize;
            double azimuth = HalfSize - (row + 0.5 + jitterV) / Height * 2.0 * HalfSize;
            x = ground * geometry.RangeX + azimuth * geometry.AzimuthX;
            z = ground * geometry.RangeZ + azimuth * geometry.AzimuthZ;
        }

        public int RayCount(int rangeSamples) => Math.Max(1, rangeSamples) * Height;

        /// <summary>Speckled, stretched 8-bit image, row 0 at the top (far azimuth). The buffer is
        /// reused between calls — consume it before forming the next image.</summary>
        public byte[] Form(int looks, double noiseFloor)
        {
            int n = intensity.Length;
            uint state = (uint)seed;
            int lookCount = Math.Max(1, Math.Min(8, looks));
            double floor = Math.Max(0.0, noiseFloor);

            for (int i = 0; i < n; i++)
            {
                double speckle = 0.0;
                for (int l = 0; l < lookCount; l++) speckle += Exponential(ref state);
                speckle /= lookCount;
                double value = intensity[i] * speckle + floor * Exponential(ref state);
                db[i] = 10.0 * Math.Log10(value + 1e-9);
            }

            Array.Clear(histogram, 0, histogram.Length);
            for (int i = 0; i < n; i++) histogram[Bin(db[i])]++;
            double low = Percentile(histogram, n, 0.02);
            double high = Percentile(histogram, n, 0.996);
            if (high - low < 1.0) high = low + 1.0;

            for (int i = 0; i < n; i++)
            {
                double t = OrbitMath.Clamp((db[i] - low) / (high - low), 0.0, 1.0);
                bytes[i] = (byte)Math.Round(Math.Pow(t, 0.9) * 255.0);
            }
            return bytes;
        }

        private void Deposit(int column, int row, float value)
        {
            if (column < 0 || column >= Width || row < 0 || row >= Height) return;
            intensity[row * Width + column] += value;
        }

        private static double Exponential(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            double u = (state & 0x00ffffffu) / 16777216.0;
            return -Math.Log(1.0 - u);
        }

        private static int Bin(double db)
        {
            int bin = (int)((db - MinimumDb) / (MaximumDb - MinimumDb) * (HistogramBins - 1));
            return bin < 0 ? 0 : bin >= HistogramBins ? HistogramBins - 1 : bin;
        }

        private static double Percentile(int[] histogram, int total, double fraction)
        {
            int target = (int)(total * fraction);
            int running = 0;
            for (int i = 0; i < histogram.Length; i++)
            {
                running += histogram[i];
                if (running > target) return MinimumDb + (MaximumDb - MinimumDb) * i / (HistogramBins - 1);
            }
            return MaximumDb;
        }
    }
}
