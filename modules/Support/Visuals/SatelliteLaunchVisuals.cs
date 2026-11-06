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
            // Under the datum: a floating-origin shift carries the rocket, its light and its trail together.
            go.transform.SetParent(Datum.origin, false);
            go.transform.position = launchPoint;

            if (trailMaterial == null)
                trailMaterial = new Material(Shader.Find("Sprites/Default")) { name = "BoscaliLaunchTrail" };

            // The trail is redrawn from the flight path every frame (a TrailRenderer kept world points that kinked on every origin shift).
            var trailGo = new GameObject("Trail");
            trailGo.transform.SetParent(Datum.origin, false);
            var trail = trailGo.AddComponent<LineRenderer>();
            trail.useWorldSpace = false;
            trail.positionCount = 0;
            trail.widthCurve = AnimationCurve.Linear(0f, 8f / 60f, 1f, 1f);
            trail.sharedMaterial = trailMaterial;
            trail.startColor = new Color(0.85f, 0.87f, 0.92f, 0f); // the contrail: pale vapour behind, hot white at the nozzle
            trail.endColor = new Color(1f, 0.92f, 0.7f, 0.95f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;

            Light glow = go.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.85f, 0.6f);
            glow.range = 1500f;
            glow.intensity = 8f;

            SatelliteLaunchEffect effect = go.AddComponent<SatelliteLaunchEffect>();
            effect.Trail = trail;
            effect.Begin(Mathf.Clamp(duration, 3f, 90f));
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
        private const float TrailSeconds = 14f;
        private const int TrailPoints = 48;
        private const float NozzleWidth = 60f;

        /// <summary>Set by <see cref="SatelliteLaunchVisuals.Play"/>; drawn in datum space from <see cref="At"/>, its width follows the camera range so the streak reads at distance.</summary>
        public LineRenderer Trail;
        public ParticleSystem Flame, Smoke;
        private readonly Vector3[] points = new Vector3[TrailPoints];
        private float nextSmoke;
        private float elapsed;
        private float duration;
        private Vector3 origin; // datum-space (global) launch point
        private Vector3 heading;

        public void Begin(float durationSeconds)
        {
            duration = durationSeconds;
            origin = transform.localPosition;
            // Launches head away from the theatre centre, which sits at the global origin.
            var outward = new Vector3(origin.x, 0f, origin.z);
            heading = outward.sqrMagnitude > 1f ? outward.normalized : Vector3.forward;
        }

        /// <summary>The rocket's datum-space position <paramref name="seconds"/> after lift-off.</summary>
        private Vector3 At(float seconds)
        {
            float t = Mathf.Clamp01(seconds / duration);
            float climb = 1f - (1f - t) * (1f - t);
            return origin + Vector3.up * (ClimbHeight * climb) + heading * (Downrange * t * t);
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            transform.localPosition = At(elapsed);
            if (Flame != null)
            {
                Flame.Emit(2);
                if (elapsed >= nextSmoke) { nextSmoke = elapsed + 0.1f; Smoke.Emit(1); }
            }
            if (Trail != null)
            {
                float from = Mathf.Max(0f, elapsed - TrailSeconds);
                for (int i = 0; i < TrailPoints; i++) points[i] = At(Mathf.Lerp(from, elapsed, i / (TrailPoints - 1f)));
                Trail.positionCount = TrailPoints;
                Trail.SetPositions(points);
                var csm = SceneSingleton<CameraStateManager>.i;
                Camera cam = csm != null ? csm.mainCamera : Camera.main;
                float scale = cam != null ? Mathf.Clamp(Vector3.Distance(cam.transform.position, transform.position) * 0.0035f / NozzleWidth, 1f, 10f) : 1f;
                Trail.widthMultiplier = NozzleWidth * scale;
            }
            if (elapsed >= duration) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (Trail != null) Destroy(Trail.gameObject);
        }
    }
}
