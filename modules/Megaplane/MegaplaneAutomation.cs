#if !PUBLIC_RELEASE
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Readback and damage hooks for the megaplane sandbox scenarios (tests/ingame/megaplane-*.json).</summary>
    public static class MegaplaneAutomation
    {
        public static object Status(Dictionary<string, object> args)
        {
            if (!(args.TryGetValue("idUnit", out object value) && value is Aircraft a))
                return new { registered = MegaplaneTemplate.Definition != null, error = MegaplaneTemplate.LastError };
            var flight = a.GetComponent<MegaplaneFlight>();
            ControlInputs input = a.GetInputs();
            Vector3 e = a.transform.eulerAngles;
            return new
            {
                registered = true, speed = a.rb.velocity.magnitude, altitude = a.GlobalPosition().y, radarAlt = a.radarAlt,
                climbRate = a.rb.velocity.y, heading = e.y,
                slip = Mathf.Abs(Mathf.DeltaAngle(e.y, Mathf.Atan2(a.rb.velocity.x, a.rb.velocity.z) * Mathf.Rad2Deg)), pitch = Mathf.DeltaAngle(0, e.x), bank = Mathf.DeltaAngle(0, e.z),
                holdHeading = flight.Heading, holdAltitude = flight.HoldAltitude, thrust = flight.Thrust,
                mass = a.rb.mass, simple = HarmonyLib.Traverse.Create(a).Field<bool>("simplePhysics").Value,
                bodies = a.GetComponentsInChildren<Rigidbody>().Length, disabled = a.disabled,
                gear = a.gearState.ToString(), input = new { input.pitch, input.roll, input.yaw, input.throttle },
                guns = a.GetComponentsInChildren<Turret>(true).Length, gunsPicked = MegaplaneGuns.Picked,
                sideGuns = a.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith(MegaplaneGuns.Prefix + "Side_")),
                sideStations = a.weaponStations.Count(s => s.WeaponInfo == MegaplaneGuns.Bastion), sideAmmo = a.weaponStations.Where(s => s.WeaponInfo == MegaplaneGuns.Bastion).Sum(s => s.Ammo),
                railStations = a.weaponStations.Count(s => s.WeaponInfo == MegaplaneGuns.Trident),
                stationNames = a.weaponStations.Select(s => s.WeaponInfo != null ? s.WeaponInfo.weaponName : "-").ToArray(),
                lift = flight.Lift, liftBanks = flight.LiftBanks,
                turretStations = a.weaponStations.Count(s => s.HasTurret()), stations = a.weaponStations.Count,
                silos = a.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith(MegaplaneGuns.Prefix + "SiloMount_")), siloPicked = MegaplaneGuns.PickedSilo,
                missileStations = a.weaponStations.Count(s => s.Weapons.Count > 0 && s.Weapons[0] is MissileLauncher),
                missileAmmo = a.weaponStations.Where(s => s.Weapons.Count > 0 && s.Weapons[0] is MissileLauncher).Sum(s => s.Ammo),
                missileNames = a.weaponStations.Where(s => s.Weapons.Count > 0 && s.Weapons[0] is MissileLauncher).Select(s => s.WeaponInfo != null ? s.WeaponInfo.weaponName : "-").Distinct().ToArray(),
                mounts = a.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(MegaplaneGuns.Prefix)).Select(t => t.name).ToArray(),
                gearDown = a.GetComponent<MegaplaneGear>() is MegaplaneGear g ? g.Leg : -1f, gearLegs = a.GetComponent<MegaplaneGear>() is MegaplaneGear g2 ? g2.Legs : 0,
                nan = float.IsNaN(a.rb.velocity.x) || float.IsNaN(a.transform.position.x),
            };
        }

        /// <summary>Command the undercarriage ("down" true/false) and read where the animation is.</summary>
        public static object Gear(Dictionary<string, object> args)
        {
            if (!(args.TryGetValue("idUnit", out object value) && value is Aircraft a) || !(a.GetComponent<MegaplaneGear>() is MegaplaneGear g)) return new { ok = false };
            if (args.TryGetValue("down", out object d)) a.SetGear(System.Convert.ToBoolean(d));
            return new { ok = true, legs = g.Legs, leg = g.Leg, door = g.Door, deployed = a.NetworkgearDeployed, state = a.gearState.ToString() };
        }

        /// <summary>Belly railgun: "down" true/false forces the mount down or up, absent = automatic; "select" true makes it
        /// the current station; "fire" true pulls its trigger once at the current target. Reads the mount state.</summary>
        public static object Railgun(Dictionary<string, object> args)
        {
            if (!(args.TryGetValue("idUnit", out object value) && value is Aircraft a) || !(a.GetComponent<MegaplaneRailgun>() is MegaplaneRailgun r)) return new { ok = false };
            if (args.TryGetValue("down", out object d)) r.Command = d == null ? (bool?)null : System.Convert.ToBoolean(d);
            WeaponStation st = r.Turret != null ? r.Turret.GetWeaponStation() : null;
            Unit target = r.Turret != null ? r.Turret.GetTarget() : null;
            bool fired = false;
            if (args.TryGetValue("fire", out object f) && System.Convert.ToBoolean(f) && st != null && r.Live && st.Ready())
            {
                st.Fire(a, target);
                fired = true;
            }
            return new { ok = true, door = r.Door, lower = r.Lower, live = r.Live, artAim = V(r.ArtAim), nativeAim = V(r.NativeAim), pivot = V(r.MuzzleLocal), ammo = st != null ? st.Ammo : -1, fired, target = target != null ? target.unitName : null };
        }

        private static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };

        /// <summary>Destroy the damage islands carrying the named art nodes (e.g. "Pod_L1,Pod_L2"): an engine-loss test.</summary>
        public static object Detach(Dictionary<string, object> args)
        {
            if (!(args.TryGetValue("idUnit", out object value) && value is Aircraft a)) return new { ok = false };
            var names = ((string)args["nodes"]).Split(',');
            var hit = a.GetComponentsInChildren<Transform>(true).Where(t => names.Contains(t.name))
                .Select(t => t.GetComponentInParent<AeroPart>()).Where(p => p != null && p.gameObject != a.gameObject).Distinct().ToList();
            foreach (AeroPart part in hit) part.Detach(a.rb.velocity, Vector3.zero);
            return new { ok = hit.Count > 0, parts = hit.Select(p => p.name).ToArray() };
        }

        /// <summary>Belly fighter bay: start a drop ("slot" 0 front, 1 aft, -1 next) and/or read the bay state.</summary>
        public static object Bay(Dictionary<string, object> args)
        {
            if (!(args.TryGetValue("idUnit", out object value) && value is Aircraft a) || !(a.GetComponent<MegaplaneBay>() is MegaplaneBay bay)) return new { ok = false };
            bool started = args.TryGetValue("slot", out object slot) && bay.Drop(System.Convert.ToInt32(slot));
            int recalled = args.TryGetValue("recall", out object r) && System.Convert.ToBoolean(r) ? bay.Recall() : 0;
            return new
            {
                ok = true, started, recalled, door = bay.Door, stowed = bay.State.Count(x => x == 0),
                capturePhase = bay.Released.Select(f => f != null && f.GetComponent<MegaplaneCapture>() is MegaplaneCapture cap ? cap.Phase : -1).ToArray(),
                releasedAlive = bay.Released.Count(r => r != null && !r.disabled), releasedFriendly = bay.Released.Count(r => r != null && r.NetworkHQ == a.NetworkHQ), arm = bay.Arm, state = bay.State, fighter = bay.FighterKey,
                visuals = a.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("BayFighter_") && t.gameObject.activeSelf),
                released = bay.Released.Select(r => r == null ? null : new { r.unitName, alive = !r.disabled, speed = r.rb != null ? r.rb.velocity.magnitude : 0f,
                    below = a.transform.InverseTransformPoint(r.transform.position).y, sameSide = r.NetworkHQ == a.NetworkHQ }).ToArray(),
            };
        }

        /// <summary>Close-up shot camera: places the main camera at a point in the carrier's local frame (metres; x right,
        /// y up, z forward), aimed at another local point, with a field of view. Runs after the vanilla camera each frame.</summary>
        public static object View(Dictionary<string, object> args)
        {
            if (!(args.TryGetValue("idUnit", out object value) && value is Aircraft a) || Camera.main == null) return new { ok = false };
            float F(string k) => System.Convert.ToSingle(args[k]);
            ShotCam shot = Camera.main.GetComponent<ShotCam>() ?? Camera.main.gameObject.AddComponent<ShotCam>();
            shot.target = a.transform;
            shot.offset = new Vector3(F("x"), F("y"), F("z"));
            shot.look = new Vector3(F("lx"), F("ly"), F("lz"));
            shot.fov = F("fov");
            return new { ok = true };
        }

        [DefaultExecutionOrder(30000)]
        private sealed class ShotCam : MonoBehaviour
        {
            public Transform target;
            public Vector3 offset, look;
            public float fov;

            private void LateUpdate()
            {
                if (target == null) { Destroy(this); return; }
                transform.position = target.TransformPoint(offset);
                transform.rotation = Quaternion.LookRotation(target.TransformPoint(look) - transform.position, target.up);
                GetComponent<Camera>().fieldOfView = fov;
            }
        }
    }
}
#endif
