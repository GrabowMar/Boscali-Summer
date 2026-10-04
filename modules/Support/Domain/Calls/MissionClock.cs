using System;

namespace BoscaliSummer.Modules.Support.Domain.Calls
{
    /// <summary>
    /// Guards the mission-time reads every deadline depends on: finite, never decreasing, and never advancing more
    /// than <see cref="MaxStep"/> in one frame. A client reads the networked mission time before it has synced
    /// (null, unspawned or garbage); such a read holds the last good value instead of leaking into deadlines.
    /// A valid raw value far below the latched one starts a new timeline instead of freezing. Call <see cref="Reset"/> when the scene ends, because a new mission legitimately restarts at zero.
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
            if (raw > last) last = Math.Min(raw, frameBase + MaxStep);
            else if (raw < last - RebaseDrop) { last = raw; frameBase = raw; } // a latched transient must never freeze time
            return last;
        }

        public void Reset() { last = 0f; frameBase = 0f; frame = -1; }
    }
}
