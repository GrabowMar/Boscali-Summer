namespace BoscaliSummer.Core.Game
{
    /// <summary>
    /// Guards the mission-time reads every deadline depends on: finite, never decreasing, and never advancing more
    /// than <see cref="MaxStep"/> in one frame. A client reads the networked mission time before it has synced
    /// (null, unspawned or garbage); such a read holds the last good value instead of leaking into deadlines.
    /// A valid raw value far below the latched one starts a new timeline instead of freezing. Call <see cref="Reset"/> when the scene ends, because a new mission legitimately restarts at zero.
    /// Shared by Support (<c>MissionNow</c>) and Weather (<c>TryMissionTime</c>) through <see cref="MissionTimeSource"/>.
    /// </summary>
    internal sealed class MissionClock
    {
        public const float MaxStep = 600f, RebaseDrop = 5f;
        private float last, frameBase;
        private int frame = -1;

        public float Last => last;

        public float Read(float raw, bool valid, int frameIndex)
        {
            if (frame != frameIndex) { frame = frameIndex; frameBase = last; }
            if (!valid || float.IsNaN(raw) || float.IsInfinity(raw) || raw < 0f) return last;
            if (raw > last) last = System.Math.Min(raw, frameBase + MaxStep);
            else if (raw < last - RebaseDrop) { last = raw; frameBase = raw; } // a latched transient must never freeze time
            return last;
        }

        public void Reset() { last = 0f; frameBase = 0f; frame = -1; }
    }

    /// <summary>The pure rules of the clock gate (spec 9.5), kept free of game types so they can be tested.</summary>
    internal static class MissionClockGate
    {
        /// <summary>A raw read counts only if there is a clock source, it is finite and at least 0 and, in
        /// multiplayer, the shared start has been set (before that the value is lobby time).</summary>
        public static bool IsValid(bool haveSource, bool multiplayer, double multiplayerStartTime, float raw) =>
            haveSource && !float.IsNaN(raw) && !float.IsInfinity(raw) && raw >= 0f &&
            (!multiplayer || multiplayerStartTime > 0d);

        /// <summary>The guarded clock eases a late start in at <see cref="MissionClock.MaxStep"/> per frame;
        /// until the held value reaches the raw read it is not the mission time yet.</summary>
        public static bool Converged(float held, float raw) => held + 0.001f >= raw;
    }
}
