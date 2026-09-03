using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Visuals
{
    /// <summary>
    /// Spectacular high-altitude electromagnetic pulse (EMP) burst visual and audio effect.
    /// Simulates real-life high-altitude nuclear / flux-compression EMP phenomena:
    /// 1. Blinding ionospheric flash illuminating clouds and terrain for tens of kilometers.
    /// 2. Hypervelocity expanding plasma shockwave / ionization ring.
    /// 3. Spiderweb atmospheric lightning arcs branching across the upper sky.
    /// 4. Thunderous sub-bass atmospheric rumble combined with high-voltage arc crackle.
    /// </summary>
    internal sealed class EmpVisualEffect : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private static Material ringMaterial;
        private static Material arcMaterial;
        private static AudioClip empAudioClip;

        public static void Trigger(Vector3 detonationPoint, float radiusMeters)
        {
            if (GameManager.IsHeadless) return;

            var go = new GameObject("BoscaliSummer.EmpVisualEffect");
            go.transform.position = detonationPoint;
            go.transform.SetParent(Datum.origin, false);
            var effect = go.AddComponent<EmpVisualEffect>();
            effect.Initialize(detonationPoint, radiusMeters);
        }

        private Vector3 origin;
        private float maxRadius;
        private Light burstLight;
        private GameObject shockwaveObj;
        private MeshFilter shockwaveFilter;
        private MeshRenderer shockwaveRenderer;
        private Mesh shockwaveMesh;
        private readonly List<LineRenderer> lightningArcs = new List<LineRenderer>();
        private AudioSource audioSource;
        private float startTime;
        private float shockwaveDuration = 3.5f;

        private void Initialize(Vector3 point, float radius)
        {
            origin = point;
            maxRadius = radius;
            startTime = Time.time;

            EnsureMaterials();
            EnsureAudio();

            // 1. Blinding atmospheric point light flash
            var lightObj = new GameObject("BurstLight");
            lightObj.transform.SetParent(transform, false);
            burstLight = lightObj.AddComponent<Light>();
            burstLight.type = LightType.Point;
            burstLight.color = new Color(0.65f, 0.90f, 1.0f);
            burstLight.range = Mathf.Max(radius * 3f, 60000f);
            burstLight.intensity = 45f;
            burstLight.shadows = LightShadows.None;

            // 2. Expanding plasma shockwave mesh (concentric glowing rings)
            shockwaveObj = new GameObject("PlasmaShockwave");
            shockwaveObj.transform.SetParent(transform, false);
            shockwaveFilter = shockwaveObj.AddComponent<MeshFilter>();
            shockwaveRenderer = shockwaveObj.AddComponent<MeshRenderer>();
            shockwaveRenderer.sharedMaterial = ringMaterial;
            shockwaveMesh = new Mesh { name = "EmpShockwaveMesh" };
            shockwaveFilter.sharedMesh = shockwaveMesh;

            // 3. Atmospheric lightning arcs spiderwebbing outward
            int arcCount = UnityEngine.Random.Range(10, 16);
            for (int i = 0; i < arcCount; i++)
            {
                var arcObj = new GameObject("LightningArc_" + i);
                arcObj.transform.SetParent(transform, false);
                var lr = arcObj.AddComponent<LineRenderer>();
                lr.sharedMaterial = arcMaterial;
                lr.useWorldSpace = true;
                lr.startWidth = UnityEngine.Random.Range(16f, 32f);
                lr.endWidth = UnityEngine.Random.Range(2f, 6f);
                lr.startColor = new Color(0.9f, 0.98f, 1f, 1f);
                lr.endColor = new Color(0.2f, 0.6f, 1f, 0f);

                float azimuth = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float pitch = UnityEngine.Random.Range(-35f, 20f) * Mathf.Deg2Rad;
                float branchLength = UnityEngine.Random.Range(radius * 0.35f, radius * 0.95f);
                Vector3 direction = new Vector3(
                    Mathf.Cos(azimuth) * Mathf.Cos(pitch),
                    Mathf.Sin(pitch),
                    Mathf.Sin(azimuth) * Mathf.Cos(pitch));

                Vector3 target = origin + direction * branchLength;
                GenerateLightning(lr, origin, target, 24, branchLength * 0.08f);
                lightningArcs.Add(lr);
            }

            // 4. Custom atmospheric audio
            if (empAudioClip != null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.clip = empAudioClip;
                audioSource.spatialBlend = 0.35f; // broad stereo theater sound
                audioSource.minDistance = 500f;
                audioSource.maxDistance = 150000f;
                audioSource.volume = 1.0f;
                audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
                audioSource.Play();
            }

            StartCoroutine(Animate());
        }

        private IEnumerator Animate()
        {
            float elapsed = 0f;
            while (elapsed < shockwaveDuration)
            {
                elapsed = Time.time - startTime;
                float progress = Mathf.Clamp01(elapsed / shockwaveDuration);

                // Animate flash intensity (peaks instantly, exponential decay)
                if (burstLight != null)
                {
                    float lightFactor = Mathf.Pow(1f - Mathf.Clamp01(elapsed / 1.8f), 2.5f);
                    burstLight.intensity = 45f * lightFactor;
                    if (lightFactor <= 0.01f) burstLight.enabled = false;
                }

                // Animate plasma shockwave expansion
                float currentRadius = Mathf.Lerp(100f, maxRadius, Mathf.Sqrt(progress));
                float thickness = Mathf.Lerp(80f, 600f, progress);
                float alpha = Mathf.Pow(1f - progress, 1.5f);
                UpdateShockwaveMesh(currentRadius, thickness, alpha);

                // Animate lightning arcs flickering and decaying
                for (int i = 0; i < lightningArcs.Count; i++)
                {
                    LineRenderer lr = lightningArcs[i];
                    if (lr == null) continue;
                    float arcLife = Mathf.Clamp01(1f - (elapsed / (0.8f + (i % 3) * 0.3f)));
                    float flicker = UnityEngine.Random.value > 0.3f ? 1f : 0.2f;
                    Color c = new Color(0.5f, 0.85f, 1f, arcLife * flicker);
                    lr.startColor = c;
                    lr.endColor = new Color(0.1f, 0.4f, 0.9f, 0f);
                }

                yield return null;
            }

            // Cleanup
            Destroy(gameObject, 2.5f);
        }

        private void UpdateShockwaveMesh(float radius, float thickness, float alpha)
        {
            if (shockwaveMesh == null) return;

            const int segments = 64;
            Vector3[] vertices = new Vector3[(segments + 1) * 2];
            Color[] colors = new Color[vertices.Length];
            int[] triangles = new int[segments * 6];

            float innerR = Mathf.Max(0f, radius - thickness);
            float outerR = radius;

            Color innerColor = new Color(0.4f, 0.85f, 1f, alpha * 0.85f);
            Color outerColor = new Color(0.1f, 0.4f, 0.95f, 0f);

            for (int i = 0; i <= segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                float sin = Mathf.Sin(angle);
                float cos = Mathf.Cos(angle);

                int vInner = i * 2;
                int vOuter = i * 2 + 1;

                vertices[vInner] = new Vector3(sin * innerR, 0f, cos * innerR);
                vertices[vOuter] = new Vector3(sin * outerR, 0f, cos * outerR);

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

        private static void GenerateLightning(
            LineRenderer lr, Vector3 start, Vector3 end, int points, float jitterAmount)
        {
            Vector3[] positions = new Vector3[points];
            positions[0] = start;
            positions[points - 1] = end;

            Vector3 step = (end - start) / (points - 1);
            for (int i = 1; i < points - 1; i++)
            {
                float progress = i / (float)(points - 1);
                float envelope = Mathf.Sin(progress * Mathf.PI);
                Vector3 jitter = UnityEngine.Random.insideUnitSphere * jitterAmount * envelope;
                positions[i] = start + step * i + jitter;
            }

            lr.positionCount = points;
            lr.SetPositions(positions);
        }

        private static void EnsureMaterials()
        {
            if (ringMaterial != null && arcMaterial != null) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Color");

            if (ringMaterial == null)
            {
                ringMaterial = new Material(shader) { name = "EmpShockwaveMaterial" };
                ringMaterial.SetColor("_Color", new Color(0.5f, 0.9f, 1f, 1f));
                if (ringMaterial.HasProperty("_Surface")) ringMaterial.SetFloat("_Surface", 1f); // transparent
                if (ringMaterial.HasProperty("_Blend")) ringMaterial.SetFloat("_Blend", 1f); // additive
            }

            if (arcMaterial == null)
            {
                arcMaterial = new Material(shader) { name = "EmpArcMaterial" };
                arcMaterial.SetColor("_Color", new Color(0.85f, 0.95f, 1f, 1f));
            }
        }

        private static void EnsureAudio()
        {
            if (empAudioClip != null) return;

            // Synthesize 4.5 seconds of high-voltage arc discharge and deep sub-bass atmospheric rumble
            int length = (int)(SampleRate * 4.5f);
            float[] samples = new float[length];

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;

                // Sub-bass atmospheric boom (42 Hz dropping to 28 Hz)
                float bassFreq = Mathf.Lerp(42f, 26f, t / 4.5f);
                float bassEnvelope = Mathf.Pow(Mathf.Clamp01(1f - (t / 4.2f)), 1.8f);
                float bass = Mathf.Sin(2f * Mathf.PI * bassFreq * t) * bassEnvelope * 0.75f;

                // Second sub-harmonic
                float subBass = Mathf.Sin(2f * Mathf.PI * (bassFreq * 0.5f) * t) * bassEnvelope * 0.45f;

                // High-voltage electrical arc crackle / electrostatic sizzle
                float arcEnvelope = Mathf.Pow(Mathf.Clamp01(1f - (t / 1.5f)), 3f);
                float noise = (UnityEngine.Random.value * 2f - 1f) * arcEnvelope * 0.65f;

                // High-voltage descending buzz (120 Hz power-grid hum harmonics)
                float buzz = (Mathf.Sin(2f * Mathf.PI * 120f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 240f * t))
                    * arcEnvelope * 0.35f;

                samples[i] = Mathf.Clamp(bass + subBass + noise + buzz, -1f, 1f);
            }

            empAudioClip = AudioClip.Create("EmpShockSound", length, 1, SampleRate, false);
            empAudioClip.SetData(samples, 0);
        }

        private void OnDestroy()
        {
            if (shockwaveMesh != null) Destroy(shockwaveMesh);
        }
    }
}
