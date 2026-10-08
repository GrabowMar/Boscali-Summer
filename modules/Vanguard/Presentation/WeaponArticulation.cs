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

        public static void Attach(Missile missile)
        {
            VanguardRole role = VanguardKeys.RoleOf(missile.definition.jsonKey);
            if (role != VanguardRole.Decoy && role != VanguardRole.Jammer &&
                role != VanguardRole.Carrier && role != VanguardRole.Torpedo) return;
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
            animation.leftDeployed = left.localRotation;
            animation.rightDeployed = right.localRotation;
            // FBX bakes its axes and the visual root adds +90X. Use the weapon's physical up in each
            // parent frame, preserving imported joint rotations. LOD wing meshes are children of these joints.
            animation.leftAxis = left.parent.InverseTransformDirection(missile.transform.up);
            animation.rightAxis = right.parent.InverseTransformDirection(missile.transform.up);
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
            float fold = 90f * (1f - deployment);
            left.localRotation = Quaternion.AngleAxis(-fold, leftAxis) * leftDeployed;
            right.localRotation = Quaternion.AngleAxis(fold, rightAxis) * rightDeployed;
            if (deployment >= 1f) enabled = false;
        }
    }
}
