#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuclearOption.Jobs;
using UnityEngine;

namespace BoscaliSummer.Modules.Skylark
{
    /// <summary>Readback hooks used by the Skylark sandbox scenarios. No deployment or scene mutation.</summary>
    public static class SkylarkAutomation
    {
        public static object Status(Dictionary<string, object> args)
        {
            JObject report = JObject.Parse(Report());
            if (args.TryGetValue("idUnit", out object value) && value is Aircraft aircraft)
                report["flight"] = JToken.FromObject(Flight(aircraft));
            return report;
        }

        private static object Flight(Aircraft aircraft)
        {
            DuctedThrustSystem duct = aircraft.GetComponent<DuctedThrustSystem>();
            JetNozzle[] nozzles = SkylarkTemplate.Get<JetNozzle[]>(duct, "nozzles");
            ControlInputs inputs = aircraft.GetInputs();
            return new
            {
                speed = aircraft.speed, altitude = aircraft.radarAlt, fuel = aircraft.fuelLevel,
                disabled = aircraft.disabled, mass = aircraft.GetMass(), angle = duct.angle,
                pitch = inputs.pitch, roll = inputs.roll, yaw = inputs.yaw, throttle = inputs.throttle,
                vector = SkylarkFlightMode.Name(aircraft.GetComponent<SkylarkFlightMode>().Mode), drive = aircraft.GetComponent<SkylarkDrive>().Mode.ToString(),
                electric = aircraft.GetComponent<SkylarkDrive>().Electric, charge = aircraft.GetComponent<SkylarkDrive>().Charge, irSignature = aircraft.HasIRSignature(),
                mast = aircraft.GetComponent<SkylarkMast>().Deployed, turret = TurretReport(aircraft),
                rcs = aircraft.RCS, commandPlatform = BoscaliSummer.Core.Contracts.CommandPlatform.Is(aircraft),
                gear = aircraft.gearState.ToString(), gearDown = aircraft.gearState == LandingGear.GearState.LockedExtended || aircraft.gearState == LandingGear.GearState.Extending,
                boost = aircraft.GetComponent<SkylarkDrive>().Boost, dry = aircraft.GetComponent<SkylarkDrive>().Dry,
                heavy = aircraft.GetComponent<SkylarkDrive>().Heavy, thrustToWeight = aircraft.GetComponent<SkylarkDrive>().ThrustToWeight,
                fuelMass = aircraft.GetFuelQuantity(), climbRate = aircraft.rb.velocity.y,
                parasiticLoss = SkylarkTemplate.Get<float>(aircraft.GetComponentInChildren<Turbofan>(), "parasiticThrustLoss"),
                strobeFlashes = aircraft.GetComponent<SkylarkStrobe>() != null ? aircraft.GetComponent<SkylarkStrobe>().Flashes : -1,
                strobeAge = aircraft.GetComponent<SkylarkStrobe>() != null ? Time.time - aircraft.GetComponent<SkylarkStrobe>().LastFlash : -1f,
                halberd = Halberd(aircraft), fansHeld = SkylarkControl.FansHeld, landed = aircraft.IsLanded(),
                loadoutName = SkylarkWeapons.LoadoutName(aircraft),
                gears = UnityEngine.Object.FindObjectsOfType<LandingGear>().Where(g => SkylarkTemplate.Get<Aircraft>(g, "aircraft") == aircraft).Select(g => new
                {
                    g.name, g.enabled, active = g.gameObject.activeInHierarchy, fold = SkylarkTemplate.Get<float>(g, "foldAmount"),
                    hinge = V(SkylarkTemplate.Get<Transform>(g, "gearHinge").localEulerAngles),
                    angle = Vector3.Angle(SkylarkTemplate.Get<Transform>(g, "gearHinge").forward, SkylarkTemplate.Get<Transform>(g, "gearHinge").parent.forward)
                }),
                fuelBurn = SkylarkTemplate.Get<float>(aircraft.GetComponentInChildren<Turbofan>(), "fuelConsumptionMax"),
                staticThrust = SkylarkTemplate.Get<float>(aircraft.GetComponentInChildren<Turbofan>(), "staticThrust"),
                stationCount = aircraft.weaponManager.hardpointSets.Length,
                autoHover = aircraft.IsAutoHoverEnabled(), rawPitch = aircraft.GetControlsFilter().GetFlyByWire().GetRawPitch(),
                targetPitchRate = aircraft.GetControlsFilter().GetFlyByWire().GetTargetPitchAngVel(),
                attitude = V(aircraft.transform.eulerAngles), velocity = V(aircraft.rb.velocity),
                angularVelocity = V(aircraft.transform.InverseTransformDirection(aircraft.rb.angularVelocity)),
                // degrees per second, positive = nose up / right wing down
                pitchRate = -aircraft.transform.InverseTransformDirection(aircraft.rb.angularVelocity).x * Mathf.Rad2Deg,
                rollRate = -aircraft.transform.InverseTransformDirection(aircraft.rb.angularVelocity).z * Mathf.Rad2Deg,
                centerOfMass = V(aircraft.transform.InverseTransformPoint(SkylarkAnimation.PhysicsCenter(aircraft))),
                detached = aircraft.partLookup.Count(p => p.IsDetached()),
                thrustRatio = SkylarkTemplate.Get<float>(duct, "thrustRatio"),
                damageSource = SkylarkMaterials.DamageSource,
                statusDisplay = SkylarkStatusDisplay.Inspect(aircraft),
                ammunition = aircraft.weaponStations.ToDictionary(s => s.Number.ToString(), s => s.Ammo),
                weapons = aircraft.weaponStations.Select(s => new { s.Number, s.Ammo, s.FullAmmo, name = s.WeaponInfo.weaponName,
                    mounted = s.Weapons.Select(w => new { w.name, path = HierarchyPath(w.transform), ammo = w.GetAmmoTotal(), position = V(aircraft.transform.InverseTransformPoint(w.transform.position)) }) }),
                nozzles = nozzles.Select(n => new { n.name, n.priority, proportion = SkylarkTemplate.Get<float>(n, "thrustProportion"),
                    direction = V(aircraft.transform.InverseTransformDirection(SkylarkTemplate.Get<Transform>(n, "thrustTransform").forward)),
                    position = V(aircraft.transform.InverseTransformPoint(SkylarkTemplate.Get<Transform>(n, "thrustTransform").position)) }),
                parts = aircraft.partLookup.Select(p => new { p.name, p.mass, p.hitPoints, detached = p.IsDetached(),
                    position = V(aircraft.transform.InverseTransformPoint(p.transform.position)),
                    centerOfMass = V(aircraft.transform.InverseTransformPoint(p.rb.transform.TransformPoint(p.rb.centerOfMass))),
                    collisionSize = V(SkylarkTemplate.Get<Vector3>(p, "collisionSize")),
                    wingArea = p is AeroPart area ? area.WingArea : 0f,
                    airfoilID = p is AeroPart foil ? SkylarkTemplate.Get<int>(foil, "airfoilID") : -1,
                    aero = p is AeroPart job ? Aero(job) : null,
                    liftUp = p is AeroPart a ? V(aircraft.transform.InverseTransformDirection(a.LiftNormal.up)) : null })
            };
        }

