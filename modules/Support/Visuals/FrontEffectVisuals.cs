using NuclearOption.Networking;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Runtime;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// Client-side cue for a finished LAUNCH SATELLITE programme. The host event is played once per kind,
    /// from a live launch pad in the matching faction/scene mirror. Initial snapshot history stays quiet.
    /// </summary>
    internal static class FrontEffectVisuals
    {
        private const float LaunchSeconds = 16f;
        private static readonly SatelliteLaunchTracker launches = new SatelliteLaunchTracker();

        public static void Reset() => launches.Reset();

        public static void Tick(FrontMirror mirror, OpsMirror ops, float now)
        {
            if (GameManager.IsHeadless) return;
            // Slots are stable across snapshots; avoid a random airbase or a destroyed pad.
            OpsSiteRow? pad = null;
            if (mirror != null && mirror.Known && ops != null && ops.Known && ops.State.Active &&
                ops.State.Faction == mirror.State.Faction && ops.State.Epoch == mirror.State.Epoch)
                foreach (OpsSiteRow candidate in ops.State.Sites)
                    if (candidate.State == SiteState.Up && (!pad.HasValue || candidate.Slot < pad.Value.Slot)) pad = candidate;
            byte fresh = launches.Observe(mirror, now, pad.HasValue);
            if (fresh == 0 || !pad.HasValue ||
                !SupportTargeting.TryMapPoint(new GlobalPosition(pad.Value.X, 0f, pad.Value.Z), out Vector3 site)) return;
            for (int kind = 0; kind < 3; kind++)
                if ((fresh & (1 << kind)) != 0) SatelliteLaunchVisuals.Play(site, LaunchSeconds);
        }

        /// <summary>The owned airbase whose nearest enemy-held airbase is farthest away.</summary>
        internal static bool TryRearAirbase(FactionHQ own, out Vector3 site)
        {
            site = default;
            if (own == null) return false;
            float best = -1f;
            foreach (Airbase a in own.GetAirbases())
            {
                if (a == null || a.AttachedAirbase || a.CurrentHQ != own) continue;
                Vector3 p = a.center != null ? a.center.position : a.transform.position;
                float nearestEnemy = float.MaxValue;
                if (FactionRegistry.airbaseLookup != null)
                    foreach (Airbase e in FactionRegistry.airbaseLookup.Values)
                    {
                        if (e == null || e.CurrentHQ == null || e.CurrentHQ == own || e.AttachedAirbase) continue;
                        Vector3 q = e.center != null ? e.center.position : e.transform.position;
                        nearestEnemy = Mathf.Min(nearestEnemy, (q - p).sqrMagnitude);
                    }
                if (nearestEnemy > best) { best = nearestEnemy; site = p; }
            }
            return best >= 0f;
        }
    }
}
