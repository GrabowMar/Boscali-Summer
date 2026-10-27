#if !PUBLIC_RELEASE
using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Vanguard
{
    /// <summary>
    /// Sim hook for ORCA SLEEPER (tests/ingame/vg-orca.json):
    ///   orca do=lay [from]           ORCA with no lock from the lead along the first bearing whose DropRange point is
    ///                                deep water (>45 m) and clear of the test ship; tracked as VG_Orca_sleeper.
    ///   orca do=friend|enemy [offset, type]   a hull of that side `offset` m from the sleeper (holds position).
    /// </summary>
    public static partial class VanguardAutomation
    {
        private const string SleeperKey = "VG_Orca_sleeper";
        private static Ship sleeperPrey;

        private static Dictionary<string, object> Orca(Dictionary<string, object> args, Aircraft lead)
        {
            string type = Text(args, "type") ?? "PatrolBoat1";
            switch (Text(args, "do") ?? "lay")
            {
                case "lay": return LaySleeper(lead, (int)Number(args, "from", 0f));
                case "friend": return HullNearSleeper(friendHq, Number(args, "offset", 600f), type, 0, "VG_OrcaFriend");
                case "enemy": return HullNearSleeper(enemyHq, Number(args, "offset", 900f), type, 180, "VG_OrcaEnemy");
                default: return Failure("VanguardAutomation", "orca", "do must be lay, friend or enemy");
            }
        }

        private static Dictionary<string, object> LaySleeper(Aircraft lead, int from)
        {
            if (lead == null) return Failure("VanguardAutomation", "orca", "no friendly aircraft");
            MissileDefinition def = Find(m => m.jsonKey == VanguardKeys.Orca);
            if (def == null) return Failure("VanguardAutomation", "orca", "VG_Orca not in the encyclopedia");
            float drop = OrcaSleeper.DropRange(lead.GlobalPosition().y);
            Vector3 fwd = lead.transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            for (int b = from; b < from + 360; b += 22)
            {
                Vector3 dir = Quaternion.Euler(0f, b, 0f) * fwd;
                Vector3 p = lead.transform.position + dir * drop;
                if (!DeepWater(p, 45f)) continue;
                if (testShip != null && !testShip.disabled &&
                    Vector3.ProjectOnPlane(testShip.transform.position - p, Vector3.up).magnitude < OrcaSleeper.AttackRadius + 1000f) continue;
                // The launch heading sets the bearing; the flight itself picks the drop point (DropRange ahead).
                Missile shot = NetworkSceneSingleton<Spawner>.i.SpawnMissile(def, lead.transform.position - Vector3.up * 30f,
                    Quaternion.LookRotation(dir), dir * Mathf.Max(lead.rb.velocity.magnitude, 150f), null, lead);
                Launched[SleeperKey] = shot;
                TopSpeed[SleeperKey] = 0f;
                sleeperPrey = null;
                shot.onDisableUnit += _ =>
                {
                    Deaths[SleeperKey + "_deathAge"] = shot.timeSinceSpawn;
                    Deaths[SleeperKey + "_deathAltitude"] = shot.GlobalPosition().y;
                    if (sleeperPrey != null) Deaths[SleeperKey + "_deathRange"] = FastMath.Distance(shot.GlobalPosition(), sleeperPrey.GlobalPosition());
                };
                // Seed the counters the scenario bounds from above so "never happened" reads 0, not missing.
                foreach (string counter in new[] { "orca.set", "orca.wake", "orca.hit", "orca.expired" }) VanguardStats.Bump(counter, 0);
                return new Dictionary<string, object> { ["ok"] = shot != null, ["bearing"] = b, ["drop"] = drop };
            }
            return Failure("VanguardAutomation", "orca", "no deep water " + drop.ToString("F0") + " m from the lead");
        }

        private static Dictionary<string, object> HullNearSleeper(FactionHQ hq, float offset, string type, int from, string name)
        {
            if (!Launched.TryGetValue(SleeperKey, out Missile sleeper) || sleeper == null || sleeper.disabled)
                return Failure("VanguardAutomation", "orca", "no live sleeper (lay first)");
            if (hq == null) return Failure("VanguardAutomation", "orca", "no faction for the hull");
            if (!Encyclopedia.Lookup.TryGetValue(type, out UnitDefinition def))
                return Failure("VanguardAutomation", "orca", type + " not in Encyclopedia.Lookup");
            Vector3 centre = sleeper.transform.position;
            for (int b = from; b < from + 360; b += 30)
            {
                Vector3 p = centre + Quaternion.Euler(0f, b, 0f) * Vector3.forward * offset;
                if (!DeepWater(p, 15f)) continue;
                p.y = Datum.LocalSeaY;
                Ship ship = NetworkSceneSingleton<Spawner>.i.SpawnShip(def.unitPrefab, p.ToGlobalPosition(),
                    Quaternion.Euler(0f, b + 90f, 0f), hq, name, 1f, true);
                if (hq == enemyHq) sleeperPrey = ship;
                return new Dictionary<string, object> { ["ok"] = ship != null, ["bearing"] = b, ["offset"] = offset };
            }
            return Failure("VanguardAutomation", "orca", "no deep water " + offset.ToString("F0") + " m from the sleeper");
        }

        private static bool DeepWater(Vector3 p, float depth)
        {
            p.y = Datum.LocalSeaY + 3000f; // probe from above any hill
            return !(Physics.Raycast(p, Vector3.down, out RaycastHit hit, 3500f, (int)PhysicsLayers.StaticsMask) &&
                hit.point.y > Datum.LocalSeaY - depth);
        }
    }
}
#endif