        private static readonly Dictionary<Aircraft, int> BurstStart = new Dictionary<Aircraft, int>();

        private static object Halberd(Aircraft aircraft)
        {
            SkylarkTurret turret = aircraft.GetComponent<SkylarkTurret>();
            SkylarkDrive drive = aircraft.GetComponent<SkylarkDrive>();
            int ammo = aircraft.weaponStations.Count > 0 ? aircraft.weaponStations[0].Ammo : 0;
            WeaponInfo info = aircraft.weaponStations.Count > 0 ? aircraft.weaponStations[0].WeaponInfo : null;
            return new
            {
                name = info != null ? info.weaponName : null, shortName = info != null ? info.shortName : null, muzzleVelocity = info != null ? info.muzzleVelocity : 0f,
                pierce = info != null ? info.pierceDamage : 0f, ammo, lastBurst = BurstStart.TryGetValue(aircraft, out int start) ? start - ammo : -1,
                rate = turret != null ? turret.Rate : -1f, shots = turret != null ? turret.Shots : -1, ripples = turret != null ? turret.Ripples : -1,
                deploy = turret != null ? turret.Deploy : -1f, refused = SkylarkCoilsDryPatch.Refused,
                gunShots = drive.GunShots, gunDrain = drive.GunDrain, drainPerShot = drive.GunShots > 0 ? drive.GunDrain / drive.GunShots : 0f
            };
        }

