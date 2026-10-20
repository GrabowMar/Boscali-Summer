using BoscaliSummer.Core.Fx;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// Every peer: REMORA rails go empty as its rounds leave (RemoraBoard), elevons deflect with bank and roll rate
    /// read from the synced transform, and a synced drop in flares left bursts hot decoys behind the drone.
    /// Missing joints are skipped. Wings unfold in WeaponArticulation.
    /// </summary>
    internal sealed class RemoraVisuals : MonoBehaviour
    {
        private Missile missile;
        private Transform missileL, missileR, elevonL, elevonR;
        private Quaternion restL, restR;
        private Vector3 axisL, axisR;
        private float bank, rollRate;
        private ParticleSystem flares;
        private int seenFlares = -1;

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
            v.flares = FlareParticles(missile.transform);
        }

        /// <summary>Rail of round 0 (MissileL) / 1 (MissileR), or null when the model has none.</summary>
        public static Transform Pylon(Missile missile, int pylon)
        {
            var v = missile.GetComponent<RemoraVisuals>();
            return v == null ? null : pylon == 0 ? v.missileL : v.missileR;
        }

        private static float Bank(Transform t) => Mathf.Asin(Mathf.Clamp(-t.right.y, -1f, 1f)) * Mathf.Rad2Deg;

        // One pooled burst emitter per drone: world-space hot cores that fall behind the airframe.
        private static ParticleSystem FlareParticles(Transform parent)
        {
            if (GlowFx.Glow == null) return null;
            var go = new GameObject("RemoraFlares");
            go.transform.SetParent(parent, false);
            ParticleSystem particles = go.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(2.2f, 1.3f, 0.55f, 1f), new Color(2.4f, 1.8f, 1.1f, 1f));
            main.gravityModifier = 0.35f;
            main.maxParticles = 96;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = particles.emission;
            emission.enabled = false;
            var fade = particles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.55f, 0.2f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = new Material(GlowFx.Glow) { name = "BoscaliSummer.RemoraFlare" };
            renderer.minParticleSize = 0f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return particles;
        }

        private void Burst()
        {
            if (flares == null || missile == null) return;
            Transform t = missile.transform;
            for (int i = 0; i < 18; i++)
            {
                Vector3 velocity = -t.forward * Random.Range(6f, 16f) + t.right * Random.Range(-4f, 4f) + t.up * Random.Range(-3f, 0f)
                    + (missile.rb != null ? missile.rb.velocity : Vector3.zero);
                flares.Emit(new ParticleSystem.EmitParams
                {
                    position = t.position,
                    velocity = velocity,
                    startSize = Random.Range(0.6f, 1.3f),
                    startLifetime = Random.Range(0.8f, 1.5f),
                    startColor = new Color(2.2f, 1.3f, 0.55f, 1f),
                }, 1);
            }
        }

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
                if (seenFlares >= 0 && view.Flares < seenFlares) Burst();
                seenFlares = view.Flares;
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
