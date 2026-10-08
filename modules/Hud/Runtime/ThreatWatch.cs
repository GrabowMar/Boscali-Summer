using System.Collections.Generic;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Reads the threat picture for the board's threat row (2026-10-07, after KaceyTronic-RWR) from the game's own
    /// warning systems, the same ones the vanilla RWR uses: <c>Aircraft.onRadarWarning</c> (a lock when
    /// <c>isTarget</c>, else a search spike; missiles' own seekers are skipped) and the aircraft's
    /// <c>MissileWarning.knownMissiles</c> list, polled. One subscription, moved when the native HUD's aircraft changes
    /// and dropped on <see cref="Release"/>. Client-local, read-only.
    /// </summary>
    internal sealed class ThreatWatch
    {
        private Aircraft subscribed;
        private MissileWarning warning;
        private Unit lockEmitter;
        private float lastLock = float.NegativeInfinity, lastSpike = float.NegativeInfinity, lastNewMissile = float.NegativeInfinity;
        private int lastMissiles;

        public ThreatPicture Read(Aircraft aircraft)
        {
            if (aircraft != subscribed) Swap(aircraft);
            ThreatPicture t = ThreatPicture.Clear;
            if (aircraft == null) return t;
            float now = Time.unscaledTime;

            List<Missile> known = warning != null ? warning.knownMissiles : null;
            Missile nearest = null;
            float best = float.MaxValue;
            int count = 0;
            if (known != null)
                for (int i = 0; i < known.Count; i++)
                {
                    Missile m = known[i];
                    if (m == null || m.disabled) continue;
                    count++;
                    float d = (m.transform.position - aircraft.transform.position).sqrMagnitude;
                    if (d < best) { best = d; nearest = m; }
                }
            if (count > lastMissiles) lastNewMissile = now;
            lastMissiles = count;

            t.Missiles = count;
            t.SinceLock = now - lastLock;
            t.SinceSpike = now - lastSpike;
            t.SinceNewMissile = now - lastNewMissile;

            Unit source = nearest != null ? nearest : t.SinceLock < HudThreats.LockHold && lockEmitter != null && !lockEmitter.disabled ? lockEmitter : null;
            if (source != null)
            {
                Vector3 local = aircraft.transform.InverseTransformPoint(source.transform.position);
                HudThreats.Clock(local.x, local.y, local.z, out t.BearingDeg, out t.ElevationDeg);
                t.HasBearing = true;
                t.BearingIsMissile = nearest != null;
            }
            return t;
        }

        private void Swap(Aircraft aircraft)
        {
            if (subscribed != null) subscribed.onRadarWarning -= OnRadarWarning;
            subscribed = aircraft;
            warning = aircraft != null ? aircraft.GetComponent<MissileWarning>() : null;
            lockEmitter = null;
            lastLock = lastSpike = lastNewMissile = float.NegativeInfinity;
            lastMissiles = 0;
            if (aircraft != null) aircraft.onRadarWarning += OnRadarWarning;
        }

        private void OnRadarWarning(Aircraft.OnRadarWarning e)
        {
            if (e.emitter == null || e.emitter is Missile) return;
            float now = Time.unscaledTime;
            if (e.isTarget)
            {
                lastLock = now;
                lockEmitter = e.emitter;
            }
            else lastSpike = now;
        }

        public void Release() => Swap(null);
    }
}
