namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// When the relief map regroups its contact stacks: immediately on a changed
    /// roster or selection, on a beat for driving units, but never churned by a
    /// moving view — pans hold membership and settle shortly after release, with a
    /// stale cap so long drags still refresh. Pure so the timing contract is tested.
    /// </summary>
    internal static class ReliefClusterPlan
    {
        internal const float BeatInterval = .25f;
        internal const float SettleDelay = .35f;
        internal const float MaxStale = 2f;

        internal enum Action { Recluster, Refresh }

        /// <summary>
        /// Picks the declutter action and the next beat. A roster change always
        /// regroups; a moving view only refreshes (badges follow live) unless the
        /// grouping has gone stale; a settled view regroups on the beat.
        /// </summary>
        internal static Action Decide(bool rosterChanged, bool viewChanged,
            float now, float nextBeat, float lastRecluster, out float beat)
        {
            beat = nextBeat;
            if (rosterChanged) return Action.Recluster;
            if (viewChanged)
            {
                if (now - lastRecluster >= MaxStale) return Action.Recluster;
                beat = now + SettleDelay;
                return Action.Refresh;
            }
            if (now >= nextBeat) return Action.Recluster;
            return Action.Refresh;
        }
    }
}
