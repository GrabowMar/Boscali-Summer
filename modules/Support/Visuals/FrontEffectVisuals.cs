using NuclearOption.Networking;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// Client-side cue for a finished LAUNCH SATELLITE programme: when the mirrored SPACE front log gains a new Effect row ("SATELLITE ON STATION"), the cosmetic launch
    /// streak (<see cref="SatelliteLaunchVisuals"/>) climbs from the faction's rear airbase (the owned one farthest from enemy-held ground). Dedupe is by log time with a
    /// tolerance (the mirror shifts host times onto the client clock), and rows older than <see cref="MaxAge"/> are history, not news.
    /// </summary>
    internal static class FrontEffectVisuals
    {
        private const float MaxAge = 20f, Tolerance = 1.5f, LaunchSeconds = 16f;
        private static float lastTime = -1f;

        public static void Reset() => lastTime = -1f;

        public static void Tick(FrontMirror mirror, float now)
        {
            if (GameManager.IsHeadless || mirror == null || !mirror.Known) return;
            var log = mirror.State.Fronts[(int)Front.Space].Log;
            bool play = false;
            float newest = lastTime;
            for (int i = 0; i < log.Count; i++)
            {
                FrontLogRow row = log[i];
                if (row.Code != (byte)FrontLogCode.Effect) continue;
                if (row.Time > lastTime + Tolerance && now - row.Time < MaxAge) play = true;
                if (row.Time > newest) newest = row.Time;
            }
            lastTime = newest;
            if (!play || !GameManager.GetLocalPlayer<Player>(out Player player) || player == null || player.HQ == null) return;
            if (TryRearAirbase(player.HQ, out Vector3 site)) SatelliteLaunchVisuals.Play(site, LaunchSeconds);
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
