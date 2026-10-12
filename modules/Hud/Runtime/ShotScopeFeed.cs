using System;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Hud's own provider of the SHOT page's feed (<see cref="ShotScope"/>): our own deliveries in the air (the game's <c>Unit.onRegisterMissile</c> via <see cref="MissileTracker"/>,
    /// filtered to real deliveries) with the phase they are in, where they are heading, the time to the target and their trails (<see cref="ShotBook"/>: global samples), the
    /// targets they fly at, the results of the last seconds and the timeline of the last eight shots (<see cref="ShotLedger"/>), and the selected weapon. Answers from the book
    /// once per frame at most (the page repaints every frame while it shows). Live while a delivery is in the air or a result is fresh. Client-local: a remote player's shots
    /// are not ours and never show; everything read is synced to clients (positions, <c>targetID</c>, <c>seekerMode</c>).
    /// </summary>
    internal sealed class ShotScopeFeed
    {
        private const int MaxShots = ShotLedger.Capacity, MaxTargets = 6, MaxResults = 8, NameCache = 8;

        private readonly ShotBook book;
        private readonly ShotLedger ledger;
        private readonly ShotMissile[] shots = new ShotMissile[MaxShots];
        private readonly ShotTarget[] targets = new ShotTarget[MaxTargets];
        private readonly int[] targetIds = new int[MaxTargets], slots = new int[ShotLedger.Capacity], nameIds = new int[NameCache];
        private readonly string[] names = new string[NameCache];
        private readonly ShotResult[] results = new ShotResult[MaxResults];
        private readonly ShotCell[] cells = new ShotCell[ShotLedger.Capacity];
        private ShotScope cache;
        private bool live;
        private int stamp = -1, targetCount, nameNext;
        private Aircraft aircraft;

        public ShotScopeFeed(ShotBook book, ShotLedger ledger)
        {
            this.book = book;
            this.ledger = ledger;
        }

        /// <summary>The HUD's aircraft (<c>HudBoard</c>, every board tick).</summary>
        public void Update(Aircraft hud)
        {
            aircraft = hud;
            stamp = -1;
        }

        public bool Read(out ShotScope data)
        {
            if (stamp != Time.frameCount)
            {
                stamp = Time.frameCount;
                live = Build();
            }
            data = cache;
            return live;
        }

        private bool Build()
        {
            cache = default;
            if (aircraft == null || aircraft.disabled) return false;
            float now = Time.unscaledTime;
            book.Track(now);
            Transform own = aircraft.transform;
            GlobalPosition og = own.position.ToGlobalPosition();
            var frame = new PlanFrame(og.x, og.z, own.eulerAngles.y);
            targetCount = 0;
            int n = 0;
            float needM = 0f;
            // the book keeps launch order (the oldest first): the order the page draws in
            for (int r = 0; r < book.Recs.Count && n < MaxShots; r++)
            {
                ShotRec rec = book.Recs[r];
                Missile m = rec.Ref;
                if (rec.Ended || m == null || m.disabled) continue;
                int li = ledger.IndexOf(rec.Id);
                if (li < 0 || ledger.State(li) != ShotState.InFlight) continue;
                shots[n++] = ShotOf(rec, m, og, frame, now, ref needM);
            }
            int lead = ShotMath.Lead(shots, n);

            int rc = 0;
            int count = ledger.InOrder(slots);
            for (int k = 0; k < count; k++)
            {
                int i = slots[k];
                ShotState st = ledger.State(i);
                bool inFlight = st == ShotState.InFlight;
                float age = inFlight ? 0f : now - ledger.Time(i), frac = 0f;
                if (inFlight) for (int s = 0; s < n; s++) if (shots[s].Id == ledger.Id(i)) frac = shots[s].Progress;
                cells[k] = new ShotCell { Id = ledger.Id(i), Name = ledger.Name(i), State = st, Frac = frac, AgeS = age };
                if (inFlight || age > ShotMath.ResultS || rc >= MaxResults) continue;
                if (!ledger.TryPlace(i, out float px, out float py, out float pz) && !LastKnown(ledger.Id(i), out px, out py, out pz)) continue;
                results[rc++] = new ShotResult { State = st, AgeS = age, Pos = new GPos(px, py, pz) };
                needM = Mathf.Max(needM, Flat(px - og.x, pz - og.z));
            }
            if (n == 0 && rc == 0) return false;

            AddSelectedTargets();
            cache = new ShotScope
            {
                Frame = frame, OwnAltM = og.y, MissileCount = n, Missiles = shots, TargetCount = targetCount, Targets = targets, ResultCount = rc, Results = results, CellCount = count, Cells = cells,
                NeedKm = Math.Max(1f, needM / 1000f),
            };
            if (lead >= 0 && shots[lead].Target >= 0)
            {
                ShotTarget t = targets[shots[lead].Target];
                frame.Polar(t.Pos.X, t.Pos.Z, out float brg, out float rng);
                cache.HasLead = true;
                cache.LeadName = t.Name;
                cache.LeadBearingDeg = brg;
                cache.LeadRangeM = rng;
            }
            return true;
        }

        private static float Flat(float dx, float dz) => Mathf.Sqrt(dx * dx + dz * dz);

        private ShotMissile ShotOf(ShotRec rec, Missile m, GlobalPosition og, in PlanFrame frame, float now, ref float needM)
        {
            GlobalPosition g = m.transform.position.ToGlobalPosition();
            Vector3 velocity = m.rb != null ? m.rb.velocity : Vector3.zero;
            Unit target = ShotBook.TargetOf(m);
            bool hasAim = false;
            GPos aim = default;
            float range = float.NaN, closure = float.NaN, tti = float.NaN;
            if (target != null)
            {
                GlobalPosition tg = target.transform.position.ToGlobalPosition();
                aim = new GPos(tg.x, tg.y, tg.z);
                hasAim = true;
                Vector3 los = target.transform.position - m.transform.position;
                range = los.magnitude;
                Vector3 tv = target.rb != null ? target.rb.velocity : Vector3.zero;
                closure = range > 1f ? -Vector3.Dot(tv - velocity, los / range) : float.NaN;
                // a guided bomb falls to its target's height; a missile closes along the line of sight
                tti = rec.Bomb ? ShotMath.FallTime(g.y - tg.y, velocity.y) : ShotMath.TimeToGo(range, closure);
            }
            else if (rec.Bomb)
            {
                // A bomb with nothing to steer to lands where gravity puts it.
                // ponytail: the ground's height is taken under our own aircraft (the game keeps no radar altitude for a falling bomb on clients) and drag is ignored; over hills or from
                // high and fast the splash point is a little off. Cast a ray under the bomb and fold in WeaponInfo.dragCoef if that shows.
                float height = Mathf.Max(0f, g.y - (og.y - aircraft.radarAlt)), fall = ShotMath.FallTime(height, velocity.y), reach = Flat(velocity.x, velocity.z) * fall;
                aim = new GPos(g.x + velocity.x * fall, g.y - height, g.z + velocity.z * fall);
                hasAim = true;
                range = Mathf.Sqrt(reach * reach + height * height);
                tti = fall;
            }
            ShotPhase phase = ShotMath.PhaseOf(rec.Bomb, m.EngineOn(), m.timeSinceSpawn, m.boosterIsAttached, ShotBook.Locked(rec, m, target != null), range);
            // how far the plan has to reach: this weapon and where it is going
            needM = Mathf.Max(needM, Flat(g.x - og.x, g.z - og.z));
            if (hasAim) needM = Mathf.Max(needM, Flat(aim.X - og.x, aim.Z - og.z));

            int tn = 0;
            for (; tn < rec.Path.RecentCount; tn++)
                rec.Path.Recent(tn, out rec.TrailOut[3 * tn], out rec.TrailOut[3 * tn + 1], out rec.TrailOut[3 * tn + 2]);
            return new ShotMissile
            {
                Id = rec.Id, Short = rec.Short, Full = rec.Full, Kind = rec.Bomb ? ShotKind.Bomb : ShotKind.Missile, Phase = phase, Arh = rec.Arh, Pitbull = rec.EverPitbull,
                PitbullS = rec.EverPitbull ? now - rec.PitbullAt : 999f, Ghost = rec.Lost, GhostS = rec.Lost ? now - rec.LostAt : 0f, Pos = new GPos(g.x, g.y, g.z),
                HeadingDeg = ThreatMath.Norm360(Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg - frame.HeadingDeg),
                // a weapon that lost its target flies at nothing we would call a target (a flare, a decoy)
                Target = target != null && !rec.Lost ? TargetIndex(target, true) : -1, HasAim = hasAim, Aim = aim, RangeM = range, ClosureMps = closure, TtiS = tti,
                Progress = hasAim ? ShotMath.Progress(Vector3.Distance(new Vector3(g.x, g.y, g.z), new Vector3(rec.Launch.X, rec.Launch.Y, rec.Launch.Z)), range) : 0f,
                Trail = rec.TrailOut, TrailCount = tn,
            };
        }

        /// <summary>The target's slot in the target list (made if new); its place and ground velocity are read now.</summary>
        private int TargetIndex(Unit u, bool hot)
        {
            int id = u.GetInstanceID();
            for (int i = 0; i < targetCount; i++)
                if (targetIds[i] == id) { if (hot) targets[i].Hot = true; return i; }
            if (targetCount >= MaxTargets) return -1;
            GlobalPosition g = u.transform.position.ToGlobalPosition();
            Vector3 v = u.rb != null ? u.rb.velocity : Vector3.zero;
            targetIds[targetCount] = id;
            targets[targetCount] = new ShotTarget { Id = id, Name = NameOf(u, id), Air = u is Aircraft || u is Missile, Hot = hot, Pos = new GPos(g.x, g.y, g.z), VelX = v.x, VelZ = v.z };
            return targetCount++;
        }

        /// <summary>The unit's name in capitals, built once per unit (a small ring of the last few).</summary>
        private string NameOf(Unit u, int id)
        {
            for (int i = 0; i < NameCache; i++) if (nameIds[i] == id && names[i] != null) return names[i];
            int at = nameNext++ % NameCache;
            nameIds[at] = id;
            return names[at] = u.definition != null && !string.IsNullOrEmpty(u.definition.unitName) ? u.definition.unitName.ToUpperInvariant() : "TGT";
        }

        /// <summary>The pilot's selected targets that no delivery flies at yet: the plan shows them dim so the pilot sees where the next shot would go.</summary>
        private void AddSelectedTargets()
        {
            var list = aircraft.weaponManager != null ? aircraft.weaponManager.GetTargetList() : null;
            for (int i = 0; list != null && i < list.Count; i++)
                if (list[i] != null && !list[i].disabled) TargetIndex(list[i], false);
        }

        private bool LastKnown(int id, out float x, out float y, out float z)
        {
            x = y = z = 0f;
            ShotRec rec = book.Of(id);
            if (rec == null || rec.Path.RecentCount == 0) return false;
            rec.Path.Recent(rec.Path.RecentCount - 1, out x, out y, out z);
            return true;
        }

        public void Clear()
        {
            live = false;
            aircraft = null;
            stamp = -1;
        }
    }
}
