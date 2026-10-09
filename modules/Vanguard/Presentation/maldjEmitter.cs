using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// MALD-J on every peer: tracks the jammer in <see cref="MaldjField"/>, unfolds "WingL"/"WingR" over 0.3-0.8 s,
    /// raises the dorsal "Mast" 60 deg over 1.0-1.6 s and pulses every "Glow*" renderer while jamming
    /// (MaterialPropertyBlock, shared material untouched). The FBX holds the STOWED pose of every articulated part
    /// (MECHANICS.md part contract), so the wings are owned here, not by WeaponArticulation (which treats the FBX
    /// pose as deployed). Driven only by the synced transform and timeSinceSpawn. Missing parts fail soft: the jammer
    /// still jams.
    /// </summary>
    internal sealed class MaldjEmitter : MonoBehaviour
    {
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static MaterialPropertyBlock block;

        private Missile missile;
        private Transform mast;
        private Quaternion mastStowed; // relative to the missile
        private float mastSign = -1f;
        private bool mastDone;
        private Transform wingL, wingR;
        private Quaternion wingLStowed, wingRStowed; // local rotations from the FBX
        private Vector3 wingLAxis, wingRAxis;        // the missile's up, in each wing's parent frame
        private bool wingsDone;
        private Renderer[] glows;
        private Color glowBase = new Color(0.08f, 0.4f, 0.46f) * 0.8f; // Vanguard_Glow default

        public static void Attach(Missile missile)
        {
            if (VanguardKeys.RoleOf(missile.definition.jsonKey) != VanguardRole.Jammer || missile.GetComponent<MaldjEmitter>() != null) return;
            var emitter = missile.gameObject.AddComponent<MaldjEmitter>();
            emitter.missile = missile;
            if (!MaldjField.Jammers.Contains(missile)) MaldjField.Jammers.Add(missile);
            VanguardStats.Bump("maldj.denied", 0); // the sim reads these as 0 before the lobe is live
            VanguardStats.Bump("maldj.launched", 0);
            Transform visual = missile.transform.Find("VanguardVisual");
            if (visual != null) emitter.FindParts(visual);
        }

        private void FindParts(Transform visual)
        {
            var found = new System.Collections.Generic.List<Renderer>();
            foreach (Renderer r in visual.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("Glow", System.StringComparison.Ordinal)) found.Add(r);
            glows = found.ToArray();
            if (glows.Length > 0 && glows[0].sharedMaterial != null && glows[0].sharedMaterial.HasProperty(EmissionColor))
                glowBase = glows[0].sharedMaterial.GetColor(EmissionColor);
            foreach (Transform t in visual.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Mast") mast = t;
                else if (t.name == "WingL") wingL = t;
                else if (t.name == "WingR") wingR = t;
            }
            if (wingL != null && wingR != null)
            {
                wingLStowed = wingL.localRotation;
                wingRStowed = wingR.localRotation;
                wingLAxis = wingL.parent.InverseTransformDirection(missile.transform.up);
                wingRAxis = wingR.parent.InverseTransformDirection(missile.transform.up);
            }
            else wingL = wingR = null;
            if (mast == null) return;
            Transform body = missile.transform;
            mastStowed = Quaternion.Inverse(body.rotation) * mast.rotation;
            // Raise the free end upward whichever way the hinge was modelled: the tip is the mesh centre off the pivot.
            MeshFilter mesh = mast.GetComponentInChildren<MeshFilter>(true);
            Vector3 tip = mesh != null && mesh.sharedMesh != null
                ? body.InverseTransformPoint(mesh.transform.TransformPoint(mesh.sharedMesh.bounds.center)) - body.InverseTransformPoint(mast.position)
                : Vector3.forward;
            mastSign = (Quaternion.AngleAxis(MaldjShadow.MastDegrees, Vector3.right) * tip).y >
                       (Quaternion.AngleAxis(-MaldjShadow.MastDegrees, Vector3.right) * tip).y ? 1f : -1f;
        }

        private void Update()
        {
            if (missile == null || missile.disabled)
            {
                MaldjField.Jammers.Remove(missile);
                enabled = false;
                return;
            }
            float age = missile.timeSinceSpawn;
            if (wingL != null && !wingsDone)
            {
                // Port sweeps out about the missile's up and starboard the other way: the reverse of the fold
                // WeaponArticulation and the builder's StowWings apply to a deployed-pose model.
                float sweep = MaldjShadow.WingSweepDegrees * MaldjShadow.WingDeploy(age);
                wingL.localRotation = Quaternion.AngleAxis(sweep, wingLAxis) * wingLStowed;
                wingR.localRotation = Quaternion.AngleAxis(-sweep, wingRAxis) * wingRStowed;
                wingsDone = age >= MaldjShadow.WingEnd;
            }
            if (mast != null && !mastDone)
            {
                mast.rotation = missile.transform.rotation *
                    Quaternion.AngleAxis(mastSign * MaldjShadow.MastAngle(age), Vector3.right) * mastStowed;
                mastDone = age >= MaldjShadow.MastEnd;
            }
            if (glows == null || glows.Length == 0) return;
            block ??= new MaterialPropertyBlock();
            block.SetColor(EmissionColor, glowBase * MaldjShadow.Glow(age, Time.time));
            for (int i = 0; i < glows.Length; i++)
                if (glows[i] != null) glows[i].SetPropertyBlock(block);
        }

        private void OnDestroy() => MaldjField.Jammers.Remove(missile);
    }
}
