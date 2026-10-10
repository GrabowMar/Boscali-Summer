// CG-9 HAMILCAR cargo glider (design/vanguard-remake/hamilcar.md section 6; model contract hamilcar-model-v6.md section 3). Missile() and
// Mount() build the zero-thrust body (the propeller is a picture) and the hold-floor station from the Spec row; this file retunes both
// prefabs, seats the game's own Hexhound in the pod as a "Payload" child, puts the hold carriers on the Op and gates the model contract.
// Behaviour lives in BoscaliSummer.dll (Runtime/Loadouts/HamilcarFlight.cs, Presentation/HamilcarVisuals.cs), keyed on VG_Hamilcar.
// Unity editor code: cannot be compiled by `dotnet build BoscaliSummer.csproj`; written against the helpers in VanguardBuilder.cs.
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Vanguard
{
    public static partial class VanguardBuilder
    {
        const string HamilcarKey = "VG_Hamilcar";
        const string HamilcarMountJson = HamilcarKey + "_AGM_heavy_single"; // Mount(): spec key + "_" + donor rack

        // Runtime joint API (hamilcar-model-v6.md section 3). Wing is the fixed frame (centre section, booms, tail) and carries WingOuterL/R;
        // Prop spins; VehicleSeat is the empty the Hexhound is seated on.
        static readonly string[] HamilcarJoints = { "Wing", "WingOuterL", "WingOuterR", "Visor", "Ramp", "Prop", "VehicleSeat" };

        // The FBX holds the flight pose. The MOUNT visual is posed stowed (Hamilcar.py pose(stow=True)): only the two outer panels move, each
        // a third of a turn about a cube diagonal, so it swings aft and ends on edge beside its boom, leading edge down, upper surface outboard.
        // A COPY of HamilcarProfile.OuterStow / OuterAxisR / OuterAxisL (modules/Vanguard/Domain/HamilcarProfile.cs; this editor assembly cannot
        // reference the mod's): change them together. The derivation from the model's axes is written out there. The axes are in the station's
        // frame (x right, y up, z forward), the angle is Unity's (minus the model's +120).
        const float HamilcarOuterStow = -120f;
        static readonly Vector3 HamilcarOuterAxisR = new Vector3(-1f, -1f, 1f).normalized, HamilcarOuterAxisL = new Vector3(-1f, 1f, -1f).normalized;

        // The hull box's bottom face sits this far above the model origin (= the bottom of the keel strip; the pod's belly is 0.07 m up).
        // Vanilla Missile.DetectCollisions linecasts 1.1 ticks of travel along the velocity from the origin: with the box this far up that
        // line runs under the glider's own collider instead of into it, and the box rides clear of level ground on the slide. The price is
        // that the lowest 0.18 m of the belly is outside the hit box.
        const float HamilcarHullLift = 0.25f;

        // Stowed bounding box, sorted ascending (m). The v6 model is expected at 2.95 high x 3.04 wide x 9.33 long (hamilcar-model-v6.md section 3).
        static readonly float[] HamilcarEnvelope = { 3.00f, 3.10f, 9.40f };

        const int HamilcarMaxTriangles = 15000;

        // Run() calls this after the generic Missile() / Mount() passes: both prefabs and the Op already exist.
        static void Hamilcar(WeaponInfo info)
        {
            HamilcarMissile(info);
            HamilcarMount();
            CargoHoldCarriers(HamilcarMountJson, "HAMILCAR");
            // Gate what was saved, not what was edited, on real scene copies (bounds are only trustworthy there): each prefab carries
            // exactly one Payload beside its VanguardVisual, and the mount holds the stowed glider.
            var missile = Clone(R + HamilcarKey + ".prefab");
            var mounted = Clone(R + HamilcarMountJson + ".prefab");
            try
            {
                HamilcarParts(missile, out _, out _);
                HamilcarParts(mounted, out var visual, out var payload);
                ValidateHamilcar(visual, payload);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(missile);
                UnityEngine.Object.DestroyImmediate(mounted);
            }
        }

        // Edits a saved prefab the way the generic passes build it: an unlinked scene copy, written back over the same asset path.
        static void HamilcarEdit(string path, Action<GameObject> edit)
        {
            var g = Clone(path);
            try
            {
                edit(g);
                PrefabUtility.SaveAsPrefabAsset(g, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(g); }
        }

        static void HamilcarMissile(WeaponInfo info)
        {
            // Released from a hold at a ground point up to 45 km away (HamilcarProfile.MaxReach); the cruise-missile donor reaches 150 km and
            // has no minimum.
            Edit(info, s =>
            {
                P(s, "targetRequirements.minRange").floatValue = 300f;
                P(s, "targetRequirements.maxRange").floatValue = 45000f;
            });
            HamilcarEdit(R + HamilcarKey + ".prefab", root =>
            {
                Transform visual = root.transform.Find("VanguardVisual") ?? throw new Exception(HamilcarKey + " lost VanguardVisual");
                // The donor's cruise-missile colliders are the wrong size, and neither the wing, booms and tail (all under the Wing joint) nor
                // the propeller may be a hit box: one box around the pod, measured off every other detail mesh, so a reworked model keeps fitting.
                foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                Transform[] joints = visual.GetComponentsInChildren<Transform>(true);
                Transform wing = joints.FirstOrDefault(t => t.name == "Wing")
                    ?? throw new Exception("Hamilcar model has no 'Wing' joint (hamilcar-model-v6.md section 3)");
                Transform prop = joints.FirstOrDefault(t => t.name == "Prop")
                    ?? throw new Exception("Hamilcar model has no 'Prop' joint (hamilcar-model-v6.md section 3)");
                var parts = visual.GetComponentsInChildren<Renderer>(true)
                    .Where(r => LodLevel(r.name) == 0 && !r.transform.IsChildOf(wing) && !r.transform.IsChildOf(prop)).ToArray();
                Bounds box = parts[0].bounds;
                foreach (var part in parts.Skip(1)) box.Encapsulate(part.bounds);
                Vector3 low = root.transform.InverseTransformPoint(box.min), high = root.transform.InverseTransformPoint(box.max);
                low.y = HamilcarHullLift;
                var hull = root.AddComponent<BoxCollider>();
                hull.center = (low + high) / 2f;
                hull.size = high - low;
                Debug.Log("[Vanguard] HAMILCAR_HULL centre=" + hull.center.ToString("F3") + " size=" + hull.size.ToString("F3"));
                root.GetComponent<OpticalSeekerCruiseMissile>().triggerMissileWarning = false;
                // Zero thrust still runs Missile.Motor.Activate: it plays the donor's engine loop and start-up sound and registers an IR source at
                // the donor's full intensity. Strip them as the GLAIVE cruise stage does; destructEffects stay, a shot-down glider should burn.
                Edit(root.GetComponent<Missile>(), s =>
                {
                    var motors = P(s, "motors");
                    for (int i = 0; i < motors.arraySize; i++)
                    {
                        var stage = motors.GetArrayElementAtIndex(i);
                        foreach (string field in new[] { "particleSystems", "trailEmitters", "audioSources", "lights" })
                            stage.FindPropertyRelative(field).arraySize = 0;
                        stage.FindPropertyRelative("startupSource").objectReferenceValue = null;
                        stage.FindPropertyRelative("IR_intensity").floatValue = .08f;
                    }
                });
                BuildPayload(root.transform, HamilcarSeat(visual));
            });
        }

        static void HamilcarMount()
        {
            HamilcarEdit(R + HamilcarMountJson + ".prefab", root =>
            {
                var launcher = root.GetComponentsInChildren<MountedMissile>(true).Single();
                Transform station = launcher.transform;
                Transform visual = station.Find("VanguardVisual") ?? throw new Exception(HamilcarMountJson + " lost VanguardVisual");
                // The hardpoint is the hold floor (a vanilla cargo mount puts its wheel plane there) and the model origin is the keel strip's bottom.
                // The donor station hangs 0.31 m under a wing pylon: bring it to the hardpoint. RailLaunch spawns the missile at the moved
                // station, so the model origin, the Payload and the spawn point all share that spot and the hand-over has no jump.
                station.localPosition = Vector3.zero;
                // The pylon and the donor missile mesh stay hidden: the glider and its cargo are all that shows in the hold.
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    if (!renderer.transform.IsChildOf(visual)) renderer.enabled = false;
                BuildPayload(station, HamilcarSeat(visual)); // seated on the deck while the glider is still in its FBX pose
                // Stowed: the two outer panels only (Wing, the fixed frame, is never turned). The visual hangs under the station pitched 90 deg,
                // so the station's right / up / forward are the model's +X / +Z / +Y, the frame the two axes are written in.
                foreach (Transform joint in visual.GetComponentsInChildren<Transform>(true))
                {
                    if (joint.name != "WingOuterR" && joint.name != "WingOuterL") continue;
                    Vector3 axis = station.TransformDirection(joint.name == "WingOuterR" ? HamilcarOuterAxisR : HamilcarOuterAxisL);
                    // Rotate localRotation only: FBX joints carry a 100x scale. The axis is taken into the joint parent's frame.
                    joint.localRotation = Quaternion.AngleAxis(HamilcarOuterStow, joint.parent.InverseTransformDirection(axis)) * joint.localRotation;
                }
                // MountedMissile.RailLaunch: wait railDelay (the ramp opens meanwhile), slide the station railSpeed m/s along its own Backward
                // axis until it has moved railLength, then spawn the missile prefab at the moved station with the aircraft's point velocity plus
                // the rail's. 12 m at 7 m/s is 1.7 s, so the glider leaves 3.7 s after the trigger.
                Edit(launcher, s =>
                {
                    P(s, "railDirection").enumValueIndex = 4; // RailDirection: Forward, Down, Right, Left, Backward, Up
                    P(s, "railLength").floatValue = 12f;
                    P(s, "railSpeed").floatValue = 7f;
                    P(s, "railDelay").floatValue = 2f;
                });
            });
            // A hold store has no pylon drag or radar return (the vanilla cargo mounts carry 0); the AGM donor's 0.1 / 0.025 are external-rack values.
            Edit(Load<WeaponMount>(R + "WM_" + HamilcarMountJson + ".asset"), s =>
            {
                P(s, "drag").floatValue = 0f;
                P(s, "RCS").floatValue = 0f;
                P(s, "emptyRCS").floatValue = 0f;
            });
        }

        static Transform HamilcarSeat(Transform visual) =>
            visual.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "VehicleSeat")
            ?? throw new Exception("Hamilcar model has no 'VehicleSeat' anchor (hamilcar-model-v6.md section 3)");

        // The Hexhound in the pod: the game's own UGV1_grenade pieces, zero copied geometry. Every mesh and material is the MAIN asset of a
        // _donotship placeholder; ModBuilder turns each into a "name|type" patch (sharedMesh / sharedMaterials[i]) that Blueprinter restores to the
        // live game asset at load, as it does for the rack hardware. A sub-asset reference would fail the build ("Unsupported game subasset").
        // The Payload is a SIBLING of VanguardVisual (the generic LOD and material checks only walk the visual) and a picture only: no
        // MountedCargo, collider or sound. Its origin is the Hexhound prefab root, the ground-contact plane 0.011 m under the wheel bottoms
        // (UGV1_grenadex1: chassis node 0.782 up, axles 0.447 below it, wheel radius 0.324), put exactly on the VehicleSeat anchor: the model
        // places that empty 0.011 m below the deck top (z 0.189 under a deck at 0.20) so the wheels stand on the deck. Nose along +Z.
        static void BuildPayload(Transform parent, Transform seat)
        {
            var payload = Clone(D + "GameObject/UGV1_grenadex1_PLACEHOLDER.prefab"); // UGV1_grenade > chassis, axle1..3, cover, traverse > barrel, scanner
            payload.name = "Payload";
            foreach (var part in payload.GetComponentsInChildren<Component>(true).Where(x => !(x is Transform || x is MeshFilter || x is MeshRenderer)).ToArray())
                UnityEngine.Object.DestroyImmediate(part);
            foreach (var node in payload.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = parent.gameObject.layer;
            payload.transform.SetParent(parent, false);
            payload.transform.localRotation = Quaternion.identity; // the glider's nose is the parent's +Z, like VanguardVisual after its 90 deg pitch
            payload.transform.position = seat.position;

            // The unit prefab's own LOD split: every piece to 2 % screen height, then the 48-triangle chassis blob down to 0.1 %.
            Transform chassis = payload.transform.Find("UGV1_grenade") ?? throw new Exception("Hexhound placeholder lost UGV1_grenade");
            var far = new GameObject("chassis_LOD1") { layer = parent.gameObject.layer };
            far.transform.SetParent(chassis, false);
            far.AddComponent<MeshFilter>().sharedMesh = Load<Mesh>(D + "Mesh/ugv1_chassis_LOD1_PLACEHOLDER.asset");
            var farRenderer = far.AddComponent<MeshRenderer>();
            farRenderer.sharedMaterial = Load<Material>(D + "Material/__dedupe/LOD_PLACEHOLDER.mat");
            var group = chassis.gameObject.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.None;
            group.SetLODs(new[]
            {
                new LOD(0.02f, chassis.GetComponentsInChildren<MeshRenderer>(true).Where(r => r != farRenderer).Cast<Renderer>().ToArray()),
                new LOD(0.001f, new Renderer[] { farRenderer }),
            });
            group.RecalculateBounds();
        }

        // A prefab copy's VanguardVisual and its single Payload, which must be siblings and a usable set of Hexhound pieces.
        static void HamilcarParts(GameObject prefab, out GameObject visual, out GameObject payload)
        {
            var found = prefab.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Payload").ToArray();
            if (found.Length != 1) throw new Exception(prefab.name + " needs exactly one Payload, has " + found.Length);
            Transform sibling = found[0].parent.Find("VanguardVisual") ?? throw new Exception(prefab.name + " Payload is not a sibling of VanguardVisual");
            visual = sibling.gameObject;
            payload = found[0].gameObject;
            if (payload.GetComponentsInChildren<Component>(true).Any(x => !(x is Transform || x is MeshFilter || x is MeshRenderer || x is LODGroup)))
                throw new Exception(prefab.name + " Payload must carry meshes only: no collider, script or sound");
            var renderers = payload.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length < 5 || renderers.Any(r => r.GetComponent<MeshFilter>().sharedMesh == null || r.sharedMaterials.Any(m => m == null)))
                throw new Exception(prefab.name + " Payload needs at least five renderers with a mesh and materials, has " + renderers.Length);
            foreach (string axle in new[] { "axle1", "axle2", "axle3" }) // the runtime spins these by name
                if (renderers.Count(r => r.name == axle) != 1) throw new Exception(prefab.name + " Payload needs exactly one " + axle);
        }

        // On the mount copy: its visual holds the stowed pose, so the hold envelope is measured where it matters.
        static void ValidateHamilcar(GameObject visual, GameObject payload)
        {
            var joints = visual.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            foreach (string name in HamilcarJoints)
                if (!joints.ContainsKey(name)) throw new Exception("Hamilcar lost joint " + name);
            foreach (string name in new[] { "WingOuterL", "WingOuterR" })
                if (!joints[name].IsChildOf(joints["Wing"])) throw new Exception("Hamilcar " + name + " must hang under Wing");

            Vector3 size = VisualBounds(visual).size;
            float[] sorted = { size.x, size.y, size.z };
            Array.Sort(sorted);
            for (int i = 0; i < sorted.Length; i++)
                if (sorted[i] > HamilcarEnvelope[i])
                    throw new Exception("Hamilcar stowed box " + size + " exceeds the hold envelope " + string.Join(" x ", HamilcarEnvelope));

            int triangles = visual.GetComponentsInChildren<MeshRenderer>(true).Where(r => LodLevel(r.name) == 0)
                .Sum(r => (int)r.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0) / 3);
            if (triangles > HamilcarMaxTriangles) throw new Exception("Hamilcar LOD0 has " + triangles + " triangles, budget " + HamilcarMaxTriangles);
            Debug.Log("[Vanguard] HAMILCAR_MODEL_OK joints=" + HamilcarJoints.Length + " payloadRenderers=" + payload.GetComponentsInChildren<MeshRenderer>(true).Length +
                      " box=" + size + " tris=" + triangles);
        }
    }
}