        private static string HierarchyPath(Transform t) => t == null ? "" : (t.parent == null ? "" : HierarchyPath(t.parent) + "/") + t.name;

        private static object TurretReport(Aircraft aircraft)
        {
            Turret turret = SkylarkWeapons.FindTurret(aircraft);
            if (turret == null) return null;
            Transform elevation = SkylarkTemplate.Get<Transform>(turret, "elevationTransform");
            return new { yaw = Mathf.DeltaAngle(0f, turret.transform.localEulerAngles.y), pitch = Mathf.DeltaAngle(0f, elevation.localEulerAngles.x),
                manual = SkylarkTemplate.Get<bool>(turret, "manual"), ready = SkylarkTemplate.Get<bool>(turret, "ready") };
        }

        private static object Aero(AeroPart part)
        {
            var fields = SkylarkTemplate.Get<PtrAllocation<AeroPartFields>>(part, "JobFields");
            if (!fields.IsCreated) return null;
            AeroPartFields value = fields.Value();
            return new { force = V(value.force), torque = V(value.torque), mode = value.hasForce.ToString(),
                finite = !float.IsNaN(value.force.sqrMagnitude) && !float.IsInfinity(value.force.sqrMagnitude),
                collisionSize = V(value.collisionSize), centerOfLift = V(value.centerOfLift) };
        }

        public static object Setup(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")))
                return new { ok = false, error = "requires nomodkit scenario" };
            Aircraft aircraft = args.TryGetValue("idUnit", out object unit) ? unit as Aircraft : null;
            if (aircraft == null || aircraft.definition.jsonKey != SkylarkTemplate.Key) return new { ok = false, error = "expected Skylark id" };
            if (args.TryGetValue("gear", out object gear)) aircraft.SetGear(Convert.ToBoolean(gear));
            if (args.TryGetValue("ignition", out object ignition)) aircraft.NetworkIgnition = Convert.ToBoolean(ignition);
            if (args.TryGetValue("fuel", out object fuel))
            {
                float level = Mathf.Clamp01(Convert.ToSingle(fuel));
                aircraft.NetworkfuelLevel = level;
                foreach (FuelTank tank in aircraft.partLookup.SelectMany(p => p.GetComponentsInChildren<FuelTank>(true)).Distinct()) tank.Refuel(level);
            }
            if (args.TryGetValue("loadout", out object loadout)) aircraft.Networkloadout = aircraft.definition.aircraftParameters.loadouts[Convert.ToInt32(loadout)];
            CameraStateManager camera = CameraStateManager.i;
            if (args.TryGetValue("look", out object look)) SkylarkTemplate.Set(camera.cockpitState, "tiltView", Convert.ToSingle(look));
            if (args.TryGetValue("charge", out object charge)) aircraft.GetComponent<SkylarkDrive>().ForceCharge(Convert.ToSingle(charge));
            if (args.TryGetValue("nav", out object nav) && Convert.ToBoolean(nav)) aircraft.GetComponentInChildren<NavLights>(true)?.ToggleNavLights();
            if (args.TryGetValue("pilotVisible", out object visible))
                SkylarkTemplate.Get<SkinnedMeshRenderer>(aircraft.pilots[0], "skinnedMeshRenderer").enabled = Convert.ToBoolean(visible);
            return Status(args);
        }

