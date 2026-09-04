using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Visuals
{
    /// <summary>
    /// Spectacular high-altitude electromagnetic pulse (EMP) burst visual, atmospheric,
    /// and acoustic effect.
    /// Simulates real-life high-altitude nuclear / flux-compression EMP phenomena:
    /// 1. Blinding ionospheric flash illuminating clouds and terrain for tens of kilometers.
    /// 2. 3D volumetric expanding plasma ionization sphere and dual hypervelocity compression rings.
    /// 3. Fractal 3D branching atmospheric lightning spiderwebs arcing across the upper sky.
    /// 4. Thunderous sub-bass atmospheric surge, high-voltage arc crackle, and power-grid whine.
    /// </summary>
    internal sealed class EmpVisualEffect : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private static Material sphereMaterial;
        private static Material ringMaterial;
        private static Material arcMaterial;
        private static AudioClip empAudioClip;

        public static void Trigger(Vector3 detonationPoint, float radiusMeters)
        {
            if (GameManager.IsHeadless) return;

            var go = new GameObject("BoscaliSummer.EmpVisualEffect");
            go.transform.position = detonationPoint;
            go.transform.SetParent(Datum.origin, true);
            var effect = go.AddComponent<EmpVisualEffect>();
            effect.Initialize(detonationPoint, radiusMeters);
        }

        private Vector3 origin;
        private float maxRadius;
        private Light burstLight;
        private Light auroraLight;

        // 3D Volumetric Ionization Sphere
        private GameObject sphereObj;
        private MeshFilter sphereFilter;
        private MeshRenderer sphereRenderer;
        private Mesh sphereMesh;

        // Primary & Tilted Compression Rings
        private GameObject ringObj;
        private MeshFilter ringFilter;
        private MeshRenderer ringRenderer;
        private Mesh ringMesh;

        private GameObject tiltedRingObj;
        private MeshFilter tiltedRingFilter;
        private MeshRenderer tiltedRingRenderer;
        private Mesh tiltedRingMesh;

        private readonly List<LineRenderer> lightningArcs = new List<LineRenderer>();
        private AudioSource audioSource;
        private float startTime;
        private const float ShockwaveDuration = 4.2f;

        private void Initialize(Vector3 point, float radius)
        {
            origin = point;
            maxRadius = radius;
            startTime = Time.time;

            EnsureMaterials();
            EnsureAudio();

            // 1. Blinding multi-phase atmospheric lighting
            var lightObj = new GameObject("BurstLight");
            lightObj.transform.SetParent(transform, false);
            burstLight = lightObj.AddComponent<Light>();
            burstLight.type = LightType.Point;
            burstLight.color = new Color(0.7f, 0.92f, 1.0f);
            burstLight.range = Mathf.Max(radius * 3.5f, 85000f);
            burstLight.intensity = 75f;
            burstLight.shadows = LightShadows.None;

            var auroraObj = new GameObject("AuroraLight");
            auroraObj.transform.SetParent(transform, false);
            auroraLight = auroraObj.AddComponent<Light>();
            auroraLight.type = LightType.Point;
            auroraLight.color = new Color(0.4f, 0.6f, 1.0f);
            auroraLight.range = Mathf.Max(radius * 2.5f, 60000f);
            auroraLight.intensity = 24f;
            auroraLight.shadows = LightShadows.None;

            // 2. 3D Volumetric Expanding Ionization Sphere
            sphereObj = new GameObject("IonizationSphere");
            sphereObj.transform.SetParent(transform, false);
            sphereFilter = sphereObj.AddComponent<MeshFilter>();
            sphereRenderer = sphereObj.AddComponent<MeshRenderer>();
            sphereRenderer.sharedMaterial = sphereMaterial;
            sphereMesh = BuildSphereMesh(16, 24);
            sphereFilter.sharedMesh = sphereMesh;

            // 3. Equatorial & Tilted Plasma Shockwave Rings
            ringObj = new GameObject("EquatorialShockwave");
            ringObj.transform.SetParent(transform, false);
            ringFilter = ringObj.AddComponent<MeshFilter>();
            ringRenderer = ringObj.AddComponent<MeshRenderer>();
            ringRenderer.sharedMaterial = ringMaterial;
            ringMesh = new Mesh { name = "EmpEquatorialMesh" };
            ringFilter.sharedMesh = ringMesh;

            tiltedRingObj = new GameObject("TiltedShockwave");
            tiltedRingObj.transform.SetParent(transform, false);
            tiltedRingObj.transform.localRotation = Quaternion.Euler(28f, 45f, 15f);
            tiltedRingFilter = tiltedRingObj.AddComponent<MeshFilter>();
            tiltedRingRenderer = tiltedRingObj.AddComponent<MeshRenderer>();
            tiltedRingRenderer.sharedMaterial = ringMaterial;
            tiltedRingMesh = new Mesh { name = "EmpTiltedMesh" };
            tiltedRingFilter.sharedMesh = tiltedRingMesh;

            // 4. 3D Atmospheric Fractal Branching Lightning Arcs
            int arcCount = UnityEngine.Random.Range(16, 22);
            for (int i = 0; i < arcCount; i++)
            {
                CreateLightningBranch(i, origin, radius);
            }

            // 5. Atmospheric Audio
            if (empAudioClip != null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.clip = empAudioClip;
                audioSource.spatialBlend = 0.35f;
                audioSource.minDistance = 600f;
                audioSource.maxDistance = 150000f;
                audioSource.volume = 1.0f;
                audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
                audioSource.Play();
            }

            // 6. Camera Electromagnetic Shudder for nearby players
            var csm = SceneSingleton<CameraStateManager>.i;
            Camera cam = csm?.mainCamera ?? Camera.main;
            if (cam != null)
            {
                float camDist = Vector3.Distance(cam.transform.position, origin);
                if (camDist < radius * 1.8f)
                {
                    float factor = Mathf.Clamp01(1f - (camDist / (radius * 1.8f)));
                    if (csm != null) csm.ShakeCamera(0.8f * factor, 1.6f * factor);
                }
            }

            StartCoroutine(Animate());
        }

        private void CreateLightningBranch(int index, Vector3 center, float radius)
        {
            var arcObj = new GameObject("LightningArc_" + index);
            arcObj.transform.SetParent(transform, false);
            var lr = arcObj.AddComponent<LineRenderer>();
            lr.sharedMaterial = arcMaterial;
            lr.useWorldSpace = true;
            lr.startWidth = UnityEngine.Random.Range(22f, 38f);
            lr.endWidth = UnityEngine.Random.Range(2f, 6f);
            lr.startColor = new Color(0.92f, 0.98f, 1f, 1f);
            lr.endColor = new Color(0.2f, 0.65f, 1f, 0f);

            float azimuth = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float pitch = UnityEngine.Random.Range(-45f, 35f) * Mathf.Deg2Rad;
            float branchLength = UnityEngine.Random.Range(radius * 0.45f, radius * 1.05f);
            Vector3 direction = new Vector3(
                Mathf.Cos(azimuth) * Mathf.Cos(pitch),
                Mathf.Sin(pitch),
                Mathf.Sin(azimuth) * Mathf.Cos(pitch));

            Vector3 primaryEnd = center + direction * branchLength;
            GenerateBranchingLightning(lr, center, primaryEnd, 28, branchLength * 0.09f);
            lightningArcs.Add(lr);

            // Add secondary forked branch for 50% of arcs
            if (UnityEngine.Random.value > 0.45f)
            {
                var subObj = new GameObject("LightningSubArc_" + index);
                subObj.transform.SetParent(transform, false);
                var subLr = subObj.AddComponent<LineRenderer>();
                subLr.sharedMaterial = arcMaterial;
                subLr.useWorldSpace = true;
                subLr.startWidth = lr.startWidth * 0.6f;
                subLr.endWidth = 1.5f;

                Vector3 midPoint = center + direction * (branchLength * 0.5f);
                Vector3 subDir = Quaternion.Euler(UnityEngine.Random.Range(-30f, 30f), UnityEngine.Random.Range(-30f, 30f), 0f) * direction;
                Vector3 subEnd = midPoint + subDir * (branchLength * 0.4f);
                GenerateBranchingLightning(subLr, midPoint, subEnd, 16, branchLength * 0.06f);
                lightningArcs.Add(subLr);
            }
        }

        private IEnumerator Animate()
        {
            float elapsed = 0f;
            while (elapsed < ShockwaveDuration)
            {
                elapsed = Time.time - startTime;
                float progress = Mathf.Clamp01(elapsed / ShockwaveDuration);

                // Animate flash lighting
                if (burstLight != null)
                {
                    float coreFactor = Mathf.Pow(1f - Mathf.Clamp01(elapsed / 1.6f), 3.0f);
                    burstLight.intensity = 75f * coreFactor;
                    if (coreFactor <= 0.01f) burstLight.enabled = false;
                }

                if (auroraLight != null)
                {
                    float auroraFactor = Mathf.Pow(1f - Mathf.Clamp01(elapsed / 3.8f), 1.8f);
                    float pulse = 0.85f + Mathf.Sin(elapsed * 12f) * 0.15f;
                    auroraLight.intensity = 24f * auroraFactor * pulse;
                    if (auroraFactor <= 0.01f) auroraLight.enabled = false;
                }

                // Animate 3D Volumetric Sphere expansion
                if (sphereObj != null)
                {
                    float sphereProgress = Mathf.Clamp01(elapsed / 2.2f);
                    float sphereScale = Mathf.Lerp(80f, maxRadius, Mathf.Sqrt(sphereProgress));
                    sphereObj.transform.localScale = Vector3.one * sphereScale;

                    float sphereAlpha = Mathf.Pow(1f - sphereProgress, 2.0f);
                    UpdateSphereColors(sphereMesh, sphereAlpha);
                    if (sphereProgress >= 1f && sphereObj.activeSelf) sphereObj.SetActive(false);
                }

                // Animate plasma shockwave expansion
                float currentRadius = Mathf.Lerp(120f, maxRadius, Mathf.Sqrt(progress));
                float thickness = Mathf.Lerp(100f, 750f, progress);
                float alpha = Mathf.Pow(1f - progress, 1.5f);
                UpdateRingMesh(ringMesh, currentRadius, thickness, alpha, true);
                UpdateRingMesh(tiltedRingMesh, currentRadius * 0.88f, thickness * 0.85f, alpha * 0.75f, false);

                // Animate lightning arcs flickering and decaying
                for (int i = 0; i < lightningArcs.Count; i++)
                {
                    LineRenderer lr = lightningArcs[i];
                    if (lr == null) continue;
                    float arcLife = Mathf.Clamp01(1f - (elapsed / (1.2f + (i % 4) * 0.35f)));
                    float flicker = UnityEngine.Random.value > 0.28f ? 1f : 0.15f;
                    Color c = new Color(0.55f, 0.88f, 1f, arcLife * flicker);
                    lr.startColor = c;
                    lr.endColor = new Color(0.1f, 0.45f, 0.95f, 0f);
                }

                yield return null;
            }

            Destroy(gameObject, 2.0f);
        }

        private static Mesh BuildSphereMesh(int latitudeSegments, int longitudeSegments)
        {
            var mesh = new Mesh { name = "EmpIonizationSphereMesh" };
            int vertexCount = (latitudeSegments + 1) * (longitudeSegments + 1);
            Vector3[] vertices = new Vector3[vertexCount];
            Color[] colors = new Color[vertexCount];
            int[] triangles = new int[latitudeSegments * longitudeSegments * 6];

            int v = 0;
            for (int lat = 0; lat <= latitudeSegments; lat++)
            {
                float vNorm = lat / (float)latitudeSegments;
                float pitch = (vNorm - 0.5f) * Mathf.PI;
                float sinPitch = Mathf.Sin(pitch);
                float cosPitch = Mathf.Cos(pitch);

                for (int lon = 0; lon <= longitudeSegments; lon++)
                {
                    float uNorm = lon / (float)longitudeSegments;
                    float yaw = uNorm * Mathf.PI * 2f;
                    float sinYaw = Mathf.Sin(yaw);
                    float cosYaw = Mathf.Cos(yaw);

                    vertices[v] = new Vector3(cosPitch * sinYaw, sinPitch, cosPitch * cosYaw);
                    colors[v] = new Color(0.4f, 0.85f, 1f, 0.5f);
                    v++;
                }
            }

            int t = 0;
            for (int lat = 0; lat < latitudeSegments; lat++)
            {
                for (int lon = 0; lon < longitudeSegments; lon++)
                {
                    int current = lat * (longitudeSegments + 1) + lon;
                    int next = current + longitudeSegments + 1;

                    triangles[t++] = current;
                    triangles[t++] = next;
                    triangles[t++] = current + 1;

                    triangles[t++] = current + 1;
                    triangles[t++] = next;
                    triangles[t++] = next + 1;
                }
            }

            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void UpdateSphereColors(Mesh mesh, float alpha)
        {
            if (mesh == null) return;
            Color[] colors = mesh.colors;
            Color c = new Color(0.45f, 0.85f, 1f, alpha * 0.45f);
            for (int i = 0; i < colors.Length; i++) colors[i] = c;
            mesh.colors = colors;
        }

        private static void UpdateRingMesh(Mesh mesh, float radius, float thickness, float alpha, bool equatorial)
        {
            if (mesh == null) return;

            const int segments = 64;
            Vector3[] vertices = new Vector3[(segments + 1) * 2];
            Color[] colors = new Color[vertices.Length];
            int[] triangles = new int[segments * 6];

            float innerR = Mathf.Max(0f, radius - thickness);
            float outerR = radius;

            Color innerColor = equatorial
                ? new Color(0.5f, 0.9f, 1f, alpha * 0.9f)
                : new Color(0.6f, 0.4f, 1f, alpha * 0.75f);
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
                    int tIdx = i * 6;
                    triangles[tIdx] = vInner;
                    triangles[tIdx + 1] = vOuter;
                    triangles[tIdx + 2] = vInner + 2;

                    triangles[tIdx + 3] = vInner + 2;
                    triangles[tIdx + 4] = vOuter;
                    triangles[tIdx + 5] = vOuter + 2;
                }
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }

        private static void GenerateBranchingLightning(
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
            if (sphereMaterial != null && ringMaterial != null && arcMaterial != null) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Color");

            if (sphereMaterial == null)
            {
                sphereMaterial = new Material(shader) { name = "EmpIonizationSphereMat" };
                sphereMaterial.SetColor("_Color", new Color(0.4f, 0.85f, 1f, 1f));
                if (sphereMaterial.HasProperty("_Surface")) sphereMaterial.SetFloat("_Surface", 1f);
                if (sphereMaterial.HasProperty("_Blend")) sphereMaterial.SetFloat("_Blend", 1f);
            }

            if (ringMaterial == null)
            {
                ringMaterial = new Material(shader) { name = "EmpShockwaveMaterial" };
                ringMaterial.SetColor("_Color", new Color(0.5f, 0.9f, 1f, 1f));
                if (ringMaterial.HasProperty("_Surface")) ringMaterial.SetFloat("_Surface", 1f);
                if (ringMaterial.HasProperty("_Blend")) ringMaterial.SetFloat("_Blend", 1f);
            }

            if (arcMaterial == null)
            {
                arcMaterial = new Material(shader) { name = "EmpArcMaterial" };
                arcMaterial.SetColor("_Color", new Color(0.85f, 0.95f, 1f, 1f));
                if (arcMaterial.HasProperty("_Surface")) arcMaterial.SetFloat("_Surface", 1f);
                if (arcMaterial.HasProperty("_Blend")) arcMaterial.SetFloat("_Blend", 1f);
            }
        }

        private static void EnsureAudio()
        {
            if (empAudioClip != null) return;

            // Synthesize 5.5 seconds of high-fidelity cinematic EMP sound:
            // supersonic dielectric snap + massive 24Hz sub-bass surge + high-voltage arc crackle + descending inverter whine
            int length = (int)(SampleRate * 5.5f);
            float[] samples = new float[length];

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;

                // 1. Supersonic dielectric ionization snap (0.0s - 0.05s)
                float snapEnvelope = Mathf.Exp(-t * 40f);
                float snap = (UnityEngine.Random.value * 2f - 1f) * snapEnvelope * 0.95f;

                // 2. Sub-bass atmospheric pulse surge (34 Hz dropping to 18 Hz)
                float bassFreq = Mathf.Lerp(34f, 18f, t / 5.5f);
                float bassEnvelope = Mathf.Pow(Mathf.Clamp01(1f - (t / 5.2f)), 1.6f);
                float bass = Mathf.Sin(2f * Mathf.PI * bassFreq * t) * bassEnvelope * 0.85f;
                float subBass = Mathf.Sin(2f * Mathf.PI * (bassFreq * 0.5f) * t) * bassEnvelope * 0.5f;

                // 3. High-voltage arc discharge & electrostatic sizzle
                float arcEnvelope = Mathf.Pow(Mathf.Clamp01(1f - (t / 2.2f)), 2.5f);
                float noise = (UnityEngine.Random.value * 2f - 1f) * arcEnvelope * 0.6f;

                // 4. Power-grid descending inverter whine (sweeping down from 400Hz to 60Hz)
                float whineFreq = Mathf.Lerp(400f, 60f, Mathf.Clamp01(t / 2.5f));
                float whine = Mathf.Sin(2f * Mathf.PI * whineFreq * t) * arcEnvelope * 0.4f;

                // 5. Cavernous thunder reverberation tail
                float echo = Mathf.Sin(2f * Mathf.PI * 48f * t) * Mathf.Pow(Mathf.Clamp01(1f - (t / 5.5f)), 2.0f) * 0.3f;

                samples[i] = Mathf.Clamp(snap + bass + subBass + noise + whine + echo, -1f, 1f);
            }

            empAudioClip = AudioClip.Create("EmpShockSound", length, 1, SampleRate, false);
            empAudioClip.SetData(samples, 0);
        }

        private void OnDestroy()
        {
            if (sphereMesh != null) Destroy(sphereMesh);
            if (ringMesh != null) Destroy(ringMesh);
            if (tiltedRingMesh != null) Destroy(tiltedRingMesh);
        }
    }
}
