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
                autoHover = aircraft.IsAutoHoverEnabled(), rawPitch = aircraft.GetControlsFilter().GetFlyByWire().GetRawPitch(),
                targetPitchRate = aircraft.GetControlsFilter().GetFlyByWire().GetTargetPitchAngVel(),
                attitude = V(aircraft.transform.eulerAngles), velocity = V(aircraft.rb.velocity),
                angularVelocity = V(aircraft.transform.InverseTransformDirection(aircraft.rb.angularVelocity)),
                centerOfMass = V(aircraft.transform.InverseTransformPoint(SkylarkAnimation.PhysicsCenter(aircraft))),
                detached = aircraft.partLookup.Count(p => p.IsDetached()),
                thrustRatio = SkylarkTemplate.Get<float>(duct, "thrustRatio"),
                damageSource = SkylarkMaterials.DamageSource,
                statusDisplay = SkylarkStatusDisplay.Inspect(aircraft),
                ammunition = aircraft.weaponStations.ToDictionary(s => s.Number.ToString(), s => s.Ammo),
                weapons = aircraft.weaponStations.Select(s => new { s.Number, s.Ammo, s.FullAmmo, name = s.WeaponInfo.weaponName,
                    mounted = s.Weapons.Select(w => new { w.name, ammo = w.GetAmmoTotal(), position = V(aircraft.transform.InverseTransformPoint(w.transform.position)) }) }),
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
            if (args.TryGetValue("fuel", out object fuel))
            {
                float level = Mathf.Clamp01(Convert.ToSingle(fuel));
                aircraft.NetworkfuelLevel = level;
                foreach (FuelTank tank in aircraft.partLookup.SelectMany(p => p.GetComponentsInChildren<FuelTank>(true)).Distinct()) tank.Refuel(level);
            }
            if (args.TryGetValue("loadout", out object loadout)) aircraft.Networkloadout = aircraft.definition.aircraftParameters.loadouts[Convert.ToInt32(loadout)];
            CameraStateManager camera = CameraStateManager.i;
            if (args.TryGetValue("look", out object look)) SkylarkTemplate.Set(camera.cockpitState, "tiltView", Convert.ToSingle(look));
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
            aircraft.pilots[0].SwitchStateNew(new SkylarkHoverHold(throttle, auto));
            return new { ok = true, mode = "native player auto-hover with neutral controls" };
        }

        public static object Fire(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"))) return new { ok = false };
            Aircraft aircraft = args.TryGetValue("idUnit", out object unit) ? unit as Aircraft : null;
            if (aircraft == null || aircraft.definition.jsonKey != SkylarkTemplate.Key) return new { ok = false };
            byte station = Convert.ToByte(args["station"]);
            aircraft.weaponManager.SetActiveStation(station);
            if (args.TryGetValue("targetUnit", out object target) && target is Unit targetUnit) aircraft.weaponManager.AddTargetList(targetUnit);
            float seconds = args.TryGetValue("seconds", out object duration) ? Mathf.Clamp(Convert.ToSingle(duration), 0f, 5f) : 0f;
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
            object mode = SkylarkTemplate.Get<object>(system, "mode");
            SkylarkTemplate.Set(system, "mode", Enum.Parse(mode.GetType(), "Manual"));
            float degrees = args.TryGetValue("degrees", out object angle) ? Convert.ToSingle(angle) : 90f;
            aircraft.GetInputs().customAxis1 = 1f - Mathf.Clamp(degrees, 0f, 90f) / 90f;
            return new Dictionary<string, object> { ["ok"] = true, ["commandedDegrees"] = degrees };
        }

        public static string Report()
        {
            AircraftDefinition def = SkylarkTemplate.Definition;
            if (def == null) return JsonConvert.SerializeObject(new { registered = false, error = SkylarkTemplate.LastError });
            Aircraft aircraft = def.unitPrefab.GetComponent<Aircraft>();
            var stores = aircraft.weaponManager.hardpointSets.SelectMany(s => s.weaponOptions).Where(m => m != null).Distinct()
                .Select(m => new { key = m.jsonKey, name = m.mountName, mass = m.mass, ammo = m.ammo,
                    bounds = Bounds(SkylarkTemplate.LocalBounds(m.prefab.transform, m.prefab.GetComponentsInChildren<Renderer>(true))) });
            return JsonConvert.SerializeObject(new
            {
                registered = true, key = def.jsonKey, index = ((INetworkDefinition)def).LookupIndex,
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
        private readonly float throttle;
        private readonly bool autoHover;
        internal SkylarkHoverHold(float throttle = .5f, bool autoHover = true) { this.throttle = throttle; this.autoHover = autoHover; }
        public override void EnterState(Pilot pilot)
        {
            this.pilot = pilot; aircraft = pilot.aircraft; controlInputs = aircraft.GetInputs();
            stateDisplayName = "Skylark native hover test";
            if (aircraft.IsAutoHoverEnabled() != autoHover) aircraft.GetControlsFilter().ToggleAutoHover();
        }
        public override void UpdateState(Pilot pilot) { }
        public override void FixedUpdateState(Pilot pilot)
        {
            controlInputs.pitch = 0; controlInputs.roll = 0; controlInputs.yaw = 0; controlInputs.throttle = throttle;
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
