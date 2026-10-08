using UnityEngine;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// A purely cosmetic launch streak: a bright trail climbing away from an off-map launch
    /// site over the ascent to orbit insertion. Deliberately not a vanilla Missile — there is
    /// nothing to arm, guide, detonate or collide with; the mechanic (insertion time, first
    /// pass) lives entirely in the orbital model. Every client plays it independently the
    /// first time it sees a satellite in ascent, so no extra network message is needed.
    /// </summary>
    internal static class SatelliteLaunchVisuals
    {
        private static Material trailMaterial;

        public static void Play(Vector3 launchPoint, float duration)
        {
            var go = new GameObject("BoscaliSatelliteLaunch");
            go.transform.position = launchPoint;

            if (trailMaterial == null)
                trailMaterial = new Material(Shader.Find("Sprites/Default")) { name = "BoscaliLaunchTrail" };

            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 14f;
            trail.startWidth = 60f;
            trail.endWidth = 8f;
            trail.sharedMaterial = trailMaterial;
            trail.startColor = new Color(1f, 0.92f, 0.7f, 0.95f);
            trail.endColor = new Color(0.85f, 0.87f, 0.92f, 0f); // the contrail: hot white at the nozzle, pale vapour behind
            trail.minVertexDistance = 40f;

            Light glow = go.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.85f, 0.6f);
            glow.range = 1500f;
            glow.intensity = 8f;

            SatelliteLaunchEffect effect = go.AddComponent<SatelliteLaunchEffect>();
            effect.Begin(Mathf.Clamp(duration, 3f, 90f));
            effect.Trail = trail;
            if (!GameManager.IsHeadless)
            {
                // World-space exhaust plume: a hot flame core and a smoke wake that hangs in the sky as the rocket pulls away.
                effect.Flame = SupportParticles.Layer(go.transform, "Exhaust flame", true, 120, 0.7f, 90f, new Color(2.2f, 1.2f, 0.45f));
                effect.Smoke = SupportParticles.Layer(go.transform, "Exhaust smoke", false, 220, 14f, 70f, new Color(0.88f, 0.88f, 0.9f, 0.5f));
                foreach (ParticleSystem ps in new[] { effect.Flame, effect.Smoke })
                {
                    var main = ps.main;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 3f);
                }
            }
            Ignition(launchPoint);
        }

        /// <summary>The pad: a white-hot flash, an expanding dust ring and a column of smoke that outlives the launch marker. Self-removing.</summary>
        private static void Ignition(Vector3 point)
        {
            if (GameManager.IsHeadless) return;
            var pad = new GameObject("BoscaliSatelliteLaunchPad");
            pad.transform.SetParent(Datum.origin, false);
            pad.transform.position = point;
            var flash = SupportParticles.Layer(pad.transform, "Ignition flash", true, 8, 1.2f, 160f, new Color(4f, 2.6f, 1.2f));
            flash.Emit(4);
            var dust = SupportParticles.Layer(pad.transform, "Pad dust", false, 96, 7f, 60f, new Color(0.55f, 0.48f, 0.4f, 0.55f));
            SupportParticles.Ring(dust, 96, 20f, 55f, 3f);
            var column = SupportParticles.Layer(pad.transform, "Exhaust column", false, 80, 12f, 45f, new Color(0.8f, 0.78f, 0.75f, 0.5f));
            for (int i = 0; i < 80; i++)
                column.Emit(new ParticleSystem.EmitParams
                {
                    position = Random.insideUnitSphere * 8f,
                    velocity = new Vector3(Random.Range(-6f, 6f), Random.Range(25f, 90f), Random.Range(-6f, 6f)),
                    rotation = Random.Range(0f, 360f)
                }, 1);
            Object.Destroy(pad, 14f);
        }
    }

    /// <summary>Climbs a launch marker on a gravity-turn arc over its lifetime, then removes itself.</summary>
    internal sealed class SatelliteLaunchEffect : MonoBehaviour
    {
        private const float ClimbHeight = 45000f;
        private const float Downrange = 30000f;

        /// <summary>Set by <see cref="SatelliteLaunchVisuals.Play"/>; its width follows the camera range so the streak reads from the cockpit at distance.</summary>
        public TrailRenderer Trail;
        public ParticleSystem Flame, Smoke;
        private float nextSmoke;
        private float elapsed;
        private float duration;
        private GlobalPosition origin;
        private Vector3 heading;

        public void Begin(float durationSeconds)
        {
            duration = durationSeconds;
            origin = transform.position.ToGlobalPosition();
            // Launches head away from the theatre centre, which sits at the global origin.
            var outward = new Vector3(origin.x, 0f, origin.z);
            heading = outward.sqrMagnitude > 1f ? outward.normalized : Vector3.forward;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float climb = 1f - (1f - t) * (1f - t);
            // Rebuilt from the global origin every frame so a floating-origin shift cannot tear the trail.
            transform.position = origin.ToLocalPosition() + Vector3.up * (ClimbHeight * climb) + heading * (Downrange * t * t);
            if (Flame != null)
            {
                Flame.Emit(2);
                if (elapsed >= nextSmoke) { nextSmoke = elapsed + 0.1f; Smoke.Emit(1); }
            }
            if (Trail != null)
            {
                var csm = SceneSingleton<CameraStateManager>.i;
                Camera cam = csm != null ? csm.mainCamera : Camera.main;
                if (cam != null) Trail.widthMultiplier = Mathf.Clamp(Vector3.Distance(cam.transform.position, transform.position) * 0.0035f / 60f, 1f, 10f);
            }
            if (elapsed >= duration) Destroy(gameObject);
        }
    }
}
