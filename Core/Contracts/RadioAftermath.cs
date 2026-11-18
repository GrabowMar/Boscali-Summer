using System;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Mission-clock radio sequence; snapshots inherit its timing without replaying old warnings.</summary>
    internal static class RadioAftermath
    {
        public static bool Active { get; private set; }
        public static uint Epoch { get; private set; }
        public static uint Site { get; private set; }
        public static int Revision { get; private set; }
        public static int SurvivorCalls { get; private set; }
        public static float StartedAt { get; private set; }
        public static float NextSurvivorAt { get; private set; }
        public const float PanicSeconds = 48f, SilenceSeconds = 600f;
        public static uint Drop { get; private set; }
        public static float DroppedAt { get; private set; }
        public static float SilenceUntil => StartedAt + PanicSeconds + SilenceSeconds;
        public static int EmergencyCalls { get; private set; }
        public static bool Blocking => Active || releaseActive;
        private static bool warning, releaseActive;
        private static int panic;
        private static float nextPanic;

        public static bool Silent(float now) => Active && now >= StartedAt + PanicSeconds && now < SilenceUntil;

        public static void Advance(float now)
        {
            if (releaseActive && !Active && Finite(now) && now - DroppedAt >= 180f)
            { releaseActive = warning = false; Revision++; }
        }

        public static void Release(uint epoch, uint drop, float now, float age)
        {
            if (drop == 0 || !Finite(now) || !Finite(age) || age < 0f) return;
            if (Epoch == epoch && drop <= Drop) return;
            if (Epoch != epoch) Reset();
            Epoch = epoch; Drop = drop; DroppedAt = now - age;
            releaseActive = !Active && age < 180f;
            // Impact snapshots own the blackout; an older release heartbeat cannot reopen its warning.
            warning = age < 2f && (!Active || now >= SilenceUntil);
            Revision++;
        }

        public static bool TryEmergency(float now, bool ready, bool busy, out int stage)
        {
            stage = -1;
            if (!ready || busy || !Finite(now)) return false;
            if (warning)
            {
                warning = false;
                if ((!Active || now >= SilenceUntil) && now - DroppedAt < 12f) { stage = 0; EmergencyCalls++; return true; }
            }
            if (!Active || now - StartedAt >= PanicSeconds || now < nextPanic || panic >= 4) return false;
            stage = ++panic; EmergencyCalls++;
            nextPanic = now + 2f;
            return true;
        }

        public static void Observe(uint epoch, uint site, float now, float age)
        {
            if (site == 0 || !Finite(now) || !Finite(age) || age < 0f) return;
            if (Active && Epoch == epoch && site <= Site) return;
            bool first = !Active || Epoch != epoch;
            if (Epoch != epoch) { Drop = 0; EmergencyCalls = 0; }
            Active = true; Epoch = epoch; Site = site; Revision++;
            StartedAt = now - age;
            warning = releaseActive = false;
            panic = age < 2f ? 0 : 4;
            nextPanic = now;
            if (first) SurvivorCalls = 0;
            NextSurvivorAt = System.Math.Max(SilenceUntil, now + 18f + Unit(site, (uint)Revision) * 12f);
        }

        public static bool TrySurvivor(float now, bool ready, bool busy, out int role)
        {
            role = -1;
            if (!Active || !ready || busy || !Finite(now) || now < SilenceUntil || now < NextSurvivorAt) return false;
            role = SurvivorCalls++ % 5;
            NextSurvivorAt = now + 40f + Unit(Site, (uint)SurvivorCalls + 17u) * 35f;
            return true;
        }

        public static void Reset()
        {
            Active = false; Epoch = Site = Drop = 0; StartedAt = DroppedAt = NextSurvivorAt = nextPanic = 0f;
            SurvivorCalls = EmergencyCalls = panic = 0; warning = releaseActive = false; Revision++;
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
