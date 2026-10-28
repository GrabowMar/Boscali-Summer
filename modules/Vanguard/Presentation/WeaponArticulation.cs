using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>Every peer: visual wing deployment from the missile's age; never changes aero or colliders.</summary>
    internal sealed class WeaponArticulation : MonoBehaviour
    {
        private const float DeployStart = 0.3f;
        private const float DeployEnd = 0.8f;
        private Missile missile;
        private Transform left;
        private Transform right;
        private Quaternion leftDeployed;
        private Quaternion rightDeployed;
        private Vector3 leftAxis;
        private Vector3 rightAxis;
        private float swing;
        private float sign = 1f; // MANTA: -1, its panels fold about forward the other way round (down and under)
        private const float MantaFold = 180f;

        public static void Attach(Missile missile)
        {
            VanguardRole role = VanguardKeys.RoleOf(missile.definition.jsonKey);
            // MALD-J (Jammer) is animated by MaldjEmitter; REMORA (Drone) folds its wings here.
            if (role != VanguardRole.Decoy && role != VanguardRole.Torpedo && role != VanguardRole.Drone) return;
            if (missile.GetComponent<WeaponArticulation>() != null) return;
            Transform visual = missile.transform.Find("VanguardVisual");
            if (visual == null) return;
            Transform left = null, right = null;
            foreach (Transform joint in visual.GetComponentsInChildren<Transform>(true))
            {
                if (joint.name == "WingL") left = joint;
                else if (joint.name == "WingR") right = joint;
            }
            if (left == null || right == null) return;
            var animation = missile.gameObject.AddComponent<WeaponArticulation>();
            animation.missile = missile;
            animation.left = left;
            animation.right = right;
            // FBX bakes its axes and the visual root adds +90X. Use the weapon's physical up in each
            // parent frame, preserving imported joint rotations. LOD wing meshes are children of these joints.
            animation.leftAxis = left.parent.InverseTransformDirection(missile.transform.up);
            animation.rightAxis = right.parent.InverseTransformDirection(missile.transform.up);
            // The MALD-X FBX holds the flight pose. ORCA and REMORA hold the stowed pose (MECHANICS.md part contract), so
            // their deployed rotation is the rest pose swung open by the model's fold angle: Orca.py 90, Remora.py STOW_DEG 68.
            if (missile.definition.jsonKey == VanguardKeys.Manta && Runtime.StoreArt.Prefab(Runtime.StoreArt.Manta) != null)
            {
                // XQ-90 MANTA (baked model, deployed rest pose): the outer panels unfold from 180 deg down-and-under about their
                // crank hinge (the airframe's forward axis), away from the carrier's wing. Port turns +, starboard - about forward.
                animation.leftAxis = left.parent.InverseTransformDirection(missile.transform.forward);
                animation.rightAxis = right.parent.InverseTransformDirection(missile.transform.forward);
                animation.swing = MantaFold;
                animation.sign = -1f;
                animation.leftDeployed = left.localRotation;
                animation.rightDeployed = right.localRotation;
                animation.Apply();
                return;
            }
            animation.swing = role == VanguardRole.Drone ? 68f : 90f;
            bool stowedRest = role != VanguardRole.Decoy;
            animation.leftDeployed = stowedRest ? Quaternion.AngleAxis(animation.swing, animation.leftAxis) * left.localRotation : left.localRotation;
            animation.rightDeployed = stowedRest ? Quaternion.AngleAxis(-animation.swing, animation.rightAxis) * right.localRotation : right.localRotation;
            animation.Apply();
        }

        private void Update()
        {
            if (missile == null || missile.disabled || left == null || right == null)
            {
                enabled = false;
                return;
            }
            if (missile.timeSinceSpawn >= DeployStart) Apply();
        }

        private void Apply()
        {
            float deployment = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(DeployStart, DeployEnd, missile.timeSinceSpawn));
            // Port (-X) and starboard (+X) fold aft around Unity up; source Blender Z rotations reverse handedness.
            float fold = swing * (1f - deployment);
            left.localRotation = Quaternion.AngleAxis(-fold * sign, leftAxis) * leftDeployed;
            right.localRotation = Quaternion.AngleAxis(fold * sign, rightAxis) * rightDeployed;
            if (deployment >= 1f) enabled = false;
        }

        /// <summary>The MANTA's carriage fold: both outer panels (WingL/WingR of the baked model under <paramref name="model"/>)
        /// turned <paramref name="degrees"/> down about <paramref name="frame"/>'s forward axis, from the model's deployed rest pose.</summary>
        public static void FoldManta(Transform model, Transform frame, float degrees)
        {
            foreach (Transform joint in model.GetComponentsInChildren<Transform>(true))
            {
                if (joint.name != "WingL" && joint.name != "WingR") continue;
                Vector3 axis = joint.parent.InverseTransformDirection(frame.forward);
                joint.localRotation = Quaternion.AngleAxis(joint.name == "WingL" ? degrees : -degrees, axis) * joint.localRotation;
            }
        }
    }

    /// <summary>
    /// One hinged part of a baked model (HAMILCAR, KESTREL). Only localRotation is ever written, about a missile axis taken into
    /// the joint's parent frame at rest: the FBX joints carry their own baked axes and a 100x scale, and neither may change.
    /// </summary>
    internal struct RigJoint
    {
        public Transform Part;
        private Quaternion rest;
        private Vector3 axis;
        private float shown;

        public static RigJoint Find(Transform[] parts, string name, Vector3 worldAxis, ref string missing)
        {
            foreach (Transform part in parts)
                if (part.name == name && part.parent != null)
                    return new RigJoint { Part = part, rest = part.localRotation, axis = part.parent.InverseTransformDirection(worldAxis) };
            missing = missing == null ? name : missing + ", " + name;
            return default;
        }

        public void Turn(float degrees)
        {
            if (degrees == shown || Part == null) return;
            shown = degrees;
            Part.localRotation = Quaternion.AngleAxis(degrees, axis) * rest;
        }

        public float Off(float degrees) => Part == null ? 0f : Quaternion.Angle(Part.localRotation, Quaternion.AngleAxis(degrees, axis) * rest);
    }
}
