using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// One convective cell of the storm lattice (spec 6): circular, with a constant <see cref="Strength"/> read at its centre. Rain,
    /// cover, shape, wind and lightning each have a finite support, so a 5 x 5 block of 16 km cells sees every cell that can touch
    /// a point. All coordinates are tape-frame metres; the field converts with <see cref="TapeFrame.ToTape"/>.
    /// </summary>
    internal struct StormCell
    {
        /// <summary>Supports as multiples of the radius R: shape and core 2.9 R (the cover disc is gone by 2.2 R, see CoverAt), rain halo 5.5 R, wind and turbulence 4 R, lightning 6 R.</summary>
        public const float CoverSupport = 2.9f, HaloSupport = 5.5f, WindSupport = 4f, LightningSupport = 6f;

        public int Gx, Gz, K;
        /// <summary>hash(layout seed, gx, gz, k): the cell's lightning events derive from it.</summary>
        public uint Seed;
        public float X, Z;
        public float Radius, Rank, Level, Strength;
        /// <summary>Cloud base at the centre and the full depth of the tower above it.</summary>
        public float Base, Depth;
        /// <summary>Peak rain P = 30 + 90 * Severity mm/h at full strength.</summary>
        public float PeakRain;
        public float LightningPeak;
        public bool Severe, Hail;

        /// <summary>The widest support of the cell: nothing of it lies further than this from the centre.</summary>
        public float Reach => LightningSupport * Radius;

        /// <summary>Tower top: the base plus the depth, rising with the strength (a weak cell is a young, low tower).</summary>
        public float Top => Base + Depth * WeatherMath.Smoothstep(0.1f, 0.6f, Strength);

        /// <summary>Strikes per minute at the centre: only a developed cell is electric.</summary>
        public float LightningRate => LightningPeak * WeatherMath.Smoothstep(0.55f, 0.85f, Strength);

        public float UpdraftLevel => WeatherMath.Smoothstep(0.05f, 0.3f, Strength) * (1f - WeatherMath.Smoothstep(0.5f, 0.9f, Strength));
        public float DowndraftLevel => WeatherMath.Smoothstep(0.4f, 0.7f, Strength);

        public float Distance(float x, float z)
        {
            float dx = x - X, dz = z - Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        public float RainAtDistance(float d)
        {
            if (d >= HaloSupport * Radius) return 0f;
            float core = WeatherMath.Gauss(d, Radius) * Taper(d, CoverSupport * Radius);
            float halo = 0.12f * WeatherMath.Gauss(d, 2.5f * Radius) * Taper(d, HaloSupport * Radius);
            return PeakRain * Strength * (core + halo);
        }

        public float CoverAtDistance(float d)
        {
            if (Strength <= 0f) return 0f;
            return Strength * (1f - WeatherMath.Smoothstep(Radius, 2.2f * Radius, d));
        }

        public float ShapeAtDistance(float d) =>
            d >= CoverSupport * Radius ? 0f : Strength * WeatherMath.Gauss(d, 1.4f * Radius) * Taper(d, CoverSupport * Radius);

        public float CoreAtDistance(float d) =>
            d >= CoverSupport * Radius ? 0f : Strength * WeatherMath.Gauss(d, Radius) * Taper(d, CoverSupport * Radius);

        public float LightningWeightAtDistance(float d)
        {
            if (d >= LightningSupport * Radius) return 0f;
            return (float)Math.Exp(-(d * d) / (9f * Radius * Radius)) * (1f - WeatherMath.Smoothstep(3f * Radius, 6f * Radius, d));
        }

        /// <summary><see cref="WindAt"/> for a caller that holds the point's offset from the centre (dx, dz) and its length d.</summary>
        public void WindAtOffset(float dx, float dz, float d, out float outX, out float outZ, out float vertical, out float turbulence)
        {
            outX = outZ = vertical = turbulence = 0f;
            if (d >= WindSupport * Radius) return;
            float taper = Taper(d, WindSupport * Radius);
            float down = DowndraftLevel, up = UpdraftLevel;

            // A draft level of exactly 0 (a young or a spent cell; the levels are never negative) makes every term it multiplies exactly +0, as the Gaussians are finite:
            // the exponentials of those terms are not taken.
            float rising = up == 0f ? 0f : 6f * up * WeatherMath.Gauss(d, Radius);
            float sinking = down == 0f ? 0f : 8f * down * WeatherMath.Gauss(d, Radius * 0.8f);
            vertical = (rising - sinking) * taper;

            // The gust front spreads out from the core as the downdraft matures.
            // The direction of the outflow is undefined at the centre: fade the gust to nothing there, or crossing the core flips it 12 m/s.
            float gust = 0f;
            if (down != 0f)
            {
                float ring = Radius * (1f + 0.75f * WeatherMath.Smoothstep(0.4f, 1f, Strength));
                gust = (15f * down * WeatherMath.Gauss(d - ring, Radius * 0.6f) + 6f * down * WeatherMath.Gauss(d, Radius * 0.7f)) * taper *
                    WeatherMath.Smoothstep(0f, 0.3f * Radius, d);
            }
            if (d > 1f)
            {
                outX = dx / d * gust;
                outZ = dz / d * gust;
            }

            turbulence = (0.25f * up + 0.55f * down + 0.2f * Strength) * WeatherMath.Gauss(d, Radius * 2f) * taper;
        }

        /// <summary>1 inside 0.7 of the support, easing to exactly 0 at the support.</summary>
        private static float Taper(float d, float support) => 1f - WeatherMath.Smoothstep(0.7f * support, support, d);
    }

    /// <summary>
    /// The storm lattice (spec 6, 6.1): cells on an infinite jittered grid of 16 km cells in the tape frame, 0 to 3 candidates per
    /// cell, radius 3-5.3 km (the Cumulonimbus range), always circular. <see cref="Resolve"/> is the one pure resolver Sample, the
    /// map rasteriser, the CloudBodies mirror and lightning all use. Activation is read at the centre: level = clamp01(Convective)
    /// of the tape row there (CapacityCell 1); the slice-4 marker bias joins it at the marked line.
    /// </summary>
    internal static class StormLattice
    {
        public const float Cell = 16000f;
        /// <summary>5 x 5 cells, 32 km each way: 6 R at R = 5.3 km is 31.8 km.</summary>
        public const int Half = 2;
        /// <summary>A cell has 0 to this many candidates.</summary>
        public const int MaxCandidates = 3;
        public const float RadiusMin = 3000f, RadiusMax = 5300f;
        /// <summary>A cell at or above this strength is active: it shows, it can strike, it is enumerated.</summary>
        public const float ActiveStrength = 0.5f;
        /// <summary>Lightning looks at a 25 x 25 block (192 km guaranteed on every side), the nearest 24 within 180 km.</summary>
        public const int EnumerateHalf = 12, EnumerateCap = 24;
        public const float EnumerateRange = 180000f;

        private const uint SaltMix = 0xBB67AE85u;
        private const int SlotCount = 0, SlotX = 1, SlotZ = 2, SlotRank = 3, SlotRadius = 4, SlotSevere = 5,             SlotHail = 7, SlotDepth = 8, SlotLightning = 9, SlotStrike = 10;

        [ThreadStatic] private static StormCell[] scratch;

        /// <summary>The cumulus lattice's twin of the same name: <see cref="Resolve"/> from the cell's hash prefix and candidate-count draw; with <paramref name="dropWeak"/> a cell of strength 0
        /// returns false before its shape is built, and the tape row is read for only the columns used. <paramref name="cell"/> is written only when the cell is returned.</summary>
        private static bool Candidate(in LatticeSource src, uint prefix, float uCell, int gx, int gz, int k, bool dropWeak, ref StormCell cell)
        {
            if (k >= (int)(uCell * (MaxCandidates + 1))) return false;
            float x = (gx + LatticeHash.UnitIn(prefix, k, SlotX)) * Cell;
            float z = (gz + LatticeHash.UnitIn(prefix, k, SlotZ)) * Cell;
            float s = src.Frame.STape(x, z);
            src.Tape.Locate(s, out int at0, out int at1, out float w);

            float conv = Scalar.Clamp01(src.Tape.Column(SkyColumns.ConvectiveAt, at0, at1, w));
            // Beading (spec 5.3): the line raises the activation of a cell that already stands near it. The bias is read at the cell centre, so a cell's
            // strength stays a constant of the cell in the tape frame; CapacityCell is 1.
            float bias = src.Markers == null || src.Markers.Count == 0 ? 0f : src.Markers.BiasAt(s, src.Frame, x, z);
            float level = Scalar.Clamp01(conv + bias);
            float rank = LatticeHash.UnitIn(prefix, k, SlotRank);
            float strength = WeatherMath.Smoothstep(rank * 0.7f, rank * 0.7f + 0.35f, level);
            if (dropWeak && !(strength > 0f)) return false;
            float severity = Scalar.Clamp01(src.Tape.Column(SkyColumns.SeverityAt, at0, at1, w));
            float severeAt = LatticeHash.UnitIn(prefix, k, SlotSevere);
            float severeBlend = WeatherMath.Smoothstep(severeAt, severeAt + 0.15f, severity);

            cell = default;
            cell.Gx = gx; cell.Gz = gz; cell.K = k;
            cell.Seed = LatticeHash.Hash(src.LayoutSeed, gx, gz, k, 0x5C);
            cell.X = x; cell.Z = z;
            cell.Radius = LatticeHash.RangeIn(prefix, k, SlotRadius, RadiusMin, RadiusMax);
            cell.Rank = rank;
            cell.Level = level;
            cell.Strength = strength;
            cell.Base = Scalar.Clamp(src.Tape.Column(SkyColumns.CloudBaseAt, at0, at1, w), 400f, 2500f);
            cell.Depth = 1200f + 4600f * conv + LatticeHash.RangeIn(prefix, k, SlotDepth, 0f, 800f + 1800f * conv) + 3000f * severeBlend;
            cell.PeakRain = 30f + 90f * severity;
            cell.Severe = severeBlend > 0.5f;
            cell.Hail = cell.Severe && cell.PeakRain >= 75f && LatticeHash.UnitIn(prefix, k, SlotHail) < 0.6f;
            float quiet = conv < 0.55f ? 0f : LatticeHash.RangeIn(prefix, k, SlotLightning, 0.3f, 4f) * (conv - 0.55f) / 0.45f;
            cell.LightningPeak = Scalar.Lerp(quiet, LatticeHash.RangeIn(prefix, k, SlotStrike, 6f, 18f), severeBlend);
            return true;
        }

        /// <summary>The cells of one lattice cell that are out (strength above 0), in candidate order. Returns the count.</summary>
        public static int ResolveCell(in LatticeSource src, int gx, int gz, StormCell[] into)
        {
            if (src.ConstantConvective == 0f) return 0;
            uint prefix = LatticeHash.CellPrefix(src.Salt ^ SaltMix, gx, gz);
            float uCell = LatticeHash.UnitIn(prefix, 0, SlotCount);
            int candidates = (int)(uCell * (MaxCandidates + 1)), count = 0;
            StormCell c = default;
            for (int k = 0; k < candidates; k++)
                if (Candidate(src, prefix, uCell, gx, gz, k, true, ref c)) into[count++] = c;
            return count;
        }

        /// <summary>
        /// The cells that can touch the lattice cell a point is in: a flat list refreshed only when the point crosses into another cell (see
        /// <see cref="CumulusLattice.Neighbourhood"/>). One per field, reset with its cache. Not thread-safe.
        /// </summary>
        public sealed class Neighbourhood
        {
            public readonly StormCell[] Items = new StormCell[(2 * Half + 1) * (2 * Half + 1) * MaxCandidates];
            public int Count;
            private int cellX = int.MinValue, cellZ;

            public void Reset() => cellX = int.MinValue;

            public void Refresh(in LatticeSource src, CellCache<StormCell> cache, float qx, float qz)
            {
                int x = LatticeHash.CellOf(qx, Cell), z = LatticeHash.CellOf(qz, Cell);
                if (x == cellX && z == cellZ) return;
                cellX = x; cellZ = z; Count = 0;
                if (src.ConstantConvective == 0f) return; // a sky without convection: no cell can be out, so skip the 25 cell lookups
                float midX = (x + 0.5f) * Cell, midZ = (z + 0.5f) * Cell, slack = 0.7072f * Cell;
                for (int gz = z - Half; gz <= z + Half; gz++)
                for (int gx = x - Half; gx <= x + Half; gx++)
                {
                    int n = Fetch(src, cache, gx, gz, out StormCell[] items);
                    for (int i = 0; i < n; i++)
                    {
                        float dx = items[i].X - midX, dz = items[i].Z - midZ, reach = items[i].Reach + slack;
                        if (dx * dx + dz * dz <= reach * reach) Items[Count++] = items[i];
                    }
                }
            }
        }

        /// <summary>The cells (strength above 0) of lattice cell (gx, gz), through the cache when there is one.</summary>
        public static int Fetch(in LatticeSource src, CellCache<StormCell> cache, int gx, int gz, out StormCell[] items)
        {
            if (cache != null && cache.TryGet(gx, gz, out items, out int cached)) return cached;
            items = cache != null ? cache.Reserve(gx, gz, MaxCandidates) : scratch ??= new StormCell[MaxCandidates];
            int count = ResolveCell(src, gx, gz, items);
            if (cache != null) cache.Commit(gx, gz, count);
            return count;
        }

        /// <summary>
        /// The active cells within <paramref name="range"/> of a tape-frame point, nearest first (ties by cell id), at most
        /// <paramref name="cap"/> of them, read from the 25 x 25 block around the point. Lightning's per-slot enumeration (6.1): a pure
        /// function of the tape and the point. Returns the count written to <paramref name="into"/>.
        /// </summary>
        public static int Enumerate(in LatticeSource src, float qx, float qz, float range, StormCell[] into, int cap = EnumerateCap)
        {
            cap = Math.Min(cap, into.Length);
            int count = 0;
            float r2 = range * range;
            int cx = LatticeHash.CellOf(qx, Cell), cz = LatticeHash.CellOf(qz, Cell);
            StormCell[] cellCells = scratch ??= new StormCell[MaxCandidates];
            for (int gz = cz - EnumerateHalf; gz <= cz + EnumerateHalf; gz++)
            for (int gx = cx - EnumerateHalf; gx <= cx + EnumerateHalf; gx++)
            {
                int n = ResolveCell(src, gx, gz, cellCells);
                for (int i = 0; i < n; i++)
                {
                    StormCell c = cellCells[i];
                    if (c.Strength < ActiveStrength) continue;
                    float d2 = Dist2(c, qx, qz);
                    if (d2 > r2) continue;
                    int at = count;
                    while (at > 0 && Before(c, d2, into[at - 1], Dist2(into[at - 1], qx, qz))) at--;
                    if (at >= cap) continue;
                    int last = Math.Min(count, cap - 1);
                    for (int j = last; j > at; j--) into[j] = into[j - 1];
                    into[at] = c;
                    if (count < cap) count++;
                }
            }
            return count;
        }

        /// <summary>How many active cells lie within <paramref name="range"/> of a tape-frame point (the activeCellCount readout).</summary>
        public static int CountActive(in LatticeSource src, CellCache<StormCell> cache, float qx, float qz, float range)
        {
            int rings = (int)Math.Ceiling(range / Cell) + 1, total = 0;
            int cx = LatticeHash.CellOf(qx, Cell), cz = LatticeHash.CellOf(qz, Cell);
            float r2 = range * range;
            for (int gz = cz - rings; gz <= cz + rings; gz++)
            for (int gx = cx - rings; gx <= cx + rings; gx++)
            {
                int n = Fetch(src, cache, gx, gz, out StormCell[] items);
                for (int i = 0; i < n; i++)
                    if (items[i].Strength >= ActiveStrength && Dist2(items[i], qx, qz) <= r2) total++;
            }
            return total;
        }

        private static float Dist2(in StormCell c, float qx, float qz)
        {
            float dx = c.X - qx, dz = c.Z - qz;
            return dx * dx + dz * dz;
        }

        private static bool Before(in StormCell a, float d2a, in StormCell b, float d2b)
        {
            if (d2a != d2b) return d2a < d2b;
            if (a.Gx != b.Gx) return a.Gx < b.Gx;
            if (a.Gz != b.Gz) return a.Gz < b.Gz;
            return a.K < b.K;
        }
    }
}
