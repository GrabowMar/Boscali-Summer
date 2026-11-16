using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Domain.Fronts
{
    /// <summary>The client's copy of its own faction's front state (see <see cref="FactionMirror{T}"/>). Times are moved onto the client's mission clock when a snapshot is applied.</summary>
    internal sealed class FrontMirror : FactionMirror<FrontStateData>
    {
        /// <summary>The source clock of the accepted snapshot, for event identity independent of network delay.</summary>
        public float HostNow { get; private set; }

        protected override bool Bounds(FrontStateData d)
        {
            for (int i = 0; i < d.Fronts.Length; i++)
                if (d.Fronts[i].Queue.Count > FrontWire.MaxQueue || d.Fronts[i].Log.Count > FrontWire.MaxLog) return false;
            return true;
        }

        protected override FrontStateData Shift(FrontStateData d, float offset, float clientNow)
        {
            HostNow = d.Now;
            FrontStateData copy = d.Clone();
            copy.Now = clientNow;
            if (copy.PriorityLockUntil > 0f) copy.PriorityLockUntil += offset;
            for (int i = 0; i < copy.Fronts.Length; i++)
            {
                FrontRow f = copy.Fronts[i];
                if (f.DirectiveLockUntil > 0f) f.DirectiveLockUntil += offset;
                for (int l = 0; l < f.Log.Count; l++) { FrontLogRow r = f.Log[l]; r.Time += offset; f.Log[l] = r; }
            }
            return copy;
        }

        /// <summary>Perk POWER from the mirrored superiority; 1 until the first snapshot.</summary>
        public float Power(Front front) => Known ? FrontSuperiority.Power(State.Fronts[(int)front].Superiority) : 1f;
    }
}
