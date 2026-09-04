using System;
using System.Collections;
using System.Collections.Generic;
using NuclearOption.Effects;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Features.Support.Visuals
{
    /// <summary>
    /// Spectacular high-altitude electromagnetic pulse (EMP) burst visual, atmospheric,
    /// and acoustic effect.
    /// Simulates real-life high-altitude nuclear / flux-compression EMP phenomena:
    /// 1. Blinding prompt ionospheric flash illuminating clouds and terrain for tens of kilometers.
    /// 2. 3D volumetric expanding plasma ionization sphere and dual hypervelocity compression rings.
    /// 3. Native URP ground shockwave decal projector conforming across the terrain below.
    /// 4. Fractal 3D branching atmospheric lightning spiderwebs arcing across the upper sky.
    /// 5. Coronal ionization spark particle burst.
    /// 6. Multi-layered acoustics: supersonic dielectric snap, distance-delayed thunder via
    ///    ExplosionAudioManager, sub-bass atmospheric pulse, and descending inverter whine.
    /// </summary>
    internal sealed class EmpVisualEffect : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private static readonly List<(Vector3 pos, float time)> recentStrikes =
            new List<(Vector3 pos, float time)>();

        private static Material sphereMaterial;
        private static Material ringMaterial;
        private static Material arcMaterial;
        private static Material sparkMaterial;
        private static AudioClip empAudioClip;

        private static readonly int id_decalSize = Shader.PropertyToID("_DecalSize");
        private static readonly int id_opacity = Shader.PropertyToID("_Opacity");
        private static readonly int id_shockwaveExpansion = Shader.PropertyToID("_ShockwaveExpansion");

        public static void Trigger(Vector3 detonationPoint, float radiusMeters)
        {
            if (GameManager.IsHeadless) return;

            // Deduplication guard: suppress redundant triggers within 3.0 seconds and 2500m
            float now = Time.time;
            for (int i = recentStrikes.Count - 1; i >= 0; i--)
            {
                if (now - recentStrikes[i].time > 3.0f)
                {
                    recentStrikes.RemoveAt(i);
                }
                else if (Vector3.Distance(recentStrikes[i].pos, detonationPoint) < 2500f)
                {
                    return; // Duplicate trigger suppressed
                }
            }
            recentStrikes.Add((detonationPoint, now));

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

        // Equatorial & Tilted Compression Rings
        private GameObject ringObj;
        private MeshFilter ringFilter;
        private MeshRenderer ringRenderer;
        private Mesh ringMesh;

        private GameObject tiltedRingObj;
        private MeshFilter tiltedRingFilter;
        private MeshRenderer tiltedRingRenderer;
        private Mesh tiltedRingMesh;

        // Ground Decal Projector Shockwave
        private GameObject groundDecalObj;
        private DecalProjector decalProjector;
        private Material decalMaterial;
        private bool hasGroundDecal;
        private float groundRadius;
        private float groundShockwaveProgression;
        private float groundDecalOpacity = 1f;

        // Coronal ionization sparks
        private ParticleSystem sparkSystem;

        private readonly List<LineRenderer> lightningArcs = new List<LineRenderer>();
        private AudioSource audioSource;
        private float startTime;
        private const float ShockwaveDuration = 5.0f;

        private void Initialize(Vector3 point, float radius)
        {
            origin = point;
            maxRadius = radius;
            startTime = Time.time;

            NukeEffectAssets.EnsureResolved();
            EnsureMaterials();
            EnsureAudio();

            // 1. Blinding multi-phase atmospheric lighting (Prompt Compton Flash + Auroral Glow)
            var lightObj = new GameObject("BurstLight");
            lightObj.transform.SetParent(transform, false);
            burstLight = lightObj.AddComponent<Light>();
            burstLight.type = LightType.Point;
            burstLight.color = new Color(0.82f, 0.96f, 1.0f);
            burstLight.range = Mathf.Max(radius * 4f, 110000f);
            burstLight.intensity = 160f;
            burstLight.shadows = LightShadows.None;

            var auroraObj = new GameObject("AuroraLight");
            auroraObj.transform.SetParent(transform, false);
            auroraLight = auroraObj.AddComponent<Light>();
            auroraLight.type = LightType.Point;
            auroraLight.color = new Color(0.35f, 0.58f, 1.0f);
            auroraLight.range = Mathf.Max(radius * 2.8f, 75000f);
            auroraLight.intensity = 32f;
            auroraLight.shadows = LightShadows.None;

            // 2. 3D Volumetric Expanding Ionization Sphere (smoothed 32x48 mesh)
            sphereObj = new GameObject("IonizationSphere");
            sphereObj.transform.SetParent(transform, false);
            sphereFilter = sphereObj.AddComponent<MeshFilter>();
            sphereRenderer = sphereObj.AddComponent<MeshRenderer>();
            sphereRenderer.sharedMaterial = sphereMaterial;
            sphereMesh = BuildSphereMesh(32, 48);
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
            tiltedRingObj.transform.localRotation = Quaternion.Euler(32f, 42f, 18f);
            tiltedRingFilter = tiltedRingObj.AddComponent<MeshFilter>();
            tiltedRingRenderer = tiltedRingObj.AddComponent<MeshRenderer>();
            tiltedRingRenderer.sharedMaterial = ringMaterial;
            tiltedRingMesh = new Mesh { name = "EmpTiltedMesh" };
            tiltedRingFilter.sharedMesh = tiltedRingMesh;

            // 4. Ground Decal Projector Shockwave (conforming to terrain below)
            SetupGroundDecal(point, radius);

            // 5. Coronal ionization spark particle burst
            SetupSparkBurst();

            // 6. 3D Atmospheric Fractal Branching Lightning Arcs
            int arcCount = UnityEngine.Random.Range(24, 32);
            for (int i = 0; i < arcCount; i++)
            {
                CreateLightningBranch(i, origin, radius);
            }

            // 7. Multi-layered Acoustic Design
            SetupAcoustics();

            // 8. Camera Electromagnetic Shudder
            TriggerCameraShudder();

            StartCoroutine(Animate());
        }

        private void SetupGroundDecal(Vector3 point, float radius)
        {
            if (Physics.Raycast(point, Vector3.down, out var hit, 25000f, PhysicsLayers.StaticsMask))
            {
                hasGroundDecal = true;
                groundRadius = radius * 1.15f;
                groundShockwaveProgression = 20f;

                if (NukeEffectAssets.GroundDecalPrefab != null)
                {
                    groundDecalObj = Instantiate(NukeEffectAssets.GroundDecalPrefab, hit.point + Vector3.up * 3f, Quaternion.LookRotation(Vector3.down));
                    groundDecalObj.transform.SetParent(Datum.origin, true);
                    decalProjector = groundDecalObj.GetComponent<DecalProjector>()
                                  ?? groundDecalObj.GetComponentInChildren<DecalProjector>();
                }

                if (decalProjector == null && NukeEffectAssets.ShockwaveDecalMaterial != null)
                {
                    groundDecalObj = new GameObject("EmpGroundDecal");
                    groundDecalObj.transform.position = hit.point + Vector3.up * 3f;
                    groundDecalObj.transform.rotation = Quaternion.LookRotation(Vector3.down);
                    groundDecalObj.transform.SetParent(Datum.origin, true);
                    decalProjector = groundDecalObj.AddComponent<DecalProjector>();
                    decalProjector.material = NukeEffectAssets.ShockwaveDecalMaterial;
                }

                if (decalProjector != null)
                {
                    decalProjector.size = new Vector3(groundRadius * 2f, groundRadius * 2f, groundRadius * 2f);
                    decalMaterial = new Material(decalProjector.material);
                    decalProjector.material = decalMaterial;
                    decalMaterial.SetFloat(id_decalSize, groundRadius);
                    decalMaterial.SetFloat(id_opacity, 1.0f);
                }
            }
        }

        private void SetupSparkBurst()
        {
            var sparkObj = new GameObject("CoronalSparks");
            sparkObj.transform.SetParent(transform, false);
            sparkSystem = sparkObj.AddComponent<ParticleSystem>();
            var renderer = sparkObj.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = NukeEffectAssets.EjectaParticleMaterial
                                   ?? sparkMaterial;

            var main = sparkSystem.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.duration = 1.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(650f, 1350f);
            main.startSize = new ParticleSystem.MinMaxCurve(12f, 28f);
            main.gravityModifier = 0.05f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.85f, 0.96f, 1.0f, 1f),
                new Color(0.35f, 0.65f, 1.0f, 0.85f));
            main.maxParticles = 500;

            var emission = sparkSystem.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 320, 450) });

            var shape = sparkSystem.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 40f;
        }

        private void SetupAcoustics()
        {
            // 1. Supersonic dielectric ionization snap (plays immediately on arrival)
            if (GameAssets.i?.sonicBoom != null)
            {
                var snapObj = new GameObject("DielectricSnap");
                snapObj.transform.position = origin;
                snapObj.transform.SetParent(transform, false);
                var snapSrc = snapObj.AddComponent<AudioSource>();
                snapSrc.clip = GameAssets.i.sonicBoom;
                snapSrc.spatialBlend = 0.45f;
                snapSrc.minDistance = 800f;
                snapSrc.maxDistance = 100000f;
                snapSrc.volume = 1.0f;
                snapSrc.pitch = UnityEngine.Random.Range(1.15f, 1.25f); // High-voltage electrical snap
                snapSrc.rolloffMode = AudioRolloffMode.Logarithmic;
                snapSrc.Play();
            }

            // 2. Realistic distance-delayed heavy explosion rumble via ExplosionAudioManager
            if (NukeEffectAssets.NukeExplosionClip != null && SceneSingleton<ExplosionAudioManager>.i != null)
            {
                var boomObj = new GameObject("EmpAtmosphericBoom");
                boomObj.transform.position = origin;
                boomObj.transform.SetParent(transform, false);
                var boomSrc = boomObj.AddComponent<AudioSource>();
                boomSrc.clip = NukeEffectAssets.NukeExplosionClip;
                boomSrc.spatialBlend = 1.0f;
                boomSrc.minDistance = 800f;
                boomSrc.maxDistance = 120000f;
                var filter = boomObj.AddComponent<AudioLowPassFilter>();
                SceneSingleton<ExplosionAudioManager>.i.AddExplosionAudio(boomSrc, filter, 0.4f);
            }

            // 3. Sub-bass atmospheric pulse, arc crackle & descending inverter whine
            if (empAudioClip != null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.clip = empAudioClip;
                audioSource.spatialBlend = 0.35f;
                audioSource.minDistance = 800f;
                audioSource.maxDistance = 150000f;
                audioSource.volume = 1.0f;
                audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
                audioSource.Play();
            }
        }

        private void TriggerCameraShudder()
        {
            var csm = SceneSingleton<CameraStateManager>.i;
            Camera cam = csm?.mainCamera ?? Camera.main;
            if (cam == null) return;

            float camDist = Vector3.Distance(cam.transform.position, origin);
            if (camDist < maxRadius * 2.2f)
            {
                float factor = Mathf.Clamp01(1f - (camDist / (maxRadius * 2.2f)));
                if (csm != null) csm.ShakeCamera(1.1f * factor, 2.2f * factor);
            }
        }

        private void CreateLightningBranch(int index, Vector3 center, float radius)
        {
            var arcObj = new GameObject("LightningArc_" + index);
            arcObj.transform.SetParent(transform, false);
            var lr = arcObj.AddComponent<LineRenderer>();
            lr.sharedMaterial = arcMaterial;
            lr.useWorldSpace = true;
            lr.startWidth = UnityEngine.Random.Range(26f, 44f);
            lr.endWidth = UnityEngine.Random.Range(3f, 7f);
            lr.startColor = new Color(0.92f, 0.98f, 1f, 1f);
            lr.endColor = new Color(0.2f, 0.65f, 1f, 0f);

            float azimuth = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float pitch = UnityEngine.Random.Range(-55f, 50f) * Mathf.Deg2Rad;
            float branchLength = UnityEngine.Random.Range(radius * 0.45f, radius * 1.1f);
            Vector3 direction = new Vector3(
                Mathf.Cos(azimuth) * Mathf.Cos(pitch),
                Mathf.Sin(pitch),
                Mathf.Sin(azimuth) * Mathf.Cos(pitch));

            Vector3 primaryEnd = center + direction * branchLength;
            GenerateBranchingLightning(lr, center, primaryEnd, 28, branchLength * 0.08f);
            lightningArcs.Add(lr);

            // Add secondary forked branch for 50% of arcs
            if (UnityEngine.Random.value > 0.45f)
            {
                var subObj = new GameObject("LightningSubArc_" + index);
                subObj.transform.SetParent(transform, false);
                var subLr = subObj.AddComponent<LineRenderer>();
                subLr.sharedMaterial = arcMaterial;
                subLr.useWorldSpace = true;
                subLr.startWidth = lr.startWidth * 0.65f;
                subLr.endWidth = 1.8f;

                Vector3 midPoint = center + direction * (branchLength * 0.5f);
                Vector3 subDir = Quaternion.Euler(UnityEngine.Random.Range(-35f, 35f), UnityEngine.Random.Range(-35f, 35f), 0f) * direction;
                Vector3 subEnd = midPoint + subDir * (branchLength * 0.45f);
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

                // 1. Animate flash lighting
                if (burstLight != null)
                {
                    float coreFactor = Mathf.Pow(1f - Mathf.Clamp01(elapsed / 1.4f), 3.0f);
                    burstLight.intensity = 160f * coreFactor;
                    if (coreFactor <= 0.01f) burstLight.enabled = false;
                }

                if (auroraLight != null)
                {
                    float auroraFactor = Mathf.Pow(1f - Mathf.Clamp01(elapsed / 4.2f), 1.8f);
                    float pulse = 0.85f + Mathf.Sin(elapsed * 11f) * 0.15f;
                    auroraLight.intensity = 32f * auroraFactor * pulse;
                    if (auroraFactor <= 0.01f) auroraLight.enabled = false;
                }

                // 2. Animate 3D Volumetric Sphere expansion
                if (sphereObj != null)
                {
                    float sphereProgress = Mathf.Clamp01(elapsed / 2.6f);
                    float sphereScale = Mathf.Lerp(120f, maxRadius, Mathf.Sqrt(sphereProgress));
                    sphereObj.transform.localScale = Vector3.one * sphereScale;

                    float sphereAlpha = Mathf.Pow(1f - sphereProgress, 2.0f);
                    UpdateSphereColors(sphereMesh, sphereAlpha);
                    if (sphereProgress >= 1f && sphereObj.activeSelf) sphereObj.SetActive(false);
                }

                // 3. Animate plasma shockwave rings expansion
                float currentRadius = Mathf.Lerp(150f, maxRadius, Mathf.Sqrt(progress));
                float thickness = Mathf.Lerp(120f, 850f, progress);
                float alpha = Mathf.Pow(1f - progress, 1.5f);
                UpdateRingMesh(ringMesh, currentRadius, thickness, alpha, true);
                UpdateRingMesh(tiltedRingMesh, currentRadius * 0.88f, thickness * 0.85f, alpha * 0.75f, false);

                // 4. Animate ground shockwave decal expansion
                if (hasGroundDecal && decalMaterial != null)
                {
                    groundShockwaveProgression += 1350f * Time.deltaTime;
                    decalMaterial.SetFloat(id_shockwaveExpansion, (1f * groundRadius) / Mathf.Max(1f, groundShockwaveProgression));

                    if (groundShockwaveProgression > groundRadius)
                    {
                        groundDecalOpacity -= Time.deltaTime * 0.18f;
                        decalMaterial.SetFloat(id_opacity, Mathf.Max(0f, groundDecalOpacity));
                        if (groundDecalOpacity <= 0f && groundDecalObj != null)
                        {
                            Destroy(groundDecalObj);
                            groundDecalObj = null;
                        }
                    }
                }

                // 5. Animate lightning arcs flickering and decaying
                for (int i = 0; i < lightningArcs.Count; i++)
                {
                    LineRenderer lr = lightningArcs[i];
                    if (lr == null) continue;
                    float arcLife = Mathf.Clamp01(1f - (elapsed / (1.4f + (i % 4) * 0.35f)));
                    float flicker = UnityEngine.Random.value > 0.25f ? 1f : 0.1f;
                    Color c = new Color(0.6f, 0.9f, 1f, arcLife * flicker);
                    lr.startColor = c;
                    lr.endColor = new Color(0.15f, 0.45f, 0.95f, 0f);
                }

                yield return null;
            }

            if (groundDecalObj != null) Destroy(groundDecalObj);
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
            if (sphereMaterial != null && ringMaterial != null && arcMaterial != null && sparkMaterial != null) return;

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

            if (sparkMaterial == null)
            {
                sparkMaterial = new Material(shader) { name = "EmpSparkMat" };
                sparkMaterial.SetColor("_Color", new Color(0.7f, 0.95f, 1f, 1f));
                if (sparkMaterial.HasProperty("_Surface")) sparkMaterial.SetFloat("_Surface", 1f);
                if (sparkMaterial.HasProperty("_Blend")) sparkMaterial.SetFloat("_Blend", 1f);
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
            if (decalMaterial != null) Destroy(decalMaterial);
            if (groundDecalObj != null) Destroy(groundDecalObj);
        }
    }
}
