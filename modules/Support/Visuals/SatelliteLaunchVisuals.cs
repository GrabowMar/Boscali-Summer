using UnityEngine;

namespace BoscaliSummer.Features.Support.Visuals
{
    /// <summary>
    /// A purely cosmetic satellite-launch streak: a bright trail climbing away from the
    /// station point over the same duration the satellite spends in transit. Deliberately not
    /// a vanilla Missile — there is nothing here to arm, guide, detonate or collide with,
    /// since the actual mechanic (real transit time, coverage denied while in flight) already
    /// lives entirely in <c>Constellation</c>/<c>OpsStateMessage</c>. This just gives whoever
    /// is watching something to see; every client triggers it independently the moment it
    /// first observes the satellite in <c>SatelliteState.Transit</c>, so no extra network
    /// message is needed for something with no gameplay effect.
    /// </summary>
    internal static class SatelliteLaunchVisuals
    {
        public static void Play(Vector3 launchPoint, float duration)
        {
            var go = new GameObject("BoscaliSatelliteLaunch");
            go.transform.position = launchPoint;

            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 1.4f;
            trail.startWidth = 5f;
            trail.endWidth = 0.4f;
            trail.material = new Material(Shader.Find("Sprites/Default"));
            trail.startColor = new Color(0.35f, 1f, 0.85f, 0.95f);
            trail.endColor = new Color(0.35f, 1f, 0.85f, 0f);
            trail.minVertexDistance = 4f;

            Light glow = go.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(0.5f, 1f, 0.9f);
            glow.range = 500f;
            glow.intensity = 8f;

            SatelliteLaunchEffect effect = go.AddComponent<SatelliteLaunchEffect>();
            effect.Begin(Mathf.Clamp(duration, 3f, 60f));
        }
    }

    /// <summary>Eases a launch marker straight up over its lifetime, then removes itself.</summary>
    internal sealed class SatelliteLaunchEffect : MonoBehaviour
    {
        private const float ClimbHeight = 6000f;

        private float elapsed;
        private float duration;
        private Vector3 origin;

        public void Begin(float durationSeconds)
        {
            duration = durationSeconds;
            origin = transform.position;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - (1f - t) * (1f - t);
            transform.position = origin + Vector3.up * (ClimbHeight * eased);
            if (elapsed >= duration) Destroy(gameObject);
        }
    }
}
