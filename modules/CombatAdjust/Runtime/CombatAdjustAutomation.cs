#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.CombatAdjust.Runtime;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.CombatAdjust
{
    /// <summary>
    /// nomodkit hook for tests/ingame/combat-*.json. <c>status</c> reads the CombatStats counters; the targeted scenarios spawn hostile
    /// armour, a radar vehicle or a ship (<c>armour</c>/<c>sam</c>/<c>ship</c>, named groups), fire one missile of a chosen seeker kind at
    /// each unit of a group (<c>fire</c>), force the reaction rolls (<c>force</c>), switch a rule live (<c>set</c>) and score a group (<c>results</c>).
    /// </summary>
    public static class CombatAdjustAutomation
    {
        private const string Source = "CombatAdjustAutomation";

        private sealed class Shot
        {
            public Unit Target;
            public bool Gone;
            public float Miss = -1f;
        }

        private static readonly Dictionary<string, List<Unit>> groups = new Dictionary<string, List<Unit>>();
        private static readonly Dictionary<string, List<Shot>> shots = new Dictionary<string, List<Shot>>();
        private static readonly Dictionary<string, string> fired = new Dictionary<string, string>();

        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            CombatAdjustService s = CombatAdjustService.Active;
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) || s == null || !GameAccess.IsServer())
                return Failure(Source, "Step", "requires a hosted nomodkit scenario with the combat module installed");
            string action = Text(args, "action") ?? "";
            switch (action)
            {
                case "status": return Status(s);
                case "force":
                    CombatStats.ForceReactions = !Has(args, "on") || Bool(args, "on");
                    return new Dictionary<string, object> { { "ok", true }, { "force", CombatStats.ForceReactions } };
                case "set": return Set(s, Text(args, "rule"), !Has(args, "on") || Bool(args, "on"));
                case "armour": return SpawnGround(args, true);
                case "sam": return SpawnGround(args, false);
                case "ship": return SpawnShip(args);
                case "fire": return Fire(args);
                case "results": return Results(Text(args, "group") ?? "");
                default: return Failure(Source, action, "unknown action");
            }
        }

        private static Dictionary<string, object> Status(CombatAdjustService s) => new Dictionary<string, object>
        {
            { "ok", true },
            { "externalCombat", s.ExternalCombat },
            { "missilesSeen", CombatStats.MissilesSeen },
            { "linkShared", CombatStats.LinkShared },
            { "linkDropped", CombatStats.LinkDropped },
            { "duds", CombatStats.Duds },
            { "wanders", CombatStats.Wanders },
            { "smokePops", CombatStats.SmokePops },
            { "smokeBlinds", CombatStats.SmokeBlinds },
            { "decoySalvos", CombatStats.DecoySalvos },
            { "seduced", CombatStats.Seduced },
            { "radarsDark", CombatStats.RadarsDark },
            { "expired", CombatStats.Expired }
        };

        private static Dictionary<string, object> Set(CombatAdjustService s, string rule, bool on)
        {
            switch (rule)
            {
                case "smoke": s.Settings.ArmourSmoke.Value = on; break;
                case "decoys": s.Settings.ShipDecoys.Value = on; break;
                case "emcon": s.Settings.SamEmcon.Value = on; break;
                case "link": s.Settings.LinkDiscipline.Value = on; break;
                case "fog": s.Settings.TrackFog.Value = on; break;
                default: return Failure(Source, "set", "unknown rule " + rule);
            }
            return new Dictionary<string, object> { { "ok", true }, { "rule", rule }, { "on", on } };
        }

        /// <summary>Armour: the heaviest armoured vehicle type with no radar. SAM: a vehicle type that carries a radar. Held in place.</summary>
        private static Dictionary<string, object> SpawnGround(Dictionary<string, object> args, bool armour)
        {
            FactionHQ hq = FactionRegistry.HqFromName(Text(args, "faction") ?? "Primeva");
            if (hq == null) return Failure(Source, armour ? "armour" : "sam", "no such faction");
            VehicleDefinition pick = null;
            foreach (VehicleDefinition d in Encyclopedia.i.vehicles)
            {
                if (d == null || d.unitPrefab == null || d.unitPrefab.GetComponent<GroundVehicle>() == null) continue;
                bool radar = d.unitPrefab.GetComponentInChildren<Radar>(true) != null;
                if (armour ? radar || d.armorTier <= 0f || (pick != null && d.armorTier <= pick.armorTier) : !radar || pick != null) continue;
                pick = d;
            }
            if (pick == null) return Failure(Source, armour ? "armour" : "sam", "no matching vehicle type");
            string group = Text(args, "group") ?? (armour ? "armour" : "sam");
            int count = Mathf.Clamp((int)Number(args, "count", 1f), 1, 8);
            float x = Number(args, "x", 15000f), z = Number(args, "z", -3000f);
            var list = new List<Unit>();
            var ids = new List<object>();
            for (int i = 0; i < count; i++)
            {
                if (!DryGround(x + i * 150f, z, out GlobalPosition at)) continue;
                GroundVehicle v = NetworkSceneSingleton<Spawner>.i.SpawnVehicle(pick.unitPrefab, at, Quaternion.Euler(0f, 90f, 0f), Vector3.zero,
                    hq, "CA_" + group + "_" + i, 1f, true, null);
                if (v == null) continue;
                list.Add(v);
                ids.Add(v.persistentID.Id);
            }
            groups[group] = list;
            return new Dictionary<string, object>
            {
                { "ok", list.Count > 0 }, { "type", pick.unitName }, { "armorTier", pick.armorTier }, { "spawned", list.Count }, { "ids", ids }
            };
        }

        private static Dictionary<string, object> SpawnShip(Dictionary<string, object> args)
        {
            FactionHQ hq = FactionRegistry.HqFromName(Text(args, "faction") ?? "Primeva");
            ShipDefinition def = null;
            foreach (ShipDefinition d in Encyclopedia.i.ships)
                if (d != null && d.unitPrefab != null && d.unitPrefab.GetComponent<Ship>() != null) { def = d; break; }
            if (hq == null || def == null) return Failure(Source, "ship", "no faction or no ship type");
            string group = Text(args, "group") ?? "ship";
            float x = Number(args, "x", 30000f), z = Number(args, "z", -30000f);
            for (int ring = 0; ring <= 8; ring++)
                for (int b = 0; b < 360; b += 30)
                {
                    float r = ring * 1500f;
                    float px = x + Mathf.Sin(b * Mathf.Deg2Rad) * r, pz = z + Mathf.Cos(b * Mathf.Deg2Rad) * r;
                    Vector3 p = new GlobalPosition(px, 0f, pz).ToLocalPosition();
                    p.y = Datum.LocalSeaY + 3000f;
                    bool land = Physics.Raycast(p, Vector3.down, out RaycastHit hit, 3500f, (int)PhysicsLayers.StaticsMask) && hit.point.y > Datum.LocalSeaY - 15f;
                    if (land) continue;
                    p.y = Datum.LocalSeaY;
                    Ship ship = NetworkSceneSingleton<Spawner>.i.SpawnShip(def.unitPrefab, p.ToGlobalPosition(), Quaternion.Euler(0f, 90f, 0f), hq, "CA_" + group, 1f, true);
                    groups[group] = new List<Unit> { ship };
                    return new Dictionary<string, object> { { "ok", ship != null }, { "type", def.unitName }, { "x", px }, { "z", pz } };
                }
            return Failure(Source, "ship", "no deep water near the point");
        }

        /// <summary>One missile per live unit of the group, from <c>range</c> m on <c>bearing</c>, <c>climb</c> m up, owned by a friendly aircraft.</summary>
        private static Dictionary<string, object> Fire(Dictionary<string, object> args)
        {
            string group = Text(args, "group") ?? "";
            if (!groups.TryGetValue(group, out List<Unit> targets)) return Failure(Source, "fire", "no group " + group);
            string kind = Text(args, "kind") ?? "optical";
            MissileDefinition def = PickMissile(kind);
            if (def == null) return Failure(Source, "fire", "no " + kind + " missile in the encyclopedia");
            FactionHQ ownerHq = FactionRegistry.HqFromName(Text(args, "owner") ?? "Boscali");
            Aircraft owner = null;
            foreach (Aircraft a in UnitRegistry.allAircraft)
                if (a != null && !a.disabled && a.NetworkHQ == ownerHq) { owner = a; break; }
            if (owner == null) return Failure(Source, "fire", "no friendly aircraft to own the missiles");
            float range = Number(args, "range", 2800f), climb = Number(args, "climb", 800f), speed = Number(args, "speed", 250f);
            Vector3 bearing = Quaternion.Euler(0f, Number(args, "bearing", 270f), 0f) * Vector3.forward;
            var list = new List<Shot>();
            foreach (Unit target in targets)
            {
                if (target == null || target.disabled) continue;
                ownerHq.RpcUpdateTrackingInfo(target.persistentID); // the launching side knows where it is, as a real launch needs
                Vector3 t = target.transform.position;
                Vector3 from = t + bearing * range + Vector3.up * climb;
                Missile m = NetworkSceneSingleton<Spawner>.i.SpawnMissile(def, from, Quaternion.LookRotation(t - from), (t - from).normalized * speed, target, owner);
                if (m == null) continue;
                var shot = new Shot { Target = target };
                m.onDisableUnit += _ =>
                {
                    shot.Gone = true;
                    if (shot.Target != null) shot.Miss = FastMath.Distance(m.GlobalPosition(), shot.Target.GlobalPosition());
                };
                list.Add(shot);
            }
            shots[group] = list;
            fired[group] = def.jsonKey;
            return new Dictionary<string, object>
            {
                { "ok", list.Count > 0 }, { "missile", def.jsonKey }, { "seeker", MissileBook.SeekerOf(def.unitPrefab.GetComponent<Missile>())?.GetType().Name ?? "none" },
                { "fired", list.Count }
            };
        }

        private static Dictionary<string, object> Results(string group)
        {
            groups.TryGetValue(group, out List<Unit> units);
            shots.TryGetValue(group, out List<Shot> list);
            int alive = 0, radarsOn = 0, gone = 0, missed = 0;
            float missSum = 0f;
            if (units != null)
                foreach (Unit u in units)
                {
                    if (u == null || u.disabled) continue;
                    alive++;
                    if (u.radar is Radar r && r != null && r.activated) radarsOn++;
                }
            if (list != null)
                foreach (Shot s in list)
                {
                    if (!s.Gone) continue;
                    gone++;
                    if (s.Miss >= 0f) missSum += s.Miss;
                    if (s.Miss > 25f) missed++;
                }
            return new Dictionary<string, object>
            {
                { "ok", true }, { "units", units?.Count ?? 0 }, { "alive", alive }, { "radarsOn", radarsOn },
                { "fired", list?.Count ?? 0 }, { "gone", gone }, { "missed", missed }, { "meanMiss", gone > 0 ? missSum / gone : -1f },
                { "missile", fired.TryGetValue(group, out string key) ? key : "" }
            };
        }

        private static MissileDefinition PickMissile(string kind)
        {
            MissileDefinition fallback = null;
            foreach (MissileDefinition d in Encyclopedia.i.missiles)
            {
                if (d == null || d.unitPrefab == null || d.jsonKey == null) continue;
                Missile prefab = d.unitPrefab.GetComponent<Missile>();
                MissileSeeker seeker = prefab != null ? MissileBook.SeekerOf(prefab) : null;
                bool agm = d.jsonKey.StartsWith("AGM", StringComparison.Ordinal);
                switch (kind)
                {
                    case "optical":
                        if (seeker != null && seeker.GetType() == typeof(OpticalSeeker)) { if (agm) return d; fallback = fallback ?? d; }
                        break;
                    case "laser":
                        if (seeker is LaserSeeker) return d;
                        break;
                    case "arm":
                        if (seeker is ARMSeeker) return d;
                        break;
                    case "ashm":
                        if (d.jsonKey.StartsWith("AShM", StringComparison.Ordinal)) { if (seeker is ARHSeeker) return d; fallback = fallback ?? d; }
                        break;
                }
            }
            return fallback;
        }

        private static bool DryGround(float x, float z, out GlobalPosition at)
        {
            Vector3 p = new GlobalPosition(x, 0f, z).ToLocalPosition();
            p.y = Datum.LocalSeaY + 5000f;
            if (Physics.Raycast(p, Vector3.down, out RaycastHit hit, 6000f, (int)PhysicsLayers.StaticsMask) && hit.point.y > Datum.LocalSeaY + 1f)
            {
                at = hit.point.ToGlobalPosition();
                return true;
            }
            at = default;
            return false;
        }
    }
}
#endif
