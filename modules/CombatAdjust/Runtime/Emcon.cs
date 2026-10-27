using System.Collections.Generic;
using BoscaliSummer.Modules.CombatAdjust.Domain;

namespace BoscaliSummer.Modules.CombatAdjust.Runtime
{
    /// <summary>
    /// SAM EMCON (host). A ground radar an anti-radiation missile comes for switches off (<c>Radar.activated</c>, a plain unsynced field) and
    /// back on after; vanilla does the rest: ARMSeeker.TrackCurrentTarget returns null for a dark radar, so the missile drifts on its last
    /// estimate and re-evaluates emitters every 4.5 s. A dark radar also stops searching, so the site is blind while it hides.
    /// </summary>
    internal sealed class Emcon
    {
        private const float ReachMetres = 25000f, Chance = 0.65f;

        private readonly EmconBook book = new EmconBook();
        private readonly HashSet<uint> watched = new HashSet<uint>();
        private readonly HashSet<uint> hunted = new HashSet<uint>();
        private readonly List<uint> dark = new List<uint>(), light = new List<uint>(), scratch = new List<uint>();

        public void Tick(MissileBook missiles, float now, bool on)
        {
            if (!on)
            {
                ReleaseAll();
                return;
            }
            hunted.Clear();
            foreach (Missile m in missiles.Live)
            {
                if (m == null || m.disabled || !missiles.TryGet(m, out MissileBook.Entry e) || e.Kind != SeekerKind.AntiRadiation) continue;
                // Vanilla ARMSeeker.Seek calls SetTarget(null) the moment the radar goes dark, so targetID cannot say the missile is
                // still coming: the radar it darkened stays hunted for the missile's whole life (the 45 s cap still bounds it).
                if (e.EmconRadar != 0) hunted.Add(e.EmconRadar);
                if (e.EmconJudged) continue;
                if (!m.targetID.IsValid || !UnitRegistry.TryGetUnit(m.targetID, out Unit u) || u == null || u.disabled) continue;
                if (u is Aircraft || u is Ship || !(u.radar is Radar)) continue;
                if ((u.transform.position - m.transform.position).sqrMagnitude > ReachMetres * ReachMetres) continue;
                e.EmconJudged = true;
                uint uid = u.persistentID.Id, mid = MissileBook.Id(m);
                if (!CombatStats.ForceReactions && !Triggers.EmconReacts(mid, uid, Chance)) continue;
                e.EmconRadar = uid;
                hunted.Add(uid);
                book.Darken(uid, now, Triggers.EmconDelay(mid, uid));
                watched.Add(uid);
            }
            scratch.Clear();
            foreach (uint id in watched) scratch.Add(id);
            foreach (uint id in scratch)
            {
                if (hunted.Contains(id)) continue;
                book.MissileGone(id, now);
                if (!book.ShouldBeDark(id, now)) watched.Remove(id);
            }
            dark.Clear();
            light.Clear();
            book.Due(now, dark, light);
            foreach (uint id in dark) { Set(id, false); CombatStats.RadarsDark++; }
            foreach (uint id in light) Set(id, true);
            // Vanilla may switch a radar back on (re-attach, AI); hold it dark while it should be.
            foreach (uint id in scratch)
                if (book.ShouldBeDark(id, now)) Set(id, false);
        }

        /// <summary>Switches back on every radar this module switched off (rule off, scene end, teardown).</summary>
        public void ReleaseAll()
        {
            light.Clear();
            book.ReleaseAll(light);
            foreach (uint id in light) Set(id, true);
            light.Clear();
            watched.Clear();
        }

        private static void Set(uint unitId, bool on)
        {
            if (UnitRegistry.TryGetUnit(new PersistentID { Id = unitId }, out Unit u) && u != null && !u.disabled && u.radar is Radar radar && radar != null)
                radar.activated = on;
        }
    }
}
