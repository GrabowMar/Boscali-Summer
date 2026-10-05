using System.Collections.Generic;

using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;

namespace BoscaliSummer.Modules.Wing.Runtime
{
    internal sealed partial class WingService
    {
        /// <summary>Closure samplers per member (bounded by the wing: pruned each snapshot fill, cleared on Activate). Sampled on
        /// a fixed cadence because several callers fill snapshots (the net tick and the WMC panel).</summary>
        private readonly Dictionary<WingMember, ClosureSample> stationPrev = new Dictionary<WingMember, ClosureSample>();

        /// <summary>Station Board's numbers for one member (spec 2026-10-04 §4.1 MemberStation): slot error, closure and phase, in
        /// the snapshot's byte forms. A member not flying formation reads as in slot with no error.</summary>
        internal void Station(WingMember m, bool formation, out byte err10, out sbyte closure, out byte phase)
        {
            err10 = 0;
            closure = 0;
            phase = (byte)StationPhase.InSlot;
            WingFrame f = formation ? FrameOf(m) : null;
            if (f == null || m.Brain.Slot < 0 || m.Brain.Slot >= f.Count)
            {
                stationPrev.Remove(m);
                return;
            }
            float err = (f.Slots[m.Brain.Slot].Ref.Pos - m.Last.Pos).Length;
            stationPrev.TryGetValue(m, out ClosureSample sample);
            float c = StationMath.Sample(ref sample, err, Time.unscaledTime);
            stationPrev[m] = sample;
            err10 = StationMath.QuantiseError(err);
            closure = StationMath.QuantiseClosure(c);
            phase = (byte)StationMath.Phase(m.Brain.LastRejoin.Sigma, m.Brain.LastRejoin.FallingBehind, err);
        }

        /// <summary>Drops samplers of members no longer in the wing (called once per snapshot fill).</summary>
        private void PruneStation()
        {
            if (stationPrev.Count <= Members.Count) return;
            var gone = new List<WingMember>();
            foreach (WingMember k in stationPrev.Keys) if (!Members.Contains(k)) gone.Add(k);
            foreach (WingMember k in gone) stationPrev.Remove(k);
        }

        /// <summary>A new wing: no closure history, no old mission's acks.</summary>
        private void ResetStation()
        {
            stationPrev.Clear();
            Presentation.WingAcks.Feed.Clear();
        }
    }
}
