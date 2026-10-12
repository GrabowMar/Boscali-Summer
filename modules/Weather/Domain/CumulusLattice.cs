using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// One cumulus group of the lattice (spec 6): a rosette of 3-7 round blobs stretched along an axis. Every number is a pure
    /// function of (tape identity, gx, gz, k) and of the tape row at the group's centre, so <see cref="Strength"/> is a constant of
    /// the group and nothing about it changes in time at a fixed tape position.
    /// </summary>
    internal struct CumulusGroup
    {
        public int Gx, Gz, K;
        public LowGenus Genus;
        /// <summary>Centre in the tape frame, metres.</summary>
        public float X, Z;
        /// <summary>Group radius R and the stretch along the axis (1 = round).</summary>
        public float Radius, Aspect;
        public float AxisX, AxisZ;
        public float Rank, Level, Strength;
        /// <summary>How far above the local cloud base the tallest puff of this group reaches at full cover, metres.</summary>
        public float TopDepth;
        /// <summary>E: the extent of the template, max over blobs of (|offset| + radius), times the aspect. Cover is zero beyond it.</summary>
        public float Reach;
        public int BlobCount;
        public float B0X, B0Z, B0R, B1X, B1Z, B1R, B2X, B2Z, B2R, B3X, B3Z, B3R, B4X, B4Z, B4R, B5X, B5Z, B5R, B6X, B6Z, B6R;

        /// <summary>Cover 0..1 at a tape-frame point: the strongest blob, (1 - d^2/r^2)^2 inside its radius, times the strength.
        /// The aspect stretches the group along its axis. Smooth to zero at the edge of every blob, so no cull ever cuts it.</summary>
        public float CoverAt(float qx, float qz)
        {
            if (Strength <= 0f) return 0f;
            float dx = qx - X, dz = qz - Z;
            if (dx * dx + dz * dz >= Reach * Reach) return 0f;
            float a = (dx * AxisX + dz * AxisZ) / Aspect, c = -dx * AxisZ + dz * AxisX;
            float m = BlobCover(a - B0X, c - B0Z, B0R);
            m = Math.Max(m, BlobCover(a - B1X, c - B1Z, B1R));
            m = Math.Max(m, BlobCover(a - B2X, c - B2Z, B2R));
            if (BlobCount > 3)
            {
                m = Math.Max(m, BlobCover(a - B3X, c - B3Z, B3R));
                if (BlobCount > 4)
                {
                    m = Math.Max(m, BlobCover(a - B4X, c - B4Z, B4R));
                    if (BlobCount > 5)
                    {
                        m = Math.Max(m, BlobCover(a - B5X, c - B5Z, B5R));
                        if (BlobCount > 6) m = Math.Max(m, BlobCover(a - B6X, c - B6Z, B6R));
                    }
                }
            }
            return m * Strength;
        }

        /// <summary>The blob offsets and radii of the template in group units (before the aspect), for tests.</summary>
        public void Blob(int index, out float ox, out float oz, out float r)
        {
            switch (index)
            {
                case 0: ox = B0X; oz = B0Z; r = B0R; break;
                case 1: ox = B1X; oz = B1Z; r = B1R; break;
                case 2: ox = B2X; oz = B2Z; r = B2R; break;
                case 3: ox = B3X; oz = B3Z; r = B3R; break;
                case 4: ox = B4X; oz = B4Z; r = B4R; break;
                case 5: ox = B5X; oz = B5Z; r = B5R; break;
                default: ox = B6X; oz = B6Z; r = B6R; break;
            }
        }

        /// <summary>The tape-frame position of a blob's centre (the blob offset is in group units: the along part is stretched by the aspect).</summary>
        public void BlobCentre(int index, out float x, out float z)
        {
            Blob(index, out float ox, out float oz, out _);
            float along = ox * Aspect;
            x = X + along * AxisX - oz * AxisZ;
            z = Z + along * AxisZ + oz * AxisX;
        }

        internal void SetBlob(int index, float ox, float oz, float r)
        {
            switch (index)
            {
                case 0: B0X = ox; B0Z = oz; B0R = r; break;
                case 1: B1X = ox; B1Z = oz; B1R = r; break;
                case 2: B2X = ox; B2Z = oz; B2R = r; break;
                case 3: B3X = ox; B3Z = oz; B3R = r; break;
                case 4: B4X = ox; B4Z = oz; B4R = r; break;
                case 5: B5X = ox; B5Z = oz; B5R = r; break;
                default: B6X = ox; B6Z = oz; B6R = r; break;
            }
        }

        private static float BlobCover(float x, float z, float radius)
        {
            float q = 1f - (x * x + z * z) / (radius * radius);
            return q > 0f ? q * q : 0f;
        }
    }

    /// <summary>
    /// The cumulus lattice (spec 6): groups on an infinite jittered grid of 12 km cells in the tape frame, 0 to N candidates per
    /// cell (N by genus, 4.2), each placed inside its own cell. Reach: E &lt;= 1.25 * R * aspect &lt;= <see cref="Half"/> cells, so a
    /// 5 x 5 block around a point sees every group that can touch it.
    /// </summary>
    internal static class CumulusLattice
    {
        public const float Cell = 12000f;
        /// <summary>Neighbourhood half-width h: 2 means 5 x 5 cells, 24 km each way.</summary>
        public const int Half = 2;
        /// <summary>The largest candidates-per-cell constant of any genus (Cumulonimbus 20).</summary>
        public const int MaxCandidates = 20;
        /// <summary>A row with at least this much Streets makes street zones: aspect 3-6 groups, radius capped so the extent stays inside the reach.</summary>
        public const float StreetThreshold = 0.3f;
        public const float StreetAspectMin = 3f, StreetAspectMax = 6f;
        /// <summary>R * aspect of a street group is at most this: 1.25 * 19.2 km = 24 km = <see cref="Half"/> * <see cref="Cell"/>.</summary>
        public const float StreetReach = 19200f;
        /// <summary>A group at or above this strength is out: it shows, the bench can find it, the readout counts it.</summary>
        public const float ActiveStrength = 0.5f;

        private const uint SaltMix = 0x6A09E667u;
        private const int SlotCount = 0, SlotX = 1, SlotZ = 2, SlotRank = 3, SlotRadius = 4, SlotAspect = 5, SlotAxis = 6,
            SlotBlobs = 7, SlotTop = 8, SlotBlob = 16;

        [ThreadStatic] private static CumulusGroup[] scratch;

        /// <summary>
        /// The one pure resolver: candidate k of cell (gx, gz), or false when the cell has no such candidate. A candidate exists when
        /// k &lt; floor(u_cell * (N + 1)), N the genus constant at the candidate's own centre, so the count of a cell is uniform on
        /// 0..N. The group is returned even at strength 0; <see cref="Strength"/> is read at the centre, never at a sample point.
        /// </summary>
        public static bool Resolve(in LatticeSource src, int gx, int gz, int k, out CumulusGroup g)
        {
            g = default;
            uint salt = src.Salt ^ SaltMix;
            float uCell = LatticeHash.Unit(salt, gx, gz, 0, SlotCount);
            if (k >= (int)(uCell * (MaxCandidates + 1))) return false;
            float x = (gx + LatticeHash.Unit(salt, gx, gz, k, SlotX)) * Cell;
            float z = (gz + LatticeHash.Unit(salt, gx, gz, k, SlotZ)) * Cell;
            SkyParams row = src.RowAt(x, z, out LowGenus genus);
            GenusRow table = SkyArchetypes.Genus(genus);
            if (k >= (int)(uCell * (table.Candidates + 1))) return false;

            float rank = LatticeHash.Unit(salt, gx, gz, k, SlotRank);
            bool street = row.Streets >= StreetThreshold;
            float aspect = street
                ? LatticeHash.Range(salt, gx, gz, k, SlotAspect, StreetAspectMin, StreetAspectMax)
                : LatticeHash.Range(salt, gx, gz, k, SlotAspect, 1f, Math.Max(1f, table.AspectMax));
            float rMin = table.RadiusMin * 1000f, rMax = table.RadiusMax * 1000f;
            if (street) rMax = Math.Max(rMin, Math.Min(rMax, StreetReach / aspect));
            float radius = LatticeHash.Range(salt, gx, gz, k, SlotRadius, rMin, rMax);
            float spread = street ? 0.14f : 0.28f;
            float turn = LatticeHash.Range(salt, gx, gz, k, SlotAxis, -spread, spread);
            float cos = (float)Math.Cos(turn), sin = (float)Math.Sin(turn);

            g.Gx = gx; g.Gz = gz; g.K = k;
            g.Genus = genus;
            g.X = x; g.Z = z;
            g.Radius = radius; g.Aspect = aspect;
            g.AxisX = src.Frame.UX * cos - src.Frame.UZ * sin;
            g.AxisZ = src.Frame.UX * sin + src.Frame.UZ * cos;
            g.Rank = rank;
            g.Level = row.CumulusLevel;
            g.Strength = WeatherMath.Smoothstep(rank * 0.7f, rank * 0.7f + 0.35f, g.Level);
            g.TopDepth = LatticeHash.Range(salt, gx, gz, k, SlotTop, 0.65f, 1.10f) * Math.Max(0f, row.PuffDepth);
            g.BlobCount = 3 + Math.Min(4, (int)(LatticeHash.Unit(salt, gx, gz, k, SlotBlobs) * 5f));
            float reach = 0f;
            for (int j = 0; j < g.BlobCount; j++)
            {
                float angle = LatticeHash.Unit(salt, gx, gz, k, SlotBlob + 4 * j) * 2f * (float)Math.PI;
                float dist = radius * LatticeHash.Range(salt, gx, gz, k, SlotBlob + 4 * j + 1, 0f, 0.5f);
                float r = radius * LatticeHash.Range(salt, gx, gz, k, SlotBlob + 4 * j + 2, 0.5f, 0.75f);
                g.SetBlob(j, dist * (float)Math.Cos(angle), dist * (float)Math.Sin(angle), r);
                reach = Math.Max(reach, dist + r);
            }
            g.Reach = reach * aspect;
            return true;
        }

        /// <summary>The groups of one cell that are out (strength above 0), in candidate order. Returns the count.</summary>
        public static int ResolveCell(in LatticeSource src, int gx, int gz, CumulusGroup[] into)
        {
            if (src.ConstantCumulusLevel == 0f) return 0;
            float uCell = LatticeHash.Unit(src.Salt ^ SaltMix, gx, gz, 0, SlotCount);
            // Resolve refuses k >= floor(u * (N + 1)) with N at most CandidateBound, so no candidate past the bound can exist.
            int candidates = Math.Min((int)(uCell * (MaxCandidates + 1)), src.CandidateBound), count = 0;
            for (int k = 0; k < candidates; k++)
                if (Resolve(src, gx, gz, k, out CumulusGroup g) && g.Strength > 0f) into[count++] = g;
            return count;
        }

        /// <summary>The groups (strength above 0) of lattice cell (gx, gz), through the cache when there is one.</summary>
        public static int Fetch(in LatticeSource src, CellCache<CumulusGroup> cache, int gx, int gz, out CumulusGroup[] items)
        {
            if (cache != null && cache.TryGet(gx, gz, out items, out int cached)) return cached;
            items = cache != null ? cache.Reserve(gx, gz, Math.Max(1, Math.Min(MaxCandidates, src.CandidateBound))) : scratch ?? (scratch = new CumulusGroup[MaxCandidates]);
            int count = ResolveCell(src, gx, gz, items);
            if (cache != null) cache.Commit(gx, gz, count);
            return count;
        }

        /// <summary>
        /// The cover of the lattice at a tape-frame point, read from the (2 * half + 1)^2 cells around it. <paramref name="clear"/> is
        /// the product of (1 - cover) over the groups (groups combine as 1 - (1 - c1)(1 - c2)...), <paramref name="shape"/> the
        /// strongest single group and <paramref name="crownDepth"/> the tallest crown above the local base. A null cache resolves
        /// every cell afresh (tests); the field passes its own.
        /// </summary>
        public static void Sample(in LatticeSource src, CellCache<CumulusGroup> cache, float qx, float qz, int half,
            out float clear, out float shape, out float crownDepth)
        {
            clear = 1f; shape = 0f; crownDepth = 0f;
            int cx = LatticeHash.CellOf(qx, Cell), cz = LatticeHash.CellOf(qz, Cell);
            for (int gz = cz - half; gz <= cz + half; gz++)
            for (int gx = cx - half; gx <= cx + half; gx++)
            {
                int count = Fetch(src, cache, gx, gz, out CumulusGroup[] items);
                for (int i = 0; i < count; i++)
                {
                    float c = items[i].CoverAt(qx, qz);
                    if (c <= 0f) continue;
                    clear *= 1f - c;
                    if (c > shape) shape = c;
                    float crown = items[i].TopDepth * (float)Math.Pow(c, 0.65);
                    if (crown > crownDepth) crownDepth = crown;
                }
            }
        }

        /// <summary>How many groups at or above <see cref="ActiveStrength"/> have their centre within a range of a tape-frame point.</summary>
        public static int CountActive(in LatticeSource src, CellCache<CumulusGroup> cache, float qx, float qz, float range)
        {
            int rings = (int)Math.Ceiling(range / Cell) + 1, total = 0;
            int cx = LatticeHash.CellOf(qx, Cell), cz = LatticeHash.CellOf(qz, Cell);
            float r2 = range * range;
            for (int gz = cz - rings; gz <= cz + rings; gz++)
            for (int gx = cx - rings; gx <= cx + rings; gx++)
            {
                int n = Fetch(src, cache, gx, gz, out CumulusGroup[] items);
                for (int i = 0; i < n; i++)
                {
                    float dx = items[i].X - qx, dz = items[i].Z - qz;
                    if (items[i].Strength >= ActiveStrength && dx * dx + dz * dz <= r2) total++;
                }
            }
            return total;
        }

        /// <summary>
        /// The groups that can touch the lattice cell a point is in: a flat list refreshed only when the point crosses into another cell, so a
        /// map row (hundreds of samples inside one 12 km cell) loops over a few dozen groups instead of hashing 25 cells per sample. One per
        /// field, reset with its cache. Not thread-safe.
        /// </summary>
        public sealed class Neighbourhood
        {
            /// <summary>The groups in the order a row-major scan of the 5 x 5 block meets them (so the products below do not depend on the list).</summary>
            public readonly CumulusGroup[] Items = new CumulusGroup[(2 * Half + 1) * (2 * Half + 1) * MaxCandidates];
            public int Count;
            private int cellX = int.MinValue, cellZ;

            public void Reset() => cellX = int.MinValue;

            public void Refresh(in LatticeSource src, CellCache<CumulusGroup> cache, float qx, float qz)
            {
                int x = LatticeHash.CellOf(qx, Cell), z = LatticeHash.CellOf(qz, Cell);
                if (x == cellX && z == cellZ) return;
                cellX = x; cellZ = z; Count = 0;
                if (src.ConstantCumulusLevel == 0f) return; // a sky without cumulus: no group can be out, so skip the 25 cell lookups
                float midX = (x + 0.5f) * Cell, midZ = (z + 0.5f) * Cell, slack = 0.7072f * Cell;
                for (int gz = z - Half; gz <= z + Half; gz++)
                for (int gx = x - Half; gx <= x + Half; gx++)
                {
                    int n = Fetch(src, cache, gx, gz, out CumulusGroup[] items);
                    for (int i = 0; i < n; i++)
                    {
                        float dx = items[i].X - midX, dz = items[i].Z - midZ, reach = items[i].Reach + slack;
                        if (dx * dx + dz * dz <= reach * reach) Items[Count++] = items[i];
                    }
                }
            }
        }

        /// <summary>The same as the cache-based <see cref="Sample(in LatticeSource, CellCache{CumulusGroup}, float, float, int, out float, out float, out float)"/> (to the bit),
        /// reading the groups from a refreshed <see cref="Neighbourhood"/>: the field's path.</summary>
        public static void Sample(in LatticeSource src, CellCache<CumulusGroup> cache, Neighbourhood near, float qx, float qz,
            out float clear, out float shape, out float crownDepth)
        {
            clear = 1f; shape = 0f; crownDepth = 0f;
            near.Refresh(src, cache, qx, qz);
            for (int i = 0; i < near.Count; i++)
            {
                float c = near.Items[i].CoverAt(qx, qz);
                if (c <= 0f) continue;
                clear *= 1f - c;
                if (c > shape) shape = c;
                float crown = near.Items[i].TopDepth * (float)Math.Pow(c, 0.65);
                if (crown > crownDepth) crownDepth = crown;
            }
        }

        /// <summary>The out group (strength at least <paramref name="minStrength"/>) whose centre is nearest a tape-frame point, searching
        /// <paramref name="rings"/> cells each way. False when there is none.</summary>
        public static bool FindNearest(in LatticeSource src, CellCache<CumulusGroup> cache, float qx, float qz, int rings,
            float minStrength, out CumulusGroup best)
        {
            best = default;
            float bestD2 = float.MaxValue;
            int cx = LatticeHash.CellOf(qx, Cell), cz = LatticeHash.CellOf(qz, Cell);
            for (int gz = cz - rings; gz <= cz + rings; gz++)
            for (int gx = cx - rings; gx <= cx + rings; gx++)
            {
                int count = Fetch(src, cache, gx, gz, out CumulusGroup[] items);
                for (int i = 0; i < count; i++)
                {
                    if (items[i].Strength < minStrength) continue;
                    float dx = items[i].X - qx, dz = items[i].Z - qz, d2 = dx * dx + dz * dz;
                    if (d2 < bestD2) { bestD2 = d2; best = items[i]; }
                }
            }
            return bestD2 < float.MaxValue;
        }
    }
}
