using System.Collections.Generic;
using BoscaliSummer.Modules.CombatAdjust.Domain;
using BoscaliSummer.Modules.CombatAdjust.Visuals;
using UnityEngine;

namespace BoscaliSummer.Modules.CombatAdjust.Runtime
{
    /// <summary>
    /// Ship decoys. Vanilla ships are not IRadarReturn, so no seeker ever hears a ship's chaff; this fires the salvo itself. Every peer fires
    /// the same salvos (a guided missile closing on the ship crosses 6 km, then 2.5 km; 6 per ship, 8 s apart); the host rolls each inbound
    /// missile within 8 km once per salvo and holds a seduced one's aim on the decoy point off the ship's beam.
    /// </summary>
    internal sealed class ShipDecoys
    {
        public const int Salvos = 6;
        private const float FarMetres = 6000f, NearMetres = 2500f, ReachMetres = 8000f, GapSeconds = 8f, JudgeWindow = 2f;

        private sealed class ShipState
        {
            public int Fired;
            public float Last = -999f;
            public GlobalPosition Point;
        }

        private readonly Dictionary<uint, ShipState> ships = new Dictionary<uint, ShipState>();

        public void Tick(MissileBook book, float now, bool host)
        {
            foreach (Missile m in book.Live)
            {
                if (m == null || !book.TryGet(m, out MissileBook.Entry e) || e.DecoyStage >= 2 || !Triggers.DecoyEligible(e.Kind)) continue;
                if (!m.targetID.IsValid || !UnitRegistry.TryGetUnit(m.targetID, out Unit u) || !(u is Ship ship) || ship.disabled) continue;
                Vector3 toShip = ship.transform.position - m.transform.position;
                if (m.rb != null && Vector3.Dot(m.rb.velocity, toShip) <= 0f) continue;
                float d2 = toShip.sqrMagnitude;
                int stage = d2 < NearMetres * NearMetres ? 2 : d2 < FarMetres * FarMetres ? 1 : 0;
                if (stage <= e.DecoyStage) continue;
                e.DecoyStage = stage;
                Salvo(ship, now);
            }
            if (host) Seduce(book, now);
        }

        public void Clear() => ships.Clear();

        private void Seduce(MissileBook book, float now)
        {
            foreach (Missile m in book.Live)
            {
                if (m == null || !book.TryGet(m, out MissileBook.Entry e) || !Triggers.DecoyEligible(e.Kind)) continue;
                if (!m.targetID.IsValid || !UnitRegistry.TryGetUnit(m.targetID, out Unit u) || !(u is Ship ship) || ship.disabled) continue;
                if (!e.Seduced && ships.TryGetValue(ship.persistentID.Id, out ShipState s) && s.Fired > 0 && now - s.Last < JudgeWindow)
                {
                    int salvo = s.Fired - 1;
                    if (e.DecoyJudgedSalvo < salvo && (ship.transform.position - m.transform.position).sqrMagnitude <= ReachMetres * ReachMetres)
                    {
                        e.DecoyJudgedSalvo = salvo;
                        if (CombatStats.ForceReactions || Seeded.Roll01(MissileBook.Id(m), ship.persistentID.Id, (uint)salvo) < Triggers.DecoyChance(e.Kind))
                        {
                            e.Seduced = true;
                            e.DecoyPoint = s.Point;
                            CombatStats.Seduced++;
                        }
                    }
                }
                if (!e.Seduced) continue;
                e.Decoy = e.DecoyPoint - ship.GlobalPosition();
                book.Touch(m, e);
            }
        }

        private void Salvo(Ship ship, float now)
        {
            uint sid = ship.persistentID.Id;
            if (!ships.TryGetValue(sid, out ShipState s)) ships[sid] = s = new ShipState();
            if (s.Fired >= Salvos || now - s.Last < GapSeconds) return;
            float side = Seeded.Roll01(sid, 0xDEC0u, (uint)s.Fired) < 0.5f ? -1f : 1f;
            float distance = Seeded.Range(sid, 0xDEC1u, (uint)s.Fired, 150f, 300f);
            Vector3 beam = ship.transform.right;
            beam.y = 0f;
            beam = (beam.sqrMagnitude > 1e-4f ? beam.normalized : Vector3.right) * (side * distance);
            s.Point = ship.GlobalPosition() + beam + Vector3.up * 20f;
            s.Fired++;
            s.Last = now;
            CombatStats.DecoySalvos++;
            SmokeCloudVisual.Decoy(s.Point);
        }
    }
}
