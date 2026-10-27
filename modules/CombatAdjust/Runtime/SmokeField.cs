using System.Collections.Generic;
using BoscaliSummer.Modules.CombatAdjust.Domain;
using BoscaliSummer.Modules.CombatAdjust.Visuals;
using UnityEngine;

namespace BoscaliSummer.Modules.CombatAdjust.Runtime
{
    /// <summary>
    /// Armour smoke. Every peer judges the same missiles with the same seeded rolls and pops the same clouds; the host's clouds are the ones
    /// that blind seekers (<see cref="Blind"/>) and eyes (<see cref="Blocks"/>). Clouds are kept as GlobalPositions (floating origin).
    /// </summary>
    internal sealed class SmokeField
    {
        public const int Charges = 2, MaxClouds = 16;
        private const float TriggerRange = 3000f, ReactChance = 0.7f, AheadMetres = 15f, RepopSeconds = 20f;

        private struct Cloud
        {
            public GlobalPosition Centre;
            public float Born;
            public GameObject Visual;
        }

        private struct Pending
        {
            public uint Vehicle;
            public float At;
            public Vector3 TowardThreat;
        }

        private readonly Cloud[] clouds = new Cloud[MaxClouds];
        private readonly List<Pending> pending = new List<Pending>();
        private readonly Dictionary<uint, int> used = new Dictionary<uint, int>();
        private readonly Dictionary<uint, float> lastPop = new Dictionary<uint, float>();

        public SmokeField() => Clear();

        public bool Any { get; private set; }

        /// <summary>Every peer, 5 Hz: judge new threats once, pop due smoke, retire spent clouds.</summary>
        public void Trigger(MissileBook book, float now)
        {
            foreach (Missile m in book.Live)
            {
                if (m == null || !book.TryGet(m, out MissileBook.Entry e) || e.SmokeJudged) continue;
                if (e.Kind != SeekerKind.Laser && e.Kind != SeekerKind.Ir && e.Kind != SeekerKind.Optical) { e.SmokeJudged = true; continue; }
                if (!m.targetID.IsValid || !UnitRegistry.TryGetUnit(m.targetID, out Unit u) || !(u is GroundVehicle vehicle) || vehicle.disabled) continue;
                uint vid = vehicle.persistentID.Id;
                bool armoured = vehicle.definition != null && vehicle.definition.armorTier > 0f;
                if (!Triggers.SmokeEligible(e.Kind, armoured, Charges - Used(vid))) { e.SmokeJudged = true; continue; }
                Vector3 toVehicle = vehicle.transform.position - m.transform.position;
                if (toVehicle.sqrMagnitude > TriggerRange * TriggerRange) continue;
                if (m.rb != null && Vector3.Dot(m.rb.velocity, toVehicle) <= 0f) continue;
                e.SmokeJudged = true;
                uint mid = MissileBook.Id(m);
                if (CombatStats.ForceReactions || Triggers.Reacts(mid, vid, ReactChance))
                    pending.Add(new Pending { Vehicle = vid, At = now + Triggers.ReactDelay(mid, vid), TowardThreat = -toVehicle });
            }
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].At > now) continue;
                Pending p = pending[i];
                pending.RemoveAt(i);
                Pop(p, now);
            }
            Any = false;
            for (int i = 0; i < MaxClouds; i++)
            {
                if (clouds[i].Born < 0f) continue;
                if (now - clouds[i].Born >= Triggers.CloudGoneAt) clouds[i] = new Cloud { Born = -1f };
                else Any = true;
            }
        }

        /// <summary>Host, 10 Hz: a laser/TV/IR missile whose line to its target crosses a cloud aims 20-50 m off while blocked, plus 1 s.</summary>
        public void Blind(MissileBook book, float now)
        {
            foreach (Missile m in book.Live)
            {
                if (m == null || !book.TryGet(m, out MissileBook.Entry e)) continue;
                if (e.Kind != SeekerKind.Laser && e.Kind != SeekerKind.Ir && e.Kind != SeekerKind.Optical) continue;
                bool blocked = Any && m.targetID.IsValid && UnitRegistry.TryGetUnit(m.targetID, out Unit target) && target != null &&
                    Blocks(m.transform.position, target.transform.position);
                if (blocked)
                {
                    e.SmokeUntil = now + 1f;
                    if (e.Smoke == Vector3.zero) CombatStats.SmokeBlinds++;
                    uint mid = MissileBook.Id(m);
                    float angle = Seeded.Roll01(mid, 0x5309u, 0) * 2f * Mathf.PI + 0.3f * now;
                    float length = Seeded.Range(mid, 0x5309u, 1, 20f, 50f);
                    e.Smoke = new Vector3(Mathf.Cos(angle) * length, 0f, Mathf.Sin(angle) * length);
                    book.Touch(m, e);
                }
                else if (e.Smoke != Vector3.zero && now > e.SmokeUntil)
                {
                    e.Smoke = Vector3.zero;
                    book.Touch(m, e);
                }
            }
        }

        public bool Blocks(Vector3 from, Vector3 to)
        {
            if (!Any) return false;
            float now = Time.timeSinceLevelLoad;
            for (int i = 0; i < MaxClouds; i++)
            {
                if (clouds[i].Born < 0f) continue;
                float radius = Triggers.CloudRadius(now - clouds[i].Born);
                if (radius > 0f && Triggers.SegmentHitsSphere(from, to, clouds[i].Centre.ToLocalPosition(), radius)) return true;
            }
            return false;
        }

        public void Clear()
        {
            for (int i = 0; i < MaxClouds; i++)
            {
                if (clouds[i].Visual != null) Object.Destroy(clouds[i].Visual);
                clouds[i] = new Cloud { Born = -1f };
            }
            pending.Clear();
            used.Clear();
            lastPop.Clear();
            Any = false;
        }

        private void Pop(Pending p, float now)
        {
            if (!UnitRegistry.TryGetUnit(new PersistentID { Id = p.Vehicle }, out Unit vehicle) || vehicle == null || vehicle.disabled) return;
            if (Used(p.Vehicle) >= Charges) return;
            if (lastPop.TryGetValue(p.Vehicle, out float last) && now - last < RepopSeconds) return;
            used[p.Vehicle] = Used(p.Vehicle) + 1;
            lastPop[p.Vehicle] = now;
            Vector3 toward = p.TowardThreat;
            toward.y = 0f;
            toward = toward.sqrMagnitude > 1f ? toward.normalized : vehicle.transform.forward;
            GlobalPosition centre = vehicle.GlobalPosition() + toward * AheadMetres + Vector3.up * 3f;
            int slot = Oldest();
            if (clouds[slot].Visual != null) Object.Destroy(clouds[slot].Visual);
            clouds[slot] = new Cloud { Centre = centre, Born = now, Visual = SmokeCloudVisual.Screen(centre) };
            Any = true;
            CombatStats.SmokePops++;
        }

        private int Oldest()
        {
            int best = 0;
            for (int i = 0; i < MaxClouds; i++)
            {
                if (clouds[i].Born < 0f) return i;
                if (clouds[i].Born < clouds[best].Born) best = i;
            }
            return best;
        }

        private int Used(uint vehicle) => used.TryGetValue(vehicle, out int n) ? n : 0;
    }
}
