using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Presentation;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Vanguard
{
    /// <summary>
    /// KESTREL sim hook (tests/ingame/vg-kestrel.json): Step {action:"kestrel", do:...}.
    ///   do=status    the generic status plus numeric glider telemetry under "kestrel" (the default); the first call that finds
    ///                the glider hands it to the scenario recorder as id "kestrel"
    ///   do=targets   x, z (map metres), offset (600): an enemy vehicle on flat ground near the point, designated for the lead,
    ///                and a second one `offset` metres from it
    ///   do=fit       the lead carries the sled mount alone on the first hardpoint set that offers it; the stations rebuild over the next seconds
    ///   do=fire      releases it through the station's own launch path (bay, rail, spawn); the designation made by `targets` is its objective
    ///   do=view      angle hold|close|glider|beam|off: chase camera (hold: behind the lead; close: on the glider, hold until it exists)
    ///   do=lasetest  standoff (0): a vanilla laser-seeker store at the lased unit; its miss distance lands in status
    /// </summary>
    public static partial class VanguardAutomation
    {
        private static Dictionary<string, object> Kestrel(Dictionary<string, object> args, Aircraft lead)
        {
            switch (Text(args, "do") ?? "status")
            {
                case "targets": return KestrelSim.Targets(lead, Number(args, "x", 0f), Number(args, "z", 0f), Number(args, "offset", 600f));
                case "fit": return KestrelSim.Fit(lead);
                case "fire": return KestrelSim.Fire(lead);
                case "view":
                    KestrelSim.Chase.Mode = Text(args, "angle") ?? "glider";
                    KestrelSim.Chase.Lead = lead;
                    if (KestrelSim.Chase.Mode != "off" && UnityEngine.Object.FindObjectOfType<KestrelSim.Chase>() == null)
                        new GameObject("KestrelChase").AddComponent<KestrelSim.Chase>();
                    return KestrelSim.Status(lead);
                case "lasetest": return KestrelSim.LaseTest(lead, Number(args, "standoff", 0f));
                case "status": return KestrelSim.Status(lead);
                default: return Failure("VanguardAutomation", "kestrel", "do must be status, targets, fit, fire, view or lasetest");
            }
        }

        /// <summary>Everything KESTREL adds to the sim hook lives here so it cannot collide with other loadouts' partials.</summary>
        private static class KestrelSim
        {
            private static readonly string[] Counters = { "launch", "orbit", "spot", "lase", "release", "depart", "lost" };
            private static Unit second;
            private static KestrelDrone glider;
            private static bool registered, lasetestFired, lasetestDone;
            private static float lasetestMiss = 99999f; // not yet died reads as a miss
            private static string lasetestKey = "";

            private static bool Alive(Unit unit) => unit != null && !unit.disabled;

            private static bool Offers(WeaponStation station) =>
                station?.WeaponInfo != null && station.WeaponInfo.weaponPrefab != null &&
                station.WeaponInfo.weaponPrefab.GetComponent<Missile>() is Missile m && m.definition != null && m.definition.jsonKey == VanguardKeys.Kestrel;

            private static KestrelDrone Glider()
            {
                if (glider != null && !glider.GetComponent<Missile>().disabled) return glider;
                glider = null;
                foreach (KestrelDrone found in UnityEngine.Object.FindObjectsOfType<KestrelDrone>())
                    if (!found.GetComponent<Missile>().disabled) { glider = found; break; }
                return glider;
            }

            public static Dictionary<string, object> Status(Aircraft lead)
            {
                Dictionary<string, object> state = VanguardAutomation.Status(lead);
                var k = new Dictionary<string, object>();
                foreach (string counter in Counters) k[counter] = VanguardStats.Get("kestrel." + counter);
                FactionHQ hq = lead != null ? lead.NetworkHQ : null;
                int ammo = 0, detached = 0;
                if (lead != null)
                {
                    foreach (WeaponStation station in lead.weaponStations) if (Offers(station)) ammo += station.Ammo;
                    foreach (UnitPart part in lead.partLookup) if (part != null && part.IsDetached()) detached++;
                }
                k["stationAmmo"] = ammo;
                k["carrierAlive"] = Alive(lead) ? 1 : 0;
                k["carrierDetached"] = detached;
                int rampOpen = 0;
                if (lead != null) foreach (CargoRamp ramp in lead.GetComponentsInChildren<CargoRamp>()) if (ramp.enabled) rampOpen++;
                k["rampOpen"] = rampOpen; // a latched ramp disables itself
                Unit first = groundTarget;
                k["firstAlive"] = Alive(first) ? 1 : 0;
                k["secondAlive"] = Alive(second) ? 1 : 0;
                // known = the faction has ever had a fix on it; tracked = the last fix is under 4 s old.
                k["knownFirst"] = hq != null && first != null && hq.GetTrackingData(first.persistentID) != null ? 1 : 0;
                k["knownSecond"] = hq != null && second != null && hq.GetTrackingData(second.persistentID) != null ? 1 : 0;
                k["trackedFirst"] = hq != null && first != null && hq.IsTargetBeingTracked(first) ? 1 : 0;
                k["trackedSecond"] = hq != null && second != null && hq.IsTargetBeingTracked(second) ? 1 : 0;
                k["firstLased"] = hq != null && first != null && hq.IsTargetLased(first) ? 1 : 0;
                k["lasetestFired"] = lasetestFired ? 1 : 0;
                k["lasetestDone"] = lasetestDone ? 1 : 0;
                k["lasetestMiss"] = lasetestMiss;
                k["lasetestKey"] = lasetestKey;
                KestrelDrone drone = Glider();
                k["alive"] = drone != null ? 1 : 0;
                if (drone != null)
                {
                    Missile m = drone.GetComponent<Missile>();
                    if (!registered)
                    {
                        registered = true;
                        state["ids"] = new Dictionary<string, object> { ["kestrel"] = m };
                    }
                    Unit lased = drone.Lased;
                    k["phase"] = (int)drone.Phase;
                    k["stationLeft"] = drone.StationLeft;
                    k["age"] = m.timeSinceSpawn;
                    k["speed"] = m.speed;
                    k["agl"] = m.radarAlt;
                    k["spotted"] = drone.Spotted;
                    k["lased"] = lased != null ? 1 : 0;
                    k["lasedName"] = lased != null && lased.definition != null ? lased.definition.unitName : "";
                    k["lasedIsDesignated"] = lased != null && lased == first ? 1 : 0;
                    k["lasedIsSecond"] = lased != null && lased == second ? 1 : 0;
                    // Measured against the orbit centre: the lase can move to another unit without the orbit moving.
                    GlobalPosition reference = drone.Centre;
                    Vector3 radial = m.GlobalPosition() - reference;
                    k["radius"] = new Vector2(radial.x, radial.z).magnitude;
                    k["height"] = radial.y;
                    if (lead != null) k["fromLead"] = FastMath.Distance(m.GlobalPosition(), lead.GlobalPosition());
                    KestrelVisuals visuals = m.GetComponent<KestrelVisuals>();
                    k["joints"] = visuals != null ? visuals.JointsFound : 0;
                    k["jointsWanted"] = KestrelVisuals.JointsWanted;
                    k["deployErr"] = visuals != null ? visuals.DeployError : 9999f;
                    k["deployed"] = visuals != null && visuals.JointsFound == KestrelVisuals.JointsWanted && visuals.DeployError < 1f ? 1 : 0;
                }
                state["kestrel"] = k;
                return state;
            }

            // An enemy Hexhound on flat ground: the point itself, else rings of 300 m steps around it.
            private static Unit Spawn(UnitDefinition def, Vector3 desired, Quaternion facing)
            {
                desired.y = Datum.LocalSeaY + 1400f; // DryGround probes from +500 m: start above any hill
                if (!GroundPlacement.TryPlace(def, desired, facing, out Vector3 point)) return null;
                return NetworkSceneSingleton<Spawner>.i.SpawnVehicle(def.unitPrefab, point.ToGlobalPosition(), facing, Vector3.zero, enemyHq, "VG_TestTarget", 1f, true, null);
            }

            public static Dictionary<string, object> Targets(Aircraft lead, float x, float z, float offset)
            {
                if (lead == null || enemyHq == null) return Failure("VanguardAutomation", "kestrel", "need lead and an enemy HQ");
                if (!Encyclopedia.Lookup.TryGetValue("UGV1_grenade", out UnitDefinition def))
                    return Failure("VanguardAutomation", "kestrel", "UGV1_grenade not in Encyclopedia.Lookup");
                Vector3 centre = new GlobalPosition(x, 0f, z).ToLocalPosition();
                Unit first = null;
                for (int ring = 0; ring <= 5 && first == null; ring++)
                    for (int a = 0; a < (ring == 0 ? 1 : 8) && first == null; a++)
                    {
                        Vector3 dir = Quaternion.Euler(0f, a * 45f, 0f) * Vector3.forward;
                        first = Spawn(def, centre + dir * (ring * 300f), Quaternion.LookRotation(dir));
                    }
                if (first == null) return Failure("VanguardAutomation", "kestrel", "no flat ground within 1.5 km of the map point");
                for (int b = 90; b < 450 && second == null; b += 30)
                {
                    Vector3 dir = Quaternion.Euler(0f, b, 0f) * Vector3.forward;
                    second = Spawn(def, first.transform.position + dir * offset, Quaternion.LookRotation(-dir));
                }
                if (second == null) return Failure("VanguardAutomation", "kestrel", "no flat ground " + offset + " m from the first target");
                groundTarget = first;
                DroneOrders.Order(lead, first); // the launcher's designation, made before the release
                Vector3 toFirst = first.transform.position - lead.transform.position, toSecond = second.transform.position - lead.transform.position;
                return new Dictionary<string, object>
                {
                    ["ok"] = true, ["rangeFirst"] = new Vector2(toFirst.x, toFirst.z).magnitude, ["rangeSecond"] = new Vector2(toSecond.x, toSecond.z).magnitude,
                    ["apart"] = Vector3.Distance(first.transform.position, second.transform.position),
                };
            }

            public static Dictionary<string, object> Fit(Aircraft lead)
            {
                if (lead == null) return Failure("VanguardAutomation", "kestrel", "no friendly aircraft");
                WeaponMount mount = null;
                foreach (WeaponMount w in Encyclopedia.i.weaponMounts)
                    if (w != null && w.jsonKey == VanguardKeys.KestrelMount) mount = w;
                if (mount == null) return Failure("VanguardAutomation", "kestrel", VanguardKeys.KestrelMount + " is not in the Encyclopedia");
                AdoptPrefabOptions(lead);
                HardpointSet[] sets = lead.weaponManager.hardpointSets;
                int set = Array.FindIndex(sets, s => s.weaponOptions.Contains(mount));
                if (set < 0) return Failure("VanguardAutomation", "kestrel", "no hardpoint set on " + lead.definition.unitName + " offers the sled mount");
                var loadout = new NuclearOption.SavedMission.Loadout { weapons = new List<WeaponMount>() };
                for (int i = 0; i < sets.Length; i++) loadout.weapons.Add(i == set ? mount : null);
                lead.Networkloadout = loadout;
                return new Dictionary<string, object> { ["ok"] = true, ["set"] = set, ["setName"] = sets[set].name };
            }

            // The station's own launch (MountedMissile.Fire: bay, rail, spawn), the way Drain fires a station. No target: the
            // glider takes the designation, so that path is the one under test. fired 0 = no loaded station yet.
            public static Dictionary<string, object> Fire(Aircraft lead)
            {
                if (lead == null) return Failure("VanguardAutomation", "kestrel", "no friendly aircraft");
                foreach (WeaponStation station in lead.weaponStations)
                {
                    if (!Offers(station) || station.Ammo <= 0) continue;
                    station.LaunchMount(lead, null, lead.GlobalPosition() + lead.transform.forward * 3000f);
                    return new Dictionary<string, object> { ["ok"] = true, ["fired"] = 1, ["station"] = (int)station.Number };
                }
                return new Dictionary<string, object> { ["ok"] = true, ["fired"] = 0 };
            }

            // A vanilla store with a LaserSeeker, aimed at the lased unit and given nothing else to go on: it only hits if the
            // glider's mark stands (sensors.md section 8). standoff > 0 starts it that many metres short of the unit at the lead's height.
            public static Dictionary<string, object> LaseTest(Aircraft lead, float standoff)
            {
                var report = new Dictionary<string, object> { ["ok"] = true, ["fired"] = 0 };
                KestrelDrone drone = Glider();
                Unit target = drone != null ? drone.Lased : null;
                if (lead == null || target == null) return report;
                MissileDefinition def = Find(m => m.unitPrefab.GetComponentInChildren<LaserSeeker>(true) != null);
                if (def == null) return report;
                Transform t = lead.transform;
                Vector3 origin = t.position - t.up * 3f + t.forward * 10f;
                if (standoff > 0f)
                {
                    Vector3 flat = target.transform.position - t.position;
                    flat.y = 0f;
                    origin = target.transform.position - flat.normalized * standoff;
                    origin.y = t.position.y;
                }
                Vector3 aim = (target.transform.position + Vector3.up - origin).normalized;
                Missile shot = NetworkSceneSingleton<Spawner>.i.SpawnMissile(def, origin, Quaternion.LookRotation(aim),
                    aim * Mathf.Max(lead.rb.velocity.magnitude, 100f), target, lead);
                lasetestKey = def.jsonKey;
                lasetestFired = true;
                lasetestDone = false;
                lasetestMiss = 99999f;
                Unit aimed = target;
                shot.onDisableUnit += _ =>
                {
                    lasetestDone = true;
                    if (aimed != null) lasetestMiss = FastMath.Distance(shot.GlobalPosition(), aimed.GlobalPosition());
                };
                report["fired"] = 1;
                report["key"] = def.jsonKey;
                return report;
            }

            /// <summary>
            /// Sim-only chase camera. hold: aft-starboard of the lead, on the cargo ramp. close: on the glider (the hold view
            /// until it exists), for the unfold. glider: outboard of the orbit, looking in. beam: a little down the laser line,
            /// beside it, so the line crosses the frame.
            /// </summary>
            [DefaultExecutionOrder(2000)]
            public sealed class Chase : MonoBehaviour
            {
                public static string Mode = "glider";
                public static Aircraft Lead;
                private KestrelDrone watched;
                private float nextFind;

                private void LateUpdate()
                {
                    CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
                    if (camera == null || Mode == "off") return;
                    // The search is a scene scan: four a second while there is no glider to follow.
                    if (Mode != "hold" && (watched == null || watched.GetComponent<Missile>().disabled) && Time.unscaledTime >= nextFind)
                    {
                        nextFind = Time.unscaledTime + .25f;
                        watched = Glider();
                    }
                    KestrelDrone drone = Mode != "hold" && watched != null && !watched.GetComponent<Missile>().disabled ? watched : null;
                    Vector3 eye, look;
                    if (drone == null)
                    {
                        if (Lead == null || Lead.disabled) return;
                        Transform l = Lead.transform;
                        eye = l.position - l.forward * 28f + l.up * 7f + l.right * 12f;
                        look = l.position - l.forward * 9f;
                    }
                    else
                    {
                        Transform t = drone.transform;
                        Unit lased = drone.Lased;
                        if (Mode == "close")
                        {
                            eye = t.position + t.right * 14f + t.up * 5f - t.forward * 8f;
                            look = t.position;
                        }
                        else if (Mode == "beam" && lased != null)
                        {
                            Vector3 line = lased.transform.position - t.position;
                            Vector3 side = Vector3.Cross(Vector3.up, line.normalized).normalized;
                            Vector3 pivot = t.position + line * .12f;
                            eye = pivot + side * 160f + Vector3.up * 40f;
                            look = pivot;
                        }
                        else
                        {
                            eye = t.position + t.right * 40f + Vector3.up * 14f - t.forward * 18f;
                            look = t.position - t.right * 25f - Vector3.up * 12f;
                        }
                    }
                    camera.SetCameraPosition(eye.ToGlobalPosition(), Quaternion.LookRotation(look - eye, Vector3.up));
                    camera.cameraVelocity = Vector3.zero;
                }
            }
        }
    }
}
