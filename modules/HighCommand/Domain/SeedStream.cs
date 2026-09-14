namespace BoscaliSummer.Features.HighCommand.Domain
{
    /// <summary>
    /// Deterministic xorshift stream for generated identities. Every draw comes from the
    /// seed alone, so a synced seed reproduces the same name, traits, portrait and bio on
    /// every machine without shipping any text over the wire.
    /// </summary>
    internal sealed class SeedStream
    {
        private uint state;

        public SeedStream(uint seed) => state = seed == 0u ? 0x9E3779B9u : seed;

        public uint Next()
        {
            unchecked
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return state;
            }
        }

        public int Range(int count) => count <= 1 ? 0 : (int)(Next() % (uint)count);

        public T Pick<T>(T[] items) => items[Range(items.Length)];

        public bool Chance(int percent) => Range(100) < percent;
    }
}
