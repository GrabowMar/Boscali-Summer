namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The measured rate of the aircraft's capacitor, kJ per second (negative: draining), from the 10 Hz samples the board takes: the change since the
    /// newest sample at least <see cref="WindowS"/> old, so one noisy tick does not flip the chevrons and a half second of history is enough to call a trend.
    /// Pure, a fixed ring (no allocation). The game has no rate to read, only the charge: the consumer is not named, only measured.
    /// </summary>
    internal sealed class CapacitorTrend
    {
        public const float WindowS = 0.45f;
        private const int Size = 12;

        private readonly float[] time = new float[Size], kj = new float[Size];
        private int count, head;

        public void Reset() { count = head = 0; }

        /// <summary>Take a sample and return the rate over the window (0 until the window has history).</summary>
        public float Push(float now, float kJ)
        {
            // A clock that went backwards (a new scene, a paused render) starts over.
            if (count > 0 && now < time[(head + Size - 1) % Size]) Reset();
            time[head] = now;
            kj[head] = kJ;
            head = (head + 1) % Size;
            if (count < Size) count++;
            for (int back = 2; back <= count; back++)
            {
                int i = (head + Size - back) % Size;
                float age = now - time[i];
                if (age >= WindowS) return (kJ - kj[i]) / age;
            }
            return 0f;
        }
    }
}
