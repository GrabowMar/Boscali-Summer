using System;
using System.Collections;
using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Visuals
{
    /// <summary>
    /// Delivers the cinematic visual, lighting, atmospheric, and acoustic effects for the
    /// "Rod from God" orbital kinetic strike during both descent and ground impact phases.
    /// </summary>
    internal static class KineticRodStrikeVisuals
    {
        public static void Track(Missile missile, Vector3 target)
        {
            if (missile == null || GameManager.IsHeadless) return;
            if (missile.GetComponent<KineticRodDescentEffect>() != null) return;

            var descent = missile.gameObject.AddComponent<KineticRodDescentEffect>();
            descent.Initialize(target);
        }

        public static void TriggerImpact(Vector3 impactPosition)
        {
            if (GameManager.IsHeadless) return;
            KineticRodImpactEffect.Spawn(impactPosition);
        }
    }

    /// <summary>
    /// Attached to the plunging kinetic rod missile during atmospheric re-entry.
    /// Creates a blinding white-hot spearhead light, hypervelocity ionization/plasma trail,
    /// re-entry spark spall particles, and a screaming Mach-8 hypersonic tearing audio.
    /// </summary>
    internal sealed class KineticRodDescentEffect : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private static Material plasmaTrailMaterial;
        private static Material sparkMaterial;
        private static AudioClip hypersonicSoundClip;

        private Missile missile;
        private Vector3 targetPosition;
        private Light headLight;
        private TrailRenderer plasmaTrail;
        private ParticleSystem sparkSystem;
        private AudioSource audioSource;
        private Vector3 lastPosition;
        private bool hasDetonated;

        public void Initialize(Vector3 target)
        {
            targetPosition = target;
            missile = GetComponent<Missile>();
            lastPosition = transform.position;

            EnsureAssets();

            // 1. Blinding incandescent white-hot kinetic spearhead light
            var lightObj = new GameObject("RodHeadLight");
            lightObj.transform.SetParent(transform, false);
            lightObj.transform.localPosition = Vector3.forward * 2f;
            headLight = lightObj.AddComponent<Light>();
            headLight.type = LightType.Point;
            headLight.color = new Color(1f, 0.94f, 0.82f);
            headLight.range = 16000f;
            headLight.intensity = 38f;
            headLight.shadows = LightShadows.None;

            // 2. Hypervelocity re-entry plasma trail
            plasmaTrail = gameObject.AddComponent<TrailRenderer>();
            plasmaTrail.sharedMaterial = plasmaTrailMaterial;
            plasmaTrail.time = 1.35f;
            plasmaTrail.minVertexDistance = 8f;
            plasmaTrail.startWidth = 16f;
            plasmaTrail.endWidth = 1.8f;
            plasmaTrail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.2f, 0.8f),
                new Keyframe(0.6f, 0.35f),
                new Keyframe(1f, 0.05f));

            Gradient trailGradient = new Gradient();
            trailGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 1f, 0.95f), 0.0f),     // Pure white-hot core
                    new GradientColorKey(new Color(1f, 0.65f, 0.15f), 0.25f),  // Solar plasma orange
                    new GradientColorKey(new Color(1f, 0.22f, 0.02f), 0.65f),  // Re-entry flame red
                    new GradientColorKey(new Color(0.35f, 0.35f, 0.35f), 1.0f) // Dissipating atmospheric wake
                },
                new[]
                {
                    new GradientAlphaKey(1.0f, 0.0f),
                    new GradientAlphaKey(0.9f, 0.4f),
                    new GradientAlphaKey(0.4f, 0.8f),
                    new GradientAlphaKey(0.0f, 1.0f)
                });
            plasmaTrail.colorGradient = trailGradient;

            // 3. Hypervelocity spark spall particle emitter
            var sparkObj = new GameObject("RodSparks");
            sparkObj.transform.SetParent(transform, false);
            sparkObj.transform.localPosition = Vector3.back * 1.5f;
            sparkSystem = sparkObj.AddComponent<ParticleSystem>();
            var renderer = sparkObj.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = sparkMaterial;

            var main = sparkSystem.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(60f, 180f);
            main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 6f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.95f, 0.7f, 1f),
                new Color(1f, 0.45f, 0.05f, 0.8f));
            main.maxParticles = 600;

            var emission = sparkSystem.emission;
            emission.rateOverTime = 120f;

            var shape = sparkSystem.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 1.2f;
            shape.rotation = new Vector3(0f, 180f, 0f); // Spray backwards

            // 4. Spatialized hypersonic screaming atmospheric tear audio
            if (hypersonicSoundClip != null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.clip = hypersonicSoundClip;
                audioSource.loop = true;
                audioSource.spatialBlend = 0.75f;
                audioSource.minDistance = 300f;
                audioSource.maxDistance = 45000f;
                audioSource.volume = 1.0f;
                audioSource.dopplerLevel = 1.8f;
                audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
                audioSource.Play();
            }
        }

        private void Update()
        {
            if (missile != null && !missile.disabled)
            {
                lastPosition = transform.position;

                // Pre-impact tremor: building atmospheric rumble in final kilometer
                float distToGround = transform.position.y - targetPosition.y;
                if (distToGround < 2200f && distToGround > 0f)
                {
                    var csm = SceneSingleton<CameraStateManager>.i;
                    Camera cam = csm?.mainCamera ?? Camera.main;
                    if (cam != null)
                    {
                        float camDist = Vector3.Distance(cam.transform.position, targetPosition);
                        if (camDist < 12000f)
                        {
                            float factor = Mathf.Clamp01(1f - (camDist / 12000f)) * Mathf.Clamp01(1f - (distToGround / 2200f));
                            if (csm != null) csm.ShakeCamera(0.25f * factor, 0.5f * factor);
                        }
                    }
                }
            }
            else if (!hasDetonated)
            {
                TriggerDetonation();
            }
        }

        private void OnDestroy()
        {
            if (!hasDetonated)
            {
                TriggerDetonation();
            }
        }

        private void TriggerDetonation()
        {
            if (hasDetonated) return;
            hasDetonated = true;
            KineticRodStrikeVisuals.TriggerImpact(lastPosition);
        }

        private static void EnsureAssets()
        {
            if (plasmaTrailMaterial != null && sparkMaterial != null && hypersonicSoundClip != null) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Color");

            if (plasmaTrailMaterial == null)
            {
                plasmaTrailMaterial = new Material(shader) { name = "KineticRodPlasmaTrailMat" };
                plasmaTrailMaterial.SetColor("_Color", new Color(1f, 0.85f, 0.5f, 1f));
                if (plasmaTrailMaterial.HasProperty("_Surface")) plasmaTrailMaterial.SetFloat("_Surface", 1f);
                if (plasmaTrailMaterial.HasProperty("_Blend")) plasmaTrailMaterial.SetFloat("_Blend", 1f);
            }

            if (sparkMaterial == null)
            {
                sparkMaterial = new Material(shader) { name = "KineticRodSparkMat" };
                sparkMaterial.SetColor("_Color", new Color(1f, 0.7f, 0.2f, 1f));
                if (sparkMaterial.HasProperty("_Surface")) sparkMaterial.SetFloat("_Surface", 1f);
                if (sparkMaterial.HasProperty("_Blend")) sparkMaterial.SetFloat("_Blend", 1f);
            }

            if (hypersonicSoundClip == null)
            {
                // Synthesize screaming Mach-8 atmospheric air-shear & turbine shock roar
                int length = (int)(SampleRate * 3.5f);
                float[] samples = new float[length];
                for (int i = 0; i < length; i++)
                {
                    float t = i / (float)SampleRate;
                    // High-velocity aerodynamic screech (1400Hz - 2200Hz whistling vortex)
                    float screech = Mathf.Sin(2f * Mathf.PI * (1650f + Mathf.Sin(2f * Mathf.PI * 8f * t) * 220f) * t) * 0.35f;
                    // Supersonic air shear turbulent noise
                    float noise = (UnityEngine.Random.value * 2f - 1f) * 0.45f;
                    // Deep aerodynamic displacement rumble (65Hz)
                    float rumble = Mathf.Sin(2f * Mathf.PI * 65f * t) * 0.4f;
                    samples[i] = Mathf.Clamp(screech + noise + rumble, -1f, 1f);
                }
                hypersonicSoundClip = AudioClip.Create("KineticRodHypersonicSound", length, 1, SampleRate, false);
                hypersonicSoundClip.SetData(samples, 0);
            }
        }
    }

    /// <summary>
    /// Delivers the ground-zero kinetic impact: blinding flash, hypervelocity ground
    /// shockwave ring, towering vertical ejecta geyser, radial debris streamers,
    /// seismic camera shake, and cataclysmic procedural multi-layer audio.
    /// </summary>
    internal sealed class KineticRodImpactEffect : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private static Material shockwaveRingMaterial;
        private static Material ejectaMaterial;
        private static AudioClip impactAudioClip;

        public static void Spawn(Vector3 impactPosition)
        {
            var go = new GameObject("BoscaliSummer.KineticRodImpact");
            go.transform.position = impactPosition;
            go.transform.SetParent(Datum.origin, true);
            var effect = go.AddComponent<KineticRodImpactEffect>();
            effect.Initialize(impactPosition);
        }

        private Vector3 groundZero;
        private Light impactLight;
        private GameObject shockwaveObj;
        private MeshFilter shockwaveFilter;
        private MeshRenderer shockwaveRenderer;
        private Mesh shockwaveMesh;
        private ParticleSystem ejectaColumn;
        private ParticleSystem debrisStreamers;
        private AudioSource audioSource;
        private float startTime;
        private const float EffectDuration = 6.5f;

        private void Initialize(Vector3 point)
        {
            groundZero = point;
            startTime = Time.time;

            EnsureAssets();

            // 1. Blinding cataclysmic impact flash
            var lightObj = new GameObject("ImpactFlash");
            lightObj.transform.SetParent(transform, false);
            impactLight = lightObj.AddComponent<Light>();
            impactLight.type = LightType.Point;
            impactLight.color = new Color(0.95f, 0.98f, 1.0f);
            impactLight.range = 95000f;
            impactLight.intensity = 130f;
            impactLight.shadows = LightShadows.None;

            // 2. Expanding ground compression shockwave ring
            shockwaveObj = new GameObject("GroundShockwaveRing");
            shockwaveObj.transform.SetParent(transform, false);
            shockwaveFilter = shockwaveObj.AddComponent<MeshFilter>();
            shockwaveRenderer = shockwaveObj.AddComponent<MeshRenderer>();
            shockwaveRenderer.sharedMaterial = shockwaveRingMaterial;
            shockwaveMesh = new Mesh { name = "KineticGroundShockwaveMesh" };
            shockwaveFilter.sharedMesh = shockwaveMesh;

            // 3. Towering vertical kinetic ejecta column
            var ejectaObj = new GameObject("VerticalEjecta");
            ejectaObj.transform.SetParent(transform, false);
            ejectaColumn = ejectaObj.AddComponent<ParticleSystem>();
            var colRenderer = ejectaObj.GetComponent<ParticleSystemRenderer>();
            colRenderer.sharedMaterial = ejectaMaterial;

            var mainCol = ejectaColumn.main;
            mainCol.simulationSpace = ParticleSystemSimulationSpace.World;
            mainCol.duration = 1.5f;
            mainCol.startLifetime = new ParticleSystem.MinMaxCurve(2.8f, 5.2f);
            mainCol.startSpeed = new ParticleSystem.MinMaxCurve(180f, 420f);
            mainCol.startSize = new ParticleSystem.MinMaxCurve(12f, 32f);
            mainCol.gravityModifier = 0.85f;
            mainCol.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.9f, 0.7f, 1f),
                new Color(1f, 0.35f, 0.05f, 0.75f));
            mainCol.maxParticles = 500;

            var emissionCol = ejectaColumn.emission;
            emissionCol.rateOverTime = 0f;
            emissionCol.SetBursts(new[] { new ParticleSystem.Burst(0f, 220, 300) });

            var shapeCol = ejectaColumn.shape;
            shapeCol.shapeType = ParticleSystemShapeType.Cone;
            shapeCol.angle = 9f; // Narrow high-speed vertical jet
            shapeCol.radius = 8f;
            shapeCol.rotation = new Vector3(-90f, 0f, 0f); // Shoot straight up

            // 4. Radial kinetic debris streamers & spall
            var debrisObj = new GameObject("RadialDebris");
            debrisObj.transform.SetParent(transform, false);
            debrisStreamers = debrisObj.AddComponent<ParticleSystem>();
            var debRenderer = debrisObj.GetComponent<ParticleSystemRenderer>();
            debRenderer.sharedMaterial = ejectaMaterial;

            var mainDeb = debrisStreamers.main;
            mainDeb.simulationSpace = ParticleSystemSimulationSpace.World;
            mainDeb.duration = 1.2f;
            mainDeb.startLifetime = new ParticleSystem.MinMaxCurve(3.0f, 6.0f);
            mainDeb.startSpeed = new ParticleSystem.MinMaxCurve(120f, 320f);
            mainDeb.startSize = new ParticleSystem.MinMaxCurve(6f, 18f);
            mainDeb.gravityModifier = 1.2f;
            mainDeb.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.8f, 0.3f, 1f),
                new Color(0.9f, 0.2f, 0.05f, 0.8f));
            mainDeb.maxParticles = 400;

            var emissionDeb = debrisStreamers.emission;
            emissionDeb.rateOverTime = 0f;
            emissionDeb.SetBursts(new[] { new ParticleSystem.Burst(0f, 150, 220) });

            var shapeDeb = debrisStreamers.shape;
            shapeDeb.shapeType = ParticleSystemShapeType.Hemisphere;
            shapeDeb.radius = 12f;
            shapeDeb.rotation = new Vector3(-90f, 0f, 0f);

            // 5. Cataclysmic procedural multi-layered impact audio
            if (impactAudioClip != null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.clip = impactAudioClip;
                audioSource.spatialBlend = 0.45f;
                audioSource.minDistance = 600f;
                audioSource.maxDistance = 90000f;
                audioSource.volume = 1.0f;
                audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
                audioSource.Play();
            }

            // 6. Trigger seismic camera shake based on distance
            TriggerSeismicShock();

            StartCoroutine(Animate());
        }

        private void TriggerSeismicShock()
        {
            Camera cam = SceneSingleton<CameraStateManager>.i?.mainCamera ?? Camera.main;
            if (cam == null) return;

            float distance = Vector3.Distance(cam.transform.position, groundZero);
            if (distance > 35000f) return;

            // Seismic wave travels through ground at ~3200m/s
            float seismicDelay = distance / 3200f;
            float intensity = Mathf.Clamp01(1f - (distance / 30000f));
            float lowFreq = Mathf.Lerp(0.3f, 2.8f, intensity * intensity);
            float highFreq = Mathf.Lerp(0.4f, 3.8f, intensity);

            StartCoroutine(DelayedCameraShake(seismicDelay, lowFreq, highFreq, intensity));
        }

        private IEnumerator DelayedCameraShake(float delay, float lowFreq, float highFreq, float intensity)
        {
            if (delay > 0.02f)
                yield return new WaitForSeconds(delay);

            var csm = SceneSingleton<CameraStateManager>.i;
            if (csm != null)
            {
                csm.ShakeCamera(lowFreq, highFreq);
            }

            // Continuous tremor decay over 2.5 seconds
            float elapsed = 0f;
            float duration = 2.5f * intensity;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float decay = Mathf.Pow(1f - Mathf.Clamp01(elapsed / duration), 2f);
                if (csm != null && decay > 0.1f)
                {
                    csm.ShakeCamera(lowFreq * decay * 0.4f, highFreq * decay * 0.4f);
                }
                yield return null;
            }
        }

        private IEnumerator Animate()
        {
            float elapsed = 0f;
            while (elapsed < EffectDuration)
            {
                elapsed = Time.time - startTime;
                float progress = Mathf.Clamp01(elapsed / EffectDuration);

                // Animate flash: instant peak, fast decay to warm crater glow
                if (impactLight != null)
                {
                    if (elapsed < 0.35f)
                    {
                        float t = elapsed / 0.35f;
                        impactLight.intensity = Mathf.Lerp(130f, 32f, t);
                        impactLight.color = Color.Lerp(new Color(0.95f, 0.98f, 1f), new Color(1f, 0.55f, 0.15f), t);
                    }
                    else
                    {
                        float t = Mathf.Clamp01((elapsed - 0.35f) / 3.2f);
                        impactLight.intensity = Mathf.Lerp(32f, 0f, t * t);
                        if (impactLight.intensity <= 0.05f) impactLight.enabled = false;
                    }
                }

                // Animate ground shockwave expansion (out to 2200m)
                float shockwaveProgress = Mathf.Clamp01(elapsed / 2.2f);
                float radius = Mathf.Lerp(15f, 2200f, Mathf.Sqrt(shockwaveProgress));
                float thickness = Mathf.Lerp(30f, 380f, shockwaveProgress);
                float alpha = Mathf.Pow(1f - shockwaveProgress, 1.8f);
                UpdateShockwaveRingMesh(radius, thickness, alpha);

                yield return null;
            }

            Destroy(gameObject, 3.0f);
        }

        private void UpdateShockwaveRingMesh(float radius, float thickness, float alpha)
        {
            if (shockwaveMesh == null) return;

            const int segments = 64;
            Vector3[] vertices = new Vector3[(segments + 1) * 2];
            Color[] colors = new Color[vertices.Length];
            int[] triangles = new int[segments * 6];

            float innerR = Mathf.Max(0f, radius - thickness);
            float outerR = radius;

            Color innerColor = new Color(1f, 0.65f, 0.15f, alpha * 0.9f);
            Color outerColor = new Color(1f, 0.25f, 0.02f, 0f);

            for (int i = 0; i <= segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                float sin = Mathf.Sin(angle);
                float cos = Mathf.Cos(angle);

                int vInner = i * 2;
                int vOuter = i * 2 + 1;

                vertices[vInner] = new Vector3(sin * innerR, 1.5f, cos * innerR);
                vertices[vOuter] = new Vector3(sin * outerR, 1.5f, cos * outerR);

                colors[vInner] = innerColor;
                colors[vOuter] = outerColor;

                if (i < segments)
                {
                    int t = i * 6;
                    triangles[t] = vInner;
                    triangles[t + 1] = vOuter;
                    triangles[t + 2] = vInner + 2;

                    triangles[t + 3] = vInner + 2;
                    triangles[t + 4] = vOuter;
                    triangles[t + 5] = vOuter + 2;
                }
            }

            shockwaveMesh.Clear();
            shockwaveMesh.vertices = vertices;
            shockwaveMesh.colors = colors;
            shockwaveMesh.triangles = triangles;
            shockwaveMesh.RecalculateBounds();
        }

        private static void EnsureAssets()
        {
            if (shockwaveRingMaterial != null && ejectaMaterial != null && impactAudioClip != null) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Color");

            if (shockwaveRingMaterial == null)
            {
                shockwaveRingMaterial = new Material(shader) { name = "KineticShockwaveRingMat" };
                shockwaveRingMaterial.SetColor("_Color", new Color(1f, 0.75f, 0.3f, 1f));
                if (shockwaveRingMaterial.HasProperty("_Surface")) shockwaveRingMaterial.SetFloat("_Surface", 1f);
                if (shockwaveRingMaterial.HasProperty("_Blend")) shockwaveRingMaterial.SetFloat("_Blend", 1f);
            }

            if (ejectaMaterial == null)
            {
                ejectaMaterial = new Material(shader) { name = "KineticEjectaMat" };
                ejectaMaterial.SetColor("_Color", new Color(1f, 0.6f, 0.15f, 1f));
                if (ejectaMaterial.HasProperty("_Surface")) ejectaMaterial.SetFloat("_Surface", 1f);
                if (ejectaMaterial.HasProperty("_Blend")) ejectaMaterial.SetFloat("_Blend", 1f);
            }

            if (impactAudioClip == null)
            {
                // Synthesize 6.0 seconds of cataclysmic kinetic impact: supersonic transient crack +
                // massive 22Hz sub-bass earth shock + long rolling reverberation
                int length = (int)(SampleRate * 6.0f);
                float[] samples = new float[length];

                for (int i = 0; i < length; i++)
                {
                    float t = i / (float)SampleRate;

                    // 1. Supersonic kinetic fracture crack / transient snap (0.0s - 0.08s)
                    float snapEnvelope = Mathf.Exp(-t * 35f);
                    float snap = (UnityEngine.Random.value * 2f - 1f) * snapEnvelope * 0.9f;

                    // 2. Colossal seismic ground impact thud (22 Hz fundamental dropping to 14 Hz)
                    float bassFreq = Mathf.Lerp(24f, 14f, t / 6.0f);
                    float bassEnvelope = Mathf.Pow(Mathf.Clamp01(1f - (t / 5.5f)), 1.4f);
                    float bass = Mathf.Sin(2f * Mathf.PI * bassFreq * t) * bassEnvelope * 0.85f;
                    float subBass = Mathf.Sin(2f * Mathf.PI * (bassFreq * 0.5f) * t) * bassEnvelope * 0.55f;

                    // 3. Ejecta roar and debris turbulence
                    float roarEnvelope = Mathf.Pow(Mathf.Clamp01(1f - (t / 3.8f)), 2.2f);
                    float roar = (UnityEngine.Random.value * 2f - 1f) * roarEnvelope * 0.45f;

                    // 4. Rolling mountain thunder echoes (stochastic reverberation)
                    float echo = (Mathf.Sin(2f * Mathf.PI * 42f * t) + Mathf.Sin(2f * Mathf.PI * 58f * t) * 0.5f)
                        * Mathf.Pow(Mathf.Clamp01(1f - (t / 5.8f)), 1.6f) * 0.35f;

                    samples[i] = Mathf.Clamp(snap + bass + subBass + roar + echo, -1f, 1f);
                }

                impactAudioClip = AudioClip.Create("KineticRodImpactSound", length, 1, SampleRate, false);
                impactAudioClip.SetData(samples, 0);
            }
        }

        private void OnDestroy()
        {
            if (shockwaveMesh != null) Destroy(shockwaveMesh);
        }
    }
}
