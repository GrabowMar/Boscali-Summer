using System.Collections.Generic;
using BoscaliSummer.Modules.Hud.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>What the board remembers about one of our own weapons in the air: where it left, where it has been (global positions: the floating origin shifts local ones), the target it was given, what kind of seeker it flies on, whether an active radar has gone active and whether the weapon has lost its target.</summary>
    internal sealed class ShotRec
    {
        public int Id;
        public Missile Ref;
        public bool Bomb;
        /// <summary>An active radar seeker (data-linked until its pitbull), and any radar seeker (ARH or SARH: the game reports their lock in <c>Missile.seekerMode</c>).</summary>
        public bool Arh, Radar;
        /// <summary>The seeker's cone half angle in degrees (0: the game gives none) and, for an active radar, the range it turns its seeker on at.</summary>
        public float ConeDeg, ActiveRangeM;
        public string Short, Full, Seeker;
        public Unit FirstTarget;
        /// <summary>The target it was given is alive and is no longer what it guides on (a flare, chaff, a notch, terrain, a decoy): it flies on, hollow, until it has it back or ends.</summary>
        public bool Lost;
        public float LostAt, PitbullAt = float.NaN, PitbullRangeM = float.NaN, EndedAt = float.NaN;
        public bool EverPitbull;
        public GPos Launch;
        /// <summary>The farthest it has been from its target (the missile cam's range tape starts there).</summary>
        public float MaxRangeM;
        public readonly MissilePath Path = new MissilePath();
        public readonly float[] TrailOut = new float[MissilePath.RecentSamples * 3];
        public bool Ended => !float.IsNaN(EndedAt);
    }

    /// <summary>
    /// The SHOT page's and the MISSILE CAM page's shared memory of our own weapons in the air (<see cref="MissileTracker"/> says which are; this keeps what the game does not):
    /// the launch point and a path of global samples, the target each was given and whether it has since lost it, the first lock of an active radar (the pitbull). Never writes
    /// to a missile. Records outlive their missile for <see cref="KeepEndedS"/> so a result can be put where the weapon was last seen.
    /// <para>The seeker's state is read from what the game syncs to every peer: <c>Missile.seekerMode</c> (only the radar seekers leave "passive": locked, or searching) and the
    /// private <c>Missile.target</c> (set from the synced <c>targetID</c>; every seeker drops it when it loses the target). Three private fields are prefab settings, so a client
    /// has them too (tests/BoscaliSummer.PatchProbe pins the names): <c>ARHSeeker.maxTrackingAngle</c> and <c>SARHSeeker.seekerAngle</c> (the cone's half angle) and
    /// <c>ARHSeeker.terminalRange</c> (the range an active radar turns on at). The seeker's own run-time flags (radarLockEstablished...) exist on the host only and are not used.</para>
    /// </summary>
    internal sealed class ShotBook
    {
        private const float KeepEndedS = 10f;
        private const int MaxRecs = 24;

        private static readonly AccessTools.FieldRef<Missile, Unit> TargetRef = NativeRefs.Field<Missile, Unit>("target");
        private static readonly AccessTools.FieldRef<ARHSeeker, float> ArhCone = NativeRefs.Field<ARHSeeker, float>("maxTrackingAngle");
        private static readonly AccessTools.FieldRef<ARHSeeker, float> ArhActiveRange = NativeRefs.Field<ARHSeeker, float>("terminalRange");
        private static readonly AccessTools.FieldRef<SARHSeeker, float> SarhCone = NativeRefs.Field<SARHSeeker, float>("seekerAngle");

        private readonly ShotLedger ledger;
        private readonly List<ShotRec> recs = new List<ShotRec>(8);
        private int stamp = -1;

        public ShotBook(ShotLedger ledger) { this.ledger = ledger; }

        public IReadOnlyList<ShotRec> Recs => recs;

        /// <summary>The record of the weapon with this instance id (a recently ended one too), null when unknown.</summary>
        public ShotRec Of(int id)
        {
            for (int i = 0; i < recs.Count; i++) if (recs[i].Id == id) return recs[i];
            return null;
        }

        /// <summary>The record of a weapon of ours: made now if the book was never told of it (it was in the air before the board watched, or it is a decoy the shot ledger does not count), so its path starts here. Null when the book is full of live records.</summary>
        public ShotRec Ensure(Missile m, float now) => Of(m.GetInstanceID()) ?? Launched(m, m.GetWeaponInfo(), now);

        /// <summary>One of our weapons was just registered by the game.</summary>
        public ShotRec Launched(Missile m, WeaponInfo info, float now)
        {
            if (m == null || (recs.Count >= MaxRecs && !Prune())) return null;
            string seeker;
            try { seeker = m.GetSeekerType() ?? ""; } catch { seeker = ""; }
            var rec = new ShotRec
            {
                Id = m.GetInstanceID(), Ref = m, Bomb = info != null && info.bomb, Short = Short(info), Seeker = seeker.Trim().ToUpperInvariant(),
                Full = info != null && !string.IsNullOrEmpty(info.weaponName) ? info.weaponName.ToUpperInvariant() : Short(info), FirstTarget = TargetOf(m),
            };
            GlobalPosition at = m.transform.position.ToGlobalPosition();
            rec.Launch = new GPos(at.x, at.y, at.z);
            rec.Path.Add(now, at.x, at.y, at.z);
            MissileSeeker s = m.GetComponent<MissileSeeker>();
            if (s is ARHSeeker arh)
            {
                rec.Arh = rec.Radar = true;
                rec.ConeDeg = NativeRefs.Get(ArhCone, arh);
                rec.ActiveRangeM = NativeRefs.Get(ArhActiveRange, arh);
            }
            else if (s is SARHSeeker sarh)
            {
                rec.Radar = true;
                rec.ConeDeg = NativeRefs.Get(SarhCone, sarh);
            }
            recs.Add(rec);
            return rec;
        }

        /// <summary>"SKYWY": the weapon's short name in capitals (its full name, then MSL, when the game gives none).</summary>
        public static string Short(WeaponInfo info) =>
            info != null && !string.IsNullOrEmpty(info.shortName) ? info.shortName.ToUpperInvariant() : info != null && !string.IsNullOrEmpty(info.weaponName) ? info.weaponName.ToUpperInvariant() : "MSL";

        /// <summary>Sample every record (the paths thin themselves) and read what its seeker is on. Once per frame at most: the board calls it every tick and so do the feeds whose page shows.</summary>
        public void Track(float now)
        {
            if (stamp == Time.frameCount) return;
            stamp = Time.frameCount;
            for (int i = recs.Count - 1; i >= 0; i--)
            {
                ShotRec r = recs[i];
                Missile m = r.Ref;
                if (m == null || m.disabled)
                {
                    if (!r.Ended) r.EndedAt = now;
                    else if (now - r.EndedAt > KeepEndedS) recs.RemoveAt(i);
                    continue;
                }
                GlobalPosition at = m.transform.position.ToGlobalPosition();
                r.Path.Add(now, at.x, at.y, at.z);
                Unit target = TargetOf(m);
                if (target != null)
                {
                    if (r.FirstTarget == null) r.FirstTarget = target;
                    r.MaxRangeM = Mathf.Max(r.MaxRangeM, Vector3.Distance(m.transform.position, target.transform.position));
                }
                if (r.Arh && !r.EverPitbull && Locked(r, m, target != null))
                {
                    r.EverPitbull = true;
                    r.PitbullAt = now;
                    r.PitbullRangeM = Vector3.Distance(m.transform.position, target.transform.position);
                }
                bool lost = ShotMath.LostTarget(r.Bomb, r.FirstTarget != null && !r.FirstTarget.disabled, ReferenceEquals(target, r.FirstTarget));
                if (lost == r.Lost) continue;
                r.Lost = lost;
                r.LostAt = now;
                ledger.MarkLost(r.Id, lost);
            }
        }

        /// <summary>Room for one more: the oldest ended record goes. False when every record is still in the air.</summary>
        private bool Prune()
        {
            int oldest = -1;
            for (int i = 0; i < recs.Count; i++)
                if (recs[i].Ended && (oldest < 0 || recs[i].EndedAt < recs[oldest].EndedAt)) oldest = i;
            if (oldest >= 0) recs.RemoveAt(oldest);
            return oldest >= 0;
        }

        public void Clear() => recs.Clear();

        /// <summary>The unit a weapon is guiding on, null when it has none.</summary>
        public static Unit TargetOf(Missile m)
        {
            Unit t = NativeRefs.Get(TargetRef, m);
            return t != null && !t.disabled ? t : null;
        }

        /// <summary>The weapon's own seeker has its target (<see cref="ShotMath.SeekerLocked"/>).</summary>
        public static bool Locked(ShotRec r, Missile m, bool hasTarget) => ShotMath.SeekerLocked(r.Radar, m.seekerMode == Missile.SeekerMode.activeLock, hasTarget);
    }
}
