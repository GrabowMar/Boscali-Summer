using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.FireAndDestruction.Configuration;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Fire
{
    /// <summary>Grounded concrete pressure fronts and ballistic grit. Four collapse slots or six impact slots.</summary>
    internal sealed class CollapseBurstPool
    {
        private sealed class Burst
        {
            internal GameObject Root;
            internal ParticleSystem Dust, Grit;
            internal ParticleSystemRenderer GritRenderer;
            internal AudioSource Sound;
            internal GlobalPosition Position;
            internal Vector2 Footprint;
            internal uint Seed;
            internal int LoftCount;
            internal float Expires, LoftAt;
        }
        private readonly List<Burst> bursts = new List<Burst>(6);
        private readonly int impactCapacity;
        private Material dustMaterial, concreteMaterial;
        private Texture2D dustTexture, concreteTexture;
        private Mesh gritMesh;
        private bool depthLease;

        private FireAndDestructionSettings settings;

        internal CollapseBurstPool(int impactCapacity = 0) { this.impactCapacity = impactCapacity; }
        /// <summary>The collapse pool (no fixed impact capacity) sizes itself from these; its owner hands them over.</summary>
        internal void Configure(FireAndDestructionSettings fire) => settings = fire;
        internal void Warm()
        {
            if (GameManager.IsHeadless || bursts.Count != 0 || !EnsureAssets()) return;
            bursts.Add(Create());
        }
        public void Emit(GlobalPosition position, Vector2 halfExtents) => Emit(position, Vector3.up, halfExtents, true);
        internal void EmitImpact(GlobalPosition position, Vector3 normal, float size) =>
            Emit(position, normal, new Vector2(Mathf.Clamp(size * 0.4f, 1f, 5f), Mathf.Clamp(size * 0.4f, 1f, 5f)), false);

        private void Emit(GlobalPosition position, Vector3 normal, Vector2 footprint, bool collapse)
        {
            if (GameManager.IsHeadless) return;
            Vector3 origin = position.ToLocalPosition();
            float distanceSq = DistanceSq(origin);
            if (distanceSq > 1600f * 1600f || !EnsureAssets()) return;
            Burst burst = null;
            foreach (Burst candidate in bursts) if (!candidate.Root.activeSelf) { burst = candidate; break; }
            if (burst == null)
            {
                int maximum = impactCapacity > 0 ? Mathf.Clamp(impactCapacity, 1, 6) :
                    FxBudget.ScaleCount(settings.MaximumCollapseBursts, FxBus.Scales.Particles);
                if (bursts.Count >= maximum) return;
                burst = Create(); bursts.Add(burst);
            }
            // Acquire only for a real slot. The pooled effect owns the whole burst, including the delayed loft.
            if (!depthLease) depthLease = DestructionAssets.AcquireDepth();
            burst.Position = position; burst.Expires = Time.timeSinceLevelLoad + (collapse ? 7f : 3.2f);
            burst.Root.transform.position = origin; burst.Root.SetActive(true);
            burst.Dust.Clear(); burst.Grit.Clear();
            bool near = distanceSq < 400f * 400f, middle = distanceSq < 900f * 900f;
            ParticleSystem.CollisionModule collision = burst.Grit.collision;
            collision.enabled = distanceSq < 250f * 250f;
            burst.GritRenderer.shadowCastingMode = distanceSq < 300f * 300f ? ShadowCastingMode.On : ShadowCastingMode.Off;
            ParticleSystem.NoiseModule noise = burst.Dust.noise; noise.enabled = middle;
            burst.Dust.Play(); burst.Grit.Play();
            if (collapse) Rumble(burst);
            normal = normal.sqrMagnitude < 0.001f ? Vector3.up : normal.normalized;
            Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.85f ? Vector3.forward : Vector3.up).normalized;
            Vector3 other = Vector3.Cross(normal, tangent).normalized;
            uint seed = Deterministic.Hash(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.z), 211);
            bool small = !collapse && footprint.x <= 1.01f;
            int dustCount = collapse ? (near ? 28 : middle ? 20 : 12) : (small ? 8 : near ? 18 : 12);
            burst.Seed = seed; burst.Footprint = footprint;
            burst.LoftCount = collapse ? dustCount / 2 : 0;
            burst.LoftAt = Time.timeSinceLevelLoad + 0.35f;
            float extent = Mathf.Max(footprint.x, footprint.y);
            for (int i = 0; i < dustCount; i++)
            {
                float angle = i * 2.399963f + FractureGeometry.Rand(seed, 3) * 6f;
                Vector3 radial = tangent * Mathf.Cos(angle) + other * Mathf.Sin(angle);
                float ring = Mathf.Sqrt(FractureGeometry.Rand(seed, i + 11));
                Vector3 offset = collapse ? new Vector3(Mathf.Cos(angle) * footprint.x * ring,
                    0.15f,
                    Mathf.Sin(angle) * footprint.y * ring) : radial * ring * extent;
                Vector3 velocity = collapse
                    ? new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (4f + ring * 7f) + Vector3.up * (0.6f + FractureGeometry.Rand(seed, i + 31) * 1.2f)
                    : normal * (4f + ring * 6f) + radial * 3f + Vector3.up * 0.6f;
                float size = collapse ? Mathf.Clamp(extent * 0.32f, 3f, 7.5f) : 1.2f + extent * 0.55f;
                EmitDust(burst, origin + offset + normal * 0.3f, velocity, size * (0.7f + ring * 0.55f),
                    collapse ? 4.2f + ring * 2.2f : small ? 0.6f + ring * 0.5f : 1.4f + ring * 1.3f, seed, i);
            }
            int gritCount = !middle ? 0 : collapse ? (near ? 24 : 12) : (small ? 4 : 8);
            for (int i = 0; i < gritCount; i++)
            {
                float angle = i * 2.399963f;
                Vector3 radial = tangent * Mathf.Cos(angle) + other * Mathf.Sin(angle);
                var parameters = new ParticleSystem.EmitParams
                {
                    position = Datum.origin.InverseTransformPoint(origin + normal * 0.5f + radial * extent * 0.5f),
                    velocity = normal * (3f + FractureGeometry.Rand(seed, i + 71) * 6f) + radial * (collapse ? 7f : 4f),
                    startLifetime = collapse ? 3.5f : 2f,
                    startSize = (small ? 0.05f : 0.12f) + FractureGeometry.Rand(seed, i + 91) * (collapse ? 0.65f : small ? 0.12f : 0.32f),
                    startColor = new Color(0.6f, 0.57f, 0.51f, 1f),
                    rotation3D = new Vector3(i * 73f, i * 137f, i * 51f)
                };
                burst.Grit.Emit(parameters, 1);
            }
        }

        // One recorded rumble per collapse, positional, capped by the voice bus. The weapon already made the bang.
        private static void Rumble(Burst burst)
        {
            AudioClip clip = SfxClips.Get("building-collapse");
            if (clip == null || !FxVoiceBus.TryOneShot(clip.length)) return;
            if (burst.Sound == null)
            {
                burst.Sound = burst.Root.AddComponent<AudioSource>();
                burst.Sound.playOnAwake = false; burst.Sound.spatialBlend = 1f; burst.Sound.dopplerLevel = 0f;
                burst.Sound.rolloffMode = AudioRolloffMode.Linear; burst.Sound.minDistance = 150f; burst.Sound.maxDistance = 4000f;
            }
            SoundManager sound = SoundManager.i;
            if (sound != null) burst.Sound.outputAudioMixerGroup = sound.EffectsMixer;
            burst.Sound.PlayOneShot(clip);
            Rumbles++;
        }

        /// <summary>Collapse rumbles played since load (automation readout).</summary>
        internal static int Rumbles { get; private set; }

        internal void EmitLanding(GlobalPosition position, float size)
        {
            Vector3 origin = position.ToLocalPosition() + Vector3.up * 0.2f;
            if (GameManager.IsHeadless || DistanceSq(origin) > 1600f * 1600f) return;
            Burst nearest = null; float best = 10000f;
            foreach (Burst burst in bursts)
            {
                if (!burst.Root.activeSelf) continue;
                float distance = (burst.Position - position).sqrMagnitude;
                if (distance < best) { best = distance; nearest = burst; }
            }
            if (nearest == null) return;
            float life = Mathf.Clamp(2.5f + size * 0.25f, 2.5f, 5f);
            nearest.Expires = Mathf.Max(nearest.Expires, Time.timeSinceLevelLoad + life);
            for (int i = 0; i < 5; i++)
            {
                float angle = i * 1.2566f;
                EmitDust(nearest, origin, new Vector3(Mathf.Cos(angle) * 4f, 0.6f, Mathf.Sin(angle) * 4f),
                    Mathf.Clamp(size, 2f, 9f), life, 271u, i);
            }
        }

        private static float DistanceSq(Vector3 point)
        {
            Camera camera = SceneSingleton<CameraStateManager>.i?.mainCamera ?? Camera.main;
            return camera != null ? (camera.transform.position - point).sqrMagnitude : 0f;
        }

        private static void EmitDust(Burst burst, Vector3 point, Vector3 velocity, float size, float life, uint seed, int index)
        {
            burst.Dust.Emit(new ParticleSystem.EmitParams
            {
                position = Datum.origin.InverseTransformPoint(point), velocity = velocity,
                startSize = size, startLifetime = life,
                startColor = new Color(0.63f, 0.61f, 0.57f, 0.36f + FractureGeometry.Rand(seed, index + 121) * 0.14f),
                rotation = FractureGeometry.Rand(seed, index + 131) * 360f
            }, 1);
        }

        public void Update(float now)
        {
            bool active = false;
            foreach (Burst burst in bursts)
            {
                if (!burst.Root.activeSelf) continue;
                if (now >= burst.Expires)
                {
                    burst.Dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    burst.Grit.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    burst.Root.SetActive(false);
                }
                else
                {
                    active = true;
                    if (burst.LoftCount == 0 || now < burst.LoftAt) continue;
                    // The low front expands first; a slower, rising body follows instead of one spherical puff.
                    Vector3 origin = burst.Position.ToLocalPosition();
                    float extent = Mathf.Max(burst.Footprint.x, burst.Footprint.y);
                    for (int i = 0; i < burst.LoftCount; i++)
                    {
                        float angle = i * 2.399963f;
                        float ring = Mathf.Sqrt(FractureGeometry.Rand(burst.Seed, i + 151));
                        Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                        Vector3 offset = new Vector3(radial.x * burst.Footprint.x, 0f, radial.z * burst.Footprint.y) * ring * 0.65f;
                        EmitDust(burst, origin + offset + Vector3.up * 0.7f,
                            radial * (1f + ring * 2f) + Vector3.up * (2.2f + ring * 2f),
                            Mathf.Clamp(extent * 0.3f, 3f, 7f), 4.8f + ring * 1.4f, burst.Seed, i + 42);
                    }
                    burst.LoftCount = 0;
                }
            }
            if (!active && depthLease) { DestructionAssets.ReleaseDepth(); depthLease = false; }
        }

        internal void Audit(IDictionary<string, object> state, string prefix)
        {
            int active = 0, dust = 0, grit = 0, pending = 0, maxDust = 0, maxGrit = 0;
            foreach (Burst burst in bursts)
            {
                if (!burst.Root.activeSelf) continue;
                active++; pending += burst.LoftCount;
                int dustCount = burst.Dust.particleCount, gritCount = burst.Grit.particleCount;
                dust += dustCount; grit += gritCount;
                maxDust = Mathf.Max(maxDust, dustCount); maxGrit = Mathf.Max(maxGrit, gritCount);
            }
            state[prefix + "Slots"] = bursts.Count;
            state[prefix + "Active"] = active;
            state[prefix + "Dust"] = dust;
            state[prefix + "Grit"] = grit;
            state[prefix + "PendingLoft"] = pending;
            state[prefix + "MaxDust"] = maxDust;
            state[prefix + "MaxGrit"] = maxGrit;
            state[prefix + "DepthLease"] = depthLease ? 1 : 0;
        }

        private Burst Create()
        {
            var root = new GameObject(impactCapacity > 0 ? "BoscaliSummer.StructuralImpact" : "BoscaliSummer.CollapseBurst");
            root.transform.SetParent(Datum.origin, false);
            ParticleSystem dust = System(root.transform, "ConcretePressureFront", 50, false);
            dust.GetComponent<ParticleSystemRenderer>().sharedMaterial = dustMaterial;
            ParticleSystem.SizeOverLifetimeModule growth = dust.sizeOverLifetime;
            growth.enabled = true;
            growth.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.85f), new Keyframe(0.2f, 1.3f), new Keyframe(1f, 2.2f)));
            ParticleSystem.ColorOverLifetimeModule color = dust.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 0.85f, 0.85f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.04f), new GradientAlphaKey(0.75f, 0.45f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            ParticleSystem.NoiseModule noise = dust.noise; noise.enabled = true; noise.quality = ParticleSystemNoiseQuality.Low;
            noise.strength = 0.65f; noise.frequency = 0.17f;
            ParticleSystem.LimitVelocityOverLifetimeModule drag = dust.limitVelocityOverLifetime;
            drag.enabled = true; drag.limit = 2.2f; drag.dampen = 0.25f;
            ParticleSystem grit = System(root.transform, "BallisticConcreteGrit", 32, true);
            var gritRenderer = grit.GetComponent<ParticleSystemRenderer>();
            gritRenderer.renderMode = ParticleSystemRenderMode.Mesh; gritRenderer.mesh = gritMesh; gritRenderer.sharedMaterial = concreteMaterial;
            ParticleSystem.MainModule main = grit.main; main.gravityModifier = 0.9f; main.startRotation3D = true;
            ParticleSystem.RotationOverLifetimeModule rotation = grit.rotationOverLifetime; rotation.enabled = true;
            rotation.separateAxes = true; rotation.x = 3.2f; rotation.y = 1.7f; rotation.z = 2.4f;
            ParticleSystem.CollisionModule collision = grit.collision; collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World; collision.quality = ParticleSystemCollisionQuality.Low;
            collision.collidesWith = PhysicsLayers.StaticsMask; collision.bounce = 0.25f; collision.dampen = 0.45f;
            root.SetActive(false);
            return new Burst { Root = root, Dust = dust, Grit = grit, GritRenderer = gritRenderer };
        }

        private static ParticleSystem System(Transform parent, string name, int maximum, bool mesh)
        {
            var root = new GameObject(name); root.transform.SetParent(parent, false);
            var system = root.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = false; main.playOnAwake = false; main.duration = 1f; main.maxParticles = maximum;
            main.startSpeed = 0f; main.simulationSpace = ParticleSystemSimulationSpace.Custom; main.customSimulationSpace = Datum.origin;
            ParticleSystem.EmissionModule emission = system.emission; emission.enabled = false;
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.shadowCastingMode = mesh ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = mesh;
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return system;
        }

        private bool EnsureAssets()
        {
            if (dustMaterial != null) return true;
            Shader dust = DestructionAssets.Shader("Boscali/ConcreteDust");
            Shader surface = UnityEngine.Shader.Find("Universal Render Pipeline/Lit") ?? UnityEngine.Shader.Find("Standard");
            if (dust == null || surface == null) return false;
            const int size = 128;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float noise = Mathf.PerlinNoise(x * 0.055f + 13f, y * 0.055f + 7f) * 0.7f + Mathf.PerlinNoise(x * 0.15f, y * 0.15f) * 0.3f;
                    float radius = Mathf.Sqrt(u * u + v * v);
                    float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((0.98f - radius + (noise - 0.5f) * 0.23f) * 4f));
                    float shade = 0.65f + noise * 0.3f + v * 0.07f;
                    pixels[y * size + x] = (Color32)new Color(shade, shade, shade, alpha);
                }
            dustTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Billowing aggregate dust", wrapMode = TextureWrapMode.Clamp };
            dustTexture.SetPixels32(pixels); dustTexture.Apply(false, true);
            dustMaterial = new Material(dust) { name = "Soft concrete dust" }; dustMaterial.mainTexture = dustTexture;
            concreteTexture = DestructionAssets.ConcreteTexture();
            concreteMaterial = new Material(surface) { name = "Concrete grit" };
            concreteMaterial.SetTexture("_BaseMap", concreteTexture); concreteMaterial.SetTexture("_MainTex", concreteTexture);
            gritMesh = FractureGeometry.Section(317u);
            return true;
        }

        public void Clear()
        {
            foreach (Burst burst in bursts) if (burst.Root != null) UnityEngine.Object.Destroy(burst.Root);
            bursts.Clear();
            if (dustMaterial != null) UnityEngine.Object.Destroy(dustMaterial);
            if (concreteMaterial != null) UnityEngine.Object.Destroy(concreteMaterial);
            if (dustTexture != null) UnityEngine.Object.Destroy(dustTexture);
            if (concreteTexture != null) UnityEngine.Object.Destroy(concreteTexture);
            if (gritMesh != null) UnityEngine.Object.Destroy(gritMesh);
            dustMaterial = null; concreteMaterial = null; dustTexture = null; concreteTexture = null; gritMesh = null;
            if (depthLease) DestructionAssets.ReleaseDepth();
            depthLease = false;
        }
    }
}
