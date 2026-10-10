using System.Collections.Generic;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Reads the threat picture for the board's threat row and detail lines (2026-10-07 after KaceyTronic-RWR, detail
    /// 2026-10-10) from the game's own warning systems, the same ones the vanilla RWR uses: <c>Aircraft.onRadarWarning</c>
    /// (a lock when <c>isTarget</c>, else a search spike; missiles' own seekers are skipped) and the aircraft's
    /// <c>MissileWarning</c>, through its <c>onMissileWarning</c> / <c>offMissileWarning</c> events (no polling of
    /// <c>knownMissiles</c>). Each tick it fills the lamps' <see cref="ThreatPicture"/> and, in the caller's
    /// <see cref="BoardFrame"/>, the ranked <see cref="ThreatContact"/> rows (range, closure, time to impact, seeker) and the
    /// seeker mask of everything inbound (which countermeasure tile to highlight). One set of subscriptions, moved when the
    /// native HUD's aircraft changes and dropped on <see cref="Release"/>. Client-local, read-only.
    /// </summary>
    internal sealed class ThreatWatch
    {
        private const int MaxKnown = 16;

        private Aircraft subscribed;
        private MissileWarning warning;
        private readonly List<Missile> known = new List<Missile>(8);
        private readonly List<float> knownSince = new List<float>(8);
        private Unit lockEmitter, spikeEmitter;
        private float lastLock = float.NegativeInfinity, lastSpike = float.NegativeInfinity;
        private float lockSince, spikeSince;

        public ThreatPicture Read(Aircraft aircraft, BoardFrame frame)
        {
            if (aircraft != subscribed) Swap(aircraft);
            frame.ContactCount = 0;
            frame.InboundMask = 0;
            ThreatPicture t = ThreatPicture.Clear;
            if (aircraft == null) return t;
            float now = Time.unscaledTime;
            Vector3 ownPos = aircraft.transform.position;
            Vector3 ownVelocity = aircraft.rb != null ? aircraft.rb.velocity : Vector3.zero;

            int count = 0;
            for (int i = known.Count - 1; i >= 0; i--)
                if (known[i] == null || known[i].disabled) { known.RemoveAt(i); knownSince.RemoveAt(i); }
            for (int i = 0; i < known.Count; i++)
            {
                ThreatContact c = MissileContact(known[i], aircraft, ownPos, ownVelocity, now - knownSince[i]);
                count++;
                frame.InboundMask |= HudCm.SeekerMask(c.Seeker);
                frame.ContactCount = ThreatRank.Insert(frame.Contacts, frame.ContactCount, c);
            }
            if (now - lastLock < HudThreats.LockHold && lockEmitter != null && !lockEmitter.disabled)
                frame.ContactCount = ThreatRank.Insert(frame.Contacts, frame.ContactCount,
                    EmitterContact(ThreatKind.Lock, lockEmitter, aircraft, ownPos, now - lockSince));
            if (now - lastSpike < HudThreats.SpikeHold && spikeEmitter != null && !spikeEmitter.disabled)
                frame.ContactCount = ThreatRank.Insert(frame.Contacts, frame.ContactCount,
                    EmitterContact(ThreatKind.Spike, spikeEmitter, aircraft, ownPos, now - spikeSince));

            t.Missiles = count;
            t.SinceLock = now - lastLock;
            t.SinceSpike = now - lastSpike;
            if (frame.ContactCount > 0)
            {
                ThreatContact top = frame.Contacts[0];
                t.HasBearing = true;
                t.BearingDeg = top.BearingDeg;
                t.ElevationDeg = top.ElevationDeg;
                t.BearingIsMissile = top.Kind == ThreatKind.Missile;
            }
            return t;
        }

        private static ThreatContact MissileContact(Missile m, Aircraft own, Vector3 ownPos, Vector3 ownVelocity, float age)
        {
            Vector3 rel = m.transform.position - ownPos;
            float range = rel.magnitude;
            // Closing speed along the line of sight: positive while the range shrinks.
            float closure = range > 1f && m.rb != null ? -Vector3.Dot(m.rb.velocity - ownVelocity, rel / range) : float.NaN;
            string seeker;
            try { seeker = m.GetSeekerType() ?? ""; } catch { seeker = ""; }
            WeaponInfo info = m.GetWeaponInfo();
            var c = new ThreatContact
            {
                Kind = ThreatKind.Missile,
                Name = info != null && !string.IsNullOrEmpty(info.shortName) ? info.shortName : "",
                Seeker = seeker,
                Locked = m.seekerMode == Missile.SeekerMode.activeLock,
                RangeM = range,
                ClosureMps = closure,
                TtiS = ThreatRank.Tti(range, closure),
                AgeS = age,
            };
            Locate(own, m.transform.position, ref c);
            return c;
        }

        private static ThreatContact EmitterContact(ThreatKind kind, Unit emitter, Aircraft own, Vector3 ownPos, float age)
        {
            var c = new ThreatContact
            {
                Kind = kind,
                Name = emitter.definition != null && !string.IsNullOrEmpty(emitter.definition.unitName) ? emitter.definition.unitName : emitter.unitName ?? "",
                Seeker = "",
                RangeM = Vector3.Distance(emitter.transform.position, ownPos),
                ClosureMps = float.NaN,
                TtiS = float.NaN,
                AgeS = age,
            };
            Locate(own, emitter.transform.position, ref c);
            return c;
        }

        private static void Locate(Aircraft own, Vector3 world, ref ThreatContact c)
        {
            Vector3 local = own.transform.InverseTransformPoint(world);
            HudThreats.Clock(local.x, local.y, local.z, out c.BearingDeg, out c.ElevationDeg);
        }

        private void Swap(Aircraft aircraft)
        {
            if (subscribed != null) subscribed.onRadarWarning -= OnRadarWarning;
            if (warning != null)
            {
                warning.onMissileWarning -= OnMissileKnown;
                warning.offMissileWarning -= OnMissileGone;
            }
            subscribed = aircraft;
            warning = aircraft != null ? aircraft.GetComponent<MissileWarning>() : null;
            lockEmitter = spikeEmitter = null;
            lastLock = lastSpike = float.NegativeInfinity;
            known.Clear();
            knownSince.Clear();
            if (aircraft != null) aircraft.onRadarWarning += OnRadarWarning;
            if (warning == null) return;
            warning.onMissileWarning += OnMissileKnown;
            warning.offMissileWarning += OnMissileGone;
            // Missiles already known when we start watching (a respawn into a live threat).
            List<Missile> existing = warning.knownMissiles;
            for (int i = 0; existing != null && i < existing.Count; i++) AddKnown(existing[i], Time.unscaledTime);
        }

        private void AddKnown(Missile missile, float now)
        {
            if (missile == null || known.Count >= MaxKnown || known.Contains(missile)) return;
            known.Add(missile);
            knownSince.Add(now);
        }

        private void OnMissileKnown(MissileWarning.OnMissileWarning e)
        {
            AddKnown(e.missile, Time.unscaledTime);
        }

        private void OnMissileGone(MissileWarning.OffMissileWarning e)
        {
            int at = known.IndexOf(e.missile);
            if (at < 0) return;
            known.RemoveAt(at);
            knownSince.RemoveAt(at);
        }

        private void OnRadarWarning(Aircraft.OnRadarWarning e)
        {
            if (e.emitter == null || e.emitter is Missile) return;
            float now = Time.unscaledTime;
            if (e.isTarget)
            {
                if (now - lastLock > HudThreats.LockHold) lockSince = now;
                lastLock = now;
                lockEmitter = e.emitter;
            }
            else
            {
                if (now - lastSpike > HudThreats.SpikeHold) spikeSince = now;
                lastSpike = now;
                spikeEmitter = e.emitter;
            }
        }

        public void Release() => Swap(null);
    }
}
