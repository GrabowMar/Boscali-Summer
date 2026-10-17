using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Garrisons
{
    /// <summary>Attached ankle burns and short-lived sparks, shared by a whole HALO stick. Cosmetics only.</summary>
    internal sealed class HaloFlareVisuals
    {
        // Nozzle end-ring centres in the shipped AirborneJumper mesh's Track blend shape.
        // Its renderer carries the FBX axis conversion and scale; transform with BodyMesh, not Body.
        internal static readonly Vector3 TrackLeftNozzle = new Vector3(-0.004157610f, -0.000587777f, 0.007441084f);
        internal static readonly Vector3 TrackRightNozzle = new Vector3(0.004157610f, -0.000587777f, 0.007441084f);

        private GameObject root;
        private Material material;
        private readonly ParticleSystem heads, sparks;
        private bool emitSparks;
        private float nextSparkAt;

        public HaloFlareVisuals(Transform parent, Material glow)
        {
            root = new GameObject("HALO ankle flares");
            root.transform.SetParent(parent, false);
            material = new Material(glow) { name = "HALO flare burn" };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(4f, 4f, 4f, 1f));
            if (material.HasProperty("_Color")) material.SetColor("_Color", new Color(4f, 4f, 4f, 1f));
            heads = Layer("Attached white cores / orange halos", 64);
            sparks = Layer("Trailing flare sparks", 512);
            var main = sparks.main;
            main.gravityModifier = 0.15f;
            var renderer = sparks.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 1.8f;
            renderer.velocityScale = 0.03f;
            var fade = sparks.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.55f), 0f),
                    new GradientColorKey(new Color(1f, 0.25f, 0.025f), 0.45f), new GradientColorKey(new Color(0.8f, 0.05f, 0f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.35f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var shrink = sparks.sizeOverLifetime;
            shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
        }

        private ParticleSystem Layer(string name, int budget)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(root.transform, false);
            var particles = go.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startSpeed = 0f;
            main.maxParticles = budget;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = false;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.maxParticleSize = 0.5f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.minParticleSize = 0f;
            go.SetActive(true);
            particles.Play(false);
            return particles;
        }

        public void BeginFrame(float time)
        {
            // Core particles are rewritten after the plan moves the jumpers, so they cannot lag
            // behind the ankle at freefall speed. Sparks keep their independent short trajectories.
            heads.Clear(false);
            emitSparks = time >= nextSparkAt;
            if (emitSparks) nextSparkAt = time + 0.04f;
        }

        public void Draw(Vector3 leftWorld, Vector3 rightWorld, Vector3 velocity, float time, bool burning)
        {
            if (!burning) return;
            Burn(leftWorld, velocity, time);
            Burn(rightWorld, velocity, time + 1.7f);
        }

        public void EndFrame()
        {
            // LateUpdate rewrites the attached particles after Unity's ordinary particle update.
            // Advance this tiny, bounded head layer so its render buffers/bounds match this frame;
            // zero dt is skipped by the engine. Sparks retain the ordinary simulation clock.
            heads.Simulate(0.001f, false, false, false);
            heads.Play(false);
        }

        private void Burn(Vector3 world, Vector3 velocity, float time)
        {
            Vector3 point = root.transform.InverseTransformPoint(world);
            float pulse = 0.92f + 0.08f * Mathf.Sin(time * 37f) + 0.04f * Mathf.Sin(time * 61f);
            heads.Emit(new ParticleSystem.EmitParams { position = point, startSize = 0.8f * pulse,
                startLifetime = 0.12f, startColor = new Color(1f, 0.42f, 0.08f, 0.28f) }, 1);
            heads.Emit(new ParticleSystem.EmitParams { position = point, startSize = 0.24f * pulse,
                startLifetime = 0.12f, startColor = new Color(1f, 0.94f, 0.7f, 1f) }, 1);
            if (!emitSparks) return;
            Vector3 scatter = new Vector3(Mathf.Sin(time * 17f), Mathf.Sin(time * 23f), Mathf.Cos(time * 19f)) * 1.8f;
            sparks.Emit(new ParticleSystem.EmitParams { position = point,
                velocity = root.transform.InverseTransformVector(velocity * 0.2f + scatter), startSize = 0.075f,
                startLifetime = 0.5f + 0.1f * Mathf.Sin(time * 11f), startColor = Color.white }, 1);
        }

        public void Clear()
        {
            if (root != null) Object.Destroy(root);
            if (material != null) Object.Destroy(material);
            root = null;
            material = null;
        }
    }
}
