using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// Every peer: REMORA rails go empty as its rounds leave (RemoraBoard), elevons deflect with bank and roll rate
    /// read from the synced transform. Missing joints are skipped. Wings unfold in WeaponArticulation.
    /// </summary>
    internal sealed class RemoraVisuals : MonoBehaviour
    {
        private Missile missile;
        private Transform missileL, missileR, elevonL, elevonR;
        private Quaternion restL, restR;
        private Vector3 axisL, axisR;
        private float bank, rollRate;

        public static void Attach(Missile missile)
        {
            if (VanguardKeys.RoleOf(missile.definition.jsonKey) != VanguardRole.Drone || missile.GetComponent<RemoraVisuals>() != null) return;
            Transform visual = missile.transform.Find("VanguardVisual");
            if (visual == null) return;
            var v = missile.gameObject.AddComponent<RemoraVisuals>();
            v.missile = missile;
            foreach (Transform joint in visual.GetComponentsInChildren<Transform>(true))
                switch (joint.name)
                {
                    case "MissileL": v.missileL = joint; break;
                    case "MissileR": v.missileR = joint; break;
                    case "ElevonL": v.elevonL = joint; break;
                    case "ElevonR": v.elevonR = joint; break;
                }
            // Hinge about the airframe's span axis in each parent frame (the visual root carries the FBX +90X).
            if (v.elevonL != null)
            {
                v.restL = v.elevonL.localRotation;
                v.axisL = v.elevonL.parent.InverseTransformDirection(missile.transform.right);
            }
            if (v.elevonR != null)
            {
                v.restR = v.elevonR.localRotation;
                v.axisR = v.elevonR.parent.InverseTransformDirection(missile.transform.right);
            }
            v.bank = Bank(missile.transform);
        }

        /// <summary>Rail of round 0 (MissileL) / 1 (MissileR), or null when the model has none.</summary>
        public static Transform Pylon(Missile missile, int pylon)
        {
            var v = missile.GetComponent<RemoraVisuals>();
            return v == null ? null : pylon == 0 ? v.missileL : v.missileR;
        }

        private static float Bank(Transform t) => Mathf.Asin(Mathf.Clamp(-t.right.y, -1f, 1f)) * Mathf.Rad2Deg;

        private void Update()
        {
            if (missile == null || missile.disabled)
            {
                enabled = false;
                return;
            }
            if (RemoraBoard.Views.TryGetValue(missile.persistentID.Id, out RemoraView view))
            {
                Hide(missileL, (view.Fired & 1) != 0);
                Hide(missileR, (view.Fired & 2) != 0);
            }
            float dt = Time.deltaTime;
            if (dt <= 0f || (elevonL == null && elevonR == null)) return;
            float now = Bank(missile.transform);
            rollRate = Mathf.Lerp(rollRate, Mathf.DeltaAngle(bank, now) / dt, 1f - Mathf.Exp(-6f * dt));
            bank = now;
            RemoraArcher.Elevons(bank, rollRate, out float left, out float right);
            if (elevonL != null) elevonL.localRotation = Quaternion.AngleAxis(left, axisL) * restL;
            if (elevonR != null) elevonR.localRotation = Quaternion.AngleAxis(right, axisR) * restR;
        }

        private static void Hide(Transform rail, bool fired)
        {
            if (rail != null && fired && rail.gameObject.activeSelf) rail.gameObject.SetActive(false);
        }
    }
}
