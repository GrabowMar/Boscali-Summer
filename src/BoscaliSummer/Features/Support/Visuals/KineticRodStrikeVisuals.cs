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
    /// Incorporates reverse-engineered shockwave, decal projection, vapor cloud, and acoustic
    /// assets from the game's tactical nuclear warhead while styling for authentic hypervelocity
    /// tungsten rod kinetic impact physics.
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

        public static void TriggerImpact(Vector3 impactPosition)
        {
            if (GameManager.IsHeadless) return;

            // Deduplication guard: ignore redundant triggers within 1.5 seconds and 400m
            float now = Time.time;
            for (int i = recentStrikes.Count - 1; i >= 0; i--)
            {
                if (now - recentStrikes[i].time > 3.0f)
                {
                    recentStrikes.RemoveAt(i);
                }
                else if (Vector3.Distance(recentStrikes[i].pos, impactPosition) < 400f)
                {
                    return; // Duplicate trigger suppressed
                }
            }
            recentStrikes.Add((impactPosition, now));

            KineticRodImpactEffect.Spawn(impactPosition);
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

            // 1. Blinding incandescent white-cyan kinetic spearhead light (~12,000K ionization sheath)
            var lightObj = new GameObject("RodHeadLight");
            lightObj.transform.SetParent(transform, false);
            lightObj.transform.localPosition = Vector3.forward * 2.5f;
            headLight = lightObj.AddComponent<Light>();
            headLight.type = LightType.Point;
            headLight.color = new Color(0.92f, 0.97f, 1f);
            headLight.range = 22000f;
            headLight.intensity = 48f;
            headLight.shadows = LightShadows.None;

            // 2. Hypervelocity re-entry ionization plasma trail
            plasmaTrail = gameObject.AddComponent<TrailRenderer>();
            plasmaTrail.sharedMaterial = plasmaTrailMaterial;
            plasmaTrail.time = 1.45f;
            plasmaTrail.minVertexDistance = 7f;
            plasmaTrail.startWidth = 14f;
            plasmaTrail.endWidth = 1.6f;
            plasmaTrail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.18f, 0.75f),
                new Keyframe(0.55f, 0.35f),
                new Keyframe(1f, 0.04f));

            Gradient trailGradient = new Gradient();
            trailGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.95f, 0.98f, 1.0f), 0.0f),    // Blinding incandescent white core
                    new GradientColorKey(new Color(0.40f, 0.78f, 1.0f), 0.18f),   // Atmospheric ionization cyan sheath
                    new GradientColorKey(new Color(1.00f, 0.58f, 0.12f), 0.45f),   // Friction thermal amber/orange
                    new GradientColorKey(new Color(0.85f, 0.18f, 0.02f), 0.75f),   // Dissipating re-entry wake
                    new GradientColorKey(new Color(0.25f, 0.25f, 0.25f), 1.0f)    // Atmospheric smoke vacuum
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
    /// Delivers the ground-zero kinetic impact: blinding incandescent flash, URP DecalProjector
    /// ground shockwave conforming to terrain, towering vertical ejecta geyser/spire,
    /// persistent BlastManager crater scorch, physical blast force, and multi-layered acoustic design.
    /// </summary>
    internal sealed class KineticRodImpactEffect : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private static AudioClip impactAudioClip;
        private static Material fallbackSmokeMaterial;
        private static Material fallbackEjectaMaterial;

        private static readonly int id_decalSize = Shader.PropertyToID("_DecalSize");
        private static readonly int id_opacity = Shader.PropertyToID("_Opacity");
        private static readonly int id_shockwaveExpansion = Shader.PropertyToID("_ShockwaveExpansion");
        private static readonly int id_ShockwaveAlpha = Shader.PropertyToID("_ShockwaveAlpha");
        private static readonly int id_Emission = Shader.PropertyToID("_Emission");
        private static readonly int id_Size = Shader.PropertyToID("_Size");
        private static readonly int id_ShockwaveSoftness = Shader.PropertyToID("_ShockwaveSoftness");

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
        private const float BlastRadius = 2400f;
        private const float EffectDuration = 7.5f;

        private void Initialize(Vector3 point)
        {
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
                SceneSingleton<BlastManager>.i?.AddBlast(groundZero.ToGlobalPosition(), 55f);
            }
            catch (Exception)
            {
                // Non-critical if detail renderer is not present
            }

            // 3. Physical shockwave impulse and damage simulation
            try
            {
                Explosion.SimulateForce(groundZero, 350f);
                DamageEffects.BlastFrag(350f, groundZero, PersistentID.None, PersistentID.None);
            }
            catch (Exception)
            {
                // Non-critical
            }

            // 4. Blinding cataclysmic incandescent flash (peak 0.12s, decays to molten pit glow)
            var lightObj = new GameObject("ImpactFlash");
            lightObj.transform.SetParent(transform, false);
            lightObj.transform.localPosition = Vector3.up * 6f;
            impactLight = lightObj.AddComponent<Light>();
            impactLight.type = LightType.Point;
            impactLight.color = new Color(0.95f, 0.98f, 1.0f);
            impactLight.range = 120000f;
            impactLight.intensity = 180f;
            impactLight.shadows = LightShadows.None;

            // 5. Ground compression shockwave using native URP Decal Projector
            SetupGroundDecalShockwave();

            // 6. Atmospheric condensation vapor cloud (Wilson cloud dome)
            SetupVaporCloud();

            // 7. Towering vertical kinetic ejecta spire (pulverized rock & earth geyser)
            SetupVerticalEjectaSpire();

            // 8. Ground-hugging radial base surge
            SetupRadialBaseSurge();

            // 9. Hypervelocity incandescent spall streamers
            SetupSpallStreamers();

            // 10. Multi-layered acoustic design
            SetupAcoustics();

            // 11. Seismic bedrock camera shake
            TriggerSeismicShock();

            StartCoroutine(Animate());
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
            mainCol.duration = 2.0f;
            mainCol.startLifetime = new ParticleSystem.MinMaxCurve(3.8f, 6.8f);
            mainCol.startSpeed = new ParticleSystem.MinMaxCurve(280f, 520f); // Reaches 350m - 550m vertically!
            mainCol.startSize = new ParticleSystem.MinMaxCurve(16f, 44f);
            mainCol.gravityModifier = 0.95f; // Shoots high into sky, billows, and falls back
            mainCol.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.92f, 0.75f, 1f),
                new Color(0.22f, 0.20f, 0.18f, 0.9f));
            mainCol.maxParticles = 450;

            var emissionCol = ejectaColumn.emission;
            emissionCol.rateOverTime = 0f;
            emissionCol.SetBursts(new[] { new ParticleSystem.Burst(0f, 260, 360) });

            var shapeCol = ejectaColumn.shape;
            shapeCol.shapeType = ParticleSystemShapeType.Cone;
            shapeCol.angle = 6.5f; // Narrow high-speed vertical kinetic jet
            shapeCol.radius = 6.0f;
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
            mainSurge.duration = 1.5f;
            mainSurge.startLifetime = new ParticleSystem.MinMaxCurve(2.8f, 5.0f);
            mainSurge.startSpeed = new ParticleSystem.MinMaxCurve(120f, 240f);
            mainSurge.startSize = new ParticleSystem.MinMaxCurve(14f, 32f);
            mainSurge.gravityModifier = 0.35f;
            mainSurge.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.85f, 0.65f, 0.35f, 0.9f),
                new Color(0.35f, 0.32f, 0.30f, 0.85f));
            mainSurge.maxParticles = 280;

            var emissionSurge = baseSurge.emission;
            emissionSurge.rateOverTime = 0f;
            emissionSurge.SetBursts(new[] { new ParticleSystem.Burst(0f, 160, 220) });

            var shapeSurge = baseSurge.shape;
            shapeSurge.shapeType = ParticleSystemShapeType.Cone;
            shapeSurge.angle = 82f; // Low-angle radial blanket hugging the ground
            shapeSurge.radius = 12.0f;
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
            mainDeb.duration = 1.2f;
            mainDeb.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 5.2f);
            mainDeb.startSpeed = new ParticleSystem.MinMaxCurve(220f, 440f);
            mainDeb.startSize = new ParticleSystem.MinMaxCurve(4.0f, 12f);
            mainDeb.gravityModifier = 1.25f;
            mainDeb.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.9f, 0.4f, 1f),
                new Color(1f, 0.35f, 0.05f, 0.85f));
            mainDeb.maxParticles = 260;

            var emissionDeb = spallStreamers.emission;
            emissionDeb.rateOverTime = 0f;
            emissionDeb.SetBursts(new[] { new ParticleSystem.Burst(0f, 140, 200) });

            var shapeDeb = spallStreamers.shape;
            shapeDeb.shapeType = ParticleSystemShapeType.Cone;
            shapeDeb.angle = 38f;
            shapeDeb.radius = 8.0f;
            shapeDeb.rotation = new Vector3(-90f, 0f, 0f);
        }

        private void SetupAcoustics()
        {
            // 1. Supersonic crack / shock snap (plays immediately on arrival)
            if (GameAssets.i?.sonicBoom != null)
            {
                var snapObj = new GameObject("SonicCrack");
                snapObj.transform.position = groundZero;
                snapObj.transform.SetParent(transform, false);
                var snapSrc = snapObj.AddComponent<AudioSource>();
                snapSrc.clip = GameAssets.i.sonicBoom;
                snapSrc.spatialBlend = 0.5f;
                snapSrc.minDistance = 600f;
                snapSrc.maxDistance = 65000f;
                snapSrc.volume = 1.0f;
                snapSrc.pitch = UnityEngine.Random.Range(0.94f, 1.06f);
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
                boomSrc.maxDistance = 90000f;
                var filter = boomObj.AddComponent<AudioLowPassFilter>();
                SceneSingleton<ExplosionAudioManager>.i.AddExplosionAudio(boomSrc, filter, 0.35f);
            }

            // 3. Sub-bass seismic earth fracture rumble (travels through bedrock immediately)
            if (impactAudioClip != null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.clip = impactAudioClip;
                audioSource.spatialBlend = 0.4f;
                audioSource.minDistance = 800f;
                audioSource.maxDistance = 100000f;
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
            if (distance > 40000f) return;

            // Seismic wave travels through solid bedrock at ~3400 m/s
            float seismicDelay = distance / 3400f;
            float intensity = Mathf.Clamp01(1f - (distance / 32000f));
            float lowFreq = Mathf.Lerp(0.4f, 3.2f, intensity * intensity);
            float highFreq = Mathf.Lerp(0.5f, 4.2f, intensity);

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

            // Sustained subterranean tremor decay over 3.0 seconds
            float elapsed = 0f;
            float duration = 3.0f * intensity;
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
                        impactLight.intensity = Mathf.Lerp(180f, 42f, t);
                        impactLight.color = Color.Lerp(new Color(0.95f, 0.98f, 1f), new Color(1f, 0.55f, 0.15f), t);
                    }
                    else
                    {
                        float t = Mathf.Clamp01((elapsed - 0.15f) / 2.8f);
                        impactLight.intensity = Mathf.Lerp(42f, 0f, t * t);
                        if (impactLight.intensity <= 0.05f) impactLight.enabled = false;
                    }
                }

                // 2. Animate ground shockwave expansion (conforming URP Decal Projector)
                blastPropagation += 720f * Time.deltaTime;
                if (decalMaterial != null)
                {
                    decalMaterial.SetFloat(id_shockwaveExpansion, (1f * BlastRadius) / Mathf.Max(1f, blastPropagation));

                    if (blastPropagation > BlastRadius)
                    {
                        dustOpacity -= Time.deltaTime * 0.14f;
                        decalMaterial.SetFloat(id_opacity, Mathf.Max(0f, dustOpacity));

                        if (dustOpacity <= 0f && groundDecalObj != null)
                        {
                            Destroy(groundDecalObj);
                            groundDecalObj = null;
                        }
                    }
                }

                // 3. Animate atmospheric vapor cloud
                if (vaporCloudObj != null && vaporCloudMaterial != null)
                {
                    Camera cam = SceneSingleton<CameraStateManager>.i?.mainCamera ?? Camera.main;
                    if (cam != null)
                    {
                        vaporCloudObj.transform.LookAt(cam.transform.position);
                    }

                    float cloudScale = Mathf.Min(BlastRadius * 0.9f, blastPropagation * 0.85f);
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
                // Synthesize cataclysmic sub-bass earth fracture rumble (18Hz fundamental + acoustic shock reverberation)
                int length = (int)(SampleRate * 6.5f);
                float[] samples = new float[length];

                for (int i = 0; i < length; i++)
                {
                    float t = i / (float)SampleRate;

                    // 1. Supersonic kinetic fracture crack / transient snap (0.0s - 0.08s)
                    float snapEnvelope = Mathf.Exp(-t * 38f);
                    float snap = (UnityEngine.Random.value * 2f - 1f) * snapEnvelope * 0.9f;

                    // 2. Colossal seismic ground impact thud (22 Hz fundamental dropping to 14 Hz)
                    float bassFreq = Mathf.Lerp(24f, 14f, t / 6.5f);
                    float bassEnvelope = Mathf.Pow(Mathf.Clamp01(1f - (t / 5.8f)), 1.5f);
                    float bass = Mathf.Sin(2f * Mathf.PI * bassFreq * t) * bassEnvelope * 0.85f;
                    float subBass = Mathf.Sin(2f * Mathf.PI * (bassFreq * 0.5f) * t) * bassEnvelope * 0.55f;

                    // 3. Ejecta roar and debris turbulence
                    float roarEnvelope = Mathf.Pow(Mathf.Clamp01(1f - (t / 4.2f)), 2.2f);
                    float roar = (UnityEngine.Random.value * 2f - 1f) * roarEnvelope * 0.45f;

                    // 4. Rolling mountain thunder echoes (stochastic reverberation)
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
