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
            if (!ribbon) Bloom(particles, origin, visibleSeconds);
            return true;
        }

        // 2026-10-27 (user: countermeasures "should be easier to spot"): the fibres alone vanish past a few hundred metres.
        // Each burst bundle also blooms a pale cloud, as real chaff does, kept at least BloomMinScreen of the view tall at any range.
        public const float BloomMinScreen = 0.006f, BloomAlpha = 0.55f;
        // Vanilla flares: a bigger burning core that never shrinks below FlareMinScreen of the view (about 4 px at 1080p).
        public const float FlareSize = 1.4f, FlareMinScreen = 0.004f;

        private Material puff;
        private Texture2D soft;

        private void Bloom(ParticleSystem parent, Transform origin, float seconds)
        {
            Transform child = parent.transform.Find("Bloom");
            GameObject go = child != null ? child.gameObject : new GameObject("Bloom");
            go.transform.SetParent(parent.transform, false);
            ParticleSystem cloud = go.GetComponent<ParticleSystem>();
            if (cloud == null) cloud = go.AddComponent<ParticleSystem>();
            cloud.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = cloud.main;
            main.loop = true;       // same reason as the fibres: one burst, kept alive until RadarChaff stops the bundle
            main.duration = seconds * 2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(seconds * 0.8f, seconds);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.86f, 0.89f, 0.93f, BloomAlpha);
            main.gravityModifier = 0.004f;
            main.maxParticles = 8;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = origin;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = cloud.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0.02f, 3) });
            var shape = cloud.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;
            var drag = cloud.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 1.2f;
            drag.multiplyDragByParticleSize = false;
            drag.multiplyDragByParticleVelocity = false;
            var size = cloud.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.4f), new Keyframe(0.25f, 1f), new Keyframe(1f, 2.2f)));
            var fade = cloud.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.06f),
                    new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var renderer = cloud.GetComponent<ParticleSystemRenderer>();
            renderer.enabled = EnsurePuff();
            if (!renderer.enabled) return;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = puff;
            renderer.minParticleSize = BloomMinScreen;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
        }

        private bool EnsurePuff()
        {
            if (puff != null) return true;
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) return false;
            const int n = 32;
            soft = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Boscali chaff / soft puff", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float r = new Vector2(x - (n - 1) * 0.5f, y - (n - 1) * 0.5f).magnitude / (n * 0.5f);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - r), 1.6f);
                    pixels[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            soft.SetPixels32(pixels);
            soft.Apply(false, true);
            puff = new Material(shader) { name = "Boscali chaff / bloom", renderQueue = (int)RenderQueue.Transparent };
            puff.SetTexture("_BaseMap", soft);
            puff.SetColor("_BaseColor", Color.white);
            Transparent(puff);
            return true;
        }

        /// <summary>The vanilla flare's burning core, bigger and kept on screen at range. Idempotent: a flare launched twice is not grown twice.</summary>
        public static void BrightenFlare(ParticleSystem core)
        {
            var renderer = core != null ? core.GetComponent<ParticleSystemRenderer>() : null;
            if (renderer == null || renderer.minParticleSize >= FlareMinScreen) return;
            renderer.minParticleSize = FlareMinScreen;
            var main = core.main;
            main.startSizeMultiplier *= FlareSize;
        }

        private static void Transparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ColorMode", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
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
            Transparent(foil);
            foil.SetFloat("_ReceiveShadows", 0f);
            foil.SetColor("_EmissionColor", Color.black);
            foil.EnableKeyword("_RECEIVE_SHADOWS_OFF");
            foil.DisableKeyword("_EMISSION");
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
            if (puff != null) Object.Destroy(puff);
            if (soft != null) Object.Destroy(soft);
            foil = null;
            strip = null;
            puff = null;
            soft = null;
        }
    }
}
