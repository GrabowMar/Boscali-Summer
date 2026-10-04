using System.Collections.Generic;

using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;

namespace BoscaliSummer.Modules.Wing.Runtime
{
    internal sealed partial class WingService
    {
        /// <summary>The previous slot error per member, for closure (bounded by the wing: entries go when the member does).</summary>
        private readonly Dictionary<WingMember, Vector2> stationPrev = new Dictionary<WingMember, Vector2>();

        /// <summary>Station Board's numbers for one member (spec 2026-10-04 §4.1 MemberStation): slot error, closure and phase, in
        /// the snapshot's byte forms. A member not flying formation reads as in slot with no error.</summary>
        internal void Station(WingMember m, bool formation, out byte err10, out sbyte closure, out byte phase)
        {
            err10 = 0;
            closure = 0;
            phase = (byte)StationPhase.InSlot;
            WingFrame f = formation ? FrameOf(m) : null;
            if (f == null || m.Brain.Slot >= f.Count)
            {
                stationPrev.Remove(m);
                return;
            }
            float err = (f.Slots[m.Brain.Slot].Ref.Pos - m.Last.Pos).Length;
            float now = Time.unscaledTime;
            float c = stationPrev.TryGetValue(m, out Vector2 prev) ? StationMath.Closure(prev.x, err, now - prev.y) : 0f;
            stationPrev[m] = new Vector2(err, now);
            err10 = StationMath.QuantiseError(err);
            closure = StationMath.QuantiseClosure(c);
            phase = (byte)StationMath.Phase(m.Brain.LastRejoin.Sigma, m.Brain.LastRejoin.FallingBehind, err);
        }

        /// <summary>Drops closure history for members no longer in the wing (called once per snapshot fill).</summary>
        private void PruneStation()
        {
            if (stationPrev.Count <= Members.Count) return;
            var gone = new List<WingMember>();
            foreach (WingMember k in stationPrev.Keys) if (!Members.Contains(k)) gone.Add(k);
            foreach (WingMember k in gone) stationPrev.Remove(k);
        }
    }
}
