using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Fire
{
    // A jittered lattice provides local Voronoi neighbours without a world-sized grid.
    // Cells are built only on ignition; all clients recover the same polygon from its seed.
    internal sealed class FireFrontCell
    {
        internal const float Spacing = 28f;
        internal const int MaximumVertices = 16;
        internal struct Point
        {
            public float X, Z;
            public Point(float x, float z) { X = x; Z = z; }
        }
        public readonly int X, Z;
        public readonly Point Center;
        public readonly Point[] Vertices = new Point[MaximumVertices];
        public readonly long[] Neighbours = new long[MaximumVertices];
        public int Count { get; private set; }
        public long Key => KeyOf(X, Z);
        public static long KeyOf(int x, int z) => ((long)x << 32) ^ (uint)z;
        public static Point Seed(int x, int z)
        {
            uint hash = Deterministic.Hash(x, z, 0x6f726573);
            return new Point((x + 0.5f + (Deterministic.UnitFloat(hash) - 0.5f) * 0.38f) * Spacing,
                (z + 0.5f + (Deterministic.UnitFloat(hash ^ 0x9e3779b9u) - 0.5f) * 0.38f) * Spacing);
        }
        public static long Locate(float x, float z)
        {
            int cx = (int)Math.Floor(x / Spacing), cz = (int)Math.Floor(z / Spacing);
            float best = float.MaxValue; long key = 0;
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                Point seed = Seed(cx + dx, cz + dz);
                float distance = (seed.X - x) * (seed.X - x) + (seed.Z - z) * (seed.Z - z);
                if (distance < best) { best = distance; key = KeyOf(cx + dx, cz + dz); }
            }
            return key;
        }
        public static FireFrontCell FromKey(long key) => new FireFrontCell((int)(key >> 32), (int)key);
        private FireFrontCell(int x, int z)
        {
            X = x; Z = z; Center = Seed(x, z);
            Span<Point> input = stackalloc Point[MaximumVertices];
            Span<Point> output = stackalloc Point[MaximumVertices];
            input[0] = new Point(-Spacing * 2f, -Spacing * 2f);
            input[1] = new Point(Spacing * 2f, -Spacing * 2f);
            input[2] = new Point(Spacing * 2f, Spacing * 2f);
            input[3] = new Point(-Spacing * 2f, Spacing * 2f);
            int count = 4;
            for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                Point other = Seed(x + dx, z + dz);
                float nx = other.X - Center.X, nz = other.Z - Center.Z;
                float limit = (nx * nx + nz * nz) * 0.5f;
                int written = 0;
                for (int i = 0; i < count; i++)
                {
                    Point a = input[i], b = input[(i + 1) % count];
                    float da = a.X * nx + a.Z * nz - limit;
                    float db = b.X * nx + b.Z * nz - limit;
                    if (da <= 0f) output[written++] = a;
                    if ((da <= 0f) != (db <= 0f))
                    {
                        float t = da / (da - db);
                        output[written++] = new Point(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t);
                    }
                }
                count = written; output.Slice(0, count).CopyTo(input);
            }
            Count = count; input.Slice(0, count).CopyTo(Vertices);
            for (int i = 0; i < count; i++)
            {
                Point a = Vertices[i], b = Vertices[(i + 1) % count];
                float closest = float.MaxValue;
                for (int dx = -2; dx <= 2; dx++)
                for (int dz = -2; dz <= 2; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    Point other = Seed(x + dx, z + dz);
                    float nx = other.X - Center.X, nz = other.Z - Center.Z;
                    float limit = (nx * nx + nz * nz) * 0.5f;
                    float error = Math.Abs(a.X * nx + a.Z * nz - limit) + Math.Abs(b.X * nx + b.Z * nz - limit);
                    if (error < closest) { closest = error; Neighbours[i] = KeyOf(x + dx, z + dz); }
                }
            }
        }
        internal float EdgeScore(int edge, float windX, float windZ, int attempt)
        {
            Point other = Seed((int)(Neighbours[edge] >> 32), (int)Neighbours[edge]);
            float dx = other.X - Center.X, dz = other.Z - Center.Z;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            float wind = (float)Math.Sqrt(windX * windX + windZ * windZ);
            float alignment = wind > 0.25f ? (dx * windX + dz * windZ) / (distance * wind) : 0f;
            Point a = Vertices[edge], b = Vertices[(edge + 1) % Count];
            float length = (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
            return alignment * Math.Min(wind / 6f, 1.8f) + length / Spacing * 0.45f +
                Deterministic.UnitFloat(Deterministic.Hash(X, Z, edge, attempt)) * 0.55f;
        }
    }
}