        public static object Hold(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"))) return new { ok = false };
            Aircraft aircraft = args.TryGetValue("idUnit", out object unit) ? unit as Aircraft : null;
            if (aircraft == null || aircraft.definition.jsonKey != SkylarkTemplate.Key) return new { ok = false };
            float throttle = args.TryGetValue("throttle", out object power) ? Mathf.Clamp01(Convert.ToSingle(power)) : .5f;
            bool auto = !args.TryGetValue("autoHover", out object enabled) || Convert.ToBoolean(enabled);
            float Axis(string key) => args.TryGetValue(key, out object v) ? Mathf.Clamp(Convert.ToSingle(v), -1f, 1f) : 0f;
            aircraft.pilots[0].SwitchStateNew(new SkylarkHoverHold(throttle, auto, Axis("pitch"), Axis("roll")));
            return new { ok = true, mode = "native player auto-hover with neutral controls" };
        }

        /// <summary>Hands the aircraft back to the native player state (ends a Hold), as a pilot at the controls.</summary>
        public static object Release(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"))) return new { ok = false };
            Aircraft aircraft = args.TryGetValue("idUnit", out object unit) ? unit as Aircraft : null;
            if (aircraft == null || aircraft.definition.jsonKey != SkylarkTemplate.Key) return new { ok = false };
            aircraft.pilots[0].SwitchState(aircraft.pilots[0].playerState);
            return new { ok = true };
        }

        public static object Fire(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"))) return new { ok = false };
            Aircraft aircraft = args.TryGetValue("idUnit", out object unit) ? unit as Aircraft : null;
            if (aircraft == null || aircraft.definition.jsonKey != SkylarkTemplate.Key) return new { ok = false };
            byte station = Convert.ToByte(args["station"]);
            aircraft.weaponManager.SetActiveStation(station);
            if (station == 0 && aircraft.weaponStations.Count > 0) BurstStart[aircraft] = aircraft.weaponStations[0].Ammo;
            if (args.TryGetValue("targetUnit", out object target) && target is Unit targetUnit) aircraft.weaponManager.AddTargetList(targetUnit);
            float seconds = args.TryGetValue("seconds", out object duration) ? Mathf.Clamp(Convert.ToSingle(duration), 0f, 5f) : 0f;
            if (args.TryGetValue("trigger", out object trigger) && !Convert.ToBoolean(trigger)) return Status(args); // select only
            if (seconds > 0) aircraft.StartCoroutine(Trigger(aircraft, seconds));
            else aircraft.weaponManager.Fire();
            return Status(args);
        }

        private static System.Collections.IEnumerator Trigger(Aircraft aircraft, float seconds)
        {
            float end = Time.time + seconds;
            while (aircraft != null && !aircraft.disabled && Time.time < end)
            {
                aircraft.weaponManager.Fire();
                yield return null;
            }
        }

        public static Dictionary<string, object> Vector(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")))
                return new Dictionary<string, object> { ["ok"] = false, ["error"] = "requires nomodkit scenario" };
            Aircraft aircraft = args.TryGetValue("idUnit", out object unit) ? unit as Aircraft : null;
            if (aircraft == null || aircraft.definition.jsonKey != SkylarkTemplate.Key)
                return new Dictionary<string, object> { ["ok"] = false, ["error"] = "expected spawned Skylark id" };
            DuctedThrustSystem system = aircraft.GetComponent<DuctedThrustSystem>();
            aircraft.GetComponent<SkylarkFlightMode>().Set(VectorMode.Manual, false);
            object mode = SkylarkTemplate.Get<object>(system, "mode");
            SkylarkTemplate.Set(system, "mode", Enum.Parse(mode.GetType(), "Manual"));
            float degrees = args.TryGetValue("degrees", out object angle) ? Convert.ToSingle(angle) : 90f;
            aircraft.GetInputs().customAxis1 = 1f - Mathf.Clamp(degrees, 0f, 90f) / 90f;
            return new Dictionary<string, object> { ["ok"] = true, ["commandedDegrees"] = degrees };
        }


