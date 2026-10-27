namespace BoscaliSummer.Modules.CombatAdjust.Domain
{
    /// <summary>
    /// Rolls every peer agrees on: an integer hash of the persistent ids involved (and a salt per rule), so a client draws the same smoke
    /// or decoy the host decided, with no message between them.
    /// </summary>
    internal static class Seeded
    {
        public static float Roll01(uint a, uint b, uint salt) => (Hash(a, b, salt) >> 8) * (1f / 16777216f);

        public static float Range(uint a, uint b, uint salt, float min, float max) => min + (max - min) * Roll01(a, b, salt);

        internal static uint Hash(uint a, uint b, uint c)
        {
            unchecked
            {
                uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u) * 0x85EBCA77u ^ (c + 0x165667B1u) * 0xC2B2AE3Du;
                h ^= h >> 16; h *= 0x85EBCA6Bu;
                h ^= h >> 13; h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return h;
            }
        }
    }
}
