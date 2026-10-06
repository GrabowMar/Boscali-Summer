using System.Collections.Generic;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Domain
{
    internal enum DeparturePhase { None, Taxiing, Departing, Airborne }

    /// <summary>Keeps one current departure report per airframe on a rate-limited radio channel.</summary>
    internal sealed class DepartureChatter
    {

        private sealed class Progress
        {
            internal DeparturePhase Observed;
            internal DeparturePhase Pending;
            internal float PendingAt;
        }

        private readonly Dictionary<int, Progress> progress = new Dictionary<int, Progress>();
        private float nextTransmissionAt;

        internal void Observe(int memberId, DeparturePhase phase, float now)
        {
            if (phase == DeparturePhase.None) return;
            if (!progress.TryGetValue(memberId, out Progress item))
                progress.Add(memberId, item = new Progress());
            if (phase <= item.Observed) return;
            item.Observed = phase;
            // Replace unsaid phases with the latest so taxi cannot be announced after liftoff.
            item.Pending = phase;
            item.PendingAt = now;
        }

        internal void Silence()
        {
            foreach (Progress item in progress.Values) item.Pending = DeparturePhase.None;
        }

        internal void Forget(int memberId) => progress.Remove(memberId);

        internal void Reset()
        {
            progress.Clear();
            nextTransmissionAt = 0f;
        }
    }
}
