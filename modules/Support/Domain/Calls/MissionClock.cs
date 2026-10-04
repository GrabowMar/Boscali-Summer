using System;

namespace BoscaliSummer.Modules.Support.Domain.Calls
{
    /// <summary>
    /// Guards the mission-time reads every deadline depends on: finite, never decreasing, and never advancing more
    /// than <see cref="MaxStep"/> in one frame. A client reads the networked mission time before it has synced
    /// (null, unspawned or garbage); such a read holds the last good value instead of leaking into deadlines.
    /// Call <see cref="Reset"/> when the scene ends, because a new mission legitimately restarts at zero.
    /// </summary>
    internal sealed class MissionClock
    {
        public const float MaxStep = 600f;
        private float last, frameBase;
        private int frame = -1;

        public float Last => last;

        public float Read(float raw, bool valid, int frameIndex)
        {
            if (frame != frameIndex) { frame = frameIndex; frameBase = last; }
            if (valid && !float.IsNaN(raw) && !float.IsInfinity(raw) && raw > last)
                last = Math.Min(raw, frameBase + MaxStep);
            return last;
        }

        public void Reset() { last = 0f; frameBase = 0f; frame = -1; }
    }
}
