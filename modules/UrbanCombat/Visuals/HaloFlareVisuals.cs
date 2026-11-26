using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Attached ankle flare burns, directional flame jets, pyrotechnic sparks and warm point lights,
    /// shared by a whole HALO stick. Cosmetics only.
    /// </summary>
    internal sealed class HaloFlareVisuals
    {
        // Nozzle end-ring centres in the shipped AirborneJumper mesh's Track blend shape.
        // Its renderer carries the FBX axis conversion and scale; transform with BodyMesh, not Body.
        internal static readonly Vector3 TrackLeftNozzle = new Vector3(-0.004157610f, -0.000587777f, 0.007441084f);
        internal static readonly Vector3 TrackRightNozzle = new Vector3(0.004157610f, -0.000587777f, 0.007441084f);

        private const int MaxLights = 4;
        private const float SparkInterval = 0.025f;

        private GameObject root;
        private Material material;
        private readonly ParticleSystem heads, sparks;
        private readonly Light[] lights;
        private int activeLights;
        private bool emitSparks;
        private float nextSparkAt;
        private uint rng = 0x9E3779B9u;

        public HaloFlareVisuals(Transform parent, Material glow)
        {
            root = new GameObject("HALO ankle flares");
            root.transform.SetParent(parent, false);
            material = new Material(glow) { name = "HALO flare burn" };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(5.5f, 5.5f, 5.5f, 1f));
            if (material.HasProperty("_Color")) material.SetColor("_Color", new Color(5.5f, 5.5f, 5.5f, 1f));

            heads = Layer("Attached flare cores / flame jets", 384);
            sparks = Layer("Trailing flare sparks", 1024);

            var main = sparks.main;
            main.gravityModifier = 0.25f;
            var renderer = sparks.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 2.4f;
            renderer.velocityScale = 0.045f;
            var fade = sparks.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] {
                    new GradientColorKey(new Color(1f, 0.96f, 0.72f), 0f),
                    new GradientColorKey(new Color(1f, 0.42f, 0.05f), 0.35f),
                    new GradientColorKey(new Color(0.85f, 0.08f, 0.01f), 1f)
                },
                new[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.85f, 0.35f),
                    new GradientAlphaKey(0f, 1f)
                });
            fade.color = gradient;
            var shrink = sparks.sizeOverLifetime;
            shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.15f));

            lights = new Light[MaxLights];
            for (int i = 0; i < MaxLights; i++)
            {
                var lgo = new GameObject("HALO flare light " + (i + 1));
                lgo.transform.SetParent(root.transform, false);
                var light = lgo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.52f, 0.14f);
                light.range = 20f;
                light.intensity = 0f;
                light.shadows = LightShadows.None;
                light.enabled = false;
                lights[i] = light;
            }
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
            renderer.maxParticleSize = 0.55f;
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
            activeLights = 0;
            emitSparks = time >= nextSparkAt;
            if (emitSparks) nextSparkAt = time + SparkInterval;
        }

        public void Draw(Vector3 leftWorld, Vector3 rightWorld, Vector3 velocity, float time, float intensity, bool ignitionBurst, bool wantLight)
        {
            if (intensity <= 0.01f) return;
            Vector3 trailDir = velocity.sqrMagnitude > 1f ? -velocity.normalized : Vector3.up;
            Burn(leftWorld, velocity, trailDir, time, intensity, ignitionBurst);
            Burn(rightWorld, velocity, trailDir, time + 1.7f, intensity, ignitionBurst);

            if (wantLight && activeLights < MaxLights)
            {
                Light light = lights[activeLights++];
                if (light != null)
                {
                    float flicker = 0.88f + 0.12f * Mathf.Sin(time * 43f) + 0.06f * Mathf.Sin(time * 79f);
                    light.transform.position = (leftWorld + rightWorld) * 0.5f + trailDir * 0.35f;
                    light.intensity = 3.2f * intensity * flicker;
                    light.enabled = true;
                }
            }
        }

        public void EndFrame()
        {
            for (int i = activeLights; i < MaxLights; i++)
                if (lights[i] != null && lights[i].enabled) lights[i].enabled = false;
            // LateUpdate rewrites the attached particles after Unity's ordinary particle update.
            // Advance this tiny, bounded head layer so its render buffers/bounds match this frame;
            // zero dt is skipped by the engine. Sparks retain the ordinary simulation clock.
            heads.Simulate(0.001f, false, false, false);
            heads.Play(false);
        }

        private void Burn(Vector3 world, Vector3 velocity, Vector3 trailDir, float time, float intensity, bool ignitionBurst)
        {
            Vector3 point = root.transform.InverseTransformPoint(world);
            Vector3 localTrail = root.transform.InverseTransformDirection(trailDir);
            float pulse = 0.90f + 0.09f * Mathf.Sin(time * 41f) + 0.05f * Mathf.Sin(time * 67f) + 0.03f * Mathf.Cos(time * 103f);
            float scale = Mathf.Clamp(intensity, 0.15f, 1.5f);

            // Wide soft atmospheric glare halo (reads at long V-formation distance).
            heads.Emit(new ParticleSystem.EmitParams
            {
                position = point,
                startSize = 2.15f * pulse * scale,
                startLifetime = 0.12f,
                startColor = new Color(1f, 0.30f, 0.04f, 0.17f * Mathf.Clamp01(intensity))
            }, 1);

            // Fiery orange-amber inner flame body.
            heads.Emit(new ParticleSystem.EmitParams
            {
                position = point,
                startSize = 0.88f * pulse * scale,
                startLifetime = 0.12f,
                startColor = new Color(1f, 0.48f, 0.09f, 0.56f * Mathf.Clamp01(intensity))
            }, 1);

            // Blinding white-gold core right at the canister nozzle.
            heads.Emit(new ParticleSystem.EmitParams
            {
                position = point,
                startSize = 0.33f * pulse * scale,
                startLifetime = 0.12f,
                startColor = new Color(1f, 0.96f, 0.78f, Mathf.Clamp01(intensity))
            }, 1);

            // Directional flame jet streaming aft in the slipstream.
            float jetPulse = 0.85f + 0.15f * Mathf.Sin(time * 53f + 0.9f);
            heads.Emit(new ParticleSystem.EmitParams
            {
                position = point + localTrail * (0.30f * jetPulse),
                startSize = 0.42f * jetPulse * scale,
                startLifetime = 0.12f,
                startColor = new Color(1f, 0.80f, 0.38f, 0.90f * Mathf.Clamp01(intensity))
            }, 1);
            heads.Emit(new ParticleSystem.EmitParams
            {
                position = point + localTrail * (0.68f * jetPulse),
                startSize = 0.34f * jetPulse * scale,
                startLifetime = 0.12f,
                startColor = new Color(1f, 0.44f, 0.07f, 0.65f * Mathf.Clamp01(intensity))
            }, 1);
            heads.Emit(new ParticleSystem.EmitParams
            {
                position = point + localTrail * (1.12f * jetPulse),
                startSize = 0.24f * jetPulse * scale,
                startLifetime = 0.12f,
                startColor = new Color(0.95f, 0.22f, 0.02f, 0.38f * Mathf.Clamp01(intensity))
            }, 1);

            int sparkCount = ignitionBurst ? 6 : (emitSparks ? 3 : 0);
            if (sparkCount == 0) return;

            for (int k = 0; k < sparkCount; k++)
            {
                Vector3 spread = new Vector3(NextSigned(), NextSigned(), NextSigned()) * (ignitionBurst ? 5.5f : 3.2f);
                float jetSpeed = Mathf.Lerp(5f, 13f, Next01());
                Vector3 sparkVel = velocity * 0.45f + trailDir * jetSpeed + spread;
                sparks.Emit(new ParticleSystem.EmitParams
                {
                    position = point + localTrail * (0.12f + 0.2f * Next01()),
                    velocity = root.transform.InverseTransformVector(sparkVel),
                    startSize = Mathf.Lerp(0.055f, 0.115f, Next01()),
                    startLifetime = Mathf.Lerp(0.45f, 0.95f, Next01()),
                    startColor = Color.white
                }, 1);
            }
        }

        /// <summary>Tactical green double-blink IR/visual strobe on team leads under canopy or on the ground.</summary>
        public void DrawStrobe(Vector3 worldPos, float time, bool landed)
        {
            if (root == null || heads == null) return;
            float cycle = Mathf.Repeat(time, 1.4f);
            bool flash = landed ? (cycle < 0.16f) : (cycle < 0.09f || (cycle > 0.20f && cycle < 0.29f));
            if (!flash) return;
            Vector3 point = root.transform.InverseTransformPoint(worldPos);
            heads.Emit(new ParticleSystem.EmitParams
            {
                position = point,
                startSize = landed ? 1.8f : 2.6f,
                startLifetime = 0.12f,
                startColor = new Color(0.20f, 1f, 0.45f, landed ? 0.22f : 0.30f),
            }, 1);
            heads.Emit(new ParticleSystem.EmitParams
            {
                position = point,
                startSize = landed ? 0.48f : 0.65f,
                startLifetime = 0.12f,
                startColor = new Color(0.78f, 1f, 0.88f, 0.95f),
            }, 1);
        }

        private float Next01()
        {
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            return (rng & 0x00FFFFFFu) * (1f / 16777216f);
        }

        private float NextSigned() => Next01() * 2f - 1f;

        public void Clear()
        {
            if (root != null) Object.Destroy(root);
            if (material != null) Object.Destroy(material);
            root = null;
            material = null;
        }
    }
}
