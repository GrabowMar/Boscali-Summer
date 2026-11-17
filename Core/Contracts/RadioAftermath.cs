using System;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Client-local radio aftermath of an accepted SA-74N site. The fallout owner resets it with the mission.</summary>
    internal static class RadioAftermath
    {
        public static bool Active { get; private set; }
        public static uint Epoch { get; private set; }
        public static uint Site { get; private set; }
        public static int Revision { get; private set; }
        public static int SurvivorCalls { get; private set; }
        public static float StartedAt { get; private set; }
        public static float NextSurvivorAt { get; private set; }

        public static void Observe(uint epoch, uint site, float now, float age)
        {
            if (site == 0 || !Finite(now) || !Finite(age) || age < 0f) return;
            if (Active && Epoch == epoch && site <= Site) return;
            bool first = !Active || Epoch != epoch;
            Active = true; Epoch = epoch; Site = site; Revision++;
            StartedAt = now - age;
            if (first) SurvivorCalls = 0;
            // Late joiners inherit the blackout, without replaying an old transmission immediately.
            NextSurvivorAt = now + 18f + Unit(site, (uint)Revision) * 12f;
        }

        public static bool TrySurvivor(float now, bool ready, bool busy, out int role)
        {
            role = -1;
            if (!Active || !ready || busy || !Finite(now) || now < NextSurvivorAt) return false;
            role = SurvivorCalls++ % 5;
            NextSurvivorAt = now + 40f + Unit(Site, (uint)SurvivorCalls + 17u) * 35f;
            return true;
        }

        public static void Reset()
        {
            Active = false; Epoch = Site = 0; StartedAt = NextSurvivorAt = 0f; SurvivorCalls = 0; Revision++;
        }

        private static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
        private static float Unit(uint a, uint b)
        {
            uint h = unchecked((a ^ (b * 2654435761u)) * 16777619u);
            h ^= h >> 16;
            return (h & 0x00ffffffu) / 16777216f;
        }
    }
}