        /// <summary>Weapon research dump: every Encyclopedia mount, plus the hardpoint and turret layout of a few donor airframes. Written to the temp folder as skylark-mounts.json.</summary>
        public static object DumpMounts(Dictionary<string, object> args)
        {
            string Path(Transform t, Transform root) => t == null ? null : t == root ? "" : (t.parent == null || t.parent == root ? t.name : Path(t.parent, root) + "/" + t.name);
            object TurretOf(Turret t, Transform root) => t == null ? null : new
            {
                t.name, path = Path(t.transform, root), local = V(root.InverseTransformPoint(t.transform.position)),
                traverseRate = SkylarkTemplate.Get<float>(t, "traverseRate"), traverseRange = SkylarkTemplate.Get<float>(t, "traverseRange"),
                elevationRate = SkylarkTemplate.Get<float>(t, "elevationRate"), minElevation = SkylarkTemplate.Get<float>(t, "minElevation"), maxElevation = SkylarkTemplate.Get<float>(t, "maxElevation"),
                acquisition = SkylarkTemplate.Get<object>(t, "targetAcquisitionMode").ToString(), elevationPath = Path(SkylarkTemplate.Get<Transform>(t, "elevationTransform"), root),
                cones = SkylarkTemplate.Get<FiringCone[]>(t, "firingCones")?.Select(c => new { angle = c.GetAngle(), dir = V(root.InverseTransformDirection(c.GetDirection())) }),
                stations = SkylarkTemplate.Get<WeaponStation[]>(t, "weaponStations")?.Select(s => new { s.FullAmmo, info = s.WeaponInfo != null ? s.WeaponInfo.weaponName : null, weapons = s.Weapons.Select(w => w.GetType().Name + ":" + Path(w.transform, root)) }),
                manual = SkylarkTemplate.Get<bool>(t, "manual"), stowed = SkylarkTemplate.Get<bool>(t, "stowed"),
                renderers = t.GetComponentsInChildren<Renderer>(true).Length
            };
            var mounts = Encyclopedia.i.weaponMounts.Where(m => m != null).Select(m => new
            {
                key = m.jsonKey, name = m.mountName, m.mass, m.emptyMass, m.ammo, m.turret, bay = m.missileBay, m.GunAmmo, m.drag, m.emptyDrag,
                info = m.info == null ? null : new { m.info.weaponName, m.info.gun, m.info.missile, m.info.bomb, m.info.glideBomb, m.info.laserGuided, m.info.massPerRound, m.info.cargo, m.info.jammer, m.info.energy },
                turretPrefab = m.turret && m.prefab != null ? TurretOf(m.prefab.GetComponentInChildren<Turret>(true), m.prefab.transform) : null
            }).ToArray();
            var airframes = new[] { "VTOLTrainer1", "AttackHelo1", "QuadVTOL1" }.Select(key =>
            {
                AircraftDefinition d = Encyclopedia.i.aircraft.FirstOrDefault(a => a.jsonKey == key);
                Aircraft a = d == null ? null : d.unitPrefab.GetComponent<Aircraft>();
                if (a == null) return new { key, sets = (object)null };
                return new
                {
                    key, sets = (object)a.weaponManager.hardpointSets.Select(s => new
                    {
                        s.name, options = s.weaponOptions.Select(m => m == null ? null : m.jsonKey),
                        hardpoints = s.hardpoints.Select(h => new { path = Path(h.transform, a.transform), local = V(a.transform.InverseTransformPoint(h.transform.position)),
                            turrets = h.BuiltInTurrets?.Select(t => TurretOf(t, a.transform)), weapons = h.BuiltInWeapons?.Select(w => w.GetType().Name + ":" + Path(w.transform, a.transform)) })
                    })
                };
            }).ToArray();
            var donor = Encyclopedia.i.weaponMounts.First(m => m.jsonKey == "turret_30mm_rotary").prefab;
            var parts = donor.GetComponentsInChildren<Component>(true).Select(c => Path(c.transform, donor.transform) + ":" + c.GetType().Name).ToArray();
            var report = new { mounts, airframes, parts };
            string json = JsonConvert.SerializeObject(report, Formatting.Indented);
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skylark-mounts.json"), json);
            return new { ok = true, mounts = mounts.Length, bytes = json.Length };
        }

