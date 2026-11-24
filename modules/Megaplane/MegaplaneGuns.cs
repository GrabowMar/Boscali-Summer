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
    /// "GunMount_CIWS_*" get the fastest-firing ship gun (datalink, point defence), "_Down" hung under the belly.
    /// "SiloMount_*" (dorsal vertical-launch silos) get the ship missile launcher with the deepest magazine. With the
    /// railgun payload, the heaviest ship gun becomes the RG-240 TRIDENT under "Railgun_Pitch" (see MegaplaneRailgun).
    /// Native Turret/Gun do aiming, firing and replication: an unselected station engages on its own, the selected one is
    /// under pilot control (aims where the cockpit camera looks, or at the locked target). The clones' ship-hull parts are
    /// stripped so the carrier's own damage islands carry them.</summary>
    internal static class MegaplaneGuns
    {
        internal const string Prefix = "BS_Gun_";
        internal const string SideDonor = "turret_76mm_SideMount";
        internal const float SideScale = 3.2f;   // the Tarantula's 76 mm grown to a 155 mm gun on a 380 m hull
        private static readonly FieldInfo Stations = AccessTools.Field(typeof(Turret), "weaponStations");
        private static readonly FieldInfo Parts = AccessTools.Field(typeof(Turret), "criticalParts");
        private static readonly FieldInfo Unit = AccessTools.Field(typeof(Turret), "attachedUnit");
        private static readonly FieldInfo Mode = AccessTools.Field(typeof(Turret), "targetAcquisitionMode");
        private static readonly FieldInfo FireCtl = AccessTools.Field(typeof(Turret), "fireControl");
        private static readonly FieldInfo Detectors = AccessTools.Field(typeof(Turret), "targetDetectors");
        private static readonly FieldInfo FireRate = AccessTools.Field(typeof(Gun), "fireRate");
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
            Donor[] siloTypes = silos.OrderByDescending(d => d.Rate).GroupBy(d => d.Info).Select(g => g.First()).Take(2).ToArray();
            Picked = (side != null ? side.jsonKey + " x" + SideScale : "no side mount") + " + " + ciws.Ship + "/" + ciws.Root.name + (railgun ? " + rail " + heavy.Ship + "/" + heavy.Root.name : "");
            PickedSilo = string.Join(", ", siloTypes.Select(d => d.Ship + "/" + d.Info.weaponName + " x" + d.Rate));

            foreach (KeyValuePair<string, Transform> node in nodes.Where(n => n.Key.StartsWith("GunMount_") || n.Key.StartsWith("SiloMount_")).ToList())
            {
                AeroPart host = node.Value.GetComponentInParent<AeroPart>(true);
                if (node.Key.StartsWith("GunMount_Side_"))
                {
                    if (side != null) SideGun(aircraft, side, node.Key, node.Value, host);
                    continue;
                }
                bool isSilo = node.Key.StartsWith("SiloMount_");
                if (isSilo && siloTypes.Length == 0) continue;
                bool secondPair = node.Key == "SiloMount_2" || node.Key == "SiloMount_3";
                Donor d = isSilo ? siloTypes[secondPair && siloTypes.Length > 1 ? 1 : 0] : ciws;
                GameObject clone = Clone(d.Root.gameObject, host.transform, Prefix + (isSilo ? node.Key : node.Key.Substring("GunMount_".Length)));
                clone.transform.position = node.Value.position;
                clone.transform.rotation = aircraft.transform.rotation * Quaternion.Euler(0f, 0f, node.Key.EndsWith("_Down") ? 180f : 0f);
                Arm(aircraft, clone, "datalink");
                clone.layer = host.gameObject.layer;
                // A firing cone hung on the ship's hull (outside the clone) would point at the prefab asset: drop it (empty = all round; the hull linecast still guards self-hits).
                foreach (Turret turret in clone.GetComponentsInChildren<Turret>(true))
                    if (AccessTools.Field(typeof(Turret), "firingCones") is FieldInfo cones && cones.GetValue(turret) is System.Array arr &&
                        arr.Cast<object>().Any(c => !(AccessTools.Field(c.GetType(), "transform")?.GetValue(c) is Transform ct) || !ct.IsChildOf(clone.transform)))
                    {
                        Debug.Log("[Megaplane] " + clone.name + ": firing cones outside the mount dropped");
                        cones.SetValue(turret, System.Array.CreateInstance(arr.GetType().GetElementType(), 0));
                    }
            }
            if (side == null) Debug.LogWarning("[Megaplane] side gun donor absent: " + SideDonor);
            if (railgun && nodes.TryGetValue("Railgun_Pitch", out Transform pitch)) Rail(aircraft, heavy, nodes, pitch);
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
            foreach (Turret turret in clone.GetComponentsInChildren<Turret>(true))
            {
                Unit.SetValue(turret, aircraft);
                // The island it sits on: destroyed or shot off, the gun goes quiet (Turret_OnPartDamage / OnDetachedFromUnit).
                AeroPart island = turret.GetComponentInParent<AeroPart>(true);
                Parts.SetValue(turret, island != null && island.gameObject != aircraft.gameObject ? new UnitPart[] { island } : new UnitPart[0]);
                FireCtl.SetValue(turret, null);
                Detectors.SetValue(turret, new List<TargetDetector>());
                Mode.SetValue(turret, System.Enum.Parse(Mode.FieldType, mode));   // private enum
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
        private const float RailVelocity = 3000f, RailInterval = 12f;
        private const int RailRounds = 40;

        private static void Rail(Aircraft aircraft, Donor heavy, Dictionary<string, Transform> nodes, Transform pitch)
        {
            Transform elevator = nodes.TryGetValue("Railgun_Elevator", out Transform e) ? e : pitch.parent;
            GameObject clone = Clone(heavy.Root.gameObject, elevator, Prefix + "Railgun");
            clone.transform.rotation = aircraft.transform.rotation * Quaternion.Euler(0f, 0f, 180f);   // hung under the belly
            Turret turret = clone.GetComponentInChildren<Turret>(true);
            Gun gun = clone.GetComponentInChildren<Gun>(true);
            Transform elevation = (Transform)AccessTools.Field(typeof(Turret), "elevationTransform").GetValue(turret) ?? gun.transform;
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
                Trident.description = "Belly railgun on a lowering mount, carried instead of the parasite fighters. 3 km/s tungsten slugs, one every twelve seconds; it lowers itself when selected or given a target.";
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
            Set(turret, "traverseRange", 150f);
            Set(turret, "traverseRate", 18f);
            Set(turret, "elevationRate", 12f);
            Set(turret, "minElevation", -5f);    // upside down: "up" is toward the ground
            Set(turret, "maxElevation", 65f);
            Set(turret, "maxRange", 30000f);
            if (nodes.TryGetValue("Railgun_Muzzle", out Transform tip))
            {
                var muzzle = new GameObject("RailMuzzle").transform;
                muzzle.SetParent(gun.transform, false);
                muzzle.position = tip.position;
                Set(gun, "muzzles", new[] { muzzle });
                foreach (ParticleSystem p in clone.GetComponentsInChildren<ParticleSystem>(true)) p.transform.position = muzzle.position;
            }
            Arm(aircraft, clone, "fireSupport");
            clone.layer = elevator.gameObject.layer;
            MegaplaneRailgun rail = aircraft.gameObject.AddComponent<MegaplaneRailgun>();
            rail.Bind(nodes, turret, elevation);
        }

        /// <summary>Weapon reloads clear every station; put the guns back before the manager numbers them (same order on every
        /// peer: hierarchy order of the shared prefab). One station per weapon type, as on a ship's battery: all four BASTIONs,
        /// all seven CIWS, all four silos are one selection each (one station per turret was 15 rows and broke the weapon UI).
        /// Each turret still picks its own target; selected, the station aims them all where the pilot looks.</summary>
        internal static void Register(Aircraft aircraft)
        {
            var battery = new Dictionary<WeaponInfo, WeaponStation>();
            foreach (Turret turret in aircraft.GetComponentsInChildren<Turret>(true))
            {
                if (!(Stations.GetValue(turret) is WeaponStation[] st) || st.Length == 0) continue;
                WeaponStation station = st[0];
                // a station without weapons would throw out of this Harmony prefix and break the manager's pass
                if (station.Weapons == null || station.Weapons.Count == 0 || station.Weapons[0] == null) continue;
                WeaponInfo info = station.Weapons[0].info;
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

    /// <summary>A battery station's Fire fires every gun in it. On a ship-style battery each turret picks its own target, so a
    /// gun only fires with the turret that called: its own turret is on that target and aimed (or under pilot control).
    /// Without this the seven CIWS would all fire whenever one had a target, the aft ones through the carrier's own tail.</summary>
    [HarmonyPatch]
    internal static class MegaplaneBatteryFirePatch
    {
        private static readonly AccessTools.FieldRef<Turret, Unit> Target = AccessTools.FieldRefAccess<Turret, Unit>("target");
        private static readonly AccessTools.FieldRef<Turret, bool> Manual = AccessTools.FieldRefAccess<Turret, bool>("manual");
        private static readonly AccessTools.FieldRef<Turret, bool> OnTarget = AccessTools.FieldRefAccess<Turret, bool>("onTarget");

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Gun), nameof(Gun.Fire));
            yield return AccessTools.Method(typeof(MissileLauncher), nameof(MissileLauncher.Fire));
        }

        private static bool Prefix(Weapon __instance, Unit target, WeaponStation weaponStation)
        {
            if (weaponStation == null || weaponStation.Turrets == null || weaponStation.Turrets.Count < 2) return true;
            Turret own = __instance.GetComponentInParent<Turret>();
            if (own == null || !own.transform.root.name.StartsWith(MegaplaneTemplate.Key)) return true;
            if (Manual(own)) return true;
            return target != null && Target(own) == target && (OnTarget(own) || __instance is MissileLauncher);
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
