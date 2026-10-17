using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Core.Fx
{
    /// <summary>
    /// Green touchdown smoke at a landing point that is now expected: a bounded soft-puff plume that rises
    /// off the point for a set time, thins out, then removes itself. Popped by the HALO stick when its first
    /// canopy opens over the zone, and by the HAMILCAR flight when it turns onto Final. Cosmetic only, local
    /// to the peer that pops it.
    /// </summary>
    internal static class LandingSmoke
    {
        private const int MaximumParticles = 96;
        private const float PuffsPerSecond = 14f;

        public static void Pop(Vector3 globalLanding, float seconds)
        {
            Material mat = SmokeFx.Puff;
            if (Datum.origin == null || mat == null) return;
            seconds = Mathf.Max(1f, seconds);

            var go = new GameObject("BoscaliSummer.LandingSmoke");
            go.SetActive(false);
            go.transform.SetParent(Datum.origin, false);
            go.transform.position = new GlobalPosition(globalLanding.x, globalLanding.y, globalLanding.z).ToLocalPosition();
            // The emitter cone points up, so the plume rises off the touchdown point.
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            ParticleSystem particles = go.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = seconds;
            // Once the window has ended and the last puff has faded, the system removes its own object.
            main.stopAction = ParticleSystemStopAction.Destroy;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 5.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(2.4f, 3.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.55f, 0.95f, 0.6f, 1f), new Color(0.35f, 0.85f, 0.4f, 1f));
            main.gravityModifier = -0.05f;
            main.maxParticles = MaximumParticles;
            // Local to Datum.origin: the plume moves with the floating origin instead of jumping.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            // Pauses off-screen and catches up on return, so a distant landing costs nothing.
            main.cullingMode = ParticleSystemCullingMode.PauseAndCatchup;

            var emission = particles.emission;
            // Full rate for most of the window, then tapering so the plume thins rather than stopping dead.
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(PuffsPerSecond, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.8f, 1f), new Keyframe(1f, 0f)));

            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 2.5f;

            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.5f), new Keyframe(0.35f, 1f), new Keyframe(1f, 1.7f)));

            var fade = particles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.15f),
                    new GradientAlphaKey(0.35f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = mat;
            renderer.minParticleSize = 0f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            go.SetActive(true);
            particles.Play(true);
            // Safety net in case the stop action never fires.
            Object.Destroy(go, seconds + 12f);
        }
    }
}
