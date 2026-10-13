namespace BoscaliSummer.Core.Lifecycle
{
    /// <summary>
    /// A fixed-period gate with its own phase, so periodic services with the same period fire on different frames
    /// instead of piling their ticks into one hitch. The phase is a constant on the clock the caller reads (mission
    /// time, level time, unscaled time; it only has to be finite): the gate fires when that clock passes
    /// <c>offset + k * period</c>, so two services that read the same clock with different offsets never share a frame
    /// whenever they started, and no randomness is involved. Owners call <see cref="Reset"/> wherever they used to zero
    /// their deadline (scene reset).
    /// </summary>
    internal struct PhasedGate
    {
        private float next;
        private bool armed;

        /// <summary>
        /// True once per <paramref name="period"/>. The first call arms the gate on the next slot of the lattice
        /// <c>offset + k * align</c> (<paramref name="align"/> defaults to the period), so the first firing is less than
        /// <paramref name="align"/> seconds late; give a 1 s alignment to a 2 s or 5 s gate whose first tick should not
        /// wait a whole period (the period must then be a multiple of it). A clock that jumps back by more than a
        /// period, or a stall past a whole period, re-arms on the lattice instead of freezing or firing twice.
        /// </summary>
        public bool Due(float now, float period, float offset, float align = 0f)
        {
            float step = align > 0f ? align : period;
            if (!armed || next > now + period) { armed = true; next = NextSlot(now, step, offset); }
            if (now < next) return false;
            next += period;
            if (next <= now) next = NextSlot(now, step, offset);
            return true;
        }

        public void Reset() { armed = false; next = 0f; }

        private static float NextSlot(float now, float step, float offset) =>
            offset + (float)System.Math.Ceiling((now - offset) / step) * step;
    }
}
