namespace BoscaliSummer.Features.Squad.Domain
{
    /// <summary>
    /// Content revision of one squad snapshot. The host stamps every full reply with it and the
    /// client echoes the last one it applied, so an unchanged board costs a header instead of a
    /// kilobyte. Only the host computes it, so it needs no cross-process stability. Zero is
    /// reserved for "nothing applied yet".
    /// </summary>
    internal struct SnapshotRevision
    {
        private const uint Offset = 2166136261u;
        private const uint Prime = 16777619u;

        // FNV-1a state xor its offset basis, so default(SnapshotRevision) is a valid start.
        private uint state;

        public void Add(bool value) => Mix(value ? 1u : 0u);

        public void Add(int value) => Mix(unchecked((uint)value));

        public void Add(uint value) => Mix(value);

        /// <summary>Null and empty mix identically: both travel as an empty string.</summary>
        public void Add(string value)
        {
            int length = value?.Length ?? 0;
            Mix((uint)length);
            for (int i = 0; i < length; i++) Mix(value[i]);
        }

        public uint Value
        {
            get
            {
                unchecked
                {
                    uint h = state ^ Offset;
                    h ^= h >> 16; h *= 0x7feb352du;
                    h ^= h >> 15; h *= 0x846ca68bu;
                    h ^= h >> 16;
                    return h == 0u ? 1u : h;
                }
            }
        }

        private void Mix(uint value)
        {
            unchecked { state = (((state ^ Offset) ^ value) * Prime) ^ Offset; }
        }
    }
}
