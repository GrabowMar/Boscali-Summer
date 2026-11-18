using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>The burned land after the blast: black smoke columns leaning with the wind and fire glow at their feet,
    /// for the rest of the mission. Poses come from site age, so late snapshots and age jumps show the same picture.</summary>
    [DefaultExecutionOrder(1900)]
    internal sealed class Sa74Smolder : MonoBehaviour, ISceneService
    {
        // ponytail: one field of plumes for the newest site; an older site keeps its scorch and sky only.
        private const int Plumes = 28, Puffs = 12;
        private const float Period = 90f;
        private readonly ParticleSystem.Particle[] smokeBuffer = new ParticleSystem.Particle[Plumes * Puffs];
        private readonly ParticleSystem.Particle[] fireBuffer = new ParticleSystem.Particle[Plumes];
        private readonly Vector3[] bases = new Vector3[Plumes];
        private readonly float[] starts = new float[Plumes];
        private Sa74Fallout.Site site;
        private GameObject root;
        private ParticleSystem smoke, fire;
        private Material smokeMaterial, fireMaterial, charMaterial;
        private sealed class Ground { internal Material Material; internal string Property; internal Texture Original; internal RenderTexture Burned; internal bool Cover; }
        // The whole-map colour is recoloured; the plant cover weights are burned away so fields and grass do not stay green.
        private static readonly string[] GroundMaps = { "_macro_basecolor", "_splat_fields", "_splat_grass", "_splat_lush" };
        private readonly System.Collections.Generic.List<Ground> grounds = new System.Collections.Generic.List<Ground>();
        private int charStep = -1;
        private int count;
        internal static int ActivePlumes { get; private set; }

        public void ResetForScene() => Clear();

        private void LateUpdate()
        {
            if (Application.isBatchMode || Datum.origin == null) return;
            Sa74Fallout.Site newest = Sa74Fallout.Sites.Count > 0 ? Sa74Fallout.Sites[Sa74Fallout.Sites.Count - 1] : null;
            if (!ReferenceEquals(newest, site)) { Clear(); site = newest; }
            if (site == null) return;
            float age = Time.time - site.Started;
            if (age < 30f || (root == null && !Build())) return;
            root.transform.position = site.Position.ToLocalPosition();
            Pose(age);
            Char(age);
        }

        private bool Build()
        {
            if (SmokeFx.Puff == null || GlowFx.Glow == null) return false;
            smokeMaterial = new Material(SmokeFx.Puff) { name = "SA-74N smoldering smoke", renderQueue = 3000 };
            fireMaterial = new Material(GlowFx.Glow) { name = "SA-74N ground fires", renderQueue = 3003 };
            root = new GameObject("SA-74N burned land / " + site.Id);
            root.transform.SetParent(Datum.origin, false);
            root.transform.position = site.Position.ToLocalPosition();
            smoke = Make("smoke", smokeBuffer.Length, smokeMaterial, .5f);
            fire = Make("fires", fireBuffer.Length, fireMaterial, .2f);
            Vector3 centre = site.Position.ToLocalPosition();
            float reach = Mathf.Min(45000f, Sa74Fallout.ShockRadius * .6f);
            count = 0;
            for (int i = 0; i < Plumes; i++)
            {
                uint seed = unchecked(site.Id * 7919u + (uint)i * 104729u + 5741u);
                float distance = 3000f + Mathf.Sqrt(Hash(seed)) * (reach - 3000f), angle = i * 2.39996323f + Hash(seed + 7u);
                Vector3 above = centre + new Vector3(Mathf.Cos(angle) * distance, 20000f, Mathf.Sin(angle) * distance);
                // Only land burns: a miss is the sea.
                if (!Physics.Raycast(above, Vector3.down, out RaycastHit hit, 45000f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore) ||
                    hit.point.y <= Datum.LocalSeaY + 1f) continue;
                bases[count] = Datum.origin.InverseTransformPoint(hit.point);
                starts[count++] = Sa74BlastShape.ArrivalSeconds(distance) + 20f;
            }
            return true;
        }

        /// <summary>Burns the land into the map's whole-terrain colour texture. Decal projectors this wide are clipped by the
        /// 80 km camera far plane and leave straight-edged unburned holes; the terrain's own colour map has no such limit.</summary>
        private void Char(float age)
        {
            float strength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 150f, age));
            int step = Mathf.RoundToInt(strength * 12f);
            if (step == charStep || count == 0) return;
            Shader shader = Sa74Mushroom.BlastShader();
            if (shader == null || shader.passCount < 3) return;
            if (charMaterial == null) charMaterial = new Material(shader) { name = "SA-74N terrain char" };
            if (grounds.Count == 0)
                foreach (MeshRenderer renderer in Object.FindObjectsOfType<MeshRenderer>())
                {
                    Material material = renderer.sharedMaterial;
                    if (material == null || !material.HasProperty("_macro_basecolor") || grounds.Exists(ground => ground.Material == material)) continue;
                    if (grounds.Count >= 8) break;
                    foreach (string property in GroundMaps)
                    {
                        Texture original = material.HasProperty(property) ? material.GetTexture(property) : null;
                        if (original == null || original is RenderTexture || original.width < 16) continue;
                        bool cover = property != "_macro_basecolor";
                        // ponytail: colour up to 4096 square (about 85 MB with mips), cover weights up to 2048, while a burned map exists.
                        int limit = cover ? 2048 : 4096;
                        var burned = new RenderTexture(Mathf.Min(limit, original.width), Mathf.Min(limit, original.height), 0, RenderTextureFormat.ARGB32,
                            cover ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB)
                        {
                            name = "SA-74N burned " + original.name, useMipMap = true, autoGenerateMips = true,
                            wrapMode = original.wrapMode, filterMode = FilterMode.Trilinear, anisoLevel = Mathf.Max(original.anisoLevel, 4)
                        };
                        if (!burned.Create()) { Destroy(burned); continue; }
                        grounds.Add(new Ground { Material = material, Property = property, Original = original, Burned = burned, Cover = cover });
                    }
                }
            if (grounds.Count == 0) return;
            charStep = step;
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            MapSettings map = level != null ? level.LoadedMapSettings : null;
            Vector2 span = map != null ? map.MapSize : Core.Game.TheaterFrame.Resolve();
            if (span.x <= 0f || span.y <= 0f) return;
            GlobalPosition at = site.Position;
            float reach = Mathf.Min(60000f, Sa74Fallout.ShockRadius * .8f);
            foreach (Ground ground in grounds)
            {
                charMaterial.SetTexture("_Sa74Macro", ground.Original);
                charMaterial.SetFloat("_Sa74CharMode", ground.Cover ? 1f : 0f);
                charMaterial.SetVector("_Sa74Char", new Vector4(at.x / span.x + .5f, at.z / span.y + .5f, reach / span.x, strength));
                charMaterial.SetVector("_Sa74CharSize", new Vector4(1f / ground.Burned.width, 1f / ground.Burned.height, span.y / span.x, 0f));
                var commands = new CommandBuffer { name = "SA-74N terrain char" };
                commands.SetRenderTarget(ground.Burned);
                commands.SetViewport(new Rect(0f, 0f, ground.Burned.width, ground.Burned.height));
                commands.DrawProcedural(Matrix4x4.identity, charMaterial, 2, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(commands);
                commands.Release();
                ground.Material.SetTexture(ground.Property, ground.Burned);
            }
        }

        private ParticleSystem Make(string label, int budget, Material material, float maximumSize)
        {
            var layer = new GameObject("SA-74N " + label);
            layer.SetActive(false);
            layer.transform.SetParent(root.transform, false);
            ParticleSystem system = layer.AddComponent<ParticleSystem>();
            var main = system.main;
            main.playOnAwake = false; main.loop = false; main.startSpeed = 0f;
            main.startLifetime = 10000f; main.maxParticles = budget;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = Datum.origin;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = system.emission; emission.enabled = false;
            var shape = system.shape; shape.enabled = false;
            var renderer = layer.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.minParticleSize = 0f; renderer.maxParticleSize = maximumSize;
            // The system is paused and posed by hand: give it bounds that cover the whole burned zone.
            renderer.localBounds = new Bounds(Vector3.zero, new Vector3(120000f, 30000f, 120000f));
            layer.SetActive(true);
            system.Play(); system.Pause();
            return system;
        }

        private void Pose(float age)
        {
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            Vector3 wind = level != null ? Vector3.ClampMagnitude(level.windVelocity, 12f) : new Vector3(2.5f, 0f, .8f);
            wind.y = 0f;
            // Fires rage for the first quarter hour, then the land smolders for good.
            float settle = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(900f, 2400f, age));
            int smokeCount = 0, fireCount = 0, active = 0;
            for (int p = 0; p < count; p++)
            {
                float strength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(starts[p], starts[p] + 60f, age));
                if (strength <= .001f) continue;
                active++;
                uint seed = unchecked(site.Id * 15485863u + (uint)p * 104729u);
                float a = Hash(seed), b = Hash(seed + 11u);
                float top = 2500f + a * 2500f;
                for (int k = 0; k < Puffs; k++)
                {
                    float phase = Mathf.Repeat(age / Period + (k + b) / Puffs, 1f);
                    float c = Hash(seed + 97u + (uint)k);
                    Vector3 position = bases[p] + Vector3.up * (60f + phase * top) + wind * (phase * Period * .6f)
                        + new Vector3(Mathf.Sin(phase * 5f + c * 6.28f), 0f, Mathf.Cos(phase * 4f + c * 6.28f)) * (phase * 260f);
                    float alpha = .55f * strength * Mathf.Lerp(1f, .65f, settle) *
                        Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phase / .12f)) * Mathf.Pow(1f - phase, 1.2f);
                    if (alpha <= .004f) continue;
                    float shade = .055f + c * .03f + phase * .05f;
                    smokeBuffer[smokeCount++] = new ParticleSystem.Particle
                    {
                        position = position, startSize = Mathf.Lerp(260f, 1500f, phase) * (.8f + .4f * c),
                        startColor = new Color(shade * 1.08f, shade, shade * .92f, alpha),
                        rotation = c * 360f + age * (c - .5f) * 2f,
                        startLifetime = 10000f, remainingLifetime = 10000f, randomSeed = seed + (uint)k + 1u
                    };
                }
                float flicker = .75f + .25f * Mathf.Sin(age * 7f + p * 1.7f);
                fireBuffer[fireCount++] = new ParticleSystem.Particle
                {
                    position = bases[p] + Vector3.up * 120f, startSize = (360f + a * 240f) * flicker,
                    startColor = new Color(1f, .42f, .10f, .55f * strength * Mathf.Lerp(1f, .3f, settle) * flicker),
                    startLifetime = 10000f, remainingLifetime = 10000f, randomSeed = seed + 3u
                };
            }
            smoke.SetParticles(smokeBuffer, smokeCount);
            fire.SetParticles(fireBuffer, fireCount);
            ActivePlumes = active;
        }

        private static float Hash(uint value)
        {
            unchecked { value ^= value >> 16; value *= 0x7feb352du; value ^= value >> 15; value *= 0x846ca68bu; value ^= value >> 16; }
            return (value & 0x00ffffffu) / 16777215f;
        }

        private void Clear()
        {
            if (root != null) Destroy(root);
            if (smokeMaterial != null) Destroy(smokeMaterial);
            if (fireMaterial != null) Destroy(fireMaterial);
            if (charMaterial != null) Destroy(charMaterial);
            foreach (Ground ground in grounds)
            {
                // The terrain material is a shared asset: always hand its own texture back.
                if (ground.Material != null) ground.Material.SetTexture(ground.Property, ground.Original);
                if (ground.Burned != null) { ground.Burned.Release(); Destroy(ground.Burned); }
            }
            grounds.Clear(); charStep = -1;
            root = null; smoke = fire = null; smokeMaterial = fireMaterial = charMaterial = null; site = null;
            count = 0; ActivePlumes = 0;
        }

        private void OnDestroy() => Clear();
    }
}
