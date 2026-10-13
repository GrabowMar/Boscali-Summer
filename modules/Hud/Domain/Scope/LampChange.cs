namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The lamp strip's "just changed" rule (HUD v8), pure: a lamp is lit when its own state says so, and ALSO for
    /// <see cref="Seconds"/> after its state changed in either direction, so a switch the pilot flipped (or the game flipped for them)
    /// is seen once even if it ends up dark. The first reading after construction or <see cref="Reset"/> is a baseline, not a change.
    /// </summary>
    internal sealed class LampChange
    {
        public const float Seconds = 2.5f;
        private const int Slots = 16;

        private readonly LampState[] last = new LampState[Slots];
        private readonly float[] changedAt = new float[Slots];
        private readonly bool[] seen = new bool[Slots];

        /// <summary>Feed lamp <paramref name="index"/>'s state this tick; true while it changed within <see cref="Seconds"/>.</summary>
        public bool Recent(int index, LampState state, float now)
        {
            if ((uint)index >= Slots) return false;
            if (!seen[index]) { seen[index] = true; last[index] = state; changedAt[index] = float.NegativeInfinity; return false; }
            if (last[index] != state) { last[index] = state; changedAt[index] = now; }
            return now - changedAt[index] < Seconds;
        }

        /// <summary>A lamp that is absent from the airframe forgets its state, so it is a baseline again when it comes back.</summary>
        public void Forget(int index)
        {
            if ((uint)index < Slots) seen[index] = false;
        }

        public void Reset() => System.Array.Clear(seen, 0, Slots);
    }
}
