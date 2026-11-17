using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Networking;
using BoscaliSummer.Modules.Vanguard.Domain;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>The FLASHBACK blast permanently changes this mission, never the map asset on disk.</summary>
    [DefaultExecutionOrder(2000)]
    internal sealed class Sa74Fallout : MonoBehaviour, ISceneService
    {
        internal const float YieldKilotons = 74000f;
        internal const float CraterRadius = 1800f;
        internal const int MaxSites = 16;
        internal sealed class Site
        {
            internal uint Id;
            internal GlobalPosition Position;
            internal float Started;
            internal uint Owner;
        }
        internal static Sa74Fallout Instance;
        internal static readonly List<Site> Sites = new List<Site>();
        internal static uint Epoch = 1;
        internal static int Events, NativeBlasts, BlastColliders;
        private static readonly AccessTools.FieldRef<Missile, float> BlastYield = AccessTools.FieldRefAccess<Missile, float>("blastYield");
        private static readonly FieldInfo Warhead = AccessTools.Field(typeof(Missile), "warhead");
        private static uint nextId;
        private static uint lastReceivedId;
        private readonly Sa74FalloutTerrain terrain = new Sa74FalloutTerrain();
        private readonly List<GameObject> blasts = new List<GameObject>();
        private GameObject dressing, ashRoot;
        private VolumeProfile profile;
        private Volume volume;
        private ColorAdjustments grade;
        private WhiteBalance balance;
        private Bloom bloom;
        private FilmGrain radiationGrain;
        private Light flashLight;
        private float flashExposure, thermalPulse, winterBlend;
        private float radiationStrength, beforeFog, writtenFog;
        private bool hazeApplied;
        private readonly ParticleSystem.Particle[] snow = new ParticleSystem.Particle[1500];
        private ParticleSystem ash;
        private Material ashMaterial;
        private Texture2D ashTexture;
        private AudioSource geiger;
        private AudioClip crackle;
        private LevelInfo winterLevel;
        private float beforeConditions, beforeCloudHeight;
        private bool winterCaptured;
        internal int SnowParticles => ash != null ? ash.particleCount : 0;
        internal int SnowBudget => snow.Length;
        internal bool SnowEmitting => ash != null && ash.isPlaying && SnowParticles > 0;
        internal float RadiationStrength => radiationStrength;
        internal float AtmosphereExposure => -winterBlend * .2f;
        internal float RadiationGrain => radiationGrain != null ? radiationGrain.intensity.value : 0f;

        private void Awake() => Instance = this;
        internal static bool Active => Sites.Count != 0;
        internal static float ShockRadius
        {
            get
            {
                MapSettings map = NetworkSceneSingleton<LevelInfo>.i?.LoadedMapSettings;
                Vector2 span = map != null ? map.MapSize : TheaterFrame.Resolve();
                return Sa74BlastShape.Radius(span.x, span.y);
            }
        }
        internal static float FrontRadius(float age) => Sa74BlastShape.FrontRadius(age, ShockRadius);

        /// <summary>Called exactly once by the armed, server-owned bomb. Position is local world space.</summary>
        internal static void Detonate(Vector3 position, Unit source)
        {
            if (!GameAccess.IsServer() || Instance == null || Datum.origin == null || !Finite(position)) return;
            Events++;
            var site = new Site { Id = ++nextId, Position = position.ToGlobalPosition(), Started = Time.time,
                Owner = source != null ? source.persistentID.Id : 0 };
            // ponytail: retain sixteen distinct scars; later blasts still explode and keep winter active.
            // A map-editor archive is the upgrade path if missions ever expend more than sixteen strategic bombs.
            if (Sites.Count < MaxSites) Sites.Add(site);
            Instance.ApplySite(site, true);
            Sa74Net.Broadcast(site);
        }

        internal static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z) &&
            !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z) && p.sqrMagnitude < 1e14f;

        internal static void Receive(uint epoch, uint id, Vector3 global, float age, uint owner, int events)
        {
            if (Instance == null || Datum.origin == null || !Finite(global) || id == 0 || age < 0f || age > 864000f) return;
            if (Epoch != epoch) { Instance.Clear(); Epoch = epoch; }
            Events = Math.Max(Events, events);
            if (Sites.Exists(s => s.Id == id) || (Sites.Count >= MaxSites && id <= lastReceivedId)) return;
            lastReceivedId = Math.Max(lastReceivedId, id);
            var site = new Site { Id = id, Position = new GlobalPosition(global), Started = Time.time - age, Owner = owner };
            if (Sites.Count < MaxSites) Sites.Add(site);
            Instance.ApplySite(site, age < 2f);
        }

        private void ApplySite(Site site, bool explosion)
        {
            Sa74Destruction.Observe(site);
            BoscaliSummer.Core.Contracts.RadioAftermath.Observe(Epoch, site.Id, Time.time, Mathf.Max(0f, Time.time - site.Started));
            Vector3 at = site.Position.ToLocalPosition();
            if (explosion) NativeBlast(at, site.Owner, site.Id);
            else
            {
                var detailBlasts = SceneSingleton<NuclearOption.Effects.BlastManager>.i;
                if (detailBlasts != null) detailBlasts.AddBlast(site.Position, ShockRadius * .5f);
            }
            terrain.Scar(at, CraterRadius);
            CaptureWinter();
            if (!Application.isBatchMode) EnsureDressing();
            Presentation.Sa74BlastAudio.Observe(site, explosion);
        }

        private void NativeBlast(Vector3 at, uint owner, uint siteId)
        {
            Missile donor = null;
            Encyclopedia encyclopedia = GameAccess.EncyclopediaOrNull;
            if (encyclopedia != null)
                foreach (MissileDefinition definition in encyclopedia.missiles)
                {
                    Missile candidate = definition != null && definition.unitPrefab != null ? definition.unitPrefab.GetComponent<Missile>() : null;
                    if (candidate != null && BlastYield(candidate) > 200f && (donor == null || BlastYield(candidate) > BlastYield(donor))) donor = candidate;
                }
            GameObject effect = null;
            object warhead = donor != null ? Warhead.GetValue(donor) : null;
            if (warhead != null)
            {
                // Cargo's contact fuse can penetrate a few centimetres below the water plane.
                // A strategic surface burst keeps its fireball above water rather than hiding the entire effect.
                effect = AccessTools.Field(typeof(Missile.Warhead), "terrainEffect")?.GetValue(warhead) as GameObject;
                if (effect == null) effect = AccessTools.Field(typeof(Missile.Warhead), "airEffect")?.GetValue(warhead) as GameObject;
            }
            GameObject blast = effect != null ? UnityEngine.Object.Instantiate(effect, Datum.origin) : new GameObject("SA-74N blast");
            if (effect == null) blast.transform.SetParent(Datum.origin, false);
            blast.name = "SA-74N FLASHBACK / 74 Mt";
            bool waterContact = at.y < Datum.LocalSeaY + 1f;
            Vector3 visualPosition = at;
            if (waterContact) visualPosition.y = Datum.LocalSeaY + 8f;
            blast.transform.SetPositionAndRotation(visualPosition, Quaternion.LookRotation(Vector3.up));
            Sa74NativeGround.Prepare(blast, YieldKilotons);
            // Scale the authored fireball, not the Shockwave's independently world-sized cloud/decal.
            if (donor != null)
            {
                float scale = Mathf.Pow(YieldKilotons * 1e6f / BlastYield(donor), 1f / 3f);
                foreach (ParticleSystem particles in blast.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = particles.main;
                    main.startSizeMultiplier *= scale;
                    main.startSpeedMultiplier *= scale;
                    var shape = particles.shape;
                    if (shape.enabled) shape.scale *= scale;
                }
                // The cloned stock point light also feeds native eye adaptation. Our thermal light/glare owns this envelope.
                foreach (MonoBehaviour component in blast.GetComponentsInChildren<MonoBehaviour>(true))
                    if (component != null && component.GetType().Name == "LightAnimation") component.enabled = false;
                foreach (Light light in blast.GetComponentsInChildren<Light>(true)) { light.enabled = false; light.intensity = 0f; }
                foreach (MushroomCloud cloud in blast.GetComponentsInChildren<MushroomCloud>(true))
                {
                    cloud.yield = YieldKilotons * 1e6f;
                    ScaleMushroom(cloud, scale);
                    BrightenFireball(cloud);
                }
            }
            blast.AddComponent<Sa74BlastTag>().SiteId = siteId;
            Shockwave shockwave = blast.GetComponentInChildren<Shockwave>(true);
            if (waterContact && shockwave != null)
            {
                // Preserve the true damage origin while the visible fireball sits above the contact surface.
                shockwave.enabled = false;
                UnityEngine.Object.Destroy(shockwave);
                var front = new GameObject("SA-74N shock front");
                front.transform.SetParent(Datum.origin, false); front.transform.position = at;
                front.AddComponent<Sa74BlastTag>().SiteId = siteId;
                shockwave = front.AddComponent<Shockwave>();
                blasts.Add(front);
                UnityEngine.Object.Destroy(front, Sa74BlastShape.ArrivalSeconds(ShockRadius) + 45f);
            }
            if (shockwave == null) shockwave = blast.AddComponent<Shockwave>();
            shockwave.SetOwner(new PersistentID { Id = owner }, YieldKilotons);
            NativeBlasts++;
            blasts.Add(blast);
            UnityEngine.Object.Destroy(blast, Sa74BlastShape.ArrivalSeconds(ShockRadius) + 45f);
        }

        private static void ScaleMushroom(MushroomCloud cloud, float scale)
        {
            // The game's scaleAdjust is computed but unused: its mesh cap/fireball are driven by authored curves.
            // Scale those lengths and rise speeds on this cloned effect; colours, emission rates and timing stay native.
            var lengths = new HashSet<string> { "horizontalSizeOverTime", "riseRateOverTime", "sizeOverTime", "radiusOverTime", "startSizeOverTime" };
            Visit(cloud);
            void Visit(object owner)
            {
                if (owner == null) return;
                foreach (FieldInfo field in owner.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    object value = field.GetValue(owner);
                    if (value == null) continue;
                    if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(Optional<>))
                        value = field.FieldType.GetProperty("Value")?.GetValue(value, null);
                    if (value is AnimationCurve curve && lengths.Contains(field.Name))
                    {
                        Keyframe[] keys = curve.keys;
                        for (int i = 0; i < keys.Length; i++)
                        {
                            keys[i].value *= scale; keys[i].inTangent *= scale; keys[i].outTangent *= scale;
                        }
                        curve.keys = keys;
                    }
                    else if (value != null && value.GetType().DeclaringType == typeof(MushroomCloud)) Visit(value);
                }
            }
        }

        private static void BrightenFireball(MushroomCloud cloud)
        {
            // Keep a brilliant native fallback while the density shader/noise prepares. Never modify the donor prefab.
            AccessTools.Field(typeof(MushroomCloud), "lensFlareIntensity")?.SetValue(cloud, AnimationCurve.Constant(0f, 1f, 0f));
            object optional = AccessTools.Field(typeof(MushroomCloud), "fireball")?.GetValue(cloud);
            object fireball = optional?.GetType().GetProperty("Value")?.GetValue(optional, null);
            if (fireball == null) return;
            FieldInfo rendererField = AccessTools.Field(fireball.GetType(), "renderer");
            MeshRenderer renderer = rendererField?.GetValue(fireball) as MeshRenderer;
            float unitRadius = .5f;
            MeshFilter filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            if (filter != null && filter.sharedMesh != null)
            {
                Vector3 extents = filter.sharedMesh.bounds.extents;
                unitRadius = Mathf.Max(.01f, Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z)));
                if (renderer.transform.parent != null) unitRadius *= Mathf.Abs(renderer.transform.parent.lossyScale.x);
            }
            unitRadius = Mathf.Max(.01f, unitRadius);
            var size = new AnimationCurve();
            float[] times = { 0f, .06f, .18f, 1.1f, 4f, 8f, 15f, 28f, 45f, 65f };
            float[] radii = { 10f, 480f, 1050f, 2450f, 4600f, 5000f, 5300f, 5000f, 3800f, 0f };
            var emission = new AnimationCurve();
            for (int i = 0; i < times.Length; i++)
            {
                size.AddKey(new Keyframe(times[i], radii[i] / unitRadius));
                emission.AddKey(new Keyframe(times[i], 95f * Sa74Flash.Pulse(times[i])));
            }
            AccessTools.Field(fireball.GetType(), "sizeOverTime")?.SetValue(fireball, size);
            AccessTools.Field(fireball.GetType(), "emitOverTime")?.SetValue(fireball, emission);
            var color = new Gradient();
            color.SetKeys(new[] {
                new GradientColorKey(new Color(2.3f, 2.4f, 2.5f), 0f),
                new GradientColorKey(new Color(2.2f, 2.08f, 1.65f), .09f),
                new GradientColorKey(new Color(1.6f, .95f, .24f), .24f),
                new GradientColorKey(new Color(.8f, .23f, .04f), .55f),
                new GradientColorKey(new Color(.12f, .05f, .02f), 1f)
            }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            AccessTools.Field(fireball.GetType(), "colorOverTime")?.SetValue(fireball, color);
        }

        private float WinterStrength()
        {
            float age = 0f;
            foreach (Site site in Sites) age = Mathf.Max(age, Time.time - site.Started);
            return Sa74Flash.Winter(age);
        }

        private void CaptureWinter()
        {
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (winterCaptured || level == null) return;
            winterLevel = level;
            beforeConditions = level.conditions;
            beforeCloudHeight = level.cloudHeight;
            winterCaptured = true;
        }

        internal static void Winter(LevelInfo level)
        {
            if (!Active || Instance == null || level == null) return;
            Instance.CaptureWinter();
            float strength = Instance.WinterStrength();
            // Above .6 vanilla switches off sunlight and adds near-opaque ground fog. Fallout is an elevated dirty deck, not that storm gate.
            level.conditions = Mathf.Lerp(Instance.beforeConditions, .6f, strength);
            level.cloudHeight = Mathf.Lerp(Instance.beforeCloudHeight, 6000f, strength);
        }

        internal static void ReceiveReset(uint epoch)
        {
            if (Instance == null) return;
            if (Active || Epoch != epoch) Instance.Clear();
            Epoch = epoch;
        }

        private void EnsureDressing()
        {
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (dressing != null || level == null || level.PostProcessing == null) return;
            dressing = new GameObject("SA-74N permanent nuclear winter");
            dressing.transform.SetParent(transform, false);
            dressing.layer = level.PostProcessing.gameObject.layer;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            grade = profile.Add<ColorAdjustments>();
            grade.colorFilter.Override(Color.white);
            grade.contrast.Override(0f);
            balance = profile.Add<WhiteBalance>();
            balance.temperature.Override(0f);
            balance.tint.Override(0f);
            radiationGrain = profile.Add<FilmGrain>();
            // Native lookup textures are stripped. Reuse the same valid custom tile as the EO feed and cinematic grade.
            radiationGrain.type.Override(FilmGrainLookup.Custom);
            radiationGrain.texture.Override(FeedCamera.Noise());
            radiationGrain.response.Override(.65f);
            radiationGrain.intensity.Override(0f);
            volume = dressing.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = .9f; // above display grading, below native night vision
            volume.sharedProfile = profile;
            bloom = profile.Add<Bloom>();
            bloom.threshold.Override(1.15f); bloom.scatter.Override(.72f);
            var lightObject = new GameObject("SA-74N thermal illumination");
            lightObject.transform.SetParent(dressing.transform, false);
            flashLight = lightObject.AddComponent<Light>();
            flashLight.type = LightType.Directional; flashLight.shadows = LightShadows.None;
            flashLight.color = new Color(1f, .91f, .73f); flashLight.intensity = 0f;
            CreateAsh();
            CreateGeiger();
        }

        private void CreateAsh()
        {
            ashRoot = new GameObject("SA-74N permanent fallout snowfall");
            ashRoot.transform.SetParent(Datum.origin, false);
            ash = ashRoot.AddComponent<ParticleSystem>();
            ash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ash.main;
            main.loop = true; main.startLifetime = 10000f;
            main.startSize = .15f;
            main.startSpeed = 0f; main.startColor = Color.white;
            main.maxParticles = snow.Length;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = Datum.origin;
            var shape = ash.shape; shape.enabled = false;
            var emission = ash.emission; emission.enabled = false;
            ashTexture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var pixels = new Color[256];
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
            {
                float r = new Vector2((x - 7.5f) / 7.5f, (y - 7.5f) / 7.5f).magnitude;
                float edge = 1f + .15f * Mathf.Sin(Mathf.Atan2(y - 7.5f, x - 7.5f) * 5f);
                pixels[y * 16 + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((edge - r) * 4f));
            }
            ashTexture.SetPixels(pixels); ashTexture.Apply(false, true);
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            if (shader != null)
            {
                ashMaterial = new Material(shader) { name = "SA-74N ash", mainTexture = ashTexture, renderQueue = 3100 };
                ashMaterial.SetFloat("_Surface", 1f); ashMaterial.SetFloat("_Blend", 0f);
                ashMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); ashMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                ashMaterial.SetInt("_ZWrite", 0); ashMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                ashMaterial.SetTexture("_BaseMap", ashTexture);
                var renderer = ash.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = ashMaterial; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                renderer.maxParticleSize = .012f;
                renderer.sortMode = ParticleSystemSortMode.Distance;
            }
            ash.Play();
        }

        private void Snowfall(Camera camera, LevelInfo level)
        {
            if (ash == null || camera == null || Datum.origin == null) return;
            // A periodic world-space field remains populated after high-speed flight, floating-origin shifts and aged snapshots.
            // Two size/depth bands share one 1,500-particle draw system; no terrain rays or per-frame allocations.
            GlobalPosition eye = camera.transform.position.ToGlobalPosition();
            if (eye.y < -1f) { ash.SetParticles(snow, 0); return; }
            Vector3 wind = level != null ? Vector3.ClampMagnitude(level.windVelocity, 12f) : new Vector3(2.5f, 0f, .8f);
            float light = level != null ? Mathf.Clamp(level.GetAmbientLight() * 2.5f, .18f, 1f) : 1f;
            float age = 0f;
            foreach (Site site in Sites) age = Mathf.Max(age, Time.time - site.Started);
            int count = 0;
            for (int i = 0; i < snow.Length; i++)
            {
                uint seed = unchecked((uint)i * 104729u + 74074u);
                float a = SnowHash(seed), b = SnowHash(seed + 17u), c = SnowHash(seed + 97u);
                bool near = i < 900;
                float width = near ? 70f : 190f, height = near ? 55f : 110f;
                float fall = 1.1f + c * 1.8f;
                Vector3 relative = new Vector3(
                    Mathf.Repeat(a * width + wind.x * age - eye.x, width) - width * .5f,
                    Mathf.Repeat(b * height - fall * age - eye.y, height) - height * .5f + 12f,
                    Mathf.Repeat(c * width + wind.z * age - eye.z, width) - width * .5f);
                relative.x += Mathf.Sin(age * .7f + c * 30f) * .8f;
                // Keep flakes outside a cockpit-sized clear bubble; scene depth supplies terrain/building occlusion.
                if (relative.sqrMagnitude < 49f) continue;
                float edge = Mathf.Clamp01((1f - Mathf.Max(Mathf.Abs(relative.x), Mathf.Abs(relative.z)) / (width * .5f)) * 5f);
                float alpha = winterBlend * edge * (.45f + .4f * b);
                if (alpha < .01f) continue;
                snow[count++] = new ParticleSystem.Particle
                {
                    position = Datum.origin.InverseTransformPoint(camera.transform.position + relative),
                    startSize = near ? .07f + a * .18f : .24f + a * .32f,
                    startColor = new Color((.86f + c * .1f) * light, (.89f + c * .08f) * light, (.9f + c * .07f) * light, alpha),
                    rotation = a * 360f + age * (b - .5f) * 15f,
                    startLifetime = 10000f, remainingLifetime = 10000f, randomSeed = seed + 1u
                };
            }
            ash.SetParticles(snow, count);
            ash.GetComponent<ParticleSystemRenderer>().bounds = new Bounds(camera.transform.position, Vector3.one * 240f);
        }

        private static float SnowHash(uint value)
        {
            unchecked { value ^= value >> 16; value *= 0x7feb352du; value ^= value >> 15; value *= 0x846ca68bu; value ^= value >> 16; }
            return (value & 0x00ffffffu) / 16777215f;
        }

        private void AftermathHaze()
        {
            float current = RenderSettings.fogDensity;
            if (current > .02f) { hazeApplied = false; return; } // Preserve the native underwater preset.
            if (!hazeApplied || !Mathf.Approximately(current, writtenFog)) beforeFog = current;
            // Weather's own late haze can still obscure the horizon. Bound extinction without modifying its wind or cloud simulation.
            writtenFog = Mathf.Lerp(beforeFog, Mathf.Min(beforeFog, .00007f), winterBlend);
            RenderSettings.fogDensity = writtenFog;
            hazeApplied = true;
        }

        private void CreateGeiger()
        {
            crackle = SfxClips.Get("sa74-geiger");
            if (crackle == null) return;
            geiger = dressing.AddComponent<AudioSource>();
            geiger.clip = crackle; geiger.loop = true; geiger.playOnAwake = false; geiger.spatialBlend = 0f;
            geiger.volume = .12f; geiger.priority = 150;
        }

        private void LateUpdate()
        {
            if (!Active) return;
            CaptureWinter();
            Winter(NetworkSceneSingleton<LevelInfo>.i);
            if (Application.isBatchMode) return;
            EnsureDressing();
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Camera camera = cameras != null ? cameras.mainCamera : null;
            if (camera == null) camera = Camera.main;
            bool nv = GameAccess.TryGetNightVisionState(out _, out bool night) && night;
            if (volume != null) volume.enabled = !nv;
            float nearest = float.MaxValue, illumination = 0f;
            Vector3 source = Vector3.zero;
            flashExposure = thermalPulse = 0f;
            foreach (Site site in Sites)
            {
                float age = Mathf.Max(0f, Time.time - site.Started);
                float pulse = Sa74Flash.Pulse(age);
                thermalPulse = Mathf.Max(thermalPulse, pulse);
                if (camera == null) continue;
                Vector3 from = site.Position.ToLocalPosition() + Vector3.up * 500f;
                Vector3 toward = from - camera.transform.position;
                float distance = toward.magnitude;
                nearest = Mathf.Min(nearest, distance);
                float range = Sa74Flash.Range(distance);
                float light = pulse * range;
                if (light > illumination) { illumination = light; source = from; }
                float glare = light * Sa74Flash.Facing(Vector3.Dot(camera.transform.forward, toward.normalized));
                flashExposure = Mathf.Max(flashExposure, glare * Sa74Flash.GlareGain(age));
            }
            winterBlend = WinterStrength();
            radiationStrength = winterBlend * Mathf.Pow(Mathf.Clamp01(1f - nearest / ShockRadius), 2f);
            AftermathHaze();
            Snowfall(camera, NetworkSceneSingleton<LevelInfo>.i);
            if (grade != null)
            {
                grade.postExposure.Override(flashExposure + AtmosphereExposure);
                grade.colorFilter.Override(Color.Lerp(Color.white, new Color(.94f, .97f, 1f), winterBlend));
                grade.contrast.Override(3f * winterBlend);
                grade.saturation.Override(-24f * winterBlend);
                balance.temperature.Override(-12f * winterBlend); balance.tint.Override(-1f * winterBlend);
                radiationGrain.intensity.Override(.05f * radiationStrength);
                bloom.active = illumination > .008f;
                bloom.intensity.Override(illumination * (thermalPulse > .5f ? 4f : 1.5f));
            }
            if (flashLight != null)
            {
                // ponytail: parallel rays approximate a distant source over the local view; no extra shadow map or full-map point light.
                flashLight.enabled = illumination > .008f;
                flashLight.intensity = illumination * 9f;
                if (camera != null && illumination > .008f && (camera.transform.position - source).sqrMagnitude > .001f)
                    flashLight.transform.rotation = Quaternion.LookRotation(camera.transform.position - source);
            }
            if (geiger != null)
            {
                if (SoundManager.i != null) geiger.outputAudioMixerGroup = SoundManager.i.EffectsMixer;
                geiger.volume = .015f + .27f * radiationStrength;
                geiger.pitch = .9f + .4f * radiationStrength;
                if (radiationStrength > .005f)
                {
                    if (!geiger.isPlaying && geiger.outputAudioMixerGroup != null && FxVoiceBus.TryStartLoop("sa74-geiger")) geiger.Play();
                }
                else if (geiger.isPlaying) { geiger.Stop(); FxVoiceBus.EndLoop("sa74-geiger"); }
            }
        }

        internal Dictionary<string, object> Snapshot() => new Dictionary<string, object>
        {
            { "ok", true }, { "active", Active }, { "events", Events }, { "sites", Sites.Count },
            { "nativeBlasts", NativeBlasts }, { "blastColliders", BlastColliders },
            { "craters", terrain.Scars }, { "terrainChanged", terrain.ChangedCells }, { "terrainRestored", terrain.RestoredCells },
            { "audio", geiger != null && geiger.isPlaying }, { "ash", ash != null && ash.isPlaying },
            { "tint", profile != null }, { "yieldKt", YieldKilotons }, { "epoch", Epoch },
            { "blastRadiusKm", ShockRadius / 1000f }, { "mapWidth", TheaterFrame.Resolve().x }, { "mapHeight", TheaterFrame.Resolve().y },
            { "conditions", LevelInfo.i != null ? LevelInfo.i.conditions : 0f },
            { "thermalPulse", thermalPulse }, { "flashExposure", flashExposure }, { "winterBlend", winterBlend },
            { "flashLight", flashLight != null && flashLight.enabled ? flashLight.intensity : 0f },
            { "snowParticles", SnowParticles }, { "snowBudget", SnowBudget }, { "snowEmitting", SnowEmitting },
            { "radiationStrength", RadiationStrength }, { "radiationGrain", RadiationGrain }, { "atmosphereExposure", AtmosphereExposure },
            { "cloudHeight", LevelInfo.i != null ? LevelInfo.i.cloudHeight : 0f }, { "fogDensity", RenderSettings.fogDensity },
            { "destruction", Sa74Destruction.Snapshot() },
            { "mushroomReady", Presentation.Sa74Mushroom.Ready }, { "mushroomFrame", Presentation.Sa74Mushroom.RenderedFrame },
            { "mushroomSites", Presentation.Sa74Mushroom.VisibleSites }, { "mushroomWidth", Presentation.Sa74Mushroom.TargetWidth },
            { "mushroomHeight", Presentation.Sa74Mushroom.TargetHeight },
            { "mushroomDispersed", Presentation.Sa74Mushroom.DispersedSites },
            { "mushroomRemaining", Presentation.Sa74Mushroom.RemainingSeconds }, { "mushroomBytes", Presentation.Sa74Mushroom.TargetBytes },
            { "nativeFallbackRenderers", Presentation.Sa74Mushroom.NativeFallbackRenderers },
            { "nativeSuppressedRenderers", Presentation.Sa74Mushroom.NativeSuppressedRenderers },
            { "particlesReady", Presentation.Sa74Particles.Ready }, { "particleSystems", Presentation.Sa74Particles.Systems },
            { "particles", Presentation.Sa74Particles.Particles }, { "particleSites", Presentation.Sa74Particles.ActiveSites },
            { "condensationParticles", Presentation.Sa74Particles.CondensationParticles }, { "ejectaParticles", Presentation.Sa74Particles.EjectaParticles },
            { "dustParticles", Presentation.Sa74Particles.DustParticles },
            { "pressureParticles", Presentation.Sa74Particles.PressureParticles },
            { "particleDiagnostics", Presentation.Sa74Particles.Diagnostics() },
            { "blastAudioReady", Presentation.Sa74BlastAudio.Ready }, { "blastSoundsPlayed", Presentation.Sa74BlastAudio.Played },
            { "blastSoundClip", Presentation.Sa74BlastAudio.ClipName }, { "blastSoundEffectsMixer", Presentation.Sa74BlastAudio.EffectsRouted },
            { "blastSoundVoices", Presentation.Sa74BlastAudio.Voices }, { "blastSoundPending", Presentation.Sa74BlastAudio.Pending },
            { "blastSoundDelay", Presentation.Sa74BlastAudio.LastDelay }, { "blastSoundGain", Presentation.Sa74BlastAudio.Gain },
            { "blastSoundOutputRms", Presentation.Sa74BlastAudio.OutputRms }, { "nativeAudioSuppressed", Presentation.Sa74BlastAudio.NativeSuppressed },
            { "nativeSootCount", Sa74NativeGround.ConfiguredCount }, { "nativeSootDiameter", Sa74NativeGround.ConfiguredDiameter },
            { "nativeGroundSourceKt", Sa74NativeGround.SourceYieldKilotons },
            { "particleRadius", Presentation.Sa74Particles.OuterRadius },
            { "cloudClearanceCount", Core.Math.CloudClearance.Current.Length },
            { "cloudClearancePublications", Sa74CloudClearance.Publications },
            { "cloudHoles", Core.Math.CloudClearance.Current.Length },
            { "cloudHoleRadius", Core.Math.CloudClearance.Current.Length > 0 ? Core.Math.CloudClearance.Current[0].Radius : 0f }
        };

        public void ResetForScene() { Clear(); unchecked { Epoch++; } }
        internal void Clear()
        {
            Sa74Destruction.Reset();
            BoscaliSummer.Core.Contracts.RadioAftermath.Reset();
            Sa74NativeGround.Reset();
            terrain.Clear();
            if (winterCaptured && winterLevel != null) { winterLevel.conditions = beforeConditions; winterLevel.cloudHeight = beforeCloudHeight; }
            if (hazeApplied && Mathf.Approximately(RenderSettings.fogDensity, writtenFog)) RenderSettings.fogDensity = beforeFog;
            hazeApplied = false;
            winterCaptured = false; winterLevel = null;
            foreach (GameObject blast in blasts) if (blast != null) UnityEngine.Object.Destroy(blast);
            blasts.Clear();
            if (dressing != null) UnityEngine.Object.Destroy(dressing);
            if (ashRoot != null) UnityEngine.Object.Destroy(ashRoot);
            if (profile != null) { foreach (VolumeComponent component in profile.components) UnityEngine.Object.Destroy(component); UnityEngine.Object.Destroy(profile); }
            if (ashMaterial != null) UnityEngine.Object.Destroy(ashMaterial);
            if (ashTexture != null) UnityEngine.Object.Destroy(ashTexture);
            FxVoiceBus.EndLoop("sa74-geiger");
            dressing = ashRoot = null; profile = null; volume = null; grade = null; balance = null; bloom = null; radiationGrain = null; flashLight = null;
            ash = null; ashMaterial = null; ashTexture = null; geiger = null; crackle = null;
            flashExposure = thermalPulse = winterBlend = radiationStrength = 0f;
            Sites.Clear(); Events = NativeBlasts = BlastColliders = 0;
            lastReceivedId = 0;
        }
        private void OnDestroy() { Clear(); if (Instance == this) Instance = null; }
    }

    internal sealed class Sa74BlastTag : MonoBehaviour { internal uint SiteId; }

    /// <summary>Only the strategic blast gets a larger target buffer; vanilla detonations retain their own allocation.</summary>
    [HarmonyPatch(typeof(Shockwave), "Start")]
    internal static class Sa74BlastTargetsPatch
    {
        private static readonly FieldInfo Buffer = AccessTools.Field(typeof(Shockwave), "colliderBuffer");
        private static readonly FieldInfo Yield = AccessTools.Field(typeof(Shockwave), "yieldKilotons");
        private sealed class StartState { internal Collider[] Buffer; internal float Yield, Power, Radius; }
        private static void Prefix(Shockwave __instance, ref Collider[] ___colliderBuffer, ref float ___yieldKilotons, ref StartState __state)
        {
            if (__instance.GetComponentInParent<Sa74BlastTag>() == null) return;
            float radius = Sa74Fallout.ShockRadius;
            __state = new StartState { Buffer = ___colliderBuffer, Yield = ___yieldKilotons,
                Power = Mathf.Pow(___yieldKilotons * 1e6f, .3333f), Radius = radius };
            ___yieldKilotons = Sa74BlastShape.InitializationYield(radius);
            // One allocation per strategic blast, never a per-frame full-map search.
            Collider[] targets = Physics.OverlapSphere(__instance.transform.position, radius * 2f);
            Sa74Fallout.BlastColliders = targets.Length;
            ___colliderBuffer = targets.Length > __state.Buffer.Length ? targets : __state.Buffer;
        }
        private static void Postfix(StartState __state, ref float ___yieldKilotons, ref float ___blastPower,
            ref float ___blastRadius, ref float ___blastPropagation)
        {
            if (__state == null) return;
            // Keep the actual 74 Mt damage/impulse model; only the requested reach/query/decal footprint is expanded.
            ___yieldKilotons = __state.Yield; ___blastPower = __state.Power;
            ___blastRadius = __state.Radius; ___blastPropagation = Sa74BlastShape.InitialRadius;
        }
        private static Exception Finalizer(Shockwave __instance, Exception __exception, StartState __state)
        {
            if (__state != null)
            {
                Buffer.SetValue(null, __state.Buffer);
                Yield.SetValue(__instance, __state.Yield);
                if (__exception != null) __instance.enabled = false; // A partial native Start must never tick with its temporary initialization power.
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(LevelInfo), "UpdateVisuals")]
    internal static class Sa74WinterPatch
    {
        private static void Prefix(LevelInfo __instance) => Sa74Fallout.Winter(__instance);
    }

    /// <summary>Weather owns the final native write. Apply the strategic override at that seam, in every update phase.</summary>
    [HarmonyPatch(typeof(BoscaliSummer.Modules.Weather.Runtime.WeatherManager), "ApplyNative")]
    internal static class Sa74WeatherWritePatch
    {
        private static void Postfix(LevelInfo level) => Sa74Fallout.Winter(level);
    }
}
