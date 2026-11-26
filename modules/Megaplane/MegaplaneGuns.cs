using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>The carrier models no gun heads: it carries the game's own turrets (BOTE does the same with whole ships).
    /// "GunMount_Side_*" art empties (flank fairings) get the VL-49 Tarantula's side-door 76 mm mount, scaled up into the
    /// BL-155 BASTION gunship howitzer (left battery points -X, right +X at rest; the empty marks its base plate).
    /// CIWS turrets guard the hull and both upper and lower wing sectors. The dorsal silos carry paired NL-98 and R9 batteries. With the
    /// railgun payload, the heaviest ship gun becomes the RG-240 TRIDENT under "Railgun_Pitch" (see MegaplaneRailgun).
    /// Native Turret/Gun do aiming, firing and replication: an unselected station engages on its own, the selected one is
    /// under pilot control (aims where the cockpit camera looks, or at the locked target). The clones' ship-hull parts are
    /// stripped so the carrier's own damage islands carry them.</summary>
    internal static class MegaplaneGuns
    {
        internal const string Prefix = "BS_Gun_";
        internal const string SideDonor = "turret_76mm_SideMount";
        internal const float SideScale = 3.2f;   // scale the Tarantula's 76 mm donor to fit the Groza's flank fairings
        private static readonly FieldInfo Stations = AccessTools.Field(typeof(Turret), "weaponStations");
        private static readonly FieldInfo Parts = AccessTools.Field(typeof(Turret), "criticalParts");
        private static readonly FieldInfo Unit = AccessTools.Field(typeof(Turret), "attachedUnit");
        private static readonly FieldInfo Mode = AccessTools.Field(typeof(Turret), "targetAcquisitionMode");
        private static readonly FieldInfo FireCtl = AccessTools.Field(typeof(Turret), "fireControl");
        private static readonly FieldInfo Detectors = AccessTools.Field(typeof(Turret), "targetDetectors");
        private static readonly FieldInfo FireRate = AccessTools.Field(typeof(Gun), "fireRate");
        private static readonly FieldInfo Cones = AccessTools.Field(typeof(Turret), "firingCones");
        internal static string Picked { get; private set; }
        internal static string PickedSilo { get; private set; }
        internal static WeaponInfo Bastion { get; private set; }
        internal static WeaponInfo Trident { get; private set; }

        private struct Donor { public Transform Root; public WeaponInfo Info; public float Rate; public string Ship; }

        internal static void Set(object o, string field, object value)
        {
            FieldInfo f = AccessTools.Field(o.GetType(), field);
            if (f == null) Debug.LogWarning("[Megaplane] no field " + o.GetType().Name + "." + field);
            else f.SetValue(o, value);
        }

        internal static void Graft(Encyclopedia encyclopedia, Aircraft aircraft, Dictionary<string, Transform> nodes, bool railgun)
        {
            var donors = new List<Donor>();
            var silos = new List<Donor>();
            foreach (ShipDefinition def in encyclopedia.ships)
            {
                GameObject prefab = def != null ? def.unitPrefab : null;
                if (prefab == null) continue;
                foreach (Turret turret in prefab.GetComponentsInChildren<Turret>(true))
                {
                    if (!(Stations.GetValue(turret) is WeaponStation[] st) || st.Length == 0 || st[0].Weapons == null || st[0].Weapons.Count == 0) continue;
                    Transform root = turret.transform;   // the mount: turret plus whatever sits between it and its hull part
                    while (root.parent != null && root.parent.GetComponent<UnitPart>() == null) root = root.parent;
                    if (root.parent == null || !st.All(s => s.Weapons.All(w => w != null && w.transform.IsChildOf(root)))) continue;
                    if (st[0].Weapons[0] is MissileLauncher ml && ml.info != null)
                    {
                        // The inactive prefab has serialized ammo. Its per-launcher maxAmmo is populated on enable.
                        silos.Add(new Donor { Root = root, Info = ml.info, Rate = st.Sum(s => s.Weapons.Sum(w => w.ammo)), Ship = def.jsonKey });
                        continue;
                    }
                    if (!(st[0].Weapons[0] is Gun gun) || gun.info == null || gun.info.missile) continue;
                    donors.Add(new Donor { Root = root, Info = gun.info, Rate = (float)FireRate.GetValue(gun), Ship = def.jsonKey });
                }
            }
            if (donors.Count == 0) { Debug.LogWarning("[Megaplane] no ship gun turret to borrow"); return; }
            Donor heavy = donors.OrderByDescending(d => d.Info.massPerRound).First();
            Donor ciws = donors.OrderByDescending(d => d.Rate).First();
            WeaponMount side = encyclopedia.weaponMounts.FirstOrDefault(m => m != null && (m.jsonKey == SideDonor || m.name == SideDonor));
            Donor[] siloTypes = silos.OrderByDescending(d => d.Rate).ThenBy(d => d.Ship, StringComparer.Ordinal).GroupBy(d => d.Info).Select(g => g.First()).ToArray();
            Donor forwardSilo = Silo(siloTypes, "NL-98");
            Donor aftSilo = Silo(siloTypes, "StratoLance R9", forwardSilo.Info);
            if (aftSilo.Root == null) aftSilo = forwardSilo;
            Picked = (side != null ? side.jsonKey + " x" + SideScale : "no side mount") + " + " + ciws.Ship + "/" + ciws.Root.name + (railgun ? " + rail " + heavy.Ship + "/" + heavy.Root.name : "");
            PickedSilo = string.Join(", ", new[] { forwardSilo, aftSilo }.Where(d => d.Root != null).Select(d => d.Ship + "/" + d.Info.weaponName + " x" + d.Rate));

            foreach (KeyValuePair<string, Transform> node in nodes.Where(n => n.Key.StartsWith("GunMount_") || n.Key.StartsWith("SiloMount_")).ToList())
            {
                AeroPart host = node.Value.GetComponentInParent<AeroPart>(true);
                if (node.Key.StartsWith("GunMount_Side_"))
                {
                    if (side != null) SideGun(aircraft, side, node.Key, node.Value, host);
                    continue;
                }
                bool isSilo = node.Key.StartsWith("SiloMount_");
                bool secondPair = node.Key == "SiloMount_2" || node.Key == "SiloMount_3";
                Donor d = isSilo ? (secondPair ? aftSilo : forwardSilo) : ciws;
                if (d.Root == null || host == null) continue;
                bool down = node.Key.EndsWith("_Down", StringComparison.Ordinal);
                InstallBattery(aircraft, host, d, isSilo ? node.Key : node.Key.Substring("GunMount_".Length), node.Value.position, down, 0f);
            }
            foreach (int sign in new[] { -1, 1 })
            {
                string sideSuffix = sign < 0 ? "_L" : "_R";
                AddWingBattery(aircraft, nodes, ciws, "Wing_Inner" + sideSuffix, "CIWS_Inner" + sideSuffix + "_Down", new Vector3(72f * sign, 3.07f, -17f), true, sign);
                AddWingBattery(aircraft, nodes, ciws, "Wing_Mid" + sideSuffix, "CIWS_Mid" + sideSuffix, new Vector3(117f * sign, 6.57f, -26f), false, sign);
            }
            if (side == null) Debug.LogWarning("[Megaplane] side gun donor absent: " + SideDonor);
            if (railgun && nodes.TryGetValue("Railgun_Pitch", out Transform pitch)) Rail(aircraft, heavy, nodes, pitch);
        }

        private static Donor Silo(Donor[] donors, string name, WeaponInfo used = null)
        {
            Donor preferred = donors.FirstOrDefault(d => d.Info != used && d.Info.weaponName == name);
            return preferred.Root != null ? preferred : donors.FirstOrDefault(d => d.Info != used);
        }

        private static void AddWingBattery(Aircraft aircraft, Dictionary<string, Transform> nodes, Donor donor,
            string wingName, string name, Vector3 skinPoint, bool down, int sign)
        {
            if (!nodes.TryGetValue(wingName, out Transform wing)) return;
            AeroPart host = wing.GetComponentInParent<AeroPart>(true);
            if (host == null) return;
            // The shaped pedestal enters the wing skin and shares the surrounding damage part.
            string pedestalName = down ? "VentralBarbette_2" : "DorsalBarbette_0";
            if (nodes.TryGetValue(pedestalName, out Transform pedestal) && pedestal.GetComponent<MeshFilter>() is MeshFilter mesh && pedestal.GetComponent<MeshRenderer>() is MeshRenderer renderer)
            {
                var foundation = new GameObject("Groza_" + name + "_Base") { layer = host.gameObject.layer };
                foundation.transform.SetParent(host.transform, false);
                foundation.transform.SetPositionAndRotation(aircraft.transform.TransformPoint(skinPoint), aircraft.transform.rotation);
                foundation.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
                foundation.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            }
            InstallBattery(aircraft, host, donor, name, aircraft.transform.TransformPoint(skinPoint), down, sign);
        }

        private static void InstallBattery(Aircraft aircraft, AeroPart host, Donor donor, string name, Vector3 position, bool down, float outward)
        {
            Transform parent = host.transform;
            if (down)
            {
                // Stock CIWS can aim on the clone root itself. Keep the fixed belly roll above that traverse transform.
                parent = new GameObject(Prefix + name) { layer = host.gameObject.layer }.transform;
                parent.SetParent(host.transform, false);
                parent.SetPositionAndRotation(position, aircraft.transform.rotation * Quaternion.AngleAxis(180f, Vector3.forward));
            }
            GameObject clone = Clone(donor.Root.gameObject, parent, down ? donor.Root.name : Prefix + name);
            clone.transform.SetPositionAndRotation(position, down ? parent.rotation : aircraft.transform.rotation);
            Arm(aircraft, down ? parent.gameObject : clone, "datalink");
            clone.layer = host.gameObject.layer;
            foreach (Turret turret in clone.GetComponentsInChildren<Turret>(true))
            {
                if (donor.Info.gun)
                {
                    // The firing hemisphere follows the deck and cant of the mount.
                    var sector = new GameObject(name + "_Sector").transform;
                    sector.SetParent(host.transform, false);
                    sector.position = position;
                    sector.rotation = Quaternion.LookRotation(aircraft.transform.TransformDirection(new Vector3(.35f * outward, down ? -.94f : .94f, .01f)));
                    var cone = new FiringCone();
                    Set(cone, "transform", sector);
                    Set(cone, "coneAngle", 90f);
                    Cones.SetValue(turret, new[] { cone });
                    Set(turret, "traverseRange", 360f);
                    Set(turret, "maxElevation", 88f);
                }
                else
                {
                    Cones.SetValue(turret, Array.Empty<FiringCone>());
                    Set(turret, "firesWithoutAiming", true);
                }
            }
        }

        private static GameObject Clone(GameObject donor, Transform parent, string name)
        {
            GameObject clone = Object.Instantiate(donor, parent);
            clone.name = name;
            foreach (Joint j in clone.GetComponentsInChildren<Joint>(true)) Object.DestroyImmediate(j);
            foreach (Rigidbody rb in clone.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            foreach (UnitPart part in clone.GetComponentsInChildren<UnitPart>(true)) Object.DestroyImmediate(part);
            foreach (Collider c in clone.GetComponentsInChildren<Collider>(true)) c.enabled = false;   // hits land on the carrier's hitboxes
            return clone;
        }

        /// <summary>Hand the clone's turrets to the carrier (template refs are remapped to each spawned copy).</summary>
        private static void Arm(Aircraft aircraft, GameObject clone, string mode)
        {
            var mount = clone.AddComponent<MegaplaneBatteryMount>();
            mount.Bind(clone.GetComponentInParent<AeroPart>(true), clone.GetComponentInChildren<Turret>(true));
            foreach (Turret turret in clone.GetComponentsInChildren<Turret>(true))
            {
                Unit.SetValue(turret, aircraft);
                // The island it sits on: destroyed or shot off, the gun goes quiet (Turret_OnPartDamage / OnDetachedFromUnit).
                AeroPart island = turret.GetComponentInParent<AeroPart>(true);
                Parts.SetValue(turret, island != null && island.gameObject != aircraft.gameObject ? new UnitPart[] { island } : new UnitPart[0]);
                FireCtl.SetValue(turret, null);
                Detectors.SetValue(turret, new List<TargetDetector>());
                Mode.SetValue(turret, System.Enum.Parse(Mode.FieldType, mode));   // private enum
                Gun gun = turret.GetComponentInChildren<Gun>(true);
                if (gun != null && gun.info != Trident) Set(turret, "aimSafetyWeapon", gun);
            }
            foreach (Weapon weapon in clone.GetComponentsInChildren<Weapon>(true)) weapon.AttachToUnit(aircraft);
        }

        /// <summary>A hardpoint mount carries no station of its own (the hardpoint system makes one): give its turret one.</summary>
        private static void Station(Aircraft aircraft, Turret turret, Gun gun)
        {
            if (Stations.GetValue(turret) is WeaponStation[] st && st.Length > 0) return;
            var station = new WeaponStation(aircraft, false, false, false, false) { WeaponInfo = gun.info };
            station.Weapons.Add(gun);
            station.Turrets.Add(turret);
            Stations.SetValue(turret, new[] { station });
        }

        // BL-155 BASTION: the Tarantula's side-door 76 mm grown to an AC-130 style 155 mm howitzer. Hit damage scales with
        // pierce and blast, so the shell keeps the donor's armour behaviour and hits like a field gun; 30 rounds a minute.
        private const float BastionInterval = 2f, BastionPierce = 2.5f, BastionBlast = 5f, BastionMass = 7f;
        private const int BastionRounds = 60;

        private static void SideGun(Aircraft aircraft, WeaponMount side, string key, Transform mount, AeroPart host)
        {
            GameObject clone = Clone(side.prefab, host.transform, Prefix + key.Substring("GunMount_".Length));
            Transform t = clone.transform;
            t.localScale = Vector3.one * SideScale;
            t.rotation = aircraft.transform.rotation * Quaternion.Euler(0f, key.EndsWith("_R") ? 180f : 0f, 0f);   // donor sits on a left door
            Transform plate = clone.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "angleOffset") ?? t;
            t.position += mount.position - plate.position;   // the art empty marks the base plate
            foreach (Renderer r in clone.GetComponentsInChildren<Renderer>(true))
                if (r.name.IndexOf("ammoRack", System.StringComparison.OrdinalIgnoreCase) >= 0) r.enabled = false;   // inside the Tarantula's cabin
            if (Bastion == null)
            {
                WeaponInfo d = side.info;
                Bastion = Object.Instantiate(d);
                Bastion.name = "WI_BS_Megaplane_Bastion";
                Bastion.weaponName = "BL-155 Bastion";
                Bastion.shortName = "155";
                Bastion.description = "Side-firing 155 mm gunship howitzer in the flank ball mounts, two per side. Select it to lay the battery where you look, or let it fire on its own at ground targets.";
                Bastion.pierceDamage = d.pierceDamage * BastionPierce;
                Bastion.blastDamage = d.blastDamage * BastionBlast;
                Bastion.massPerRound = d.massPerRound * BastionMass;
                Bastion.fireInterval = BastionInterval;
                Bastion.visibilityWhenFired = d.visibilityWhenFired * 2f;
            }
            foreach (Gun gun in clone.GetComponentsInChildren<Gun>(true))
            {
                gun.info = Bastion;
                // The Tarantula mount fires guided shells, which the game spawns as missiles (interceptable, own damage):
                // the howitzer fires plain shells that carry the Bastion's pierce and blast.
                Set(gun, "guidedProjectile", null);
                Set(gun, "fireRate", 60f / BastionInterval);
                Set(gun, "fireInterval", BastionInterval);
                Set(gun, "magazineCapacity", BastionRounds);
                Set(gun, "maxMagazines", 0);
                Set(gun, "magazines", 0);
            }
            Arm(aircraft, clone, "fireSupport");
            foreach (Turret turret in clone.GetComponentsInChildren<Turret>(true))
            {
                Gun gun = turret.GetComponentInChildren<Gun>(true);
                if (gun != null) Station(aircraft, turret, gun);
            }
            clone.layer = host.gameObject.layer;
        }

        // RG-240 TRIDENT: 64 MJ class rail. A 3 km/s slug through anything but heavy armour, a small burst on impact, one
        // round in twelve seconds while the capacitors recharge; 40 slugs in the bay magazine.
        private const float RailVelocity = 3000f;
        internal const float RailInterval = 12f;
        private const int RailRounds = 40;

        private static void Rail(Aircraft aircraft, Donor heavy, Dictionary<string, Transform> nodes, Transform pitch)
        {
            Transform elevator = nodes.TryGetValue("Railgun_Elevator", out Transform e) ? e : pitch.parent;
            // Native Turret writes its own local rotation every tick. Keep the belly roll on a fixed parent above it.
            var frame = new GameObject("RailgunTraverseFrame").transform;
            frame.SetParent(elevator, false);
            frame.rotation = aircraft.transform.rotation * Quaternion.Euler(0f, 0f, 180f);
            GameObject clone = Clone(heavy.Root.gameObject, frame, Prefix + "Railgun");
            clone.transform.localRotation = Quaternion.identity;
            Turret turret = clone.GetComponentInChildren<Turret>(true);
            Gun gun = clone.GetComponentInChildren<Gun>(true);
            Transform elevation = (Transform)AccessTools.Field(typeof(Turret), "elevationTransform").GetValue(turret) ?? gun.transform;
            // The ship's trunnion is ahead of its traverse axis. Groza's cradle is coaxial, so move the hidden
            // trunnion to the traverse axis before aligning it. Otherwise the native muzzle orbits the visible bore.
            if (elevation != turret.transform && elevation.IsChildOf(turret.transform))
            {
                float height = turret.transform.InverseTransformPoint(elevation.position).y;
                elevation.position = turret.transform.TransformPoint(Vector3.up * height);
            }
            clone.transform.position += pitch.position - elevation.position;   // native trunnion on the art's
            foreach (Renderer r in clone.GetComponentsInChildren<Renderer>(true)) r.enabled = false;   // the art is the gun
            foreach (Gun other in clone.GetComponentsInChildren<Gun>(true).Where(g => g != gun))   // one rail, not a twin mount
            {
                if (Stations.GetValue(turret) is WeaponStation[] st) foreach (WeaponStation s in st) s.Weapons.Remove(other);
                Object.DestroyImmediate(other);
            }
            if (Trident == null)
            {
                WeaponInfo d = gun.info;
                Trident = Object.Instantiate(d);
                Trident.name = "WI_BS_Megaplane_Trident";
                Trident.weaponName = "RG-240 Trident";
                Trident.shortName = "RAIL";
                Trident.description = "Ventral railgun. The doors and elevator must clear before the battery can fire. The coils recharge for twelve seconds after each shot.";
                Trident.muzzleVelocity = RailVelocity;
                Trident.maxSpeed = RailVelocity;
                Trident.pierceDamage = d.pierceDamage * 6f;
                Trident.blastDamage = d.blastDamage * .6f;
                Trident.fireInterval = RailInterval;
                Trident.visibilityWhenFired = d.visibilityWhenFired * 3f;
                Trident.gravMult = .5f;   // flat trajectory over the engagement ranges the game draws
            }
            gun.info = Trident;
            Set(gun, "muzzleVelocity", RailVelocity);
            Set(gun, "fireRate", 60f / RailInterval);
            Set(gun, "fireInterval", RailInterval);
            Set(gun, "magazineCapacity", RailRounds);
            Set(gun, "maxMagazines", 0);
            Set(gun, "magazines", 0);
            Set(gun, "bulletSpread", .01f);
            Set(gun, "tracerRatio", 1);
            Set(gun, "tracerColor", new Color(.4f, 1.4f, 3f));
            Set(gun, "heatEnabled", false);
            Set(gun, "proximityTimer", false);
            Set(gun, "guidedProjectile", null);
            Set(gun, "ejectionTransform", null);
            // Native Fire and RemoteSingleFire both signal a discharge through their recoil accumulator.
            // The art controller moves its cradle and muzzle together from that signal.
            var recoil = new GameObject("RailRecoil").transform;
            recoil.SetParent(elevation, false);
            Set(gun, "recoilTransform", recoil);
            Set(gun, "recoilImpulse", 210000f);
            Set(gun, "recoilTravel", 0f);
            Set(turret, "traverseRange", 150f);
            Set(turret, "traverseRate", 32f);
            Set(turret, "elevationRate", 19f);
            Set(turret, "minElevation", 1.5f);   // belly mount: positive elevation points below the doors
            Set(turret, "maxElevation", 65f);
            Set(turret, "maxRange", 30000f);
            Set(turret, "stowed", true);
            // Replace the ship's firing limits with a direction fixed to Groza's deck, outside the traversing head.
            var sector = new GameObject("RailgunGroundSector").transform;
            sector.SetParent(frame, false);
            sector.rotation = Quaternion.LookRotation(-aircraft.transform.up, aircraft.transform.forward);
            var cone = new FiringCone();
            Set(cone, "transform", sector);
            Set(cone, "coneAngle", 88.5f);
            Cones.SetValue(turret, new[] { cone });
            Transform muzzle = null;
            if (nodes.TryGetValue("Railgun_Muzzle", out Transform tip))
            {
                muzzle = new GameObject("RailMuzzle").transform;
                muzzle.SetParent(elevation, false);
                muzzle.SetPositionAndRotation(tip.position, tip.rotation);
                Set(gun, "muzzles", new[] { muzzle });
                foreach (ParticleSystem p in clone.GetComponentsInChildren<ParticleSystem>(true))
                    if (p.transform.parent.GetComponentInParent<ParticleSystem>() == null)
                    {
                        p.transform.SetParent(muzzle, true);
                        p.transform.position = muzzle.position;
                    }
            }
            Arm(aircraft, clone, "fireSupport");
            clone.layer = elevator.gameObject.layer;
            MegaplaneRailgun rail = aircraft.gameObject.AddComponent<MegaplaneRailgun>();
            rail.Bind(nodes, turret, gun, elevation, muzzle);
        }

        /// <summary>Weapon reloads clear every station; put the guns back before the manager numbers them (same order on every
        /// peer: hierarchy order of the shared prefab). One station per weapon type groups the four Bastions, eleven CIWS and
        /// the two VLS pairs by their own ammunition and role, keeping the selection list short.
        /// Each turret still picks its own target; selected, the station aims them all where the pilot looks.</summary>
        internal static void Register(Aircraft aircraft)
        {
            var battery = new Dictionary<WeaponInfo, WeaponStation>();
            foreach (MegaplaneBatteryMount mount in aircraft.GetComponentsInChildren<MegaplaneBatteryMount>(true))
            {
                Turret turret = mount.Turret;
                if (turret == null) continue;
                if (!(Stations.GetValue(turret) is WeaponStation[] st) || st.Length == 0) continue;
                WeaponStation station = st[0];
                // a station without weapons would throw out of this Harmony prefix and break the manager's pass
                if (station.Weapons == null || station.Weapons.Count == 0 || station.Weapons[0] == null) continue;
                WeaponInfo info = station.Weapons[0].info;
                turret.SetManual(false);
                if (!battery.TryGetValue(info, out WeaponStation into))
                {
                    battery[info] = into = station;
                    into.WeaponInfo = info;
                    if (into.Turrets == null || !into.Turrets.Contains(turret)) Set(turret, "turretIndex", into.AssignTurret(turret));
                }
                else if (station != into)
                {
                    foreach (Weapon w in station.Weapons) if (w != null && !into.Weapons.Contains(w)) { into.Weapons.Add(w); w.SetWeaponStation(into); }
                    Stations.SetValue(turret, new[] { into });
                    Set(turret, "currentWeaponStation", into);
                    Set(turret, "turretIndex", into.AssignTurret(turret));
                    into.FullAmmo = 0;   // re-totalled below
                }
            }
            foreach (WeaponStation into in battery.Values)
            {
                if (aircraft.weaponStations.Contains(into)) continue;
                if (into.FullAmmo == 0) into.Rearm(into.Weapons.Sum(w => w != null ? w.GetFullAmmo() : 0) - into.GetAmmoTotal());
                aircraft.RegisterWeaponStation(into);
            }
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "OrganizeWeaponStations")]
    internal static class MegaplaneTurretPatch
    {
        private static readonly AccessTools.FieldRef<WeaponManager, Aircraft> AircraftRef = AccessTools.FieldRefAccess<WeaponManager, Aircraft>("aircraft");

        private static void Prefix(WeaponManager __instance)
        {
            if (MegaplaneModule.Is(AircraftRef(__instance))) MegaplaneGuns.Register(AircraftRef(__instance));
        }
    }
}
