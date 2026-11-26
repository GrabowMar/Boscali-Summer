using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Skylark
{
    /// <summary>
    /// Mechanical undercarriage articulation and recessed wheel-well hardware: chrome oleo pistons,
    /// analytical 2-bar scissor torque links, folding drag braces, hydraulic retraction/door actuators,
    /// steerable nose-wheel headlamp housing, main brake rotors/calipers, and bay ribs/accumulators.
    /// </summary>
    internal sealed class SkylarkGear : MonoBehaviour
    {
        [SerializeField] private Aircraft aircraft;
        [SerializeField] private string[] legSides;
        [SerializeField] private LandingGear[] legGears;
        [SerializeField] private Transform[] hinges, unsprungs, braces;
        [SerializeField] private Transform[] upperScissors, lowerScissors, upperPivots, lowerPivots;
        [SerializeField] private Transform[] actBarrels, actRods, barrelTargets, rodTargets;
        [SerializeField] private float[] scissorLens;
        [SerializeField] private Transform[] doorStruts, doorLugs;

        private Quaternion[] braceRest;
        private Vector3[] braceAxis;
#if !PUBLIC_RELEASE
        private int lastFrame;
#endif

        internal int LegCount => legGears != null ? legGears.Length : 0;
        internal int DoorActuatorCount => doorStruts != null ? doorStruts.Length : 0;

        internal static SkylarkGear Install(Aircraft aircraft, Dictionary<string, Transform> nodes)
        {
            var rig = aircraft.gameObject.AddComponent<SkylarkGear>();
            rig.aircraft = aircraft;
            return rig;
        }

        internal static void BuildWheelWells(Aircraft aircraft, Dictionary<string, Transform> nodes, float size)
        {
            // Nose wheel well recessed strictly inside Fuselage_Nose above GearDoor_F_L/R.
            if (nodes.TryGetValue("Fuselage_Nose", out Transform nose) && nodes.TryGetValue("Gear_F", out Transform gearF))
            {
                var bay = new SkylarkMeshKit.Builder();
                bay.Box(new Vector3(0f, .02f, .52f), new Vector3(.28f, .025f, 1.05f)); // bay roof
                bay.Box(new Vector3(-.145f, -.07f, .52f), new Vector3(.02f, .17f, 1.05f)); // port wall
                bay.Box(new Vector3(.145f, -.07f, .52f), new Vector3(.02f, .17f, 1.05f)); // stbd wall
                for (int i = 0; i < 4; i++)
                    bay.Box(new Vector3(0f, -.01f, .14f + i * .26f), new Vector3(.27f, .04f, .025f)); // transverse ribs
                bay.AttachInAircraftFrame("Fittings_NoseWell", aircraft.transform, nose, gearF.position, size, SkylarkMeshKit.PrimerInterior);

                var acc = new SkylarkMeshKit.Builder();
                acc.Cylinder(new Vector3(-.10f, -.02f, .22f), new Vector3(-.10f, -.02f, .42f), .028f, .028f, 8);
                acc.Box(new Vector3(.09f, -.015f, .78f), new Vector3(.05f, .045f, .06f)); // uplock hook block
                acc.AttachInAircraftFrame("Fittings_NoseWellHardware", aircraft.transform, nose, gearF.position, size, SkylarkMeshKit.HazardYellow);
            }

            // Main wheel wells recessed inside Sidepod_L / Sidepod_R above GearDoor_L/R & GearDoor_Inboard_L/R.
            foreach (string side in new[] { "L", "R" })
            {
                if (!nodes.TryGetValue("Sidepod_" + side, out Transform pod) || !nodes.TryGetValue("Gear_" + side, out Transform gear)) continue;
                float sign = side == "L" ? -1f : 1f;
                var bay = new SkylarkMeshKit.Builder();
                bay.Box(new Vector3(0f, -.01f, .62f), new Vector3(.42f, .025f, 1.48f)); // bay ceiling
                bay.Box(new Vector3(sign * .21f, -.10f, .62f), new Vector3(.02f, .18f, 1.48f)); // outboard wall
                bay.Box(new Vector3(-sign * .21f, -.10f, .62f), new Vector3(.02f, .18f, 1.48f)); // inboard wall
                for (int i = 0; i < 5; i++)
                    bay.Box(new Vector3(0f, -.04f, .02f + i * .30f), new Vector3(.40f, .045f, .028f)); // ribs
                bay.AttachInAircraftFrame("Fittings_MainWell_" + side, aircraft.transform, pod, gear.position, size, SkylarkMeshKit.PrimerInterior);

                var acc = new SkylarkMeshKit.Builder();
                acc.Cylinder(new Vector3(-sign * .14f, -.05f, .18f), new Vector3(-sign * .14f, -.05f, .44f), .032f, .032f, 8);
                acc.Box(new Vector3(0f, -.04f, 1.15f), new Vector3(.065f, .05f, .07f)); // main uplock latch
                acc.AttachInAircraftFrame("Fittings_MainWellHardware_" + side, aircraft.transform, pod, gear.position, size, SkylarkMeshKit.HazardYellow);
            }
        }

        internal void ConfigureLegs(Aircraft owner, Dictionary<string, Transform> nodes,
            List<(LandingGear Gear, string Side, Transform Hinge, Transform Unsprung, Transform Axle, Transform Spin, Transform Mount, string[] DoorNames)> built, float size)
        {
            aircraft = owner;
            int n = built.Count;
            legSides = new string[n];
            legGears = new LandingGear[n];
            hinges = new Transform[n];
            unsprungs = new Transform[n];
            braces = new Transform[n];
            upperScissors = new Transform[n];
            lowerScissors = new Transform[n];
            upperPivots = new Transform[n];
            lowerPivots = new Transform[n];
            actBarrels = new Transform[n];
            actRods = new Transform[n];
            barrelTargets = new Transform[n];
            rodTargets = new Transform[n];
            scissorLens = new float[n];
            var strutList = new List<Transform>();
            var lugList = new List<Transform>();

            for (int i = 0; i < n; i++)
            {
                var entry = built[i];
                bool front = entry.Side == "F";
                int layer = entry.Hinge.gameObject.layer;

                // Chrome inner oleo piston inside unsprung (telescopes into upper strut on compression).
                float oleoR = (front ? .034f : .042f) * size;
                var oleo = new SkylarkMeshKit.Builder();
                oleo.Cylinder(Vector3.zero, Vector3.up * (.48f * size), oleoR, oleoR, 10);
                oleo.Attach("SkylarkOleo_" + entry.Side, entry.Unsprung, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.Chrome, layer);

                // Gland seal collar at bottom of upper strut.
                var seal = new SkylarkMeshKit.Builder();
                seal.HollowRing(Vector3.zero, Vector3.up, oleoR * .95f, oleoR * 1.48f, .055f * size, 12);
                Vector3 sealLocal = entry.Hinge.InverseTransformPoint(entry.Unsprung.position + aircraft.transform.up * (.42f * size));
                seal.Attach("SkylarkGlandSeal_" + entry.Side, entry.Hinge, sealLocal, Quaternion.identity, Vector3.one, SkylarkMeshKit.DarkAlloy, layer);

                // 2-bar scissor torque links aft of the strut; lower pivot follows unsprung (steers with nose wheel).
                float scissorLen = .235f * size;
                Transform upperPivot = SkylarkTemplate.Empty("SkylarkScissorUpperPivot_" + entry.Side, entry.Hinge,
                    entry.Unsprung.position + aircraft.transform.up * (.40f * size) - aircraft.transform.forward * (.075f * size));
                upperPivot.rotation = aircraft.transform.rotation;
                Transform lowerPivot = SkylarkTemplate.Empty("SkylarkScissorLowerPivot_" + entry.Side, entry.Unsprung,
                    entry.Unsprung.position + aircraft.transform.up * (.06f * size) - aircraft.transform.forward * (.075f * size));
                lowerPivot.rotation = aircraft.transform.rotation;

                var upperArm = new SkylarkMeshKit.Builder();
                upperArm.Box(new Vector3(0f, 0f, scissorLen * .5f), new Vector3(.036f * size, .022f * size, scissorLen));
                Transform upperScissor = upperArm.Attach("SkylarkScissorUpper_" + entry.Side, upperPivot, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.DarkAlloy, layer);

                var lowerArm = new SkylarkMeshKit.Builder();
                lowerArm.Box(new Vector3(0f, 0f, scissorLen * .5f), new Vector3(.032f * size, .020f * size, scissorLen));
                Transform lowerScissor = lowerArm.Attach("SkylarkScissorLower_" + entry.Side, lowerPivot, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.DarkAlloy, layer);

                // Steerable nose-wheel headlamp housing + anchor, or main-wheel brake rotors & calipers.
                if (front)
                {
                    var fork = new SkylarkMeshKit.Builder();
                    fork.Box(new Vector3(0f, .24f * size, .048f * size), new Vector3(.078f * size, .058f * size, .055f * size));
                    fork.Cylinder(new Vector3(0f, .24f * size, .072f * size), new Vector3(0f, .24f * size, .092f * size), .026f * size, .024f * size, 10);
                    fork.Attach("SkylarkNoseLampHousing", entry.Unsprung, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.DarkAlloy, layer);
                    Transform lampAnchor = SkylarkTemplate.Empty("LandingLight_Steer", entry.Unsprung,
                        entry.Unsprung.position + aircraft.transform.up * (.24f * size) + aircraft.transform.forward * (.096f * size));
                    lampAnchor.rotation = aircraft.transform.rotation;
                    nodes["LandingLight_Steer"] = lampAnchor;
                }
                else
                {
                    float sideSign = entry.Side == "L" ? 1f : -1f;
                    var rotor = new SkylarkMeshKit.Builder();
                    rotor.HollowRing(new Vector3(sideSign * .088f * size, 0f, 0f), Vector3.right, .065f * size, .135f * size, .016f * size, 14);
                    rotor.Attach("SkylarkBrakeRotor_" + entry.Side, entry.Spin, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.Chrome, layer);

                    var caliper = new SkylarkMeshKit.Builder();
                    caliper.Box(new Vector3(sideSign * .092f * size, .03f * size, -.10f * size), new Vector3(.042f * size, .085f * size, .07f * size));
                    caliper.Attach("SkylarkBrakeCaliper_" + entry.Side, entry.Axle, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.DarkAlloy, layer);
                }

                // Drag brace & hydraulic retraction actuator spanning mount to hinge lug.
                Transform brace = nodes.TryGetValue("GearBrace_" + entry.Side, out Transform br) ? br : null;
                if (brace != null && entry.Mount != null) brace.SetParent(entry.Mount, true);

                Transform actBarrel = null, actRod = null, barrelTarget = null, rodTarget = null;
                if (entry.Mount != null)
                {
                    Vector3 mountPin = entry.Mount.position + aircraft.transform.forward * (.34f * size) + aircraft.transform.up * (.04f * size);
                    Vector3 strutPin = entry.Hinge.position - aircraft.transform.up * (.38f * size);
                    barrelTarget = SkylarkTemplate.Empty("SkylarkActuatorMountPin_" + entry.Side, entry.Mount, mountPin);
                    rodTarget = SkylarkTemplate.Empty("SkylarkActuatorStrutPin_" + entry.Side, entry.Hinge, strutPin);

                    var barrelMesh = new SkylarkMeshKit.Builder();
                    barrelMesh.Cylinder(Vector3.zero, Vector3.forward * (.28f * size), .022f * size, .022f * size, 8);
                    actBarrel = barrelMesh.Attach("SkylarkRetractBarrel_" + entry.Side, barrelTarget, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.DarkAlloy, layer);

                    var rodMesh = new SkylarkMeshKit.Builder();
                    rodMesh.Cylinder(Vector3.zero, Vector3.forward * (.26f * size), .013f * size, .013f * size, 8);
                    actRod = rodMesh.Attach("SkylarkRetractRod_" + entry.Side, rodTarget, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.Chrome, layer);
                }

                // Door hydraulic struts linking wheel-well edge to each gear door.
                foreach (string doorName in entry.DoorNames)
                {
                    if (!nodes.TryGetValue(doorName, out Transform door)) continue;
                    Vector3 wellAnchor = door.position + aircraft.transform.up * (.18f * size);
                    Transform wellPin = SkylarkTemplate.Empty("SkylarkDoorWellPin_" + doorName, door.parent, wellAnchor);
                    Transform doorLug = SkylarkTemplate.Empty("SkylarkDoorLug_" + doorName, door, door.position + aircraft.transform.forward * (.15f * size));
                    var strutMesh = new SkylarkMeshKit.Builder();
                    strutMesh.Cylinder(Vector3.zero, Vector3.forward * (.22f * size), .009f * size, .009f * size, 6);
                    Transform strut = strutMesh.Attach("SkylarkDoorStrut_" + doorName, wellPin, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.Chrome, layer, castShadows: false);
                    strutList.Add(strut);
                    lugList.Add(doorLug);
                }

                legSides[i] = entry.Side;
                legGears[i] = entry.Gear;
                hinges[i] = entry.Hinge;
                unsprungs[i] = entry.Unsprung;
                braces[i] = brace;
                upperScissors[i] = upperScissor;
                lowerScissors[i] = lowerScissor;
                upperPivots[i] = upperPivot;
                lowerPivots[i] = lowerPivot;
                actBarrels[i] = actBarrel;
                actRods[i] = actRod;
                barrelTargets[i] = barrelTarget;
                rodTargets[i] = rodTarget;
                scissorLens[i] = scissorLen;
            }

            doorStruts = strutList.ToArray();
            doorLugs = lugList.ToArray();
        }

        private void Awake()
        {
            if (aircraft == null || braces == null) return;
            braceRest = new Quaternion[braces.Length];
            braceAxis = new Vector3[braces.Length];
            for (int i = 0; i < braces.Length; i++)
            {
                if (braces[i] == null) continue;
                braceRest[i] = braces[i].localRotation;
                braceAxis[i] = braces[i].parent.InverseTransformDirection(aircraft.transform.right);
            }
        }

        private void LateUpdate()
        {
            if (aircraft == null || legGears == null || legGears.Length == 0) return;
            for (int i = 0; i < legGears.Length; i++)
            {
                if (legGears[i] == null || hinges[i] == null || unsprungs[i] == null) continue;

                float fold = Mathf.Clamp01(Quaternion.Angle(hinges[i].localRotation, Quaternion.identity) / 90f);
                if (braces[i] != null && braceRest != null)
                    braces[i].localRotation = Quaternion.AngleAxis(-82f * fold, braceAxis[i]) * braceRest[i];

                if (actBarrels[i] != null && rodTargets[i] != null)
                    actBarrels[i].LookAt(rodTargets[i].position, hinges[i].up);
                if (actRods[i] != null && barrelTargets[i] != null)
                    actRods[i].LookAt(barrelTargets[i].position, hinges[i].up);

                // Analytical 2-bar scissor link folded aft of the strut.
                Vector3 span = lowerPivots[i].position - upperPivots[i].position;
                float dist = span.magnitude;
                float sLen = scissorLens[i];
                if (dist > .001f && sLen > .001f)
                {
                    float cosHalf = Mathf.Clamp(dist / (2f * sLen), .15f, .995f);
                    float halfAngle = Mathf.Acos(cosHalf) * Mathf.Rad2Deg;
                    Vector3 aft = -hinges[i].forward;
                    Vector3 axis = Vector3.Cross(span, aft).normalized;
                    if (axis.sqrMagnitude < .001f) axis = hinges[i].right;

                    Vector3 upperDir = Quaternion.AngleAxis(-halfAngle, axis) * (span / dist);
                    Vector3 elbow = upperPivots[i].position + upperDir * sLen;
                    upperScissors[i].LookAt(elbow, hinges[i].right);
                    lowerScissors[i].LookAt(elbow, unsprungs[i].right);
                }
            }

            if (doorStruts != null && aircraft.gearState != LandingGear.GearState.LockedRetracted)
            {
                for (int i = 0; i < doorStruts.Length; i++)
                {
                    if (doorStruts[i] != null && doorLugs[i] != null)
                        doorStruts[i].LookAt(doorLugs[i].position, aircraft.transform.up);
                }
            }
#if !PUBLIC_RELEASE
            lastFrame = Time.frameCount;
#endif
        }

#if !PUBLIC_RELEASE
        internal object Inspect()
        {
            var list = new List<object>();
            if (legGears != null)
            {
                for (int i = 0; i < legGears.Length; i++)
                {
                    float fold = hinges[i] != null ? Mathf.Clamp01(Quaternion.Angle(hinges[i].localRotation, Quaternion.identity) / 90f) : -1f;
                    float comp = legGears[i] != null ? SkylarkTemplate.Get<float>(legGears[i], "compressionDistance") : 0f;
                    float steer = unsprungs[i] != null ? Mathf.DeltaAngle(0f, unsprungs[i].localEulerAngles.y) : 0f;
                    list.Add(new { Side = legSides[i], fold, compression = comp, steeringAngle = steer, hasBrace = braces[i] != null, hasScissors = upperScissors[i] != null && lowerScissors[i] != null });
                }
            }
            return new { legs = LegCount, doorActuators = DoorActuatorCount, updatedFrame = lastFrame, detail = list };
        }
#endif
    }
}
