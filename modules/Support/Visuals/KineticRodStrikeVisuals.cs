using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Effects;
using NuclearOption.Networking;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Features.Support.Visuals
{
    /// <summary>
    /// Delivers the cinematic visual, lighting, atmospheric, and acoustic effects for the
    /// "Rod from God" orbital kinetic strike during both descent and ground impact phases.
    /// Incorporates authentic hypersonic tungsten rod aerodynamics (incandescent bow shock,
    /// flame sheath, massive columnar contrail) and mini-nuke ground impact physics
    /// (building demolition, lethal overpressure shockwave, vertical ejecta geyser).
    /// </summary>
    internal static class KineticRodStrikeVisuals
    {
        private static readonly List<(Vector3 pos, float time)> recentStrikes =
            new List<(Vector3 pos, float time)>();

        public static void Track(Missile missile, Vector3 target)
        {
            if (missile == null || GameManager.IsHeadless) return;
            if (missile.GetComponent<KineticRodDescentEffect>() != null) return;

            var descent = missile.gameObject.AddComponent<KineticRodDescentEffect>();
            descent.Initialize(target);
        }

        public static void TriggerImpact(Vector3 impactPosition, PersistentID ownerID = default)
        {
            if (GameManager.IsHeadless) return;

            // Deduplication guard: ignore redundant triggers within 2.0 seconds and 350m
            float now = Time.time;
            for (int i = recentStrikes.Count - 1; i >= 0; i--)
            {
                if (now - recentStrikes[i].time > 3.0f)
                {
                    recentStrikes.RemoveAt(i);
                }
                else if (Vector3.Distance(recentStrikes[i].pos, impactPosition) < 350f)
                {
                    return; // Duplicate trigger suppressed
                }
            }
            recentStrikes.Add((impactPosition, now));

            KineticRodImpactEffect.Spawn(impactPosition, ownerID);
        }
    }

    /// <summary>
    /// Reverse-engineers and caches game-native shockwave decals, vapor clouds, particle materials,
    /// and audio clips from Nuclear Option's smallest tactical nuclear weapon (nuclearBomb1).
    /// </summary>
    internal static class NukeEffectAssets
    {
        private static bool resolved;
        public static GameObject GroundDecalPrefab { get; private set; }
        public static Material ShockwaveDecalMaterial { get; private set; }
        public static GameObject VaporCloudPrefab { get; private set; }
        public static Material VaporCloudMaterial { get; private set; }
        public static Mesh VaporCloudMesh { get; private set; }
        public static AnimationCurve VaporCloudAlphaCurve { get; private set; }
        public static float VaporCloudDetailScale { get; private set; } = 30f;
        public static AudioClip NukeExplosionClip { get; private set; }
        public static Material SmokeParticleMaterial { get; private set; }
        public static Material EjectaParticleMaterial { get; private set; }

        public static void EnsureResolved()
        {
            if (resolved) return;
            resolved = true;

            try
            {
                if (Encyclopedia.i == null || Encyclopedia.i.missiles == null) return;

                MissileDefinition tacticalNukeDef = null;
                float lowestYield = float.MaxValue;

                // Find the smallest nuclear warhead in the game (yield > 200f)
                for (int i = 0; i < Encyclopedia.i.missiles.Count; i++)
                {
                    MissileDefinition def = Encyclopedia.i.missiles[i];
                    if (def == null || def.unitPrefab == null) continue;

                    Missile missile = def.unitPrefab.GetComponent<Missile>();
                    if (missile == null) continue;

                    float yield = missile.GetYield();
                    if (yield > 200f && yield < lowestYield)
                    {
                        lowestYield = yield;
                        tacticalNukeDef = def;
                    }
                }

                if (tacticalNukeDef == null || tacticalNukeDef.unitPrefab == null) return;

                Missile nukeMissile = tacticalNukeDef.unitPrefab.GetComponent<Missile>();
                if (nukeMissile == null) return;

                // Extract warhead from missile
                FieldInfo warheadField = AccessTools.Field(typeof(Missile), "warhead");
                object warhead = warheadField?.GetValue(nukeMissile);
                if (warhead == null) return;

                FieldInfo terrainEffectField = AccessTools.Field(warhead.GetType(), "terrainEffect");
                GameObject terrainPrefab = terrainEffectField?.GetValue(warhead) as GameObject;
                if (terrainPrefab == null)
                {
                    FieldInfo airEffectField = AccessTools.Field(warhead.GetType(), "airEffect");
                    terrainPrefab = airEffectField?.GetValue(warhead) as GameObject;
                }

                if (terrainPrefab != null)
                {
                    // 1. Extract Shockwave component: ground decal projector and vapor cloud
                    Shockwave shockwave = terrainPrefab.GetComponentInChildren<Shockwave>(true);
                    if (shockwave != null)
                    {
                        FieldInfo groundDecalField = AccessTools.Field(typeof(Shockwave), "groundDecal");
                        GameObject groundDecalObj = groundDecalField?.GetValue(shockwave) as GameObject;
                        if (groundDecalObj != null)
                        {
                            GroundDecalPrefab = groundDecalObj;
                            DecalProjector proj = groundDecalObj.GetComponent<DecalProjector>()
                                               ?? groundDecalObj.GetComponentInChildren<DecalProjector>(true);
                            if (proj != null && proj.material != null)
                            {
                                ShockwaveDecalMaterial = proj.material;
                            }
                        }

                        FieldInfo vaporCloudField = AccessTools.Field(typeof(Shockwave), "vaporCloud");
                        GameObject vaporCloudObj = vaporCloudField?.GetValue(shockwave) as GameObject;
                        if (vaporCloudObj != null)
                        {
                            VaporCloudPrefab = vaporCloudObj;
                            Renderer rend = vaporCloudObj.GetComponent<Renderer>();
                            if (rend != null && rend.sharedMaterial != null)
                            {
                                VaporCloudMaterial = rend.sharedMaterial;
                            }
                            MeshFilter mf = vaporCloudObj.GetComponent<MeshFilter>();
                            if (mf != null && mf.sharedMesh != null)
                            {
                                VaporCloudMesh = mf.sharedMesh;
                            }
                        }

                        FieldInfo alphaCurveField = AccessTools.Field(typeof(Shockwave), "vaporCloudAlpha");
                        if (alphaCurveField?.GetValue(shockwave) is AnimationCurve curve)
                        {
                            VaporCloudAlphaCurve = curve;
                        }

                        FieldInfo detailScaleField = AccessTools.Field(typeof(Shockwave), "vaporCloudDetailScale");
                        if (detailScaleField?.GetValue(shockwave) is float scale && scale > 0f)
                        {
                            VaporCloudDetailScale = scale;
                        }
                    }

                    // 2. Extract explosion audio
                    ExplosionAudio expAudio = terrainPrefab.GetComponentInChildren<ExplosionAudio>(true);
                    if (expAudio != null)
                    {
                        FieldInfo soundsField = AccessTools.Field(typeof(ExplosionAudio), "explosionSounds");
                        Array sounds = soundsField?.GetValue(expAudio) as Array;
                        if (sounds != null && sounds.Length > 0)
                        {
                            object firstSound = sounds.GetValue(0);
                            if (firstSound != null)
                            {
                                FieldInfo clipsField = AccessTools.Field(firstSound.GetType(), "clips");
                                AudioClip[] clips = clipsField?.GetValue(firstSound) as AudioClip[];
                                if (clips != null && clips.Length > 0)
                                {
                                    NukeExplosionClip = clips[0];
                                }
                            }
                        }
                    }

                    if (NukeExplosionClip == null)
                    {
                        AudioSource src = terrainPrefab.GetComponentInChildren<AudioSource>(true);
                        if (src != null && src.clip != null)
                        {
                            NukeExplosionClip = src.clip;
                        }
                    }

                    // 3. Extract particle materials
                    ParticleSystemRenderer[] psRenderers = terrainPrefab.GetComponentsInChildren<ParticleSystemRenderer>(true);
                    foreach (ParticleSystemRenderer psr in psRenderers)
                    {
                        if (psr.sharedMaterial == null) continue;
                        string mName = psr.sharedMaterial.name;
                        if (SmokeParticleMaterial == null && (mName.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0 || mName.IndexOf("dust", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            SmokeParticleMaterial = psr.sharedMaterial;
                        }
                        if (EjectaParticleMaterial == null && (mName.IndexOf("fire", StringComparison.OrdinalIgnoreCase) >= 0 || mName.IndexOf("blast", StringComparison.OrdinalIgnoreCase) >= 0 || mName.IndexOf("ejecta", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            EjectaParticleMaterial = psr.sharedMaterial;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Fallbacks are handled gracefully by consumers
            }
        }
    }

    /// <summary>
    /// Procedural mesh and material generator for the authentic heavy tungsten penetrator rod.
    /// </summary>
    internal static class RodModelAssets
    {
        private static Mesh rodMesh;
        private static Material rodMaterial;

        public static Mesh TungstenRodMesh
        {
            get
            {
                if (rodMesh == null) rodMesh = BuildRodMesh();
                return rodMesh;
            }
        }

        public static Material TungstenRodMaterial
        {
            get
            {
                if (rodMaterial == null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                                 ?? Shader.Find("Standard")
                                 ?? Shader.Find("Universal Render Pipeline/Unlit")
                                 ?? Shader.Find("Unlit/Color");
                    rodMaterial = new Material(shader) { name = "TungstenCarbideMat" };
                    rodMaterial.SetColor("_Color", new Color(0.18f, 0.19f, 0.22f, 1f));
                    if (rodMaterial.HasProperty("_Metallic")) rodMaterial.SetFloat("_Metallic", 0.88f);
                    if (rodMaterial.HasProperty("_Smoothness")) rodMaterial.SetFloat("_Smoothness", 0.65f);
                }
                return rodMaterial;
            }
        }

        private static Mesh BuildRodMesh()
        {
            var mesh = new Mesh { name = "ProceduralTungstenRod" };
            const int segments = 24;
            const int rings = 6;

            // Profile along Z axis (0=nose, 6=tail)
            float[] zOffsets = { 3.4f, 3.1f, 2.7f, 0.0f, -2.7f, -3.1f };
            float[] radii = { 0.04f, 0.22f, 0.35f, 0.35f, 0.35f, 0.26f };

            int vertCount = rings * segments + 2; // + nose tip, + tail cap
            Vector3[] vertices = new Vector3[vertCount];
            Vector3[] normals = new Vector3[vertCount];
            Vector2[] uvs = new Vector2[vertCount];

            // Nose tip vertex
            int noseIdx = 0;
            vertices[noseIdx] = new Vector3(0f, 0f, 3.6f);
            normals[noseIdx] = Vector3.forward;
            uvs[noseIdx] = new Vector2(0.5f, 1f);

            int v = 1;
            for (int r = 0; r < rings; r++)
            {
                float z = zOffsets[r];
                float radius = radii[r];
                float vCoord = r / (float)(rings - 1);

                for (int s = 0; s < segments; s++)
                {
                    float angle = (s / (float)segments) * Mathf.PI * 2f;
                    float x = Mathf.Cos(angle) * radius;
                    float y = Mathf.Sin(angle) * radius;

                    vertices[v] = new Vector3(x, y, z);
                    normals[v] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), (r == 0 || r == 1) ? 0.4f : 0f).normalized;
                    uvs[v] = new Vector2(s / (float)segments, vCoord);
                    v++;
                }
            }

            // Tail cap vertex
            int tailIdx = v;
            vertices[tailIdx] = new Vector3(0f, 0f, -3.2f);
            normals[tailIdx] = Vector3.back;
            uvs[tailIdx] = new Vector2(0.5f, 0f);

            // Build triangles
            int triCount = (segments * (rings - 1) * 2 + segments * 2) * 3;
            int[] triangles = new int[triCount];
            int t = 0;

            // Nose cone fan
            for (int s = 0; s < segments; s++)
            {
                int next = (s + 1) % segments;
                triangles[t++] = noseIdx;
                triangles[t++] = 1 + s;
                triangles[t++] = 1 + next;
            }

            // Cylinder body quads
            for (int r = 0; r < rings - 1; r++)
            {
                int ringStart = 1 + r * segments;
                int nextRingStart = 1 + (r + 1) * segments;

                for (int s = 0; s < segments; s++)
                {
                    int next = (s + 1) % segments;
                    int v0 = ringStart + s;
                    int v1 = ringStart + next;
                    int v2 = nextRingStart + s;
                    int v3 = nextRingStart + next;

                    triangles[t++] = v0;
                    triangles[t++] = v2;
                    triangles[t++] = v1;

                    triangles[t++] = v1;
                    triangles[t++] = v2;
                    triangles[t++] = v3;
                }
            }

            // Tail cap fan
            int lastRingStart = 1 + (rings - 1) * segments;
            for (int s = 0; s < segments; s++)
            {
                int next = (s + 1) % segments;
                triangles[t++] = tailIdx;
                triangles[t++] = lastRingStart + next;
                triangles[t++] = lastRingStart + s;
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>
    /// Attached to the plunging kinetic rod missile during atmospheric hypersonic descent.
    /// Perfectly recreates Photo 2:
    /// - Sleek physical tungsten cylinder projectile (stock missile hidden).
    /// - Searing incandescent white-hot nose cone & bow shock plasma sheath.
    /// - Aerodynamic tongues of flame licking backwards along the cylinder flanks.
    /// - Hypersonic spark shedding / ionization spall.
    /// - Massive billowing columnar contrail & condensation trail stretching miles into the sky.
    /// - Screaming Mach-8 hypersonic acoustic tear.
    /// </summary>
    internal sealed class KineticRodDescentEffect : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private static Material flameMaterial;
        private static Material sparkMaterial;
        private static Material contrailMaterial;
        private static AudioClip hypersonicSoundClip;

        private Missile missile;
        private Vector3 targetPosition;
        private Light headLight;
        private GameObject rodMeshObj;
        private ParticleSystem flameSheath;
        private ParticleSystem sparkSystem;
        private TrailRenderer plasmaTrail;
        private TrailRenderer contrailTrail;
        private AudioSource audioSource;
        private Vector3 lastPosition;
        private bool hasDetonated;

        public void MarkDetonated()
        {
            hasDetonated = true;
        }

        public void Initialize(Vector3 target)
        {
            targetPosition = target;
            missile = GetComponent<Missile>();
            lastPosition = transform.position;

            NukeEffectAssets.EnsureResolved();
            EnsureAssets();

            // 1. Hide stock missile renderers so the rod looks authentic
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = false;
            }

            // 2. Attach physical heavy tungsten carbide penetrator cylinder
            rodMeshObj = new GameObject("TungstenRodMesh");
            rodMeshObj.transform.SetParent(transform, false);
            rodMeshObj.transform.localPosition = Vector3.zero;
            // Point forward along missile flight path
            rodMeshObj.transform.localRotation = Quaternion.identity;
            var mf = rodMeshObj.AddComponent<MeshFilter>();
            mf.sharedMesh = RodModelAssets.TungstenRodMesh;
            var mr = rodMeshObj.AddComponent<MeshRenderer>();
            mr.sharedMaterial = RodModelAssets.TungstenRodMaterial;

            // 3. Searing incandescent white-hot nose cone bow shock light (~14,000K ionization sheath)
            var lightObj = new GameObject("RodHeadLight");
            lightObj.transform.SetParent(transform, false);
            lightObj.transform.localPosition = Vector3.forward * 3.6f;
            headLight = lightObj.AddComponent<Light>();
            headLight.type = LightType.Point;
            headLight.color = new Color(0.96f, 0.98f, 1f);
            headLight.range = 28000f;
            headLight.intensity = 65f;
            headLight.shadows = LightShadows.None;

            // 4. Aerodynamic ionization plasma trail (Photo 2 incandescent core)
            plasmaTrail = gameObject.AddComponent<TrailRenderer>();
            plasmaTrail.sharedMaterial = flameMaterial;
            plasmaTrail.time = 0.95f;
            plasmaTrail.minVertexDistance = 6f;
            plasmaTrail.startWidth = 9.5f;
            plasmaTrail.endWidth = 1.2f;
            plasmaTrail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.2f, 0.85f),
                new Keyframe(0.6f, 0.4f),
                new Keyframe(1f, 0.05f));

            Gradient plasmaGradient = new Gradient();
            plasmaGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.00f, 1.00f, 1.00f), 0.0f),  // Pure white incandescent bow shock
                    new GradientColorKey(new Color(1.00f, 0.78f, 0.25f), 0.15f), // Searing thermal gold
                    new GradientColorKey(new Color(1.00f, 0.42f, 0.05f), 0.45f), // Intense hypersonic friction orange
                    new GradientColorKey(new Color(0.85f, 0.15f, 0.02f), 0.75f), // Darkening plasma wake
                    new GradientColorKey(new Color(0.30f, 0.30f, 0.30f), 1.0f)   // Atmospheric vacuum
                },
                new[]
                {
                    new GradientAlphaKey(1.0f, 0.0f),
                    new GradientAlphaKey(0.95f, 0.35f),
                    new GradientAlphaKey(0.5f, 0.8f),
                    new GradientAlphaKey(0.0f, 1.0f)
                });
            plasmaTrail.colorGradient = plasmaGradient;

            // 5. Massive columnar condensation contrail (Photo 2 towering white pillar)
            var contrailObj = new GameObject("RodContrail");
            contrailObj.transform.SetParent(transform, false);
            contrailObj.transform.localPosition = Vector3.back * 2.5f;
            contrailTrail = contrailObj.AddComponent<TrailRenderer>();
            contrailTrail.sharedMaterial = contrailMaterial;
            contrailTrail.time = 3.8f; // Lingers high in the sky
            contrailTrail.minVertexDistance = 12f;
            contrailTrail.startWidth = 12f;
            contrailTrail.endWidth = 45f; // Billows wide at altitude
            contrailTrail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.2f),
                new Keyframe(0.15f, 0.55f),
                new Keyframe(0.5f, 0.85f),
                new Keyframe(1f, 1.0f));

            Gradient contrailGradient = new Gradient();
            contrailGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.95f, 0.85f), 0.0f),
                    new GradientColorKey(new Color(0.92f, 0.92f, 0.94f), 0.2f),
                    new GradientColorKey(new Color(0.85f, 0.85f, 0.88f), 1.0f)
                },
                new[]
                {
                    new GradientAlphaKey(0.75f, 0.0f),
                    new GradientAlphaKey(0.85f, 0.25f),
                    new GradientAlphaKey(0.45f, 0.75f),
                    new GradientAlphaKey(0.0f, 1.0f)
                });
            contrailTrail.colorGradient = contrailGradient;

            // 6. Aerodynamic flame sheath licking back along the cylinder (Photo 2)
            var flameObj = new GameObject("FlameSheath");
            flameObj.transform.SetParent(transform, false);
            flameObj.transform.localPosition = Vector3.forward * 2.8f;
            flameSheath = flameObj.AddComponent<ParticleSystem>();
            var flameRenderer = flameObj.GetComponent<ParticleSystemRenderer>();
            flameRenderer.sharedMaterial = flameMaterial;

            var flameMain = flameSheath.main;
            flameMain.simulationSpace = ParticleSystemSimulationSpace.World;
            flameMain.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.65f);
            flameMain.startSpeed = new ParticleSystem.MinMaxCurve(40f, 120f);
            flameMain.startSize = new ParticleSystem.MinMaxCurve(3.5f, 7.5f);
            flameMain.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.95f, 0.8f, 1f),
                new Color(1f, 0.45f, 0.05f, 0.85f));
            flameMain.maxParticles = 500;

            var flameEmission = flameSheath.emission;
            flameEmission.rateOverTime = 160f;

            var flameShape = flameSheath.shape;
            flameShape.shapeType = ParticleSystemShapeType.Cone;
            flameShape.angle = 8f;
            flameShape.radius = 0.6f;
            flameShape.rotation = new Vector3(0f, 180f, 0f); // Stream backwards along rod

            // 7. Hypersonic spark spall particle emitter
            var sparkObj = new GameObject("RodSparks");
            sparkObj.transform.SetParent(transform, false);
            sparkObj.transform.localPosition = Vector3.back * 1.0f;
            sparkSystem = sparkObj.AddComponent<ParticleSystem>();
            var sparkRenderer = sparkObj.GetComponent<ParticleSystemRenderer>();
            sparkRenderer.sharedMaterial = sparkMaterial;

            var sparkMain = sparkSystem.main;
            sparkMain.simulationSpace = ParticleSystemSimulationSpace.World;
            sparkMain.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.85f);
            sparkMain.startSpeed = new ParticleSystem.MinMaxCurve(80f, 220f);
            sparkMain.startSize = new ParticleSystem.MinMaxCurve(1.8f, 5.0f);
            sparkMain.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 1f, 0.9f, 1f),
                new Color(1f, 0.6f, 0.1f, 0.9f));
            sparkMain.maxParticles = 600;

            var sparkEmission = sparkSystem.emission;
            sparkEmission.rateOverTime = 140f;

            var sparkShape = sparkSystem.shape;
            sparkShape.shapeType = ParticleSystemShapeType.Cone;
            sparkShape.angle = 14f;
            sparkShape.radius = 0.8f;
            sparkShape.rotation = new Vector3(0f, 180f, 0f);

            // 8. Spatialized hypersonic screaming atmospheric tear audio
            if (hypersonicSoundClip != null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.clip = hypersonicSoundClip;
                audioSource.loop = true;
                audioSource.spatialBlend = 0.75f;
                audioSource.minDistance = 400f;
                audioSource.maxDistance = 55000f;
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

                // Pre-impact tremor: building atmospheric rumble in final 2.5 km
                float distToGround = transform.position.y - targetPosition.y;
                if (distToGround < 2500f && distToGround > 0f)
                {
                    var csm = SceneSingleton<CameraStateManager>.i;
                    Camera cam = csm?.mainCamera ?? Camera.main;
                    if (cam != null)
                    {
                        float camDist = Vector3.Distance(cam.transform.position, targetPosition);
                        if (camDist < 14000f)
                        {
                            float factor = Mathf.Clamp01(1f - (camDist / 14000f)) * Mathf.Clamp01(1f - (distToGround / 2500f));
                            if (csm != null) csm.ShakeCamera(0.35f * factor, 0.65f * factor);
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
            PersistentID owner = missile != null ? missile.ownerID : default;
            KineticRodStrikeVisuals.TriggerImpact(lastPosition, owner);
        }

        private static void EnsureAssets()
        {
            if (flameMaterial != null && sparkMaterial != null && contrailMaterial != null && hypersonicSoundClip != null) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Color");

            if (flameMaterial == null)
            {
                flameMaterial = new Material(shader) { name = "KineticRodFlameMat" };
                flameMaterial.SetColor("_Color", new Color(1f, 0.82f, 0.45f, 1f));
                if (flameMaterial.HasProperty("_Surface")) flameMaterial.SetFloat("_Surface", 1f);
                if (flameMaterial.HasProperty("_Blend")) flameMaterial.SetFloat("_Blend", 1f);
            }

            if (sparkMaterial == null)
            {
                sparkMaterial = new Material(shader) { name = "KineticRodSparkMat" };
                sparkMaterial.SetColor("_Color", new Color(1f, 0.72f, 0.25f, 1f));
                if (sparkMaterial.HasProperty("_Surface")) sparkMaterial.SetFloat("_Surface", 1f);
                if (sparkMaterial.HasProperty("_Blend")) sparkMaterial.SetFloat("_Blend", 1f);
            }

            if (contrailMaterial == null)
            {
                contrailMaterial = NukeEffectAssets.SmokeParticleMaterial != null
                    ? new Material(NukeEffectAssets.SmokeParticleMaterial) { name = "KineticRodContrailMat" }
                    : new Material(shader) { name = "KineticRodContrailMat" };
                contrailMaterial.SetColor("_Color", new Color(0.9f, 0.9f, 0.92f, 0.85f));
                if (contrailMaterial.HasProperty("_Surface")) contrailMaterial.SetFloat("_Surface", 1f);
                if (contrailMaterial.HasProperty("_Blend")) contrailMaterial.SetFloat("_Blend", 1f);
            }

            if (hypersonicSoundClip == null)
            {
                int length = (int)(SampleRate * 3.5f);
                float[] samples = new float[length];
                for (int i = 0; i < length; i++)
                {
                    float t = i / (float)SampleRate;
                    float screech = Mathf.Sin(2f * Mathf.PI * (1650f + Mathf.Sin(2f * Mathf.PI * 8f * t) * 220f) * t) * 0.35f;
                    float noise = (UnityEngine.Random.value * 2f - 1f) * 0.45f;
                    float rumble = Mathf.Sin(2f * Mathf.PI * 65f * t) * 0.4f;
                    samples[i] = Mathf.Clamp(screech + noise + rumble, -1f, 1f);
                }
                hypersonicSoundClip = AudioClip.Create("KineticRodHypersonicSound", length, 1, SampleRate, false);
                hypersonicSoundClip.SetData(samples, 0);
            }
        }
    }

    /// <summary>
    /// Delivers the ground-zero kinetic impact:
    /// - Mini-nuke destruction mechanics with a focused kinetic killzone:
    ///   * 150m lethal collapse zone: catastrophic building demolition (RegisterRecentExplosion)
    ///     and immediate vehicle destruction.
    ///   * 420m supersonic shockwave blast zone: overpressure damage and physical impulse tossing.
    ///   * Attribute kills to requesting player PersistentID.
    /// - Kinetic impact visuals (Photo 2 & prompt):
    ///   * Blinding daylight prompt kinetic conversion flash.
    ///   * Towering vertical supersonic ejecta spire (pulverized rock & earth geyser 700m+ high).
    ///   * Radial ground-hugging base surge dust curtain.
    ///   * Ballistic incandescent spall streamers.
    ///   * URP DecalProjector terrain-conforming shockwave & persistent BlastManager crater.
    ///   * Multi-layered seismic acoustics and bedrock P-wave camera shake.
    /// </summary>
    internal sealed class KineticRodImpactEffect : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private static AudioClip impactAudioClip;
        private static Material fallbackSmokeMaterial;
        private static Material fallbackEjectaMaterial;
        private static readonly Collider[] colliderBuffer = new Collider[512];

        private static readonly int id_decalSize = Shader.PropertyToID("_DecalSize");
        private static readonly int id_opacity = Shader.PropertyToID("_Opacity");
        private static readonly int id_shockwaveExpansion = Shader.PropertyToID("_ShockwaveExpansion");
        private static readonly int id_ShockwaveAlpha = Shader.PropertyToID("_ShockwaveAlpha");
        private static readonly int id_Emission = Shader.PropertyToID("_Emission");
        private static readonly int id_Size = Shader.PropertyToID("_Size");
        private static readonly int id_ShockwaveSoftness = Shader.PropertyToID("_ShockwaveSoftness");

        public static void Spawn(Vector3 impactPosition, PersistentID ownerID = default)
        {
            var go = new GameObject("BoscaliSummer.KineticRodImpact");
            go.transform.position = impactPosition;
            go.transform.SetParent(Datum.origin, true);
            var effect = go.AddComponent<KineticRodImpactEffect>();
            effect.Initialize(impactPosition, ownerID);
        }

        private Vector3 groundZero;
        private PersistentID ownerPersistentId;
        private Light impactLight;
        private GameObject groundDecalObj;
        private DecalProjector decalProjector;
        private Material decalMaterial;
        private GameObject vaporCloudObj;
        private Material vaporCloudMaterial;
        private ParticleSystem ejectaColumn;
        private ParticleSystem baseSurge;
        private ParticleSystem spallStreamers;
        private AudioSource audioSource;

        private float startTime;
        private float blastPropagation = 15f;
        private float dustOpacity = 1f;

        // Mini-nuke tuned killzone
        private const float LethalCoreRadius = 150f;
        private const float BlastRadius = 420f;
        private const float YieldKilotons = 0.025f; // 25 tons TNT equivalent (mini-nuke)
        private const float BlastPower = 29.24f;     // (25,000)^0.3333
        private const float EffectDuration = 7.5f;

        private struct InfluencedTarget
        {
            public Collider collider;
            public Rigidbody rb;
            public IDamageable damageable;
            public float distance;
            public bool processed;
        }

        private readonly List<InfluencedTarget> targets = new List<InfluencedTarget>(128);

        private void Initialize(Vector3 point, PersistentID ownerID)
        {
            ownerPersistentId = ownerID;
            NukeEffectAssets.EnsureResolved();
            EnsureFallbackMaterials();

            // 1. Precise terrain surface alignment via StaticsMask raycast
            groundZero = point;
            if (Physics.Linecast(point + Vector3.up * 150f, point - Vector3.up * 350f, out var groundHit, PhysicsLayers.StaticsMask))
            {
                groundZero = groundHit.point;
            }
            transform.position = groundZero;
            startTime = Time.time;

            // 2. Persistent crater scorch and vegetation clearing via BlastManager
            try
            {
                SceneSingleton<BlastManager>.i?.AddBlast(groundZero.ToGlobalPosition(), 70f);
            }
            catch (Exception) { }

            // 3. Ground-zero immediate devastation within 60m
            try
            {
                Explosion.SimulateForce(groundZero, 380f);
                DamageEffects.BlastFrag(450f, groundZero, PersistentID.None, ownerPersistentId);
            }
            catch (Exception) { }

            // 4. Index nearby targets for mini-nuke shockwave & structural building demolition
            IndexShockwaveTargets();

            // 5. Blinding prompt kinetic energy conversion flash
            var lightObj = new GameObject("ImpactFlash");
            lightObj.transform.SetParent(transform, false);
            lightObj.transform.localPosition = Vector3.up * 6f;
            impactLight = lightObj.AddComponent<Light>();
            impactLight.type = LightType.Point;
            impactLight.color = new Color(0.96f, 0.98f, 1.0f);
            impactLight.range = 130000f;
            impactLight.intensity = 220f;
            impactLight.shadows = LightShadows.None;

            // 6. Ground compression shockwave using native URP Decal Projector
            SetupGroundDecalShockwave();

            // 7. Atmospheric condensation vapor cloud (Wilson cloud dome)
            SetupVaporCloud();

            // 8. Towering vertical kinetic ejecta spire (pulverized rock & earth geyser 700m+ high)
            SetupVerticalEjectaSpire();

            // 9. Ground-hugging radial base surge dust curtain
            SetupRadialBaseSurge();

            // 10. Hypervelocity incandescent spall streamers
            SetupSpallStreamers();

            // 11. Multi-layered acoustic design
            SetupAcoustics();

            // 12. Bedrock P-wave camera shake
            TriggerSeismicShock();

            StartCoroutine(Animate());
        }

        private void IndexShockwaveTargets()
        {
            try
            {
                int count = Physics.OverlapSphereNonAlloc(groundZero, BlastRadius, colliderBuffer);
                for (int i = 0; i < count; i++)
                {
                    Collider col = colliderBuffer[i];
                    if (col == null) continue;

                    IDamageable dmg = col.gameObject.GetComponent<IDamageable>();
                    Rigidbody rb = col.attachedRigidbody;

                    if (dmg != null || rb != null)
                    {
                        float dist = Vector3.Distance(col.bounds.center, groundZero);
                        targets.Add(new InfluencedTarget
                        {
                            collider = col,
                            rb = rb,
                            damageable = dmg,
                            distance = dist,
                            processed = false
                        });
                    }
                }
            }
            catch (Exception) { }
        }

        private void SetupGroundDecalShockwave()
        {
            if (NukeEffectAssets.GroundDecalPrefab != null)
            {
                groundDecalObj = Instantiate(NukeEffectAssets.GroundDecalPrefab, groundZero + Vector3.up * 2f, Quaternion.LookRotation(Vector3.down));
                groundDecalObj.transform.SetParent(Datum.origin, true);
                decalProjector = groundDecalObj.GetComponent<DecalProjector>()
                              ?? groundDecalObj.GetComponentInChildren<DecalProjector>();
            }

            if (decalProjector == null && NukeEffectAssets.ShockwaveDecalMaterial != null)
            {
                groundDecalObj = new GameObject("KineticShockwaveDecal");
                groundDecalObj.transform.position = groundZero + Vector3.up * 2f;
                groundDecalObj.transform.rotation = Quaternion.LookRotation(Vector3.down);
                groundDecalObj.transform.SetParent(Datum.origin, true);
                decalProjector = groundDecalObj.AddComponent<DecalProjector>();
                decalProjector.material = NukeEffectAssets.ShockwaveDecalMaterial;
            }

            if (decalProjector != null)
            {
                decalProjector.size = new Vector3(BlastRadius * 2f, BlastRadius * 2f, BlastRadius * 2f);
                decalMaterial = new Material(decalProjector.material);
                decalProjector.material = decalMaterial;
                decalMaterial.SetFloat(id_decalSize, BlastRadius);
                decalMaterial.SetFloat(id_opacity, 1.0f);
            }
        }

        private void SetupVaporCloud()
        {
            if (NukeEffectAssets.VaporCloudPrefab != null)
            {
                vaporCloudObj = Instantiate(NukeEffectAssets.VaporCloudPrefab, groundZero + Vector3.up * 20f, Quaternion.identity);
                vaporCloudObj.transform.SetParent(Datum.origin, true);
                var rend = vaporCloudObj.GetComponent<Renderer>();
                if (rend != null)
                {
                    vaporCloudMaterial = new Material(rend.sharedMaterial);
                    rend.material = vaporCloudMaterial;
                }
            }
            else if (NukeEffectAssets.VaporCloudMesh != null && NukeEffectAssets.VaporCloudMaterial != null)
            {
                vaporCloudObj = new GameObject("KineticVaporCloud");
                vaporCloudObj.transform.position = groundZero + Vector3.up * 20f;
                vaporCloudObj.transform.SetParent(Datum.origin, true);
                var mf = vaporCloudObj.AddComponent<MeshFilter>();
                mf.sharedMesh = NukeEffectAssets.VaporCloudMesh;
                var mr = vaporCloudObj.AddComponent<MeshRenderer>();
                vaporCloudMaterial = new Material(NukeEffectAssets.VaporCloudMaterial);
                mr.material = vaporCloudMaterial;
            }
        }

        private void SetupVerticalEjectaSpire()
        {
            var ejectaObj = new GameObject("VerticalEjectaSpire");
            ejectaObj.transform.SetParent(transform, false);
            ejectaColumn = ejectaObj.AddComponent<ParticleSystem>();
            var colRenderer = ejectaObj.GetComponent<ParticleSystemRenderer>();
            colRenderer.sharedMaterial = NukeEffectAssets.SmokeParticleMaterial
                                      ?? NukeEffectAssets.EjectaParticleMaterial
                                      ?? fallbackSmokeMaterial;

            var mainCol = ejectaColumn.main;
            mainCol.simulationSpace = ParticleSystemSimulationSpace.World;
            mainCol.duration = 2.5f;
            mainCol.startLifetime = new ParticleSystem.MinMaxCurve(4.2f, 7.5f);
            mainCol.startSpeed = new ParticleSystem.MinMaxCurve(350f, 620f); // Rockets 600m - 800m vertically!
            mainCol.startSize = new ParticleSystem.MinMaxCurve(20f, 52f);
            mainCol.gravityModifier = 0.92f;
            mainCol.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.94f, 0.8f, 1f),
                new Color(0.20f, 0.18f, 0.16f, 0.95f));
            mainCol.maxParticles = 550;

            var emissionCol = ejectaColumn.emission;
            emissionCol.rateOverTime = 0f;
            emissionCol.SetBursts(new[] { new ParticleSystem.Burst(0f, 320, 440) });

            var shapeCol = ejectaColumn.shape;
            shapeCol.shapeType = ParticleSystemShapeType.Cone;
            shapeCol.angle = 5.5f; // Extremely focused high-speed vertical kinetic jet
            shapeCol.radius = 7.0f;
            shapeCol.rotation = new Vector3(-90f, 0f, 0f); // Straight up
        }

        private void SetupRadialBaseSurge()
        {
            var surgeObj = new GameObject("RadialBaseSurge");
            surgeObj.transform.SetParent(transform, false);
            baseSurge = surgeObj.AddComponent<ParticleSystem>();
            var surgeRenderer = surgeObj.GetComponent<ParticleSystemRenderer>();
            surgeRenderer.sharedMaterial = NukeEffectAssets.SmokeParticleMaterial
                                        ?? fallbackSmokeMaterial;

            var mainSurge = baseSurge.main;
            mainSurge.simulationSpace = ParticleSystemSimulationSpace.World;
            mainSurge.duration = 2.0f;
            mainSurge.startLifetime = new ParticleSystem.MinMaxCurve(3.2f, 5.8f);
            mainSurge.startSpeed = new ParticleSystem.MinMaxCurve(140f, 280f);
            mainSurge.startSize = new ParticleSystem.MinMaxCurve(18f, 38f);
            mainSurge.gravityModifier = 0.3f;
            mainSurge.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.85f, 0.68f, 0.40f, 0.92f),
                new Color(0.32f, 0.29f, 0.26f, 0.88f));
            mainSurge.maxParticles = 350;

            var emissionSurge = baseSurge.emission;
            emissionSurge.rateOverTime = 0f;
            emissionSurge.SetBursts(new[] { new ParticleSystem.Burst(0f, 200, 280) });

            var shapeSurge = baseSurge.shape;
            shapeSurge.shapeType = ParticleSystemShapeType.Cone;
            shapeSurge.angle = 84f; // Ground-hugging blanket
            shapeSurge.radius = 14.0f;
            shapeSurge.rotation = new Vector3(-90f, 0f, 0f);
        }

        private void SetupSpallStreamers()
        {
            var spallObj = new GameObject("SpallStreamers");
            spallObj.transform.SetParent(transform, false);
            spallStreamers = spallObj.AddComponent<ParticleSystem>();
            var debRenderer = spallObj.GetComponent<ParticleSystemRenderer>();
            debRenderer.sharedMaterial = NukeEffectAssets.EjectaParticleMaterial
                                      ?? fallbackEjectaMaterial;

            var mainDeb = spallStreamers.main;
            mainDeb.simulationSpace = ParticleSystemSimulationSpace.World;
            mainDeb.duration = 1.5f;
            mainDeb.startLifetime = new ParticleSystem.MinMaxCurve(2.8f, 5.8f);
            mainDeb.startSpeed = new ParticleSystem.MinMaxCurve(260f, 500f);
            mainDeb.startSize = new ParticleSystem.MinMaxCurve(5.0f, 14f);
            mainDeb.gravityModifier = 1.2f;
            mainDeb.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.92f, 0.45f, 1f),
                new Color(1f, 0.38f, 0.05f, 0.88f));
            mainDeb.maxParticles = 320;

            var emissionDeb = spallStreamers.emission;
            emissionDeb.rateOverTime = 0f;
            emissionDeb.SetBursts(new[] { new ParticleSystem.Burst(0f, 180, 250) });

            var shapeDeb = spallStreamers.shape;
            shapeDeb.shapeType = ParticleSystemShapeType.Cone;
            shapeDeb.angle = 42f;
            shapeDeb.radius = 9.0f;
            shapeDeb.rotation = new Vector3(-90f, 0f, 0f);
        }

        private void SetupAcoustics()
        {
            // 1. Supersonic crack / shock snap
            if (GameAssets.i?.sonicBoom != null)
            {
                var snapObj = new GameObject("SonicCrack");
                snapObj.transform.position = groundZero;
                snapObj.transform.SetParent(transform, false);
                var snapSrc = snapObj.AddComponent<AudioSource>();
                snapSrc.clip = GameAssets.i.sonicBoom;
                snapSrc.spatialBlend = 0.5f;
                snapSrc.minDistance = 600f;
                snapSrc.maxDistance = 75000f;
                snapSrc.volume = 1.0f;
                snapSrc.pitch = UnityEngine.Random.Range(0.92f, 1.04f);
                snapSrc.rolloffMode = AudioRolloffMode.Logarithmic;
                snapSrc.Play();
            }

            // 2. Realistic distance-delayed heavy explosion rumble via ExplosionAudioManager
            if (NukeEffectAssets.NukeExplosionClip != null && SceneSingleton<ExplosionAudioManager>.i != null)
            {
                var boomObj = new GameObject("NukeExplosionBoom");
                boomObj.transform.position = groundZero;
                boomObj.transform.SetParent(transform, false);
                var boomSrc = boomObj.AddComponent<AudioSource>();
                boomSrc.clip = NukeEffectAssets.NukeExplosionClip;
                boomSrc.spatialBlend = 1.0f;
                boomSrc.minDistance = 800f;
                boomSrc.maxDistance = 95000f;
                var filter = boomObj.AddComponent<AudioLowPassFilter>();
                SceneSingleton<ExplosionAudioManager>.i.AddExplosionAudio(boomSrc, filter, 0.35f);
            }

            // 3. Sub-bass seismic earth fracture rumble
            if (impactAudioClip != null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.clip = impactAudioClip;
                audioSource.spatialBlend = 0.4f;
                audioSource.minDistance = 800f;
                audioSource.maxDistance = 110000f;
                audioSource.volume = 1.0f;
                audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
                audioSource.Play();
            }
        }

        private void TriggerSeismicShock()
        {
            Camera cam = SceneSingleton<CameraStateManager>.i?.mainCamera ?? Camera.main;
            if (cam == null) return;

            float distance = Vector3.Distance(cam.transform.position, groundZero);
            if (distance > 45000f) return;

            float seismicDelay = distance / 3400f;
            float intensity = Mathf.Clamp01(1f - (distance / 35000f));
            float lowFreq = Mathf.Lerp(0.5f, 3.8f, intensity * intensity);
            float highFreq = Mathf.Lerp(0.6f, 4.8f, intensity);

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

            float elapsed = 0f;
            float duration = 3.2f * intensity;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float decay = Mathf.Pow(1f - Mathf.Clamp01(elapsed / duration), 2.2f);
                if (csm != null && decay > 0.08f)
                {
                    csm.ShakeCamera(lowFreq * decay * 0.45f, highFreq * decay * 0.45f);
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

                // 1. Animate flash: instant peak, decays to molten pit glow within 0.15s, then fades
                if (impactLight != null)
                {
                    if (elapsed < 0.15f)
                    {
                        float t = elapsed / 0.15f;
                        impactLight.intensity = Mathf.Lerp(220f, 50f, t);
                        impactLight.color = Color.Lerp(new Color(0.96f, 0.98f, 1f), new Color(1f, 0.55f, 0.15f), t);
                    }
                    else
                    {
                        float t = Mathf.Clamp01((elapsed - 0.15f) / 2.8f);
                        impactLight.intensity = Mathf.Lerp(50f, 0f, t * t);
                        if (impactLight.intensity <= 0.05f) impactLight.enabled = false;
                    }
                }

                // 2. Supersonic shockwave expansion & mini-nuke damage simulation
                blastPropagation += 680f * Time.deltaTime;

                // Apply mini-nuke damage to targets reached by shockwave front
                for (int i = 0; i < targets.Count; i++)
                {
                    InfluencedTarget target = targets[i];
                    if (target.processed || target.collider == null) continue;

                    if (target.distance <= blastPropagation)
                    {
                        target.processed = true;
                        targets[i] = target;

                        bool isCore = target.distance <= LethalCoreRadius;
                        float distNorm = Mathf.Max(target.distance / BlastPower, 1f);
                        float overpressure = 22000f / (distNorm * distNorm * distNorm);

                        if (target.damageable != null)
                        {
                            try
                            {
                                Unit unit = target.damageable.GetUnit();
                                Building building = unit as Building;

                                if (building != null)
                                {
                                    float yieldParam = isCore ? 25000f : 12000f * Mathf.Clamp01(1f - target.distance / BlastRadius);
                                    building.RegisterRecentExplosion(groundZero.ToGlobalPosition(), yieldParam);

                                    if (NetworkManagerNuclearOption.i != null && NetworkManagerNuclearOption.i.Server.Active)
                                    {
                                        float coreDmg = isCore ? 25000f : overpressure * 1.5f;
                                        target.damageable.TakeDamage(coreDmg, overpressure, 1.0f, 2000f, 25000f, ownerPersistentId);
                                    }
                                }
                                else if (NetworkManagerNuclearOption.i != null && NetworkManagerNuclearOption.i.Server.Active)
                                {
                                    float directDmg = isCore ? 12000f : overpressure;
                                    target.damageable.TakeDamage(directDmg, overpressure, 0.9f, 1000f, isCore ? 15000f : 2000f, ownerPersistentId);
                                }
                            }
                            catch (Exception) { }
                        }

                        if (target.rb != null && target.distance > 0.1f)
                        {
                            try
                            {
                                float impulse = Mathf.Min(overpressure * 28f, isCore ? 150000f : 45000f);
                                Vector3 forceDir = (target.collider.bounds.center - groundZero).normalized;
                                target.rb.AddForceAtPosition(forceDir * impulse, target.collider.bounds.center, ForceMode.Impulse);
                            }
                            catch (Exception) { }
                        }
                    }
                }

                // 3. Animate ground shockwave decal expansion
                if (decalMaterial != null)
                {
                    decalMaterial.SetFloat(id_shockwaveExpansion, (1f * BlastRadius) / Mathf.Max(1f, blastPropagation));

                    if (blastPropagation > BlastRadius)
                    {
                        dustOpacity -= Time.deltaTime * 0.16f;
                        decalMaterial.SetFloat(id_opacity, Mathf.Max(0f, dustOpacity));

                        if (dustOpacity <= 0f && groundDecalObj != null)
                        {
                            Destroy(groundDecalObj);
                            groundDecalObj = null;
                        }
                    }
                }

                // 4. Animate atmospheric vapor cloud
                if (vaporCloudObj != null && vaporCloudMaterial != null)
                {
                    Camera cam = SceneSingleton<CameraStateManager>.i?.mainCamera ?? Camera.main;
                    if (cam != null)
                    {
                        vaporCloudObj.transform.LookAt(cam.transform.position);
                    }

                    float cloudScale = Mathf.Min(BlastRadius * 0.95f, blastPropagation * 0.88f);
                    vaporCloudObj.transform.localScale = Vector3.one * cloudScale;

                    float cloudAlpha = NukeEffectAssets.VaporCloudAlphaCurve != null
                        ? NukeEffectAssets.VaporCloudAlphaCurve.Evaluate(elapsed)
                        : Mathf.Clamp01(1f - (elapsed / 1.8f));

                    vaporCloudMaterial.SetFloat(id_ShockwaveAlpha, cloudAlpha);

                    float emissive = impactLight != null && impactLight.isActiveAndEnabled ? impactLight.intensity * 0.12f : 0f;
                    if (emissive > 0f)
                    {
                        vaporCloudMaterial.SetFloat(id_Emission, emissive);
                    }

                    float detailScale = NukeEffectAssets.VaporCloudDetailScale > 0f ? NukeEffectAssets.VaporCloudDetailScale : 30f;
                    vaporCloudMaterial.SetFloat(id_Size, cloudScale / detailScale);
                    vaporCloudMaterial.SetFloat(id_ShockwaveSoftness, 4f / Mathf.Max(1f, vaporCloudObj.transform.localScale.x));

                    if (cloudAlpha <= 0f)
                    {
                        Destroy(vaporCloudObj);
                        vaporCloudObj = null;
                    }
                }

                yield return null;
            }

            if (groundDecalObj != null) Destroy(groundDecalObj);
            if (vaporCloudObj != null) Destroy(vaporCloudObj);
            Destroy(gameObject, 2.5f);
        }

        private static void EnsureFallbackMaterials()
        {
            if (fallbackSmokeMaterial != null && fallbackEjectaMaterial != null && impactAudioClip != null) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Particles/Standard Unlit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Color");

            if (fallbackSmokeMaterial == null)
            {
                fallbackSmokeMaterial = new Material(shader) { name = "KineticFallbackSmokeMat" };
                fallbackSmokeMaterial.SetColor("_Color", new Color(0.25f, 0.23f, 0.21f, 0.85f));
            }

            if (fallbackEjectaMaterial == null)
            {
                fallbackEjectaMaterial = new Material(shader) { name = "KineticFallbackEjectaMat" };
                fallbackEjectaMaterial.SetColor("_Color", new Color(1f, 0.6f, 0.15f, 1f));
            }

            if (impactAudioClip == null)
            {
                int length = (int)(SampleRate * 6.5f);
                float[] samples = new float[length];

                for (int i = 0; i < length; i++)
                {
                    float t = i / (float)SampleRate;

                    // 1. Supersonic kinetic fracture crack / transient snap
                    float snapEnvelope = Mathf.Exp(-t * 38f);
                    float snap = (UnityEngine.Random.value * 2f - 1f) * snapEnvelope * 0.9f;

                    // 2. Colossal seismic ground impact thud (22 Hz dropping to 14 Hz)
                    float bassFreq = Mathf.Lerp(24f, 14f, t / 6.5f);
                    float bassEnvelope = Mathf.Pow(Mathf.Clamp01(1f - (t / 5.8f)), 1.5f);
                    float bass = Mathf.Sin(2f * Mathf.PI * bassFreq * t) * bassEnvelope * 0.85f;
                    float subBass = Mathf.Sin(2f * Mathf.PI * (bassFreq * 0.5f) * t) * bassEnvelope * 0.55f;

                    // 3. Ejecta roar and debris turbulence
                    float roarEnvelope = Mathf.Pow(Mathf.Clamp01(1f - (t / 4.2f)), 2.2f);
                    float roar = (UnityEngine.Random.value * 2f - 1f) * roarEnvelope * 0.45f;

                    // 4. Rolling mountain thunder echoes
                    float echo = (Mathf.Sin(2f * Mathf.PI * 42f * t) + Mathf.Sin(2f * Mathf.PI * 58f * t) * 0.5f)
                        * Mathf.Pow(Mathf.Clamp01(1f - (t / 6.2f)), 1.6f) * 0.35f;

                    samples[i] = Mathf.Clamp(snap + bass + subBass + roar + echo, -1f, 1f);
                }

                impactAudioClip = AudioClip.Create("KineticRodImpactSound", length, 1, SampleRate, false);
                impactAudioClip.SetData(samples, 0);
            }
        }

        private void OnDestroy()
        {
            if (decalMaterial != null) Destroy(decalMaterial);
            if (vaporCloudMaterial != null) Destroy(vaporCloudMaterial);
            if (groundDecalObj != null) Destroy(groundDecalObj);
            if (vaporCloudObj != null) Destroy(vaporCloudObj);
        }
    }
}
