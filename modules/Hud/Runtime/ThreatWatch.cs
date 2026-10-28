using System.Collections.Generic;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Reads the threat picture for the board from the game's own warning systems, the same ones the vanilla RWR uses: <c>Aircraft.onRadarWarning</c>
    /// (a lock when <c>isTarget</c>, else a search spike; missiles' own seekers are skipped) and the aircraft's <c>MissileWarning</c>, through its
    /// <c>onMissileWarning</c> / <c>offMissileWarning</c> events (no polling of <c>knownMissiles</c>). Two readings:
    /// <list type="bullet">
    /// <item><see cref="Read"/> (10 Hz, <c>HudBoard</c>): the lamps' <see cref="ThreatPicture"/> and, in the caller's <see cref="BoardFrame"/>, the ranked
    /// <see cref="ThreatContact"/> rows (range, closure, time to impact, seeker) and the seeker mask of everything inbound; the board's missile and lock notices come from them.</item>
    /// <item><see cref="Fill"/> (every frame while the THREAT page shows, <c>ThreatScopeFeed</c>): every missile inbound on us with a number it keeps (M1, M2), its bearing off the nose,
    /// range, time to impact, the beam side it is being steered to and a short trail of where it has been on the scope; a missile that lost us (the game raises
    /// <c>offMissileWarning</c> while it is still flying: a flare, chaff, a notch) stays 2.6 s as a ghost; every radar painting us from a per-emitter table (a lock or a spike).</item>
    /// </list>
    /// One set of subscriptions, moved when the native HUD's aircraft changes and dropped on <see cref="Release"/>. Client-local, read-only.
    /// </summary>
    internal sealed class ThreatWatch
    {
        private const int MaxKnown = 16, MaxEmitters = 8, TrailSamples = 10;
        private const float TrailStepS = 0.22f;

        /// <summary>One missile the warning system has told us about: its number, the beam side kept for it, its trail on the scope, and, once it lost us, when.</summary>
        private sealed class Inbound
        {
            public Missile Ref;
            public int Number, Side;
            public float Since, DefeatedAt = float.NaN;
            public string Seeker;
            public ThreatSeeker Class;
            public readonly TrailRing Trail = new TrailRing(TrailSamples, 3, TrailStepS);
            public readonly float[] TrailOut = new float[TrailSamples * 3];
            public bool Defeated => !float.IsNaN(DefeatedAt);
        }

        /// <summary>One radar that painted us: when it last locked us and last searched us, and the state it is in now.</summary>
        private struct EmitterRec
        {
            public Unit Unit;
            public string Name;
            public float LockAt, SpikeAt, Since;
            public bool Active, IsLock, Held;
        }

        private Aircraft subscribed;
        private MissileWarning warning;
        private readonly List<Inbound> tracks = new List<Inbound>(8);
        private readonly EmitterRec[] emitters = new EmitterRec[MaxEmitters];
        private int emitterCount, counter;
        private Unit lockEmitter, spikeEmitter;
        private float lastLock = float.NegativeInfinity, lastSpike = float.NegativeInfinity;
        private float lockSince, spikeSince;

        /// <summary>How long the THREAT page keeps a radar after its newest warning. The LOCK and SPKE lamps keep their own shorter holds.</summary>
        public const float Hold = HudThreats.ContactHoldS;

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
            Prune(now);
            for (int i = 0; i < tracks.Count; i++)
            {
                Inbound k = tracks[i];
                if (k.Defeated) continue;
                ThreatContact c = MissileContact(k.Ref, aircraft, ownPos, ownVelocity, now - k.Since);
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

        /// <summary>Drop the tracks whose missile is gone and the ghosts that have faded.</summary>
        private void Prune(float now)
        {
            for (int i = tracks.Count - 1; i >= 0; i--)
            {
                Inbound k = tracks[i];
                if (k.Ref == null || k.Ref.disabled || (k.Defeated && now - k.DefeatedAt > ThreatMath.GhostS)) tracks.RemoveAt(i);
            }
        }

        /// <summary>
        /// Fill the THREAT page's missiles and emitters for this frame: positions are read now, bearings are against the nose on the horizontal plane (a banked aircraft keeps a
        /// level scope), the trail samples every 0.22 s however often this is called. Both arrays belong to the caller; the counts are returned.
        /// </summary>
        public void Fill(Aircraft aircraft, float now, ThreatMissile[] missiles, out int missileCount, ThreatEmitter[] into, out int emitterOut)
        {
            missileCount = emitterOut = 0;
            if (aircraft == null || aircraft != subscribed) return;
            Prune(now);
            Transform own = aircraft.transform;
            Vector3 ownVelocity = aircraft.rb != null ? aircraft.rb.velocity : Vector3.zero;
            for (int i = 0; i < tracks.Count && missileCount < missiles.Length; i++)
            {
                Inbound k = tracks[i];
                Missile m = k.Ref;
                if (m == null || m.disabled) continue;
                Vector3 rel = m.transform.position - own.position;
                float range = rel.magnitude, bearing = Bearing(own, rel);
                Vector3 velocity = m.rb != null ? m.rb.velocity : Vector3.zero;
                float closure = range > 1f ? -Vector3.Dot(velocity - ownVelocity, rel / range) : float.NaN;
                float tti = Tti(range, closure, velocity.magnitude);
                if (!k.Defeated) k.Side = ThreatMath.BeamSide(bearing, k.Side);
                k.Trail.Add(now, bearing, tti, range);
                int n = k.Trail.CopyTo(k.TrailOut);
                missiles[missileCount++] = new ThreatMissile
                {
                    Id = m.GetInstanceID(), Number = k.Number, Seeker = k.Seeker, Class = k.Class, Live = !k.Defeated,
                    Locked = m.seekerMode == Missile.SeekerMode.activeLock, BearingDeg = bearing, RangeM = range, ClosureMps = closure, TtiS = tti,
                    AgeS = now - k.Since, DefeatedS = k.Defeated ? now - k.DefeatedAt : 0f, BeamSide = k.Defeated ? 0 : k.Side, Trail = k.TrailOut, TrailCount = n,
                };
            }

            for (int i = emitterCount - 1; i >= 0; i--)
            {
                ref EmitterRec e = ref emitters[i];
                // A radar stays until Hold seconds after its newest warning; once its lock has run out it reads as the search form, and without a fresh warning in that form it is Held.
                bool locked = now - e.LockAt < HudThreats.LockHold, spiked = now - e.SpikeAt < HudThreats.SpikeHold;
                if (e.Unit == null || e.Unit.disabled || !HudThreats.Retained(now, e.LockAt, e.SpikeAt, Hold)) { emitters[i] = emitters[--emitterCount]; emitters[emitterCount] = default; continue; }
                if (!e.Active || e.IsLock != locked) { e.Since = now; e.Active = true; e.IsLock = locked; }
                e.Held = !locked && !spiked;
            }
            for (int i = 0; i < emitterCount && emitterOut < into.Length; i++)
            {
                EmitterRec e = emitters[i];
                Vector3 rel = e.Unit.transform.position - own.position;
                into[emitterOut++] = new ThreatEmitter { Id = e.Unit.GetInstanceID(), Name = e.Name, Lock = e.IsLock, Held = e.Held, BearingDeg = Bearing(own, rel), RangeM = rel.magnitude, AgeS = now - e.Since };
            }
        }

        /// <summary>The live inbound missiles (not the ghosts) and the number each keeps (M1, M2): the centre THREAT RING's, every frame. Returns how many.</summary>
        public int LiveMissiles(Missile[] into, int[] numbers)
        {
            int n = 0;
            for (int i = 0; i < tracks.Count && n < into.Length; i++)
            {
                Inbound k = tracks[i];
                if (k.Defeated || k.Ref == null || k.Ref.disabled) continue;
                into[n] = k.Ref;
                numbers[n++] = k.Number;
            }
            return n;
        }

        /// <summary>The radar locking us now (its lock within <see cref="HudThreats.LockHold"/>) and its name.</summary>
        public bool TryLock(float now, out Unit emitter, out string name)
        {
            emitter = now - lastLock < HudThreats.LockHold && lockEmitter != null && !lockEmitter.disabled ? lockEmitter : null;
            name = emitter != null ? NameOf(emitter) : null;
            return emitter != null;
        }

        /// <summary>Compass degrees off the nose, 0..360 clockwise, on the horizontal plane.</summary>
        private static float Bearing(Transform own, Vector3 rel)
        {
            Vector3 f = own.forward;
            float heading = Mathf.Atan2(f.x, f.z);
            return ThreatMath.Norm360((Mathf.Atan2(rel.x, rel.z) - heading) * Mathf.Rad2Deg);
        }

        /// <summary>
        /// Seconds to impact: range over the closing speed along the line of sight while it is closing, else (a missile being beamed closes slowly while it still turns in on us)
        /// range over its own speed, the time it would take flying straight at us. Never NaN: a missile with no speed left reads a long time, not nothing.
        /// </summary>
        internal static float Tti(float range, float closure, float speed)
        {
            float t = ThreatRank.Tti(range, closure);
            return !float.IsNaN(t) ? t : range / Mathf.Max(speed, ThreatRank.MinClosureMps);
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
                Name = NameOf(emitter),
                Seeker = "",
                RangeM = Vector3.Distance(emitter.transform.position, ownPos),
                ClosureMps = float.NaN,
                TtiS = float.NaN,
                AgeS = age,
            };
            Locate(own, emitter.transform.position, ref c);
            return c;
        }

        private static string NameOf(Unit emitter) =>
            emitter.definition != null && !string.IsNullOrEmpty(emitter.definition.unitName) ? emitter.definition.unitName : emitter.unitName ?? "";

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
            tracks.Clear();
            counter = 0;
            System.Array.Clear(emitters, 0, emitters.Length);
            emitterCount = 0;
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
            if (missile == null || tracks.Count >= MaxKnown) return;
            // A missile that lost us and has us again (a radar that broke lock for a moment, a seeker that looked past the flare) is live again.
            for (int i = 0; i < tracks.Count; i++) if (ReferenceEquals(tracks[i].Ref, missile)) { tracks[i].DefeatedAt = float.NaN; return; }
            string seeker;
            try { seeker = missile.GetSeekerType() ?? ""; } catch { seeker = ""; }
            tracks.Add(new Inbound { Ref = missile, Number = ++counter, Since = now, Seeker = seeker, Class = ThreatMath.ClassOf(seeker) });
        }

        private void OnMissileKnown(MissileWarning.OnMissileWarning e) => AddKnown(e.missile, Time.unscaledTime);

        /// <summary>
        /// The warning system lets go of a missile when it is gone (disabled: it hit, ran out or was shot down) or when it no longer guides on us (<c>targetID</c> changed: a flare, chaff, a
        /// notch took it). The second raises this event with the missile still flying: it stays on the scope as a ghost, hollow, for a moment (and comes back live if the game warns of
        /// it again). The first just leaves.
        /// </summary>
        private void OnMissileGone(MissileWarning.OffMissileWarning e)
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                if (!ReferenceEquals(tracks[i].Ref, e.missile)) continue;
                if (e.missile == null || e.missile.disabled) tracks.RemoveAt(i);
                else if (!tracks[i].Defeated) tracks[i].DefeatedAt = Time.unscaledTime;
                return;
            }
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
            Note(e.emitter, e.isTarget, now);
        }

        /// <summary>The per-emitter table: this radar painted us just now (a lock or a search).</summary>
        private void Note(Unit unit, bool locked, float now)
        {
            int at = -1;
            for (int i = 0; i < emitterCount && at < 0; i++) if (ReferenceEquals(emitters[i].Unit, unit)) at = i;
            if (at < 0)
            {
                if (emitterCount == MaxEmitters)
                {
                    at = 0;      // full: the radar that has been quiet longest makes room
                    for (int i = 1; i < emitterCount; i++) if (System.Math.Max(emitters[i].LockAt, emitters[i].SpikeAt) < System.Math.Max(emitters[at].LockAt, emitters[at].SpikeAt)) at = i;
                }
                else at = emitterCount++;
                emitters[at] = new EmitterRec { Unit = unit, Name = NameOf(unit).ToUpperInvariant(), LockAt = float.NegativeInfinity, SpikeAt = float.NegativeInfinity };
            }
            if (locked) emitters[at].LockAt = now; else emitters[at].SpikeAt = now;
        }

        public void Release() => Swap(null);
    }
}
