using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// Everything a lattice resolve reads (spec 6): the salts, the tape that gives the sky at a group's or a cell's centre, and the
    /// frame that places a tape-frame point on that tape. The lattices take no sample position: a group's strength, radius and
    /// shape are constants of the group, read once at its centre.
    /// </summary>
    internal readonly struct LatticeSource
    {
        public LatticeSource(uint salt, uint layoutSeed, TapeSampler tape, TapeFrame frame, MarkerSet markers = null)
        {
            Markers = markers;
            Salt = salt;
            LayoutSeed = layoutSeed;
            Tape = tape;
            Frame = frame;
            // A one-keyframe tape (live play) says at once whether any group or cell can be out: most skies have none.
            SkyParams constant = tape.IsValid && tape.IsConstant ? tape.At(0f) : default;
            ConstantCumulusLevel = tape.IsValid && tape.IsConstant ? constant.CumulusLevel : -1f;
            ConstantConvective = tape.IsValid && tape.IsConstant ? constant.Convective : -1f;
            // A one-keyframe tape has one genus, so no candidate past its N can exist; a tape that varies may carry any genus.
            CandidateBound = tape.IsValid && tape.IsConstant ? SkyArchetypes.Genus(tape.GenusAt(0f)).Candidates : CumulusLattice.MaxCandidates;
        }

        /// <summary><see cref="WeatherKey.TapeId"/>: every candidate is a pure function of it and (gx, gz, k).</summary>
        public readonly uint Salt;
        /// <summary><see cref="WeatherKey.LayoutSeed"/>: the seed of a storm cell's lightning.</summary>
        public readonly uint LayoutSeed;
        public readonly TapeSampler Tape;
        public readonly TapeFrame Frame;
        /// <summary>The tape's front markers: a storm cell's activation level is raised by the beading bias at its centre (spec 5.3). Null for a fixture tape.</summary>
        public readonly MarkerSet Markers;
        /// <summary>The cumulus level and the convective share of a one-keyframe tape; -1 when the tape varies in space.</summary>
        public readonly float ConstantCumulusLevel, ConstantConvective;
        /// <summary>The largest candidates-per-cell N the tape can ask for (the genus own N for a one-keyframe tape, else the largest of all):
        /// the cell walk stops here instead of at 20 (a Humilis cell has 13 at most, a Mediocris one 9).</summary>
        public readonly int CandidateBound;

        public bool IsValid => Tape.IsValid;

        /// <summary>The tape row at a tape-frame point and the low genus of the keyframe it lies in.</summary>
        public SkyParams RowAt(float qx, float qz, out LowGenus genus)
        {
            float s = Frame.STape(qx, qz);
            genus = Tape.GenusAt(s);
            return Tape.At(s);
        }
    }

    internal static class LatticeHash
    {
        /// <summary>
        /// A 32-bit hash of (salt, cell, candidate, slot) with murmur3's word rounds and finaliser. Deterministic.Hash is not used here: its
        /// FNV rounds leave small integer grids badly clumped (on a 120 x 120 block of cells it gives about half as many distinct values as
        /// cells), and two cells with the same hash would grow the same group.
        /// </summary>
        public static uint Hash(uint salt, int gx, int gz, int k, int slot)
        {
            unchecked
            {
                uint h = Round(salt, (uint)gx);
                h = Round(h, (uint)gz);
                h = Round(h, (uint)((k << 8) | slot));
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                return h ^ (h >> 16);
            }
        }

        private static uint Round(uint h, uint word)
        {
            unchecked
            {
                word *= 0xCC9E2D51u;
                word = (word << 15) | (word >> 17);
                word *= 0x1B873593u;
                h ^= word;
                h = (h << 13) | (h >> 19);
                return h * 5u + 0xE6546B64u;
            }
        }

        /// <summary>A uniform [0, 1) value of (salt, cell, candidate, slot): the only randomness a lattice has.</summary>
        public static float Unit(uint salt, int gx, int gz, int k, int slot) => (Hash(salt, gx, gz, k, slot) >> 8) * (1f / 16777216f);

        public static float Range(uint salt, int gx, int gz, int k, int slot, float min, float max) =>
            min + (max - min) * Unit(salt, gx, gz, k, slot);

        /// <summary>The lattice cell a coordinate falls in: floor division, correct for negative coordinates.</summary>
        public static int CellOf(float q, float cell) => (int)Math.Floor(q / cell);
    }

    /// <summary>
    /// A direct-mapped cache of the active candidates of lattice cells, owned by one field and cleared by every Build: a
    /// map or cookie build reads the same 5 x 5 block for a whole row of neighbouring samples. 8192 slots: the far window
    /// alone is 57 x 57 cells, and 1024 slots thrashed (about 20,000 cell resolves for about 3,700 distinct cells). The slot
    /// arrays are allocated on first use, to the size the caller asks for. Not thread-safe; a field is not shared between threads.
    /// </summary>
    internal sealed class CellCache<T> where T : struct
    {
        private struct Entry { public int Gx, Gz, Stamp, Count; public T[] Items; }

        private readonly Entry[] entries;
        private readonly int mask;
        private int stamp = 1;

        public CellCache(int slots = 8192)
        {
            if (slots < 2 || (slots & (slots - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(slots));
            entries = new Entry[slots];
            mask = slots - 1;
        }

        /// <summary>Forgets every cell (the tape or the key changed).</summary>
        public void Reset() => stamp++;

        public bool TryGet(int gx, int gz, out T[] items, out int count)
        {
            ref Entry e = ref entries[Slot(gx, gz)];
            if (e.Stamp == stamp && e.Gx == gx && e.Gz == gz) { items = e.Items; count = e.Count; return true; }
            items = null;
            count = 0;
            return false;
        }

        /// <summary>The array to fill for this cell (at least <paramref name="capacity"/> long, reused); call <see cref="Commit"/> after.</summary>
        public T[] Reserve(int gx, int gz, int capacity)
        {
            ref Entry e = ref entries[Slot(gx, gz)];
            if (e.Items == null || e.Items.Length < capacity) e.Items = new T[capacity];
            return e.Items;
        }

        public void Commit(int gx, int gz, int count)
        {
            ref Entry e = ref entries[Slot(gx, gz)];
            e.Gx = gx;
            e.Gz = gz;
            e.Count = count;
            e.Stamp = stamp;
        }

        private int Slot(int gx, int gz)
        {
            unchecked
            {
                uint h = (uint)gx * 0x9E3779B1u + (uint)gz * 0x85EBCA6Bu;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 13;
                return (int)(h & (uint)mask);
            }
        }
    }
}
