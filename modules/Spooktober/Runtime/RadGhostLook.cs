using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>
    /// The rad-ghost look (spec §5): every mesh renderer under a target wears one Boscali/RadGhost material instance (the
    /// world shows through a spectral blue glow) and the largest one sheds embers that drift up. Local to this peer, no network.
    /// <see cref="Handle.Remove"/> puts the original shared materials back. Without the shader it is embers only.
    /// </summary>
    internal static class RadGhostLook
    {
        /// <summary>One kind of target: shader noise, scanlines, shimmer, far glow and ember scale in metres.</summary>
        internal sealed class Profile
        {
            public float NoiseScale, ScanDensity, ShimmerCm, FarDistance, EmberRate, EmberScale;
            /// <summary>Hell's Army: Boscali/Hellfire (solid char, lava cracks) and fire-coloured embers instead of the blue ghost.</summary>
            public bool Hell;

            /// <summary>A fighter: 2 m heat cells, a scanline every 0.5 m, embers 0.15-0.4 m rising 0.4-1.2 m/s. Still see-through at
            /// 1.5 km (far glow 14 %; at 2000 m it was a solid blue cut-out there), a solid blue silhouette from about 3 km.</summary>
            public static readonly Profile Aircraft = new Profile
                { NoiseScale = 0.5f, ScanDensity = 2f, ShimmerCm = 4f, FarDistance = 4000f, EmberRate = 400f, EmberScale = 1f };

            /// <summary>A Hell's Army vehicle (any unit, ground or air): 3 m crack cells, the whole body glowing red from about 2 km,
            /// bigger and slower embers than the ghost's (a tank sheds sparks, not a ghost's mist).</summary>
            public static readonly Profile HellVehicle = new Profile
                { NoiseScale = 0.35f, FarDistance = 3000f, EmberRate = 150f, EmberScale = 1.5f, Hell = true };
        }

        /// <summary>One target wearing the look.</summary>
        internal sealed class Handle
        {
            public readonly GameObject Root;
            private readonly List<Renderer> renderers = new List<Renderer>();
            private readonly List<Material[]> originals = new List<Material[]>();
            private readonly Material material;
            private readonly ParticleSystem embers;
            private readonly float rate;
            private bool removed;

            /// <summary>Still worn: not removed, and the target still exists.</summary>
            public bool Alive => !removed && Root != null;

            internal Handle(GameObject root, Profile profile)
            {
                Root = root;
                rate = profile.EmberRate;
                Shader shader = profile.Hell ? SpookAssets.Hellfire : SpookAssets.RadGhost;
                if (shader != null)
                {
                    material = new Material(shader) { name = profile.Hell ? "BoscaliSummer.Hellfire" : "BoscaliSummer.RadGhost" };
                    material.SetFloat(NoiseScaleId, profile.NoiseScale);
                    material.SetFloat(ScanDensityId, profile.ScanDensity);
                    material.SetFloat(ShimmerId, profile.ShimmerCm);
                    material.SetFloat(FarId, profile.FarDistance);
                }
                Renderer largest = null;
                float best = -1f;
                foreach (Renderer r in RenderersOf(root))
                {
                    if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                    float size = r.bounds.size.sqrMagnitude;
                    // No mesh, or hidden (the FS-20's inactive debugArrowRed won the size test: embers parked under it never showed).
                    if (size > best && r.gameObject.activeInHierarchy && MeshOf(r) != null) { best = size; largest = r; }
                    if (material == null) continue;
                    Material[] own = r.sharedMaterials;
                    var ghost = new Material[own.Length];
                    for (int i = 0; i < ghost.Length; i++) ghost[i] = material;
                    renderers.Add(r);
                    originals.Add(own);
                    r.sharedMaterials = ghost;
                }
                if (largest != null) embers = Embers(largest, root.transform, profile); // created after the sweep: never ghosted
            }

            /// <summary>0-1: the body's glow and the ember rate (a fade in or out).</summary>
            public void SetStrength(float strength)
            {
                strength = Mathf.Clamp01(strength);
                if (material != null) material.SetFloat(StrengthId, strength);
                if (embers == null) return;
                ParticleSystem.EmissionModule emission = embers.emission;
                emission.rateOverTime = rate * strength * FxBus.Scales.Particles;
            }

            public void Remove()
            {
                removed = true;
                for (int i = 0; i < renderers.Count; i++)
                {
                    if (renderers[i] == null) continue;
                    // A hit's UnitPart damage path reads renderer.material, which cloned our material for that renderer: free it.
                    foreach (Material worn in renderers[i].sharedMaterials)
                        if (worn != null && worn != material && material != null && worn.shader == material.shader) UnityEngine.Object.Destroy(worn);
                    renderers[i].sharedMaterials = originals[i];
                }
                renderers.Clear();
                if (material != null) UnityEngine.Object.Destroy(material);
                if (embers != null) UnityEngine.Object.Destroy(embers.gameObject);
                Live.Remove(this);
            }
        }

        private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
        private static readonly int ScanDensityId = Shader.PropertyToID("_ScanDensity");
        private static readonly int ShimmerId = Shader.PropertyToID("_ShimmerScale");
        private static readonly int FarId = Shader.PropertyToID("_FarDistance");
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");
        private static readonly List<Handle> Live = new List<Handle>();
        private static Gradient emberColour, fireColour;

        /// <summary>
        /// Put the look on every mesh renderer under <paramref name="root"/>.
        /// Idempotent: a root already wearing it keeps its handle (and profile) and takes the new strength.
        /// </summary>
        public static Handle Apply(GameObject root, Profile profile, float strength = 1f)
        {
            if (root == null || profile == null) return null;
            Handle found = null;
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                if (!Live[i].Alive) Live[i].Remove(); // its target was destroyed without a Remove: free the material
                else if (Live[i].Root == root) found = Live[i];
            }
            if (found == null)
            {
                found = new Handle(root, profile);
                Live.Add(found);
            }
            found.SetStrength(strength);
            return found;
        }

        /// <summary>A one-shot puff of embers at a world point (the FALLEN ACE's blink and rebirth; fire-coloured for Hell's Army),
        /// aircraft-sized.</summary>
        public static void Burst(Vector3 worldPos, bool fire = false)
        {
            if (Datum.origin == null) return;
            ParticleSystem ps = NewEmbers("RadGhostBurst", Datum.origin, 1f, 300, fire ? FireColour : EmberColour);
            ps.transform.position = worldPos;
            ParticleSystem.MainModule main = ps.main;
            main.loop = false;
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 14f);
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 4f;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(240 * FxBus.Scales.Particles)) });
            ps.Play();
            UnityEngine.Object.Destroy(ps.gameObject, 2.5f);
        }

        /// <summary>Take the look off every target, whoever applied it (plugin teardown, before the shader's bundle unloads).</summary>
        public static void RemoveAll()
        {
            for (int i = Live.Count - 1; i >= 0; i--) Live[i].Remove();
        }

        /// <summary>Every renderer under <paramref name="root"/> and, for a unit, under its parts: on the host an aircraft flies complex
        /// physics, which unparents every part with mass (AeroPart.CreateRB: SetParent(null)), so its wings and tail are not under the
        /// root. Parts that break off later keep the look (their renderers are already swapped).</summary>
        internal static List<Renderer> RenderersOf(GameObject root)
        {
            var found = new List<Renderer>();
            root.GetComponentsInChildren(true, found);
            if (!root.TryGetComponent(out Unit unit) || unit.partLookup == null) return found;
            foreach (UnitPart part in unit.partLookup)
                if (part != null && !part.transform.IsChildOf(root.transform))
                    foreach (Renderer r in part.GetComponentsInChildren<Renderer>(true))
                        if (!found.Contains(r)) found.Add(r); // a massless part rides under an unparented one
            return found;
        }

        private static Mesh MeshOf(Renderer r) => r is SkinnedMeshRenderer skinned ? skinned.sharedMesh
            : r.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;

        /// <summary>
        /// Embers off <paramref name="source"/>. A readable mesh emits from its surface (the renderer shapes use the renderer's
        /// own world transform, so the system rides under the target root). A non-readable one (the kaiju's, maybe the game's
        /// airframes) has no CPU copy in a player build, so it emits from its bind-pose bounds box, riding the renderer's
        /// transform. Mesh bounds, not <c>Renderer.localBounds</c>: the kaiju's skinned bounds are padded 1.8x + 20 m.
        /// </summary>
        private static ParticleSystem Embers(Renderer source, Transform root, Profile profile)
        {
            Mesh mesh = MeshOf(source);
            bool surface = mesh.isReadable;
            ParticleSystem ps = NewEmbers(profile.Hell ? "HellEmbers" : "RadGhostEmbers", surface ? root : source.transform, profile.EmberScale,
                Mathf.CeilToInt(profile.EmberRate * 2f), profile.Hell ? FireColour : EmberColour);
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            if (!surface)
            {
                shape.shapeType = ParticleSystemShapeType.Box; // ScalingMode.Shape: the renderer's scale sizes the box
                shape.position = mesh.bounds.center;
                shape.scale = mesh.bounds.size;
            }
            else if (source is SkinnedMeshRenderer skinned)
            {
                shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
                shape.skinnedMeshRenderer = skinned;
                shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
            }
            else
            {
                shape.shapeType = ParticleSystemShapeType.MeshRenderer;
                shape.meshRenderer = (MeshRenderer)source;
                shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
            }
            // A fighter at 250 m/s would leave a 300 m comet of embers: let them keep most of its speed.
            ParticleSystem.InheritVelocityModule inherit = ps.inheritVelocity;
            inherit.enabled = true;
            inherit.mode = ParticleSystemInheritVelocityMode.Initial;
            inherit.curve = 0.9f;
            ps.Play();
            return ps;
        }

        /// <summary>Additive embers: 0.8-1.6 s in <paramref name="colour"/>, drifting up with a little noise. Sizes and
        /// speeds are world metres times <paramref name="scale"/>, whatever the parent's scale.</summary>
        internal static ParticleSystem NewEmbers(string name, Transform parent, float scale, int max, Gradient colour)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f * scale, 0.4f * scale);
            main.startColor = Color.white;
            main.maxParticles = max;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            if (Datum.origin != null)
            {
                // World space would leave embers behind on a floating-origin shift (same as Core/Fx/ParticleFx).
                main.simulationSpace = ParticleSystemSimulationSpace.Custom;
                main.customSimulationSpace = Datum.origin;
            }
            else main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.VelocityOverLifetimeModule rise = ps.velocityOverLifetime;
            rise.enabled = true;
            rise.space = ParticleSystemSimulationSpace.World;
            rise.x = new ParticleSystem.MinMaxCurve(0f, 0f); // all three axes in one curve mode, or Unity rejects them
            rise.y = new ParticleSystem.MinMaxCurve(0.4f * scale, 1.2f * scale);
            rise.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.3f * scale;
            noise.frequency = 0.5f / scale;
            noise.scrollSpeed = 0.3f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.octaveCount = 1;
            ParticleSystem.ColorOverLifetimeModule tint = ps.colorOverLifetime;
            tint.enabled = true;
            tint.color = colour;
            ParticleSystem.SizeOverLifetimeModule shrink = ps.sizeOverLifetime;
            shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = GlowFx.Glow;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.None;
            renderer.minParticleSize = 0.0015f; // a 0.3 m ember at 1.5 km would be under a pixel: keep a spark
            go.SetActive(true);
            return ps;
        }

        /// <summary>Hell's embers: yellow-white to orange to deep red to nothing.</summary>
        internal static Gradient FireColour
        {
            get
            {
                if (fireColour != null) return fireColour;
                fireColour = new Gradient();
                fireColour.SetKeys(
                    new[] { new GradientColorKey(new Color(1f, 0.85f, 0.4f), 0f), new GradientColorKey(new Color(1f, 0.4f, 0.05f), 0.3f),
                        new GradientColorKey(new Color(0.6f, 0.03f, 0f), 0.75f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.7f, 0.6f),
                        new GradientAlphaKey(0f, 1f) });
                return fireColour;
            }
        }

        private static Gradient EmberColour
        {
            get
            {
                if (emberColour != null) return emberColour;
                emberColour = new Gradient();
                emberColour.SetKeys(
                    new[] { new GradientColorKey(new Color(0.15f, 0.45f, 1f), 0f), new GradientColorKey(new Color(0.5f, 0.95f, 1f), 0.3f),
                        new GradientColorKey(new Color(0.1f, 0.25f, 1f), 0.75f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.7f, 0.6f),
                        new GradientAlphaKey(0f, 1f) });
                return emberColour;
            }
        }
    }
}
