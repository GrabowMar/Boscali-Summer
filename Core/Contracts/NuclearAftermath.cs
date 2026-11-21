namespace BoscaliSummer.Core.Contracts
{
    /// <summary>True from the first SA-74N burst until the scene resets, on every peer and module.
    /// Ordinary storm lightning is a weather phenomenon; after a strategic burst the mission's flashes
    /// must stop, so Weather and the native Lightning patch read this instead of a module reference.</summary>
    internal static class NuclearAftermath
    {
        public static bool Active { get; private set; }
        public static uint Epoch { get; private set; }
        public static uint Site { get; private set; }
        public static float X { get; private set; }
        public static float Y { get; private set; }
        public static float Z { get; private set; }
        public static float StartedAt { get; private set; }
        public static int Revision { get; private set; }

        public static float Age(float now) => Active && Finite(now) ? now - StartedAt : 0f;

        public static void Observe(uint epoch, uint site, float x, float y, float z, float now, float age)
        {
            if (site == 0 || !Finite(now) || !Finite(age) || age < 0f || !Finite(x) || !Finite(y) || !Finite(z)) return;
            if (Epoch != epoch) ResetState();
            if (Active && site <= Site) return;
            Active = true; Epoch = epoch; Site = site;
            X = x; Y = y; Z = z; StartedAt = now - age; Revision++;
        }

        public static void Reset()
        {
            ResetState();
            Revision++;
        }

        private static void ResetState()
        {
            Active = false; Epoch = 0; Site = 0; X = Y = Z = 0f; StartedAt = 0f;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
