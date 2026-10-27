using BoscaliSummer.Core.Fx;
using BoscaliSummer.Modules.CombatAdjust.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.CombatAdjust.Visuals
{
    /// <summary>
    /// A smoke screen (armour) or a chaff/decoy burst (ships): one self-removing soft-puff particle system parented to Datum.origin, so it
    /// rides the floating origin like LandingSmoke. Cosmetic and local to each peer; the host's cloud is what blocks seekers.
    /// </summary>
    internal static class SmokeCloudVisual
    {
        public static GameObject Screen(GlobalPosition centre) =>
            Make("BoscaliSummer.SmokeScreen", centre, Triggers.CloudGoneAt, 64, 9f, 14f, 6f,
                new Color(0.80f, 0.80f, 0.78f, 1f), new Color(0.64f, 0.65f, 0.63f, 1f), 0.9f);

        public static GameObject Decoy(GlobalPosition centre) =>
            Make("BoscaliSummer.DecoyBurst", centre, 7f, 40, 4f, 7f, 12f,
                new Color(0.86f, 0.87f, 0.92f, 1f), new Color(0.58f, 0.60f, 0.66f, 1f), 0.6f);

        private static GameObject Make(string name, GlobalPosition centre, float seconds, int puffs, float minSize, float maxSize, float spread,
            Color light, Color dark, float alpha)
        {
            Material mat = SmokeFx.Puff;
            if (Datum.origin == null || mat == null) return null;

            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(Datum.origin, false);
            go.transform.position = centre.ToLocalPosition();

            ParticleSystem particles = go.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            // Emit in the first tenth, live 80-90 %: the last puff fades out as the blocking cloud ends (Triggers.CloudGoneAt), not later.
            main.duration = seconds * 0.1f;
            main.stopAction = ParticleSystemStopAction.Destroy;
            main.startLifetime = new ParticleSystem.MinMaxCurve(seconds * 0.8f, seconds * 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(spread * 0.5f, spread);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(light, dark);
            main.gravityModifier = -0.01f;
            main.maxParticles = puffs + 16;
            // Local to Datum.origin: the cloud moves with the floating origin instead of jumping.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.cullingMode = ParticleSystemCullingMode.PauseAndCatchup;

            var emission = particles.emission;
            emission.rateOverTime = 16f / Mathf.Max(1f, main.duration);
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)puffs) });

            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 3f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            // The burst throws the puffs out, then they hang where they stopped.
            var limit = particles.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0.4f;
            limit.dampen = 0.08f;

            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.5f), new Keyframe(0.15f, 1f), new Keyframe(1f, 1.5f)));

            var fade = particles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(alpha, 0.05f),
                    new GradientAlphaKey(alpha * 0.85f, 0.8f), new GradientAlphaKey(0f, 1f) });
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
            return go;
        }
    }
}
