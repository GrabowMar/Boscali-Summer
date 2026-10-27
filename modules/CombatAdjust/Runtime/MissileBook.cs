using System.Collections.Generic;
using BoscaliSummer.Modules.CombatAdjust.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.CombatAdjust.Runtime
{
    /// <summary>
    /// Every missile alive on this peer and what the module decided about it. The aim offsets (seeker failure, smoke, decoy) are summed
    /// here; <see cref="TryOffset"/> is O(1) and allocation-free while no missile carries one.
    /// </summary>
    internal sealed class MissileBook
    {
        internal sealed class Entry
        {
            public SeekerKind Kind;
            /// <summary>The seeker sets a proximity fuse (ARH, SARH, ARM, IR, OpticalSeeker); others cannot have a dud fuse.</summary>
            public bool Fusable;
            /// <summary>The last valid target. Vanilla clears targetID (ARH terminal mode, ARM on a dark radar) while the seeker keeps homing.</summary>
            public PersistentID LastTarget;
            /// <summary>The radar this anti-radiation missile made go dark; it stays hunted for the missile's life, whatever its targetID says.</summary>
            public uint EmconRadar;
            public bool Dud;
            public float FailAt = -1f;
            public Vector3 FailDirection;
            public Vector3 Fault, Smoke, Decoy;
            public float SmokeUntil;
            public bool SmokeJudged, EmconJudged, Seduced;
            public int DecoyStage, DecoyJudgedSalvo = -1;
            public GlobalPosition DecoyPoint;
        }

        private readonly List<Missile> live = new List<Missile>(64);
        private readonly Dictionary<Missile, Entry> entries = new Dictionary<Missile, Entry>();
        private readonly HashSet<Missile> offset = new HashSet<Missile>();

        public List<Missile> Live => live;

        public Entry Add(Missile m)
        {
            if (m == null) return null;
            if (entries.TryGetValue(m, out Entry e)) return e;
            MissileSeeker seeker = SeekerOf(m);
            e = new Entry
            {
                Kind = KindOf(seeker),
                Fusable = seeker is ARHSeeker || seeker is SARHSeeker || seeker is ARMSeeker || seeker is IRSeeker ||
                    (seeker != null && seeker.GetType() == typeof(OpticalSeeker)),
                LastTarget = m.targetID
            };
            entries.Add(m, e);
            live.Add(m);
            return e;
        }

        public bool TryGet(Missile m, out Entry e) => entries.TryGetValue(m, out e);

        public void Prune()
        {
            for (int i = live.Count - 1; i >= 0; i--)
            {
                Missile m = live[i];
                if (m != null && !m.disabled)
                {
                    if (m.targetID.IsValid && entries.TryGetValue(m, out Entry alive)) alive.LastTarget = m.targetID;
                    continue;
                }
                int last = live.Count - 1;
                live[i] = live[last];
                live.RemoveAt(last);
                entries.Remove(m);
                offset.Remove(m);
            }
        }

        /// <summary>Call after changing an entry's offsets.</summary>
        public void Touch(Missile m, Entry e)
        {
            if (e.Fault != Vector3.zero || e.Smoke != Vector3.zero || e.Decoy != Vector3.zero) offset.Add(m);
            else offset.Remove(m);
        }

        public bool TryOffset(Missile m, out Vector3 sum)
        {
            sum = default;
            if (offset.Count == 0 || !offset.Contains(m) || !entries.TryGetValue(m, out Entry e)) return false;
            sum = e.Fault + e.Smoke + e.Decoy;
            return true;
        }

        public void Clear()
        {
            live.Clear();
            entries.Clear();
            offset.Clear();
        }

        /// <summary>The id every peer seeds with (instance id only if the persistent id is not assigned yet).</summary>
        public static uint Id(Missile m) => m.persistentID.Id != 0 ? m.persistentID.Id : unchecked((uint)m.GetInstanceID());

        public static MissileSeeker SeekerOf(Missile m)
        {
            MissileSeeker s = m.GetComponent<MissileSeeker>();
            return s != null ? s : m.GetComponentInChildren<MissileSeeker>(true);
        }

        private static SeekerKind KindOf(MissileSeeker s)
        {
            if (s == null) return SeekerKind.None;
            if (s is ARMSeeker) return SeekerKind.AntiRadiation;
            if (s is ARHSeeker || s is SARHSeeker) return SeekerKind.Radar;
            if (s is IRSeeker) return SeekerKind.Ir;
            if (s is LaserSeeker) return SeekerKind.Laser;
            if (s is InertialSeekerShell || s is OpticalSeekerShell) return SeekerKind.Shell;
            if (s is OpticalSeeker || s is OpticalSeekerCruiseMissile || s is OpticalSeekerBomb || s is OpticalSeekerHighDrag) return SeekerKind.Optical;
            return SeekerKind.None;
        }
    }
}
