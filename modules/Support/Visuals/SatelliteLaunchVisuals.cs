using UnityEngine;

namespace BoscaliSummer.Features.Support.Visuals
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
            trail.time = 6f;
            trail.startWidth = 60f;
            trail.endWidth = 8f;
            trail.sharedMaterial = trailMaterial;
            trail.startColor = new Color(1f, 0.92f, 0.7f, 0.95f);
            trail.endColor = new Color(0.9f, 0.9f, 0.95f, 0f);
            trail.minVertexDistance = 40f;

            Light glow = go.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.85f, 0.6f);
            glow.range = 1500f;
            glow.intensity = 8f;

            SatelliteLaunchEffect effect = go.AddComponent<SatelliteLaunchEffect>();
            effect.Begin(Mathf.Clamp(duration, 3f, 90f));
        }
    }

    /// <summary>Climbs a launch marker on a gravity-turn arc over its lifetime, then removes itself.</summary>
    internal sealed class SatelliteLaunchEffect : MonoBehaviour
    {
        private const float ClimbHeight = 45000f;
        private const float Downrange = 30000f;

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
            if (elapsed >= duration) Destroy(gameObject);
        }
    }
}
