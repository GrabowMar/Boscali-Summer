using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Math;
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
            internal GlobalPosition Position;
            internal float Expires;
        }
        private readonly List<Burst> bursts = new List<Burst>(6);
        private readonly int impactCapacity;
        private Material dustMaterial, concreteMaterial;
        private Texture2D dustTexture, concreteTexture;
        private Mesh gritMesh;
        private bool depthLease;

        internal CollapseBurstPool(int impactCapacity = 0) { this.impactCapacity = impactCapacity; }
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
            if (GameManager.IsHeadless || !EnsureAssets()) return;
            if (!depthLease) depthLease = DestructionAssets.AcquireDepth();
            Burst burst = null;
            foreach (Burst candidate in bursts) if (!candidate.Root.activeSelf) { burst = candidate; break; }
            if (burst == null)
            {
                int maximum = impactCapacity > 0 ? Mathf.Clamp(impactCapacity, 1, 6) :
                    FxBudget.ScaleCount(Plugin.Settings.FireAndDestruction.MaximumCollapseBursts, FxBus.Scales.Particles);
                if (bursts.Count >= maximum) return;
                burst = Create(); bursts.Add(burst);
            }
            burst.Position = position; burst.Expires = Time.timeSinceLevelLoad + (collapse ? 7f : 3.2f);
            burst.Root.transform.position = position.ToLocalPosition(); burst.Root.SetActive(true);
            burst.Dust.Clear(); burst.Grit.Clear();
            burst.Dust.Play(); burst.Grit.Play();
            normal = normal.sqrMagnitude < 0.001f ? Vector3.up : normal.normalized;
            Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.85f ? Vector3.forward : Vector3.up).normalized;
            Vector3 other = Vector3.Cross(normal, tangent).normalized;
            uint seed = Deterministic.Hash(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.z), 211);
            Vector3 origin = position.ToLocalPosition();
            bool small = !collapse && footprint.x <= 1.01f;
            int dustCount = collapse ? 42 : small ? 8 : 18;
            float extent = Mathf.Max(footprint.x, footprint.y);
            for (int i = 0; i < dustCount; i++)
            {
                float angle = i * 2.399963f + FractureGeometry.Rand(seed, 3) * 6f;
                Vector3 radial = tangent * Mathf.Cos(angle) + other * Mathf.Sin(angle);
                float ring = Mathf.Sqrt(FractureGeometry.Rand(seed, i + 11));
                Vector3 offset = collapse ? new Vector3(Mathf.Cos(angle) * footprint.x * ring,
                    i % 4 == 0 ? extent * (0.12f + FractureGeometry.Rand(seed, i + 21) * 0.38f) : 0.15f,
                    Mathf.Sin(angle) * footprint.y * ring) : radial * ring * extent;
                Vector3 velocity = collapse
                    ? new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (4f + ring * 7f) + Vector3.up * (0.8f + FractureGeometry.Rand(seed, i + 31) * 3f)
                    : normal * (4f + ring * 6f) + radial * 3f + Vector3.up * 0.6f;
                float size = collapse ? Mathf.Clamp(extent * 0.38f, 3.5f, 10f) : 1.2f + extent * 0.55f;
                EmitDust(burst, origin + offset + normal * 0.3f, velocity, size * (0.7f + ring * 0.55f),
                    collapse ? 4.2f + ring * 2.2f : small ? 0.6f + ring * 0.5f : 1.4f + ring * 1.3f, seed, i);
            }
            int gritCount = collapse ? 24 : small ? 4 : 8;
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

        internal void EmitLanding(GlobalPosition position, Vector3 normal, float size)
        {
            Burst nearest = null; float best = 10000f;
            foreach (Burst burst in bursts)
            {
                if (!burst.Root.activeSelf) continue;
                float distance = (burst.Position - position).sqrMagnitude;
                if (distance < best) { best = distance; nearest = burst; }
            }
            if (nearest == null) return;
            Vector3 origin = position.ToLocalPosition() + Vector3.up * 0.2f;
            for (int i = 0; i < 5; i++)
            {
                float angle = i * 1.2566f;
                EmitDust(nearest, origin, new Vector3(Mathf.Cos(angle) * 4f, 0.6f, Mathf.Sin(angle) * 4f),
                    Mathf.Clamp(size, 2f, 9f), 2.5f + size * 0.25f, 271u, i);
            }
        }

        private static void EmitDust(Burst burst, Vector3 point, Vector3 velocity, float size, float life, uint seed, int index)
        {
            burst.Dust.Emit(new ParticleSystem.EmitParams
            {
                position = Datum.origin.InverseTransformPoint(point), velocity = velocity,
                startSize = size, startLifetime = life,
                startColor = new Color(0.58f, 0.54f, 0.47f, 0.42f + FractureGeometry.Rand(seed, index + 121) * 0.16f),
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
                else active = true;
            }
            if (!active && depthLease) { DestructionAssets.ReleaseDepth(); depthLease = false; }
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
            return new Burst { Root = root, Dust = dust, Grit = grit };
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
