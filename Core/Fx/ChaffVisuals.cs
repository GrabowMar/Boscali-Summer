using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Core.Fx
{
    /// <summary>Shared optical treatment for dispenser and bait chaff; no radar or flight rules.</summary>
    internal sealed class ChaffVisuals
    {
        private Material foil;
        private Mesh strip;

        public bool Configure(ParticleSystem particles, Transform origin, float seconds, bool ribbon = false)
        {
            if (origin == null) return false;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            float visibleSeconds = ribbon ? Mathf.Min(seconds, 14f) : seconds;
            var main = particles.main;
            // RadarChaff only schedules its destruction while isPlaying. Keep an empty loop alive
            // until its native timer stops it; the burst never repeats within that timer.
            main.loop = true;
            main.duration = seconds * 2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(visibleSeconds * 0.75f, visibleSeconds * 0.96f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(ribbon ? 0.5f : 3f, ribbon ? 2.5f : 9f);
            // Deliberately legible fibre clusters, in metres; no screen-space minimum size.
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.52f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.78f, 0.82f, 0.88f, 0.9f), Color.white);
            main.gravityModifier = 0.008f;
            main.maxParticles = ribbon ? 1000 : 128;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = origin;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            var emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = ribbon ? 0.25f : 0f;
            emission.SetBursts(ribbon ? new ParticleSystem.Burst[0] : new[]
            {
                new ParticleSystem.Burst(0f, 64), new ParticleSystem.Burst(0.08f, 48)
            });
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = ribbon ? 1.2f : 0.25f;
            var drag = particles.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 0.85f;
            drag.multiplyDragByParticleSize = false;
            drag.multiplyDragByParticleVelocity = false;
            var noise = particles.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.strength = 0.35f;
            noise.frequency = 0.45f;
            noise.scrollSpeed = 0.2f;
            noise.damping = true;
            var rotation = particles.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-4f, 5f);
            rotation.y = new ParticleSystem.MinMaxCurve(-3f, 3.5f);
            rotation.z = new ParticleSystem.MinMaxCurve(-2f, 2.5f);
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.65f), new Keyframe(0.07f, 1f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0.5f)));
            var fade = particles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.035f),
                    new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.enabled = EnsureAssets();
            if (!renderer.enabled) return false;
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = strip;
            renderer.sharedMaterial = foil;
            renderer.alignment = ParticleSystemRenderSpace.World;
            renderer.minParticleSize = 0f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            return true;
        }

        private bool EnsureAssets()
        {
            if (foil != null) return true;
            // Shipped by Nuclear Option, including a native metallic-fragment material.
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Lit");
            if (shader == null) return false;
            foil = new Material(shader) { name = "Boscali chaff / silver fibres", renderQueue = (int)RenderQueue.Transparent };
            foil.SetColor("_BaseColor", new Color(0.88f, 0.91f, 0.95f, 1f));
            foil.SetFloat("_Metallic", 0.6f);
            foil.SetFloat("_Smoothness", 0.55f);
            foil.SetFloat("_Surface", 1f);
            foil.SetFloat("_Blend", 0f);
            foil.SetFloat("_ColorMode", 0f);
            foil.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            foil.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            foil.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            foil.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            foil.SetFloat("_ZWrite", 0f);
            foil.SetFloat("_Cull", (float)CullMode.Off);
            foil.SetFloat("_ReceiveShadows", 0f);
            foil.SetColor("_EmissionColor", Color.black);
            foil.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            foil.EnableKeyword("_RECEIVE_SHADOWS_OFF");
            foil.DisableKeyword("_EMISSION");
            foil.SetOverrideTag("RenderType", "Transparent");
            // A lightly folded strip catches sunlight as it tumbles, without emissive sparkle sprites.
            strip = new Mesh { name = "Folded chaff fibre cluster" };
            strip.vertices = new[] { new Vector3(-0.1f, -0.5f, 0f), new Vector3(0f, -0.5f, 0.025f),
                new Vector3(0.1f, -0.5f, 0f), new Vector3(-0.1f, 0.5f, 0f),
                new Vector3(0f, 0.5f, 0.025f), new Vector3(0.1f, 0.5f, 0f) };
            strip.uv = new[] { Vector2.zero, new Vector2(0.5f, 0f), Vector2.right,
                Vector2.up, new Vector2(0.5f, 1f), Vector2.one };
            strip.triangles = new[] { 0, 3, 1, 1, 3, 4, 1, 4, 2, 2, 4, 5 };
            strip.RecalculateNormals();
            strip.RecalculateBounds();
            return true;
        }

        public void Clear()
        {
            if (foil != null) Object.Destroy(foil);
            if (strip != null) Object.Destroy(strip);
            foil = null;
            strip = null;
        }
    }
}