        /// <summary>Gun tuning facts: the donor and Halberd shells, and the pierce armour of every vehicle and helicopter's damage parts.</summary>
        public static object GunFacts(Dictionary<string, object> args)
        {
            object Info(WeaponInfo i) => i == null ? null : new { i.weaponName, i.shortName, i.muzzleVelocity, i.pierceDamage, i.blastDamage, i.armorTierEffectiveness, i.visibilityWhenFired, i.massPerRound, i.costPerRound, i.fireInterval };
            object Armor(Unit u) => u == null ? null : u.GetComponentsInChildren<IDamageable>(true).Select(d => d.GetArmorProperties()).Where(a => a != null)
                .Select(a => new { a.pierceArmor, a.pierceTolerance, a.blastArmor, a.blastTolerance }).Distinct().Take(6);
            WeaponMount donor = Encyclopedia.i.weaponMounts.FirstOrDefault(m => m != null && m.jsonKey == "turret_30mm_rotary");
            Gun donorGun = donor != null ? donor.prefab.GetComponentInChildren<Gun>(true) : null;
            return new
            {
                donor = Info(donor?.info), donorRate = donorGun != null ? SkylarkTemplate.Get<float>(donorGun, "fireRate") : 0f,
                halberd = Info(SkylarkWeapons.TurretMount?.info),
                vehicles = Encyclopedia.i.vehicles.Where(v => v != null && v.unitPrefab != null).Select(v => new { v.unitName, v.jsonKey, armor = Armor(v.unitPrefab.GetComponent<Unit>()) }),
                aircraft = Encyclopedia.i.aircraft.Where(v => v != null && v.unitPrefab != null).Select(v => new { v.unitName, v.jsonKey, armor = Armor(v.unitPrefab.GetComponent<Unit>()) })
            };
        }

        /// <summary>Drives the same entry points as the C menu:{id, vector auto|drone|plane|manual, drive auto|ev|hybrid, mast bool}.</summary>
        public static object Modes(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"))) return new { ok = false };
            Aircraft aircraft = args.TryGetValue("idUnit", out object unit) ? unit as Aircraft : null;
            if (aircraft == null || aircraft.definition.jsonKey != SkylarkTemplate.Key) return new { ok = false, error = "expected Skylark id" };
            if (args.TryGetValue("vector", out object vector)) aircraft.GetComponent<SkylarkFlightMode>().Set((VectorMode)Enum.Parse(typeof(VectorMode), (string)vector, true));
            if (args.TryGetValue("drive", out object drive)) SkylarkNet.Request(aircraft, SkylarkNet.DriveAction, (byte)(DriveMode)Enum.Parse(typeof(DriveMode), (string)drive, true));
            if (args.TryGetValue("mast", out object mast)) aircraft.GetComponent<SkylarkMast>().Request(Convert.ToBoolean(mast));
            if (args.TryGetValue("gear", out object gear)) aircraft.SetGear(Convert.ToBoolean(gear));
            return new { ok = true };
        }

        /// <summary>Sim only: until the art carries the appended stations, stand-in anchors let the loadout machinery be exercised.</summary>
        internal static void AddTestAnchors(Dictionary<string, Transform> nodes, Transform model)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"))) return;
            void Add(string name, Transform sibling, Vector3 at)
            {
                if (nodes.ContainsKey(name)) return;
                Transform anchor = new GameObject(name).transform;
                anchor.SetParent(sibling.parent, false);
                anchor.position = at;
                nodes[name] = anchor;
            }
            foreach (string side in new[] { "L", "R" })
                Add("Hardpoint_" + side + "3", nodes["Hardpoint_" + side + "1"], (nodes["Hardpoint_" + side + "1"].position + nodes["Hardpoint_" + side + "2"].position) * .5f);
            Add("Hardpoint_C", nodes["CenterOfMass"], nodes["CenterOfMass"].position + new Vector3(0f, -.5f, 1.5f));
        }

