using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BoscaliSummer.Core.Game;
using HarmonyLib;
using Mirage;
using NuclearOption.SavedMission;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Skylark
{
    /// <summary>A native Vagrant systems clone. No replacement flight integrator, weapon system or network protocol.</summary>
    internal static class SkylarkTemplate
    {
        internal const string Key = "BS_Skylark";
        internal const int PrefabHash = 0x42534B31;
        private const string ArtRoot = "assets/blueprinter/mods/boscalisummer/skylark/";
        internal const float EmptyMass = 3100f, FuelCapacity = 1000f, MaxThrust = 62000f;
        // Fan arm length (Duct_FL to Duct_RL) of the first art generation: every authored size below scales with measured/reference.
        private const float ReferenceArm = 5.7611f;
        /// <summary>How much larger the loaded art is than the first generation (1 for the old bundle).</summary>
        internal static float Scale { get; private set; } = 1f;
        internal static AircraftDefinition Definition { get; private set; }
        internal static string LastError { get; private set; }
        private static GameObject holder;
        private static AssetBundle bundle;
        private static bool building, failed;

        internal static string ArtBundle => bundle != null ? bundle.name : null;
        internal static Texture2D LoadTexture(string filename) => bundle.LoadAsset<Texture2D>(ArtRoot + "textures/" + filename);

        internal static T Get<T>(object o, string field) => (T)AccessTools.Field(o.GetType(), field).GetValue(o);
        internal static void Set(object o, string field, object value) => AccessTools.Field(o.GetType(), field).SetValue(o, value);
        internal static Transform Node(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        internal static void EmptyArray(object o, string field)
        {
            FieldInfo f = AccessTools.Field(o.GetType(), field);
            f.SetValue(o, Array.CreateInstance(f.FieldType.GetElementType(), 0));
        }

        private static readonly string[] Optional =
        {
            "GunTurret_Yaw", "GunTurret_Pitch", "GunBarrel", "GunMuzzle", "GunGlow", "GunBayDoor_R", "GunBayDoor_L",
            "GunCoil0", "GunCoil1", "GunCoil2", "GunCoil3", "GunCoil4", "GunCoil5", "Hardpoint_L3", "Hardpoint_R3", "Hardpoint_C", "Intake_Door_L", "Intake_Door_R",
            "Exhaust_Shutter_L", "Exhaust_Shutter_R", "C2_Mast", "Strobe_Top", "Strobe_Bottom"
        };

        internal static void Register(Encyclopedia encyclopedia, AssetBundle pack = null)
        {
            if (building || failed || encyclopedia == null || Encyclopedia.Lookup == null || encyclopedia.IndexLookup == null) return;
            if (Encyclopedia.Lookup.ContainsKey(Key)) return;
            building = true;
            try
            {
                if (Definition == null)
                {
                    bundle = pack;
                    Definition = Build(encyclopedia);
                }
                if (!encyclopedia.aircraft.Contains(Definition)) encyclopedia.aircraft.Add(Definition);
                ((INetworkDefinition)Definition).LookupIndex = encyclopedia.IndexLookup.Count;
                encyclopedia.IndexLookup.Add(Definition);
                Encyclopedia.Lookup.Add(Key, Definition);
                Set(encyclopedia, "aircraftAndVehiclesCache", null);
                WeaponMount turret = SkylarkWeapons.TurretMount;
                if (!encyclopedia.weaponMounts.Contains(turret)) encyclopedia.weaponMounts.Add(turret);
                ((INetworkDefinition)turret).LookupIndex = encyclopedia.IndexLookup.Count;
                encyclopedia.IndexLookup.Add(turret);
                Encyclopedia.WeaponLookup.Add(SkylarkWeapons.TurretKey, turret);
                Definition.mass = Definition.unitPrefab.GetComponentsInChildren<UnitPart>(true).Sum(p => p.mass);
                // An existing client may already have indexed its prefabs. A later client indexes encyclopedia.aircraft itself.
                var manager = GameAccess.NetworkManagerOrNull;
                if (manager != null && manager.ClientObjectManager != null)
                    manager.ClientObjectManager.RegisterPrefab(Definition.unitPrefab.GetComponent<NetworkIdentity>());
                Debug.Log("[Skylark] Registered " + Key + " native four-fan VTOL, empty mass " + Definition.mass + " kg, art scale " + Scale.ToString("0.000") + ", ground offset " + Definition.spawnOffset.y.ToString("0.000") + " m, " + Definition.unitPrefab.GetComponent<Aircraft>().weaponManager.hardpointSets.Length + " stations");
            }
            catch (Exception e)
            {
                failed = true;
                LastError = e.ToString();
                Debug.LogError("[Skylark] Registration failed: " + e);
            }
            finally { building = false; }
        }

        private static AircraftDefinition Build(Encyclopedia encyclopedia)
        {
            AircraftDefinition donor = encyclopedia.aircraft.First(d => d.jsonKey == "VTOLTrainer1");
            GameObject art = bundle.LoadAsset<GameObject>(ArtRoot + "bs_skylark.prefab");
            if (art == null) throw new InvalidOperationException("The Boscali Summer Blueprinter pack has no usable BS_Skylark prefab.");
            holder = new GameObject("SkylarkTemplates");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            GameObject go = Object.Instantiate(donor.unitPrefab, holder.transform);
            go.name = Key;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.GetComponent<NetworkIdentity>().PrefabHash = PrefabHash;
            Aircraft aircraft = go.GetComponent<Aircraft>();
            AircraftDefinition definition = Object.Instantiate(donor);
            definition.name = Key;
            definition.jsonKey = Key;
            definition.code = "VS-4";
            definition.unitName = "VS-4 Skylark";
            definition.description = "A low-observable single-seat quad-fan VTOL scout and precision attacker. Four vectoring fans give it the tightest turn and quickest hover of any VTOL, and its faceted airframe is hard to see on radar. It trades payload, endurance and top speed for surprise and access to small landing sites.";
            definition.value = 26f;
            definition.radarSize = 0.03f;   // faceted, canted, shrouded fans, coated canopy: the low-observable pick
            definition.manpower = 1;
            definition.spawnOffset = new Vector3(0, 1.03f, 0); // replaced by the measured wheel height once the gear is fitted
            definition.restRotation = Vector3.zero;
            definition.unitPrefab = go;
            // Encyclopedia figures (km/h): 753 = the measured 209 m/s level plateau; max weight = what the turbine alone lifts vertically
            // (BOOST lifts about 15 % more); maneuverability is the fly-by-wire g limit.
            definition.aircraftInfo = new AircraftInfo { emptyWeight = EmptyMass, maxWeight = 6300f, maxSpeed = 753f, stallSpeed = 150f, maneuverability = 7.5f };
            definition.aircraftParameters = Object.Instantiate(donor.aircraftParameters);
            AircraftParameters p = definition.aircraftParameters;
            // Airfoil.id is nonserialized and indexes native prebuilt aerodynamic charts.
            // Keep the registered immutable profiles; Instantiate would otherwise reset their IDs to zero.
            p.airfoils = donor.aircraftParameters.airfoils;
            // One authored finish: retain a valid native backend key without advertising donor UV liveries.
            p.liveries = new List<AircraftParameters.Livery> { new AircraftParameters.Livery
            {
                name = "Skylark tactical grey", faction = null,
                assetReference = donor.aircraftParameters.liveries[0].assetReference
            } };
            p.name = Key + "_Parameters";
            p.aircraftName = definition.unitName;
            p.rankRequired = 2;
            p.aircraftGLimit = 8f;
            p.maxSpeed = 205f;
            p.PIDReferenceAirspeed = 100f;
            p.takeoffSpeed = 30f;
            p.takeoffDistance = 35f;
            p.cornerSpeed = 95f;
            p.turningRadius = 350f;
            p.minimumRadarAlt = 35f;
            p.approachSpeed = 40f;
            p.landingSpeed = 45f;
            p.shortLandingSpeed = 20f;
            p.cruiseThrottle = 0.75f;
            p.DefaultFuelLevel = 0.7f;
            p.verticalLanding = true;
            ((Unit)aircraft).definition = definition;
            Transform model = Object.Instantiate(art, go.transform).transform;
            model.name = "SkylarkVisuals";
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
            model.localScale = Vector3.one;
            Dictionary<string, Transform> nodes = model.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
#if !PUBLIC_RELEASE
            SkylarkAutomation.AddTestAnchors(nodes, model);
#endif
            Scale = (Vector3.Distance(nodes["Duct_FL"].position, nodes["Duct_RL"].position) + Vector3.Distance(nodes["Duct_FR"].position, nodes["Duct_RR"].position)) * .5f / ReferenceArm;
            definition.width = 7.3f * Scale;
            definition.height = 2.55f * Scale;
            definition.length = 10.116f * Scale;
            RefitAirframe(aircraft, model, nodes);
            SkylarkStatusDisplay.Refit(aircraft, p, nodes);
            SkylarkMaterials.Refit(aircraft, nodes);
            RefitControls(aircraft, nodes);
            SkylarkAerodynamics.Configure(aircraft, nodes);
            RefitPropulsion(aircraft, nodes);
            definition.spawnOffset = new Vector3(0, RefitGear(aircraft, nodes) + .03f, 0);
            SkylarkLights.Refit(aircraft, nodes);
            RefitSensors(aircraft, nodes);
            SkylarkWeapons.Refit(aircraft, p, nodes, holder.transform, encyclopedia.weaponMounts);
            SkylarkCockpit.Refit(aircraft, nodes);
            go.AddComponent<SkylarkAnimation>().Configure(nodes);
            go.AddComponent<SkylarkDrive>().Configure(nodes);
            go.AddComponent<SkylarkFlightMode>();
            go.AddComponent<SkylarkMast>().Configure(nodes);
            if (nodes.ContainsKey("GunTurret_Yaw") && nodes.ContainsKey("GunTurret_Pitch")) go.AddComponent<SkylarkTurret>().Configure(nodes);
            string[] absent = Optional.Where(n => !nodes.ContainsKey(n)).ToArray();
            if (absent.Length > 0) Debug.Log("[Skylark] art without optional nodes (those features stay off): " + string.Join(", ", absent));
            return definition;
        }

        private static void RefitAirframe(Aircraft aircraft, Transform model, Dictionary<string, Transform> nodes)
        {
            Transform root = aircraft.transform;
            AeroPart[] parts = root.GetComponentsInChildren<AeroPart>(true);
            Renderer[] nativeRenderers = root.GetComponentsInChildren<Renderer>(true).Where(r => !r.transform.IsChildOf(model)).ToArray();
            foreach (Renderer r in nativeRenderers) r.enabled = false;
            foreach (LODGroup lod in root.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            // These are the donor's damage islands. All component and joint references remain native and clone-safe.
            var mapping = new Dictionary<string, string>
            {
                { "fuselage_R", "Fuselage_Engine" }, { "cockpit_R", "Intake_L" }, { "cockpit_F", "Fuselage_Cockpit" },
                { "nose", "Fuselage_Nose" }, { "intake_L", "Canard_L" }, { "intake_R", "Canard_R" },
                { "tail", "Fuselage_Tail" }, { "vstab1", "Fin_L" }, { "vstab2", "Fin_R" },
                { "rudder", "Ruddervator_L" }, { "elevator_L", "Ruddervator_R" },
                { "wingroot_FL", "LERX_L" }, { "wingroot_FR", "LERX_R" },
                { "wingroot_RL", "Sidepod_L" }, { "wingroot_RR", "Sidepod_R" },
                { "wing1_L", "Wing_L" }, { "wing1_R", "Wing_R" }, { "flap_L", "Flap_L" }, { "flap_R", "Flap_R" },
                { "wing2_L", "Arm_RL" }, { "wing2_R", "Arm_RR" }, { "aileron_L", "Elevon_L" }, { "aileron_R", "Elevon_R" },
                { "wingtip_L", "Endplate_L" }, { "wingtip_R", "Endplate_R" }
            };
            var originalPositions = parts.ToDictionary(part => part, part => part.transform.position);
            foreach (AeroPart part in parts)
            {
                if (part.gameObject == aircraft.gameObject) continue;
                // Native SwivelTransform aims in its owning damage part's frame. All custom lift/control
                // frames are rebound below, so donor wing dihedral must not skew the rear fan axes.
                part.transform.rotation = root.rotation;
                part.transform.position = mapping.TryGetValue(part.name, out string name) ? nodes[name].position : originalPositions[part] * 0.65f;
            }
            foreach (AeroPart part in parts)
            {
                if (mapping.TryGetValue(part.name, out string name)) nodes[name].SetParent(part.transform, true);
                part.SetWingArea(part.WingArea * 0.32f * Scale * Scale);
                part.dragArea *= 0.55f * Scale * Scale;
                // New convex skins supply the new rigidbody mass centroids. Donor +/-1m control-surface offsets
                // would count the relocated hinge's lever arm twice.
                Set(part, "centerOfLift", Vector3.zero);
                Set(part, "centerOfMass", null);
                if (part.name == "elevator_R" || part.name == "nozzle_main") part.SetWingArea(0);
                if (part.Joints != null)
                    foreach (PartJoint joint in part.Joints)
                    {
                        if (joint.anchor != null) joint.anchor.position = part.transform.position;
                    }
            }
            float scale = EmptyMass / parts.Sum(part => part.mass);
            foreach (AeroPart part in parts) part.mass *= scale;
            // The single-seat quad-fan structure cannot inherit the tandem Vagrant's aft mass layout.
            // Keep empty mass fixed while placing machinery/avionics in the modeled body and cockpit,
            // with symmetric control surfaces and equal fan/support masses. This gives useful nose-wheel load.
            void Reassign(string name, float mass, string recipient = Key)
            {
                AeroPart source = parts.First(p => p.name == name), target = parts.First(p => p.name == recipient);
                target.mass += source.mass - mass;
                source.mass = mass;
            }
            Reassign("cockpit_R", 50f, "cockpit_F");
            Reassign("elevator_R", 1f); Reassign("nozzle_main", 1f);
            Reassign("tail", 90f); Reassign("vstab1", 30f); Reassign("vstab2", 30f);
            Reassign("rudder", 25f); Reassign("elevator_L", 25f);
            Reassign("intake_L", 90f); Reassign("intake_R", 90f);
            Reassign("wing2_L", 90f); Reassign("wing2_R", 90f);
            FuelTank[] tanks = root.GetComponentsInChildren<FuelTank>(true);
            float capacity = tanks.Sum(t => t.FuelCapacity);
            foreach (FuelTank tank in tanks) Set(tank, "fuelCapacity", tank.FuelCapacity * FuelCapacity / capacity);
            foreach (Transform node in nodes.Values)
                if (node.name.EndsWith("_LOD") || node.name.EndsWith("_Blur"))
                {
                    Renderer renderer = node.GetComponent<Renderer>();
                    if (renderer != null) renderer.enabled = false;
                }
            // Body mass follows the explicit Blender datum, not the Vagrant's engine location.
            Set(aircraft.GetComponent<AeroPart>(), "centerOfMass", nodes["CenterOfMass"]);
            SkylarkExterior.Refit(root, nodes);
            SkylarkGeometry.Refit(aircraft, nodes);
        }

        internal static Bounds LocalBounds(Transform parent, IEnumerable<Renderer> renderers)
        {
            Bounds bounds = default;
            bool first = true;
            foreach (Renderer r in renderers)
            {
                Bounds b = r.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 v = parent.InverseTransformPoint(r.transform.TransformPoint(b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                    if (first) { bounds = new Bounds(v, Vector3.zero); first = false; } else bounds.Encapsulate(v);
                }
            }
            return bounds;
        }

        private static void RefitControls(Aircraft aircraft, Dictionary<string, Transform> nodes)
        {
            foreach (ControlSurface surface in aircraft.GetComponentsInChildren<ControlSurface>(true))
            {
                string mesh = surface.name == "rudder" ? "Ruddervator_L" : surface.name == "elevator_L" ? "Ruddervator_R"
                    : surface.name == "aileron_L" ? "Elevon_L" : surface.name == "aileron_R" ? "Elevon_R" : null;
                if (mesh == null) { Object.DestroyImmediate(surface); continue; }
                Transform visual = nodes[mesh];
                AeroPart part = Get<UnitPart>(surface, "attachedSurface") as AeroPart;
                bool tail = mesh.StartsWith("Ruddervator");
                Transform motion;
                if (tail)
                {
                    // Native ControlSurface rotates around local X; the authored V-tail uses local Z as its hinge.
                    motion = Empty(mesh + "_NativeHinge", part.transform, visual.position);
                    Vector3 axis = visual.TransformDirection(Vector3.forward);
                    Vector3 forward = Vector3.ProjectOnPlane(aircraft.transform.forward, axis).normalized;
                    if (Vector3.Cross(forward, axis).y < 0f) axis = -axis;
                    motion.rotation = Quaternion.LookRotation(forward, Vector3.Cross(forward, axis));
                    visual.SetParent(motion, true);
                    Set(surface, "pitchRange", -29f);
                    Set(surface, "yawRange", mesh.EndsWith("L") ? 26f : -26f);
                    Set(surface, "rollRange", 0f);
                    part.SetWingArea(.35f * Scale * Scale);
                }
                else
                {
                    motion = visual;
                    // Authored elevon X points inboard/-X. Native FBW positive roll demands -Z angular velocity.
                    Set(surface, "rollRange", mesh.EndsWith("L") ? 26f : -26f);
                    Set(surface, "pitchRange", 14.5f);
                }
                Transform lift = Empty(mesh + "_Lift", motion, visual.position);
                lift.rotation = tail ? motion.rotation : Quaternion.identity;
                Set(part, "liftNormal", lift);
                Set(surface, "visibleMesh", motion.gameObject);
                Set(surface, "servoSpeed", 60f);
            }
            foreach (string side in new[] { "L", "R" })
            {
                AeroPart fin = nodes["Fin_" + side].GetComponentInParent<AeroPart>(true);
                Vector3 normal = nodes["Ruddervator_" + side].TransformDirection(Vector3.right);
                if (normal.y < 0f) normal = -normal;
                Transform lift = Empty("Fin_" + side + "_Lift", fin.transform, nodes["Fin_" + side].position);
                lift.rotation = Quaternion.LookRotation(aircraft.transform.forward, normal);
                Set(fin, "liftNormal", lift);
                fin.SetWingArea(.8f * Scale * Scale);
            }
            ControlsFilter filter = aircraft.GetComponentInChildren<ControlsFilter>(true);
            object fbw = Get<object>(filter, "flyByWire");
            Set(fbw, "gLimitPositive", 7.5f);
            Set(fbw, "cornerSpeed", 95f);
            Set(fbw, "maxRollSpeed", 210f);
            Set(fbw, "maxRollAngularVel", 5.5f);
            Set(fbw, "maxPitchAngularVel", 1.4f);
            Set(fbw, "yawTightness", 1.3f);
            object hover = Get<object>(filter, "autoHover");
            Set(hover, "hoverBaseThrottle", .5f); // Pilot neutral/climb input, not equilibrium engine throttle.
            Set(hover, "customAxis1Position", 0f); // 90-degree system: zero axis is vertical.
            Set(hover, "customAxisSlowDown", 0f); // Brake by airframe attitude; no unmodeled >90-degree fan travel.
            // Aircraft.autopilot is populated by the child's Awake, which has not run on this inactive template.
            Set(Get<object>(aircraft.GetComponentInChildren<global::Autopilot>(true), "hoverController"), "hoverThrottle", .76f);
        }

        private static void RefitPropulsion(Aircraft aircraft, Dictionary<string, Transform> nodes)
        {
            // The Vagrant's hidden reaction jets are replaced by the quad fan differential/vectoring system.
            // Retaining them would apply a second set of moments at donor airframe locations.
            foreach (ReactionControlSystem rcs in aircraft.GetComponentsInChildren<ReactionControlSystem>(true)) Object.DestroyImmediate(rcs);
            DuctedThrustSystem system = aircraft.GetComponent<DuctedThrustSystem>();
            JetNozzle donor = Get<JetNozzle[]>(system, "nozzles")[0];
            JetNozzle[] oldNozzles = Get<JetNozzle[]>(system, "nozzles");
            foreach (JetNozzle old in oldNozzles) old.enabled = false;
            JetNozzle[] nozzles = new JetNozzle[4];
            Type swivelType = typeof(DuctedThrustSystem).GetNestedType("SwivelTransform", BindingFlags.NonPublic);
            Array swivels = Array.CreateInstance(swivelType, 4);
            string[] suffixes = { "FL", "FR", "RL", "RR" };
            string[] owners = { "intake_L", "intake_R", "wing2_L", "wing2_R" };
            for (int i = 0; i < 4; i++)
            {
                AeroPart part = Node(aircraft.transform, owners[i]).GetComponent<AeroPart>();
                JetNozzle nozzle = Object.Instantiate(donor, part.transform);
                nozzle.name = "SkylarkNozzle_" + suffixes[i];
                nozzle.enabled = true;
                nozzle.transform.position = nodes["Duct_" + suffixes[i]].position;
                nozzle.transform.rotation = Quaternion.identity;
                Transform thrust = new GameObject("SkylarkThrust_" + suffixes[i]).transform;
                thrust.SetParent(nozzle.transform, false);
                Set(nozzle, "part", part);
                Set(nozzle, "thrustTransform", thrust);
                Set(nozzle, "thrustProportion", i < 2 ? .2403f : .2597f);
                Set(nozzle, "pitchThrust", i < 2 ? -.16f : .16f);
                Set(nozzle, "rollThrust", i % 2 == 0 ? .16f : -.16f);
                Set(nozzle, "thrustMaxVolume", .42f);
                EmptyArray(nozzle, "vectorTransforms");
                EmptyArray(nozzle, "afterburners");
                nozzles[i] = nozzle;
                object swivel = Activator.CreateInstance(swivelType, true);
                Set(swivel, "transform", thrust);
                Set(swivel, "part", part);
                Set(swivel, "swivelRate", 1f);
                Set(swivel, "swivelDegrees", -90f);
                Set(swivel, "swivelGain", 1f);
                Set(swivel, "yawSwivel", i % 2 == 0 ? .22f : -.22f);
                swivels.SetValue(swivel, i);
            }
            Set(system, "nozzles", nozzles);
            foreach (JetNozzle old in oldNozzles) Object.DestroyImmediate(old);
            Set(system, "swivelTransforms", swivels);
            Set(system, "swivelLimit", 90f);
            Set(system, "minSpeedForForward", 45f);
            Set(system, "thrustAtAirspeed", new AnimationCurve(new Keyframe(0, MaxThrust), new Keyframe(90, MaxThrust), new Keyframe(180, .63f * MaxThrust), new Keyframe(230, .26f * MaxThrust)));
            EmptyArray(system, "intakeDoors");
            foreach (Turbofan engine in aircraft.GetComponentsInChildren<Turbofan>(true))
            {
                Set(engine, "staticThrust", MaxThrust);
                Set(engine, "fuelConsumptionMin", .10f);
                Set(engine, "fuelConsumptionMax", .65f);
                Set(engine, "spoolRate", 1100f);
            }
        }

        internal static Transform Empty(string name, Transform parent, Vector3 position)
        {
            Transform t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.position = position;
            t.rotation = Quaternion.identity;
            return t;
        }

        /// <summary>Fits the gear and returns how far the lowest wheel's tread hangs below the root.</summary>
        private static float RefitGear(Aircraft aircraft, Dictionary<string, Transform> nodes)
        {
            float lowest = 0f;
            float size = Scale / 1.15f;
            SkylarkGear.BuildWheelWells(aircraft, nodes, size);
            var built = new List<(LandingGear Gear, string Side, Transform Hinge, Transform Unsprung, Transform Axle, Transform Spin, Transform Mount, string[] DoorNames)>();
            LandingGear[] gears = aircraft.GetComponentsInChildren<LandingGear>(true);
            foreach (LandingGear gear in gears)
            {
                bool front = Get<bool>(gear, "steering");
                string side = front ? "F" : gear.name.Contains("_L_") || gear.name.Contains("_L") ? "L" : "R";
                Transform visual = nodes["Gear_" + side];
                Transform wheel = nodes["Wheel_" + side];
                Transform mount = nodes.TryGetValue(front ? "LG_Front_Mount" : "LG_Main_Mount_" + side, out Transform m) ? m : null;
                AeroPart part = visual.GetComponentInParent<AeroPart>(true);
                Transform hinge = Empty("SkylarkGearHinge_" + side, part.transform, visual.position);
                visual.SetParent(hinge, true);
                gear.transform.SetParent(hinge, true);
                gear.transform.localRotation = Quaternion.identity;
                float radius = WheelRadius(aircraft.transform, wheel, (front ? .18f : .23f) * Scale), travel = .5f;
                lowest = Mathf.Min(lowest, aircraft.transform.InverseTransformPoint(wheel.position).y - radius);
                Transform stop = Empty("SkylarkBumpStop_" + side, hinge, wheel.position + Vector3.up * (travel - radius));
                Transform unsprung = Empty("SkylarkUnsprung_" + side, hinge, wheel.position);
                Transform axle = Empty("SkylarkAxle_" + side, unsprung, wheel.position);
                // Native LandingGear spins local +X; imported FBX wheel X points left.
                Transform spin = Empty("SkylarkWheelSpin_" + side, axle, wheel.position);
                wheel.SetParent(spin, true);
                BoxCollider collision = unsprung.gameObject.AddComponent<BoxCollider>();
                collision.size = new Vector3(.15f, radius * 2, radius * 2);
                collision.enabled = false;
                Set(gear, "attachedPart", part);
                Set(gear, "gearHinge", hinge);
                Set(gear, "bumpStop", stop.gameObject);
                Set(gear, "castPoint", stop);
                Set(gear, "unsprung", unsprung.gameObject);
                Set(gear, "axle", axle);
                Set(gear, "wheels", new[] { spin });
                Set(gear, "wheelRadius", radius);
                Set(gear, "suspensionTravel", travel);
                Set(gear, "springRate", front ? 150000f : 270000f);
                Set(gear, "dampingRate", front ? 10000f : 16000f);
                Set(gear, "maxCompression", travel * .78f);
                Set(gear, "gearCollider", collision);
                Set(gear, "foldDegrees", -90f);
                Set(gear, "foldSpeed", 45f);
                Set(gear, "hingeFoldMotion", Vector3.zero);
                Set(gear, "strutRotationTransform", axle);
                Set(gear, "strutRotation", 0f);
                Set(gear, "extendedDrag", .12f);
                EmptyArray(gear, "joints");
                EmptyArray(gear, "movingParts");
                string[] doorNames = front ? new[] { "GearDoor_F_L", "GearDoor_F_R" }
                    : new[] { "GearDoor_" + side, "GearDoor_Inboard_" + side };
                var doors = new List<LandingGear.GearDoor>();
                for (int i = 0; i < doorNames.Length; i++)
                {
                    float open = (side == "R" || front && i == 1 ? 1f : -1f) * (front ? 75f : 80f);
                    if (!front && i == 1) open = -open;
                    doors.Add(new LandingGear.GearDoor { transform = nodes[doorNames[i]], closedAngle = Vector3.zero, openAngle = new Vector3(0, open, 0) });
                }
                Set(gear, "gearDoors", doors);
                Get<ParticleSystem>(gear, "dust").transform.position = wheel.position;
                built.Add((gear, side, hinge, unsprung, axle, spin, mount, doorNames));
            }
            SkylarkGear.Install(aircraft, nodes).ConfigureLegs(aircraft, nodes, built, size);
            return -lowest;
        }

        private static float WheelRadius(Transform root, Transform wheel, float fallback)
        {
            Renderer[] tyres = wheel.GetComponentsInChildren<Renderer>(true);
            if (tyres.Length == 0) return fallback;
            Vector3 extents = LocalBounds(root, tyres).extents; // the axle is X: the other two extents are the tyre radius
            float radius = (extents.y + extents.z) * .5f;
            return radius > .1f && radius < .6f ? radius : fallback;
        }

        private static void RefitSensors(Aircraft aircraft, Dictionary<string, Transform> nodes)
        {
            UnitPart nose = nodes["Fuselage_Nose"].GetComponentInParent<UnitPart>(true);
            foreach (TargetDetector detector in aircraft.GetComponentsInChildren<TargetDetector>(true))
            {
                // The base detector is native pilot vision, not a nose-mounted IRST sensor.
                // Its empty damage callback must not leave vision following a detached nose.
                UnitPart owner = detector is Radar ? nose : nodes["Fuselage_Cockpit"].GetComponentInParent<UnitPart>(true);
                Transform mount = Empty(detector is Radar ? "SkylarkRadar" : "SkylarkPilotVision", owner.transform,
                    detector is Radar ? nodes["Fuselage_Radome"].position : nodes["CamPoint"].position);
                Set(detector, "scanner", mount);
                Set(detector, "part", owner);
                // Preserve the donor's range, magnification, scan interval, signal threshold and power consumption.
                EmptyArray(detector, "rotators");
            }
            TargetCam camera = aircraft.targetCam;
            Set(camera, "attachedPart", nose);
            Set(camera, "camMountForward", Empty("SkylarkCameraForward", nose.transform, nodes["Scanner"].position));
            Transform rear = Empty("SkylarkCameraRear", nodes["RearCam"].GetComponentInParent<UnitPart>(true).transform, nodes["RearCam"].position);
            rear.localRotation = Quaternion.Euler(0, 180f, 0);
            Set(camera, "camMountRear", rear);
            Transform landing = Empty("SkylarkCameraLanding", aircraft.transform, nodes["LandingCam"].position);
            landing.localRotation = Quaternion.Euler(90f, 0, 0);
            Set(camera, "camMountLanding", landing);
            Set(camera, "targetScreenRenderer", nodes["Tacscreen"].GetComponent<Renderer>());
            Set(camera, "vtolLandingCam", true);
            foreach (FlareEjector flare in aircraft.GetComponentsInChildren<FlareEjector>(true))
            {
                Set(flare, "ejectionPoints", new[] { "L", "R" }.Select(s => new FlareEjector.EjectionPoint
                { part = nodes["FlareEjector_" + s].GetComponentInParent<UnitPart>(true), transform = nodes["FlareEjector_" + s] }).ToArray());
                EmptyArray(flare, "flareDoors");
            }
            foreach (ChaffEjector chaff in aircraft.GetComponentsInChildren<ChaffEjector>(true))
            {
                Set(chaff, "ejectionPoints", new[] { "L", "R" }.Select(s => new ChaffEjector.EjectionPoint
                { part = nodes["FlareEjector_" + s].GetComponentInParent<UnitPart>(true), transform = nodes["FlareEjector_" + s] }).ToArray());
                EmptyArray(chaff, "flareDoors");
            }
        }
    }
}
