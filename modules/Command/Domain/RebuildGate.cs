namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// Leading and trailing edge throttle for a rebuild. The caller polls it every frame with the raw revision of whatever it
    /// mirrors; it answers true when a build is due. A change fires at once when the interval since the last build has elapsed,
    /// otherwise it is held and fires once when the interval ends; an unchanged revision never fires.
    /// </summary>
    internal sealed class RebuildGate
    {
        private readonly float interval;
        private bool built;
        private int lastRevision;
        private float lastAt;

        public RebuildGate(float minIntervalSeconds) { interval = minIntervalSeconds > 0f ? minIntervalSeconds : 0f; }

        /// <summary>True on a changed revision when the interval has elapsed; a change held back fires once when the interval ends; no change never fires.</summary>
        public bool ShouldRebuild(int rawRevision, float now)
        {
            if (float.IsNaN(now)) return false;
            if (built)
            {
                if (rawRevision == lastRevision) return false;
                // A clock that went backwards (a restarted scene) counts as elapsed: it must not wait for the old time to come round.
                if (now >= lastAt && now - lastAt < interval) return false;
            }
            built = true; lastRevision = rawRevision; lastAt = now;
            return true;
        }

        public void Reset() { built = false; }
    }
}
