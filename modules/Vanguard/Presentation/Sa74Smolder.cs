using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>The burned land after the blast: map-scale fire plumes, dense smoke columns, a high soot deck and
    /// embers, for the rest of the mission. Poses come from site age, so late snapshots and age jumps show the same picture.</summary>
    [DefaultExecutionOrder(1900)]
    internal sealed class Sa74Smolder : MonoBehaviour, ISceneService
    {
        private const int Plumes = Sa74Firestorm.Plumes, Puffs = Sa74Firestorm.Puffs;
        private const float Period = Sa74Firestorm.Period;
        private readonly ParticleSystem.Particle[] smokeBuffer = new ParticleSystem.Particle[Plumes * Puffs + Sa74Firestorm.PillarPuffs];
        private readonly ParticleSystem.Particle[] fireBuffer = new ParticleSystem.Particle[Plumes * Sa74Firestorm.FlameQuads];
        private readonly ParticleSystem.Particle[] emberBuffer = new ParticleSystem.Particle[Sa74Firestorm.Embers];
        private readonly ParticleSystem.Particle[] deckBuffer = new ParticleSystem.Particle[Sa74Firestorm.DeckPuffs];
        private readonly Vector3[] bases = new Vector3[Plumes];
        private readonly float[] starts = new float[Plumes];
        private readonly float[] scales = new float[Plumes];
        private Vector3 pillar;
        private bool hasPillar;
        private Sa74Fallout.Site site;
        private GameObject root;
        private ParticleSystem smoke, fire, embers, deck;
        private Material smokeMaterial, fireMaterial, deckMaterial, charMaterial;
        private sealed class Ground { internal Material Material; internal string Property; internal Texture Original; internal RenderTexture Burned; internal bool Cover; }
        private sealed class Detail { internal Material Material; internal string Property; internal Texture Original; internal bool Applied; }
        // The whole-map colour is recoloured; the plant cover weights are burned away so fields and grass do not stay green.
        private static readonly string[] GroundMaps = { "_macro_basecolor", "_splat_fields", "_splat_grass", "_splat_lush" };
        private readonly List<Ground> grounds = new List<Ground>();
        private readonly List<Detail> details = new List<Detail>();
        private Texture2D sootDetail;
        private int charStep = -1;
        private int count;
        internal static int ActivePlumes { get; private set; }
        internal static int ActiveFires { get; private set; }
        internal static int ActiveEmbers { get; private set; }
        internal static int ActiveDeck { get; private set; }
        internal static int CharMaterials { get; private set; }
        internal static int CharDetails { get; private set; }
        internal static int CharStep { get; private set; }
        internal static float CharReach { get; private set; }
        internal static int CharWrites { get; private set; }

        public void ResetForScene() => Clear();

        private void LateUpdate()
        {
            if (Application.isBatchMode || Datum.origin == null) return;
            Sa74Fallout.Site newest = Sa74Fallout.Sites.Count > 0 ? Sa74Fallout.Sites[Sa74Fallout.Sites.Count - 1] : null;
            if (!ReferenceEquals(newest, site)) { Clear(); site = newest; }
            if (site == null) return;
            float age = Time.time - site.Started;
            if (age < Sa74Burn.StartSeconds || (root == null && !Build())) return;
            root.transform.position = site.Position.ToLocalPosition();
            Pose(age);
            Char(age);
        }

        private bool Build()
        {
            if (SmokeFx.Puff == null || GlowFx.Glow == null) return false;
            smokeMaterial = new Material(SmokeFx.Puff) { name = "SA-74N smoldering smoke", renderQueue = 3000 };
            fireMaterial = new Material(GlowFx.Glow) { name = "SA-74N ground fires", renderQueue = 3003 };
            // HDR fire: it has to outshine the -1.5 EV nuclear-winter grade and catch bloom.
            if (fireMaterial.HasProperty("_BaseColor")) fireMaterial.SetColor("_BaseColor", new Color(3.4f, 3.4f, 3.4f, 1f));
            deckMaterial = new Material(SmokeFx.Puff) { name = "SA-74N soot deck", renderQueue = 3001 };
            root = new GameObject("SA-74N burned land / " + site.Id);
            root.transform.SetParent(Datum.origin, false);
            root.transform.position = site.Position.ToLocalPosition();
            smoke = Make("smoke", smokeBuffer.Length, smokeMaterial, .72f);
            fire = Make("fires", fireBuffer.Length, fireMaterial, .45f);
            embers = Make("embers", emberBuffer.Length, fireMaterial, .06f);
            deck = Make("soot deck", deckBuffer.Length, deckMaterial, 1f);
            Vector3 centre = site.Position.ToLocalPosition();
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            MapSettings map = level != null ? level.LoadedMapSettings : null;
            Vector2 span = map != null ? map.MapSize : Core.Game.TheaterFrame.Resolve();
            float reach = Sa74Firestorm.Reach(span.x, span.y);
            count = 0;
            for (int i = 0; i < Plumes; i++)
            {
                uint seed = unchecked(site.Id * 7919u + (uint)i * 104729u + 5741u);
                // Linear in radius, not area: fires crowd toward ground zero and thin out to the map edge.
                float distance = 800f + Hash(seed) * (reach - 800f), angle = i * 2.39996323f + Hash(seed + 7u);
                Vector3 above = centre + new Vector3(Mathf.Cos(angle) * distance, 20000f, Mathf.Sin(angle) * distance);
                // Only land burns: a miss is the sea.
                if (!Physics.Raycast(above, Vector3.down, out RaycastHit hit, 45000f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore) ||
                    hit.point.y <= Datum.LocalSeaY + 1f) continue;
                bases[count] = Datum.origin.InverseTransformPoint(hit.point);
                scales[count] = Sa74Firestorm.Scale(distance, reach);
                starts[count++] = Sa74BlastShape.ArrivalSeconds(distance) + Sa74Firestorm.IgnitionDelay(seed, distance, reach);
            }
            Vector3 zero = centre + Vector3.up * 20000f;
            hasPillar = Physics.Raycast(zero, Vector3.down, out RaycastHit ground, 45000f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore) &&
                ground.point.y > Datum.LocalSeaY + 1f;
            if (hasPillar) pillar = Datum.origin.InverseTransformPoint(ground.point);
            return true;
        }

        /// <summary>Burns the land into the map's whole-terrain colour and detail textures. Decal projectors this wide are
        /// clipped by the 80 km camera far plane and leave straight-edged unburned holes; the terrain's own maps have no such limit.</summary>
        private void Char(float age)
        {
            float strength = Sa74Burn.Strength(age);
            int step = Sa74Burn.Step(age);
            if (step == charStep || count == 0) return;
            Shader shader = Sa74Mushroom.BlastShader();
            if (shader == null || shader.passCount < 3) return;
            if (charMaterial == null) charMaterial = new Material(shader) { name = "SA-74N terrain char" };
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (grounds.Count == 0)
                foreach (MeshRenderer renderer in FindObjectsOfType<MeshRenderer>())
                {
                    Material material = renderer.sharedMaterial;
                    if (material == null || !material.HasProperty("_macro_basecolor") || grounds.Exists(ground => ground.Material == material)) continue;
                    // The sea shares the macro slot; the firestorm soots land, never the water texture.
                    if (level != null && level.ocean != null && renderer.transform.IsChildOf(level.ocean.transform)) continue;
                    // Each unique terrain material owns up to four full-map copies; sixteen covers every shipped map chunk.
                    if (grounds.Count >= 16) break;
                    // Detail albedos multiply over the macro colour; without them the burned map still shows bright rock and grass.
                    foreach (string property in material.GetTexturePropertyNames())
                    {
                        if (!property.StartsWith("_SampleTexture2D_") || !property.EndsWith("_Texture_1_Texture2D")) continue;
                        if (details.Exists(detail => detail.Material == material && detail.Property == property)) continue;
                        Texture texture = material.GetTexture(property);
                        if (texture == null || texture is RenderTexture || texture.width < 16) continue;
                        string name = texture.name ?? "";
                        if (name.EndsWith("_n") || name.IndexOf("angleVariation", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (details.Count >= 32) break;
                        details.Add(new Detail { Material = material, Property = property, Original = texture });
                    }
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
            MapSettings map = level != null ? level.LoadedMapSettings : null;
            Vector2 span = map != null ? map.MapSize : Core.Game.TheaterFrame.Resolve();
            if (span.x <= 0f || span.y <= 0f) return;
            GlobalPosition at = site.Position;
            float reach = Sa74Burn.Reach(span.x, span.y);
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
            if (strength > .12f) DarkenDetails();
            CharMaterials = grounds.Count; CharDetails = details.Count; CharStep = charStep; CharReach = reach; CharWrites++;
        }

        /// <summary>A shared near-black noise albedo replaces every ground detail while the burned map exists.</summary>
        private void DarkenDetails()
        {
            if (details.Count == 0) return;
            if (sootDetail == null) sootDetail = MakeSootDetail();
            foreach (Detail detail in details)
            {
                if (detail.Applied) continue;
                detail.Material.SetTexture(detail.Property, sootDetail);
                detail.Applied = true;
            }
        }

        private static Texture2D MakeSootDetail()
        {
            const int side = 32;
            var texture = new Texture2D(side, side, TextureFormat.RGBA32, true, true)
            { name = "SA-74N ground soot detail", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var pixels = new Color[side * side];
            for (int y = 0; y < side; y++) for (int x = 0; x < side; x++)
            {
                float noise = Mathf.PerlinNoise(x * .41f + .3f, y * .41f + .7f);
                float shade = .022f + noise * .030f;
                pixels[y * side + x] = new Color(shade * 1.04f, shade, shade * .92f, 1f);
            }
            texture.SetPixels(pixels); texture.Apply(true, true);
            return texture;
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
            Vector3 centre = site.Position.ToLocalPosition();
            // Fires rage for the first quarter hour, then the land smolders for good.
            float settle = Sa74Firestorm.Settle(age);
            int smokeCount = 0, fireCount = 0, emberCount = 0, deckCount = 0, active = 0;
            for (int p = 0; p < count; p++)
            {
                float strength = Sa74Firestorm.Growth(age, starts[p]);
                if (strength <= .001f) continue;
                active++;
                uint seed = unchecked(site.Id * 15485863u + (uint)p * 104729u);
                float a = Hash(seed), b = Hash(seed + 11u), scale = scales[p];
                float top = (4200f + a * 5200f) * Mathf.Sqrt(scale);
                Vector3 inward = centre - bases[p]; inward.y = 0f;
                inward = inward.sqrMagnitude > 1f ? inward.normalized : Vector3.zero;
                for (int k = 0; k < Puffs; k++)
                {
                    float phase = Mathf.Repeat(age / Period + (k + b) / Puffs, 1f);
                    float c = Hash(seed + 97u + (uint)k);
                    float above = 120f + phase * top;
                    // A firestorm pulls surface air inward; wind takes over as the column rises.
                    float inflow = Mathf.Pow(1f - phase, 2f) * 900f;
                    Vector3 position = bases[p] + Vector3.up * above + wind * (phase * Period * .55f) + inward * inflow
                        + new Vector3(Mathf.Sin(phase * 5f + c * 6.28f), 0f, Mathf.Cos(phase * 4f + c * 6.28f)) * (phase * 420f);
                    float alpha = .9f * strength * Mathf.Lerp(1f, .85f, settle) * Sa74Firestorm.SmokeFade(phase);
                    if (alpha <= .004f) continue;
                    Color color = SmokeColor(phase, c, Sa74Firestorm.Flicker(age * .6f, seed + (uint)k));
                    color.a = alpha;
                    smokeBuffer[smokeCount++] = new ParticleSystem.Particle
                    {
                        position = position, startSize = Mathf.Lerp(700f, 4200f, phase) * (.8f + .4f * c) * scale,
                        startColor = color, rotation = c * 360f + age * (c - .5f) * 2f,
                        startLifetime = 10000f, remainingLifetime = 10000f, randomSeed = seed + (uint)k + 1u
                    };
                }
                float flicker = Sa74Firestorm.Flicker(age, seed);
                float burning = Mathf.Lerp(1f, .72f, settle) * strength * flicker;
                fireBuffer[fireCount++] = new ParticleSystem.Particle
                {
                    position = bases[p] + Vector3.up * (90f + a * 50f), startSize = (1500f + a * 1200f) * flicker * scale,
                    startColor = new Color(1f, .24f, .035f, .40f * burning),
                    startLifetime = 10000f, remainingLifetime = 10000f, randomSeed = seed + 3u
                };
                fireBuffer[fireCount++] = new ParticleSystem.Particle
                {
                    position = bases[p] + Vector3.up * (130f + a * 50f), startSize = (800f + b * 700f) * flicker * scale,
                    startColor = new Color(1f, .46f, .08f, .72f * burning),
                    startLifetime = 10000f, remainingLifetime = 10000f, randomSeed = seed + 5u
                };
                fireBuffer[fireCount++] = new ParticleSystem.Particle
                {
                    position = bases[p] + Vector3.up * (170f + b * 60f), startSize = (330f + b * 330f) * flicker,
                    startColor = new Color(1f, .84f, .44f, .58f * burning),
                    startLifetime = 10000f, remainingLifetime = 10000f, randomSeed = seed + 7u
                };
                // A ragged fire front around the core, each tongue flickering on its own, so a fire reads as a burning line, not one blob.
                for (int f = 3; f < Sa74Firestorm.FlameQuads - 1; f++)
                {
                    uint tongue = seed + 31u * (uint)f;
                    float spin = Hash(tongue) * 6.2831853f, reachOut = (500f + Hash(tongue + 1u) * 1100f) * scale;
                    float lick = Sa74Firestorm.Flicker(age * 1.7f, tongue);
                    fireBuffer[fireCount++] = new ParticleSystem.Particle
                    {
                        position = bases[p] + new Vector3(Mathf.Cos(spin) * reachOut, 110f + Hash(tongue + 2u) * 80f, Mathf.Sin(spin) * reachOut),
                        startSize = (420f + Hash(tongue + 3u) * 520f) * lick * Mathf.Sqrt(scale),
                        startColor = new Color(1f, .38f + .2f * lick, .06f, .62f * burning * lick),
                        startLifetime = 10000f, remainingLifetime = 10000f, randomSeed = tongue
                    };
                }
                // A wide dim halo: from far away and at night the burning land reads as one orange glow, not single dots.
                fireBuffer[fireCount++] = new ParticleSystem.Particle
                {
                    position = bases[p] + Vector3.up * 60f, startSize = (3800f + b * 2400f) * scale,
                    startColor = new Color(1f, .30f, .05f, .16f * burning),
                    startLifetime = 10000f, remainingLifetime = 10000f, randomSeed = seed + 13u
                };
            }
            for (int i = 0; i < emberBuffer.Length && count > 0; i++)
            {
                int p = i % count;
                float strength = Sa74Firestorm.Growth(age, starts[p]);
                if (strength <= .001f) continue;
                uint seed = unchecked(site.Id * 32452843u + (uint)i * 104729u);
                float a = Hash(seed), b = Hash(seed + 3u);
                float phase = Mathf.Repeat(age / (38f + b * 60f) + a, 1f);
                float flicker = Sa74Firestorm.Flicker(age, seed + 9u);
                Vector3 position = bases[p] + Vector3.up * (180f + phase * (2200f + a * 2400f))
                    + wind * (phase * 900f) + new Vector3(Mathf.Sin(phase * 9f + a * 6.28f), 0f, Mathf.Cos(phase * 7f + b * 6.28f)) * (phase * 260f);
                float alpha = .90f * strength * Mathf.Pow(1f - phase, 1.4f) * flicker;
                if (alpha <= .004f) continue;
                emberBuffer[emberCount++] = new ParticleSystem.Particle
                {
                    position = position, startSize = 25f + a * 70f,
                    startColor = Color.Lerp(new Color(1f, .18f, .015f), new Color(1f, .62f, .12f), b) * new Color(1f, 1f, 1f, alpha),
                    rotation = a * 360f + age * (b - .5f) * 8f,
                    startLifetime = 10000f, remainingLifetime = 10000f, randomSeed = seed + 1u
                };
            }
            if (hasPillar) smokeCount = PosePillar(age, settle, wind, smokeCount);
            float deckStrength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 180f, age)) * Mathf.Lerp(1f, .70f, settle);
            for (int i = 0; i < deckBuffer.Length && count > 0; i++)
            {
                uint seed = unchecked(site.Id * 49979687u + (uint)i * 104729u);
                float a = Hash(seed), b = Hash(seed + 7u);
                float angle = i * 2.39996323f + b * .5f;
                float radial = Mathf.Sqrt(a) * Sa74Firestorm.DeckRadius;
                float phase = Mathf.Repeat(age / (420f + b * 360f) + b, 1f);
                Vector3 position = centre + new Vector3(Mathf.Cos(angle) * radial, 5200f + phase * 3800f + a * 1800f, Mathf.Sin(angle) * radial)
                    + wind * (phase * 1200f);
                float alpha = .16f * deckStrength * (.55f + .45f * b) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phase * 8f));
                if (alpha <= .003f) continue;
                float shade = .035f + a * .020f;
                deckBuffer[deckCount++] = new ParticleSystem.Particle
                {
                    position = position, startSize = 2600f + a * 4200f,
                    startColor = new Color(shade * 1.05f, shade, shade * .93f, alpha),
                    rotation = b * 360f + age * (a - .5f) * .5f,
                    startLifetime = 10000f, remainingLifetime = 10000f, randomSeed = seed + 1u
                };
            }
            smoke.SetParticles(smokeBuffer, smokeCount);
            fire.SetParticles(fireBuffer, fireCount);
            embers.SetParticles(emberBuffer, emberCount);
            deck.SetParticles(deckBuffer, deckCount);
            ActivePlumes = active; ActiveFires = fireCount; ActiveEmbers = emberCount; ActiveDeck = deckCount;
        }

        /// <summary>Ground zero keeps one towering black pillar for good, glowing from the fires at its root and feeding the soot deck.</summary>
        private int PosePillar(float age, float settle, Vector3 wind, int smokeCount)
        {
            float strength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Sa74Burn.StartSeconds, 90f, age)) * Mathf.Lerp(1f, .88f, settle);
            for (int k = 0; k < Sa74Firestorm.PillarPuffs; k++)
            {
                uint seed = unchecked(site.Id * 86028121u + (uint)k * 104729u);
                float c = Hash(seed);
                float phase = Mathf.Repeat(age / Sa74Firestorm.PillarPeriod + (k + c * .5f) / Sa74Firestorm.PillarPuffs, 1f);
                float alpha = .85f * strength * Sa74Firestorm.SmokeFade(phase);
                if (alpha <= .004f) continue;
                Vector3 position = pillar + Vector3.up * (200f + phase * 13000f) + wind * (phase * Sa74Firestorm.PillarPeriod * .35f)
                    + new Vector3(Mathf.Sin(phase * 3f + c * 6.28f), 0f, Mathf.Cos(phase * 2.5f + c * 6.28f)) * (phase * 1400f);
                // The glow climbs higher up the pillar than up a single fire's column: the crater is the brightest fire on the map.
                Color color = SmokeColor(phase * .55f, c, Sa74Firestorm.Flicker(age * .4f, seed));
                color.a = alpha;
                smokeBuffer[smokeCount++] = new ParticleSystem.Particle
                {
                    position = position, startSize = Mathf.Lerp(2600f, 9000f, phase) * (.85f + .3f * c),
                    startColor = color, rotation = c * 360f + age * (c - .5f),
                    startLifetime = 10000f, remainingLifetime = 10000f, randomSeed = seed + 1u
                };
            }
            return smokeCount;
        }

        /// <summary>Unlit smoke: the root carries the fire's own orange glow (it must read in the dark end state), the top is cold soot.</summary>
        private static Color SmokeColor(float phase, float c, float flicker)
        {
            float shade = .045f + c * .028f + phase * .045f;
            Color lit = new Color(.70f, .27f, .07f) * (.75f + .25f * flicker);
            Color soot = new Color(shade * 1.06f, shade, shade * .95f);
            return Color.Lerp(lit, soot, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.02f, .34f, phase)));
        }

        private static float Hash(uint value)
        {
            unchecked { value ^= value >> 16; value *= 0x7feb352du; value ^= value >> 15; value *= 0x846ca68bu; value ^= value >> 16; }
            return (value & 0x00ffffffu) / 16777215f;
        }

        private void Clear()
        {
            if (root != null) Destroy(root);
            foreach (Detail detail in details)
            {
                if (detail.Material != null && detail.Applied && detail.Material.GetTexture(detail.Property) == sootDetail)
                    detail.Material.SetTexture(detail.Property, detail.Original);
            }
            details.Clear();
            if (sootDetail != null) Destroy(sootDetail);
            sootDetail = null;
            if (smokeMaterial != null) Destroy(smokeMaterial);
            if (fireMaterial != null) Destroy(fireMaterial);
            if (deckMaterial != null) Destroy(deckMaterial);
            if (charMaterial != null) Destroy(charMaterial);
            foreach (Ground ground in grounds)
            {
                // The terrain material is a shared asset: always hand its own texture back.
                if (ground.Material != null) ground.Material.SetTexture(ground.Property, ground.Original);
                if (ground.Burned != null) { ground.Burned.Release(); Destroy(ground.Burned); }
            }
            grounds.Clear(); charStep = -1;
            root = null; smoke = fire = embers = deck = null; smokeMaterial = fireMaterial = deckMaterial = charMaterial = null; site = null;
            count = 0; hasPillar = false; ActivePlumes = ActiveFires = ActiveEmbers = ActiveDeck = 0;
            CharMaterials = 0; CharDetails = 0; CharStep = 0; CharReach = 0f; CharWrites = 0;
        }

        private void OnDestroy() => Clear();
    }
}