        public static string Report()
        {
            AircraftDefinition def = SkylarkTemplate.Definition;
            if (def == null) return JsonConvert.SerializeObject(new { registered = false, artBundle = SkylarkTemplate.ArtBundle, error = SkylarkTemplate.LastError });
            Aircraft aircraft = def.unitPrefab.GetComponent<Aircraft>();
            var stores = aircraft.weaponManager.hardpointSets.SelectMany(s => s.weaponOptions).Where(m => m != null).Distinct()
                .Select(m => new { key = m.jsonKey, name = m.mountName, mass = m.mass, ammo = m.ammo,
                    bounds = Bounds(SkylarkTemplate.LocalBounds(m.prefab.transform, m.prefab.GetComponentsInChildren<Renderer>(true))) });
            return JsonConvert.SerializeObject(new
            {
                registered = true, key = def.jsonKey, artBundle = SkylarkTemplate.ArtBundle,
                sets = aircraft.weaponManager.hardpointSets.Length, loadouts = def.aircraftParameters.loadouts.Count,
                scale = SkylarkTemplate.Scale, spawnOffset = def.spawnOffset.y, index = ((INetworkDefinition)def).LookupIndex,
                prefabHash = aircraft.GetComponent<Mirage.NetworkIdentity>().PrefabHash,
                mass = def.mass, fuel = aircraft.GetComponentsInChildren<FuelTank>(true).Sum(t => t.FuelCapacity),
                wingArea = aircraft.GetComponentsInChildren<AeroPart>(true).Sum(p => p.WingArea),
                nozzleCount = SkylarkTemplate.Get<JetNozzle[]>(aircraft.GetComponent<DuctedThrustSystem>(), "nozzles").Length,
                pilotCount = aircraft.pilots.Length,
                hardpoints = aircraft.weaponManager.hardpointSets.Select(s => new { s.name, options = s.weaponOptions.Select(m => m == null ? null : m.jsonKey),
                    anchors = s.hardpoints.Select(h => V(aircraft.transform.InverseTransformPoint(h.transform.position))) }), stores,
                instances = UnityEngine.Object.FindObjectsOfType<Aircraft>().Where(a => a.definition.jsonKey == SkylarkTemplate.Key).Select(a => new
                {
                    name = a.name, speed = a.speed, altitude = a.radarAlt, fuel = a.fuelLevel,
                    position = V(a.transform.position), velocity = V(a.rb.velocity), rotation = V(a.transform.eulerAngles),
                    mass = a.GetMass(), centerOfMass = V(a.transform.InverseTransformPoint(a.CalcCenterOfMass())),
                    angle = a.GetComponent<DuctedThrustSystem>().angle, parts = a.partLookup.Count,
                    nozzles = a.GetComponentsInChildren<JetNozzle>(true).Select(n => new { n.name, n.priority })
                })
            });
        }

        private static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
        private static object Bounds(Bounds b) => new { center = V(b.center), size = V(b.size) };
    }

    internal sealed class SkylarkHoverHold : PilotBaseState
    {
        private readonly float throttle, pitch, roll;
        private readonly bool autoHover;
        /// <summary>Neutral sticks by default; pitch / roll hold a fixed stick deflection (agility runs).</summary>
        internal SkylarkHoverHold(float throttle = .5f, bool autoHover = true, float pitch = 0f, float roll = 0f)
        { this.throttle = throttle; this.autoHover = autoHover; this.pitch = pitch; this.roll = roll; }
        public override void EnterState(Pilot pilot)
        {
            this.pilot = pilot; aircraft = pilot.aircraft; controlInputs = aircraft.GetInputs();
            stateDisplayName = "Skylark native hover test";
            if (aircraft.IsAutoHoverEnabled() != autoHover) aircraft.GetControlsFilter().ToggleAutoHover();
        }
        public override void UpdateState(Pilot pilot) { }
        public override void FixedUpdateState(Pilot pilot)
        {
            controlInputs.pitch = pitch; controlInputs.roll = roll; controlInputs.yaw = 0; controlInputs.throttle = throttle;
            aircraft.FilterInputs();
        }
        public override void LeaveState()
        {
            if (aircraft.IsAutoHoverEnabled()) aircraft.GetControlsFilter().ToggleAutoHover();
            aircraft.SetFlightAssist(true);
        }
    }
}
#endif
