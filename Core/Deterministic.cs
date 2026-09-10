using System;

namespace BoscaliSummer.Core
{
    internal static class Deterministic
    {
        public static uint Hash(int a, int b, int c, int d = 0)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = Mix(h, (uint)a);
                h = Mix(h, (uint)b);
                h = Mix(h, (uint)c);
                h = Mix(h, (uint)d);
                h ^= h >> 16;
                h *= 0x7feb352du;
                h ^= h >> 15;
                h *= 0x846ca68bu;
                return h ^ (h >> 16);
            }
        }

        public static uint HashString(string value)
        {
            unchecked
            {
                uint h = 2166136261u;
                if (value != null)
                {
                    for (int i = 0; i < value.Length; i++) h = Mix(h, value[i]);
                }
                return h;
            }
        }

        public static float UnitFloat(uint hash) => (hash & 0x00ffffffu) / 16777216f;

        public static long CellKey(float x, float z, float cellSize)
        {
            int cx = (int)Math.Floor(x / cellSize);
            int cz = (int)Math.Floor(z / cellSize);
            return ((long)cx << 32) ^ (uint)cz;
        }

        private static uint Mix(uint h, uint value) => (h ^ value) * 16777619u;
    }
}
