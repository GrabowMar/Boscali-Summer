using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Hud's own provider of the MISSILE CAM page's feed (<see cref="MissileCamScope"/>): for the missile the orbit camera rides (<see cref="MissileCam.IsOwnMissileView"/>), else for
    /// our newest one in the air (the pilot picked the page and watches from the cockpit), the missile's seeker state, where its target stands off the boresight (the last known place while it is blind), the range
    /// to go with its closure and time, its attitude, the marks of the range tape and the path since launch (<see cref="ShotBook"/>). When the last of our missiles ends (impact,
    /// burnt out, shot down) it holds the last reading for <see cref="MissileCamMath.LostSeconds"/> as a lost feed with the result of the shot. Once per frame at most; allocation-free in steady state. Client-local.
    /// </summary>
    internal sealed class MissileCamScopeFeed
    {
        private readonly MissileTracker tracker;
        private readonly ShotBook book;
        private readonly ShotLedger ledger;
        private readonly float[] pathD = new float[MissilePath.LongSamples], pathA = new float[MissilePath.LongSamples];
        private MissileCamScope cache;
        private bool live, riding, lost, haveLastTarget;
        private int stamp = -1, rideId;
        private float lastSeen;
        private Missile ridden;
        private Unit targetFor;
        private string targetWord;
        private GlobalPosition lastTarget;

        public MissileCamScopeFeed(MissileTracker tracker, ShotBook book, ShotLedger ledger)
        {
            this.tracker = tracker;
            this.book = book;
            this.ledger = ledger;
        }

        public bool Read(out MissileCamScope d)
        {
            if (stamp != Time.frameCount)
            {
                stamp = Time.frameCount;
                live = Build();
            }
            d = cache;
            return live;
        }

        private bool Build()
        {
            float now = Time.unscaledTime;
            bool view = MissileCam.IsOwnMissileView(out _, out Missile m);
            if (!view) m = Newest();
            if (m != null)
            {
                book.Track(now);
                ShotRec rec = book.Ensure(m, now);
                if (rec == null) return false;
                Ride(m, rec, now, view);
                riding = true;
                lost = false;
                lastSeen = now;
                return true;
            }
            if (riding)
            {
                // The missile is gone and no other of ours flies: one that ended leaves a lost feed, one the pilot stepped off does not.
                riding = false;
                lost = ridden == null || ridden.disabled;
                ridden = null;
            }
            if (!lost || now - lastSeen > MissileCamMath.LostSeconds)
            {
                lost = false;
                return false;
            }
            cache.Mode = CamMode.Lost;
            cache.Lost = true;
            cache.LostS = now - lastSeen;
            int li = ledger.IndexOf(rideId);
            cache.Result = li >= 0 ? ledger.State(li) : ShotState.Empty;
            return true;
        }

        /// <summary>Our newest weapon still in the air (the tracker's list is launch order), or null.</summary>
        private Missile Newest()
        {
            var list = tracker.Missiles;
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i] != null && !list[i].disabled) return list[i];
            return null;
        }

        /// <summary>Read <paramref name="m"/> into the cache: the one the camera rides (<paramref name="view"/>), or the newest in the air while the pilot watches from the cockpit.</summary>
        private void Ride(Missile m, ShotRec rec, float now, bool view)
        {
            var list = tracker.Missiles;
            int at = -1;
            for (int i = 0; i < list.Count && at < 0; i++) if (ReferenceEquals(list[i], m)) at = i;
            if (!ReferenceEquals(m, ridden))
            {
                ridden = m;
                rideId = rec.Id;
                haveLastTarget = false;
                targetFor = null;
                targetWord = null;
            }
            Transform t = m.transform;
            GlobalPosition here = t.position.ToGlobalPosition();
            Unit target = ShotBook.TargetOf(m);
            bool hasTarget = target != null;
            float range = float.NaN, closure = float.NaN, tti = float.NaN;
            if (hasTarget)
            {
                lastTarget = target.transform.position.ToGlobalPosition();
                haveLastTarget = true;
                if (!ReferenceEquals(target, targetFor))
                {
                    targetFor = target;
                    targetWord = target.definition != null && !string.IsNullOrEmpty(target.definition.unitName) ? target.definition.unitName.ToUpperInvariant() : "TGT";
                }
                Vector3 los = target.transform.position - t.position;
                range = los.magnitude;
                Vector3 own = m.rb != null ? m.rb.velocity : Vector3.zero, theirs = target.rb != null ? target.rb.velocity : Vector3.zero;
                closure = range > 1f ? -Vector3.Dot(theirs - own, los / range) : float.NaN;
                tti = ShotMath.TimeToGo(range, closure);
            }
            // Where the target stands against the boresight: now, or the last place it was seen while the seeker has nothing.
            float ex = 0f, ey = 0f;
            if (haveLastTarget)
            {
                Vector3 local = t.InverseTransformDirection(lastTarget - here);
                MissileCamMath.Error(local.x, local.y, local.z, out ex, out ey);
            }
            bool locked = ShotBook.Locked(rec, m, hasTarget);

            // The path since the launch, as ground distance from it and altitude.
            int n = 0;
            for (; n < rec.Path.LongCount; n++)
            {
                rec.Path.Long(n, out float x, out float y, out float z);
                pathD[n] = Flat(x - rec.Launch.X, z - rec.Launch.Z);
                pathA[n] = y;
            }
            // The missile the view steps to next (the tracker's list is launch order); only a ride steps.
            ShotRec next = view && list.Count > 1 && at >= 0 && list[(at + 1) % list.Count] != null ? book.Ensure(list[(at + 1) % list.Count], now) : null;
            cache = new MissileCamScope
            {
                Index = at, Count = list.Count, Weapon = rec.Short, Full = rec.Full, Seeker = rec.Seeker, Target = targetWord, Next = next?.Short,
                Mode = MissileCamMath.ModeOf(rec.Arh, rec.EverPitbull, locked, hasTarget), HasTarget = hasTarget, HasLast = haveLastTarget, ErrXDeg = ex, ErrYDeg = ey, ConeDeg = rec.ConeDeg,
                PitchDeg = Mathf.Asin(Mathf.Clamp(t.forward.y, -1f, 1f)) * Mathf.Rad2Deg, RollDeg = Attitude.Bank(t.right.y, t.up.y), HeadingDeg = t.eulerAngles.y,
                RangeM = range, ClosureMps = closure, TtiS = tti,
                TapeTopM = rec.MaxRangeM, ActiveAtM = !rec.Arh ? float.NaN : rec.EverPitbull ? rec.PitbullRangeM : rec.ActiveRangeM, ActivePassed = rec.EverPitbull,
                Phase = ShotMath.PhaseOf(rec.Bomb, m.EngineOn(), m.timeSinceSpawn, m.boosterIsAttached, locked, range),
                BurnLeftS = m.EngineOn() ? Mathf.Max(0f, m.GetRemainingBurnTime()) : 0f, BurnTotalS = Mathf.Max(0f, m.GetTotalBurnTime()),
                PathCount = n, PathD = pathD, PathA = pathA, NowD = Flat(here.x - rec.Launch.X, here.z - rec.Launch.Z), NowA = here.y,
                HasTargetPlace = haveLastTarget, TargetD = haveLastTarget ? Flat(lastTarget.x - rec.Launch.X, lastTarget.z - rec.Launch.Z) : 0f, TargetA = haveLastTarget ? lastTarget.y : 0f,
                Result = ShotState.InFlight,
            };
        }

        private static float Flat(float dx, float dz) => Mathf.Sqrt(dx * dx + dz * dz);

        public void Clear()
        {
            live = riding = lost = false;
            ridden = null;
            targetFor = null;
            stamp = -1;
        }
    }
}
