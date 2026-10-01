namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>
    /// Satellite tasking windows (§6.4, D1/D12). The schedule is derived, never stored: every
    /// peer projects the same open/close cycle from the faction key and the host's durations,
    /// so windows need no wire and no state. The host auto-schedule gates the heaviest fires
    /// and the sweeps; Tier-1 offboard fires never consult it.
    /// </summary>
    internal static class WindowMath
    {
        /// <summary>Deterministic per-faction cycle offset in [0, cycle): FNV-1a, stable everywhere.</summary>
        public static double Stagger(string factionKey, float cycleSeconds)
        {
            if (string.IsNullOrEmpty(factionKey) || cycleSeconds <= 0f || !float.IsFinite(cycleSeconds))
                return 0.0;
            uint hash = 2166136261u;
            for (int i = 0; i < factionKey.Length; i++)
            {
                hash ^= factionKey[i];
                hash *= 16777619u;
            }
            return (hash % 1000) / 1000.0 * cycleSeconds;
        }

        /// <summary>Whether the window is open at now, and seconds until it changes. Rims open.</summary>
        public static bool Open(double anchorSeconds, double nowSeconds, float openSeconds, float closedSeconds,
            out float changeIn)
        {
            if (!float.IsFinite(openSeconds) || !float.IsFinite(closedSeconds)) { changeIn = 0f; return true; }
            if (openSeconds < 0f) openSeconds = 0f;
            if (closedSeconds < 0f) closedSeconds = 0f;
            float cycle = openSeconds + closedSeconds;
            if (cycle <= 0f) { changeIn = 0f; return true; }
            double elapsed = nowSeconds - anchorSeconds;
            if (elapsed < 0.0) { changeIn = (float)-elapsed; return false; }
            double phase = elapsed % cycle;
            if (phase < openSeconds) { changeIn = (float)(openSeconds - phase); return true; }
            changeIn = (float)(cycle - phase);
            return false;
        }
    }
}
