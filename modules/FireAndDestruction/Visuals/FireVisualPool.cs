using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    internal sealed class FireVisualPool
    {
        internal sealed class Visual
        {
            public GameObject Root;
            public ParticleSystem[] Systems;
            public float[] BaseRates;
            public Vector3[] BaseShapes;
            public ParticleSystem.MinMaxCurve[] BaseSizes;
            public ParticleSystem.MinMaxCurve[] BaseLifetimes;
            public Light Light;
            public bool Active;
            public float FlameIntensity;
            public bool Forest;
            public float FootprintScale;
            public float EmissionScale;
            public float LightScale;
            public float GrowthSeconds;
            public float FlickerSeed;
            public float SizeScale = 1f;
            public float LifetimeScale = 1f;
            public float ClusterScale = 1f;
            public bool Sleeping;
            public CloudDeckSorting Deck;
            public bool BehindDeck;
            public FireFrontCell Cell;
            public Mesh FrontMesh;
            private int frontMask = -1;
            private Vector3 groundNormal = Vector3.up;
            private readonly List<Vector3> frontVertices = new List<Vector3>(FireFrontCell.MaximumVertices * 4);
            private readonly List<int> frontTriangles = new List<int>(FireFrontCell.MaximumVertices * 6);
            private float frontFraction = 1f;

            public void SetForestCell(FireFrontCell cell, Vector3 normal)
            {
                Cell = cell;
                groundNormal = normal;
                Root.transform.localScale = Vector3.one;
                Root.transform.rotation = Quaternion.identity;
                if (FrontMesh == null) FrontMesh = new Mesh { name = "Forest flame front" };
                frontMask = -1;
                SetFrontEdges(0);
            }

            public void SetFrontEdges(int connectedMask)
            {
                if (Cell == null || frontMask == connectedMask) return;
                frontMask = connectedMask;
                // Build an emission strip on exposed edges only. Shared edges disappear
                // when another cell ignites, leaving one continuous irregular perimeter.
                var vertices = frontVertices; vertices.Clear();
                var triangles = frontTriangles; triangles.Clear();
                frontFraction = Mathf.Clamp01((Cell.Count - CountEdges(connectedMask)) / (float)Cell.Count);
                for (int i = 0; i < Cell.Count; i++)
                {
                    if ((connectedMask & (1 << i)) != 0) continue;
                    FireFrontCell.Point a = Cell.Vertices[i], b = Cell.Vertices[(i + 1) % Cell.Count];
                    int start = vertices.Count;
                    vertices.Add(GroundVertex(a.X, a.Z));
                    vertices.Add(GroundVertex(b.X, b.Z));
                    vertices.Add(GroundVertex(a.X * 0.72f, a.Z * 0.72f));
                    vertices.Add(GroundVertex(b.X * 0.72f, b.Z * 0.72f));
                    triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
                    triangles.Add(start + 1); triangles.Add(start + 2); triangles.Add(start + 3);
                }
                // Detach before rebuilding: clearing a mesh still referenced by live
                // particle shapes makes Unity validate its temporary empty geometry.
                for (int i = 0; Systems != null && i < Systems.Length; i++)
                {
                    var shape = Systems[i].shape; shape.enabled = false;
                    shape.shapeType = ParticleSystemShapeType.Box; shape.mesh = null;
                }
                FrontMesh.Clear(); FrontMesh.SetVertices(vertices); FrontMesh.SetTriangles(triangles, 0);
                if (vertices.Count > 0) FrontMesh.RecalculateNormals();
                for (int i = 0; Systems != null && i < Systems.Length; i++)
                {
                    var shape = Systems[i].shape;
                    shape.enabled = vertices.Count > 0;
                    shape.shapeType = vertices.Count > 0 ? ParticleSystemShapeType.Mesh : ParticleSystemShapeType.Box;
                    shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
                    shape.mesh = vertices.Count > 0 ? FrontMesh : null; shape.scale = Vector3.one; shape.position = Vector3.zero;
                }
            }

            private Vector3 GroundVertex(float x, float z) => new Vector3(x,
                groundNormal.y > 0.25f ? 0.25f - (x * groundNormal.x + z * groundNormal.z) / groundNormal.y : 0.25f, z);

            public void SetSleeping(bool sleep)
            {
                if (Sleeping == sleep) return;
                Sleeping = sleep;
                if (sleep)
                {
                    SetLight(false);
                    if (Systems != null)
                    {
                        for (int i = 0; i < Systems.Length; i++)
                        {
                            if (Systems[i] != null && Systems[i].isPlaying)
                                Systems[i].Pause(true);
                        }
                    }
                    if (Root != null) Root.SetActive(false);
                }
                else
                {
                    if (Root != null) Root.SetActive(true);
                    if (Systems != null)
                    {
                        for (int i = 0; i < Systems.Length; i++)
                        {
                            if (Systems[i] != null)
                                Systems[i].Play(true);
                        }
                    }
                }
            }

            public void SetPosition(GlobalPosition position)
            {
                if (Root != null) Root.transform.position = position.ToLocalPosition();
            }

            public void SetLight(bool enabled)
            {
                if (Light == null) return;
                Light.enabled = enabled && FlameIntensity > 0.025f;
                if (!Light.enabled || Root == null) return;
                float seed = Root.transform.position.x * 0.001f + Root.transform.position.z * 0.002f;
                float flicker = 0.72f + Mathf.PerlinNoise(seed, Time.timeSinceLevelLoad * 2.7f) * 0.28f;
                Light.intensity = (0.25f + 2.35f * FlameIntensity) * flicker * LightScale;
                Light.range = Mathf.Lerp(14f, Forest ? 54f : 32f, FlameIntensity) * LightScale *
                    (Forest ? Mathf.Lerp(1f, 1.34f, (ClusterScale - 1f) / 2f) : 1f);
            }

            public void SetClusterScale(float scale)
            {
                ClusterScale = Forest ? Mathf.Clamp(scale, 1f, 3f) : 1f;
            }

            public void Configure(bool forest, GlobalPosition position)
            {
                Forest = forest;
                float a = Signature(position, 0.017f, 0.031f);
                float b = Signature(position, 0.043f, -0.019f);
                FootprintScale = forest ? Mathf.Lerp(1.06f, 1.34f, a) : Mathf.Lerp(0.32f, 0.53f, a);
                EmissionScale = forest ? Mathf.Lerp(1.02f, 1.24f, b) : Mathf.Lerp(0.42f, 0.67f, b);
                LightScale = forest ? Mathf.Lerp(0.82f, 1f, a) : Mathf.Lerp(0.55f, 0.78f, a);
                GrowthSeconds = forest ? Mathf.Lerp(5.5f, 9f, b) : Mathf.Lerp(12f, 21f, b);
                FlickerSeed = a * 13.7f + b * 29.1f;
                SizeScale = forest ? Mathf.Lerp(1.06f, 1.30f, a) : Mathf.Lerp(0.80f, 0.98f, a);
                LifetimeScale = forest ? Mathf.Lerp(1.08f, 1.32f, b) : Mathf.Lerp(0.90f, 1.04f, b);
                if (Root != null)
                {
                    float scale = forest ? Mathf.Lerp(1.04f, 1.18f, b) : Mathf.Lerp(0.68f, 0.84f, b);
                    Root.transform.localScale = Vector3.one * scale;
                    if (forest && Cell != null)
                    {
                        Root.transform.localScale = Vector3.one;
                        Root.transform.rotation = Quaternion.identity;
                    }
                }
                for (int i = 0; Systems != null && i < Systems.Length; i++)
                {
                    ParticleSystem.MainModule main = Systems[i].main;
                    bool tongue = forest && i < 3;
                    main.startSize3D = tongue;
                    main.startSize = BaseSizes[i];
                    main.startLifetime = BaseLifetimes[i];
                    main.startRotation = tongue
                        ? new ParticleSystem.MinMaxCurve(-0.16f, 0.16f)
                        : new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    ParticleSystem.ShapeModule shape = Systems[i].shape;
                    shape.position = forest && Cell == null ? Vector3.up * BaseShapes[i].y * 0.5f : Vector3.zero;
                    if (!forest || Cell == null) { shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.mesh = null; }
                }
            }

            public void SetPhase(float ageSeconds, float remainingFraction, Vector3 wind)
            {
                if (Systems == null || BaseRates == null) return;
                if (Root != null && Deck != null)
                    BehindDeck = Deck.Sync(Systems, Root.transform.position, BehindDeck);
                float growth = Smooth01(ageSeconds / Mathf.Max(GrowthSeconds, 1f));
                float flameEnd = Smooth01(remainingFraction / 0.22f);
                float flare = 0.76f + Mathf.PerlinNoise(FlickerSeed, Time.timeSinceLevelLoad * 0.38f) * 0.34f;
                FlameIntensity = (0.018f + growth * 0.982f) * flameEnd * EmissionScale * flare;
                if (Forest && Cell == null)
                    FlameIntensity *= Mathf.Lerp(1f, 1.30f, (ClusterScale - 1f) / 2f);
                float spread = Mathf.Lerp(FootprintScale * 0.24f, FootprintScale, growth) *
                    (Forest ? ClusterScale : 1f);

                for (int i = 0; i < Systems.Length && i < BaseRates.Length; i++)
                {
                    ParticleSystem system = Systems[i];
                    if (system == null) continue;
                    ParticleSystem.MainModule main = system.main;
                    bool ember = i == 3;
                    if (Forest)
                    {
                        // Scale from the authored ranges every tick, without accumulating
                        // size changes when this pooled effect changes phase or owner.
                        main.startLifetime = new ParticleSystem.MinMaxCurve(
                            BaseLifetimes[i].constantMin * LifetimeScale,
                            BaseLifetimes[i].constantMax * LifetimeScale);
                        float width = SizeScale * (ember ? 0.32f : 0.72f);
                        var size = new ParticleSystem.MinMaxCurve(
                            BaseSizes[i].constantMin * width, BaseSizes[i].constantMax * width);
                        if (ember) main.startSize = size;
                        else
                        {
                            main.startSizeX = size;
                            main.startSizeZ = size;
                            float height = i == 2 ? 2.1f : i == 0 ? 1.55f : 1.25f;
                            main.startSizeY = new ParticleSystem.MinMaxCurve(
                                size.constantMin * height, size.constantMax * height);
                        }
                    }
                    else
                    {
                        main.startLifetimeMultiplier = LifetimeScale;
                        main.startSizeMultiplier = SizeScale;
                    }
                    ParticleSystem.EmissionModule emission = system.emission;
                    float layerPulse = Forest ? 0.65f + Mathf.PerlinNoise(
                        FlickerSeed + i * 7.31f, Time.timeSinceLevelLoad * (ember ? 0.45f : 1.1f)) * 0.7f : 1f;
                    emission.rateOverTimeMultiplier = BaseRates[i] * FlameIntensity * layerPulse *
                        (Cell == null ? 1f : frontFraction) * FxBus.Scales.Particles;
                    ParticleSystem.ShapeModule shape = system.shape;
                    shape.scale = Cell != null && Forest ? Vector3.one : new Vector3(
                        BaseShapes[i].x * spread,
                        BaseShapes[i].y,
                        BaseShapes[i].z * spread);
                    ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
                    float drift = Forest && ember ? 0.55f : 0.16f;
                    float driftX = (Forest ? Mathf.Clamp(wind.x, -18f, 18f) : wind.x) * drift;
                    float driftZ = (Forest ? Mathf.Clamp(wind.z, -18f, 18f) : wind.z) * drift;
                    // Unity requires all linear velocity axes to use the same curve mode.
                    // Vertical rise is TwoConstants; constant X/Z produced a log per step.
                    velocity.x = new ParticleSystem.MinMaxCurve(driftX, driftX);
                    velocity.z = new ParticleSystem.MinMaxCurve(driftZ, driftZ);
                }
            }

            private static float Smooth01(float value)
            {
                value = Mathf.Clamp01(value);
                return value * value * (3f - 2f * value);
            }

            private static int CountEdges(int value)
            {
                int count = 0;
                while (value > 0) { count += value & 1; value >>= 1; }
                return count;
            }

            private static float Signature(GlobalPosition position, float xScale, float zScale)
            {
                return Mathf.Repeat(Mathf.Sin(position.x * xScale + position.z * zScale) * 43758.5453f, 1f);
            }
        }

        private const int MaximumVisuals = 32;

        private readonly List<Visual> visuals = new List<Visual>(24);
        private readonly CloudDeckSorting deck = new CloudDeckSorting();
        private Material flameMaterial;
        private ParticleSystem flameTemplate;
        private bool templatesSearched;

        public Visual Acquire(GlobalPosition position, bool forest)
        {
            for (int i = 0; i < visuals.Count; i++)
            {
                if (!visuals[i].Active)
                {
                    Activate(visuals[i], position, forest);
                    return visuals[i];
                }
            }
            if (visuals.Count >= MaximumVisuals) return null;
            Visual visual = Create();
            visuals.Add(visual);
            Activate(visual, position, forest);
            return visual;
        }

        public void Release(Visual visual)
        {
            if (visual == null || !visual.Active) return;
            visual.Sleeping = false;
            visual.Active = false;
            visual.FlameIntensity = 0f;
            visual.SetLight(false);
            if (visual.Systems != null)
                for (int i = 0; i < visual.Systems.Length; i++)
                    if (visual.Systems[i] != null)
                        visual.Systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (visual.Root != null) visual.Root.SetActive(false);
        }

        public void Clear()
        {
            for (int i = 0; i < visuals.Count; i++)
            {
                if (visuals[i].FrontMesh != null) UnityEngine.Object.Destroy(visuals[i].FrontMesh);
                if (visuals[i].Root != null) UnityEngine.Object.Destroy(visuals[i].Root);
            }
            visuals.Clear();
            deck.Clear();
            templatesSearched = false;
            flameTemplate = null;
            flameMaterial = null;
        }

        private Visual Create()
        {
            FindMaterials();
            var root = new GameObject("BoscaliSummer.FireSite");
            root.transform.SetParent(Datum.origin, false);
            var systems = new List<ParticleSystem>(3);
            var rates = new List<float>(3);
            var shapes = new List<Vector3>(3);

            if (flameMaterial != null)
            {
                AddFlameLayer(root.transform, "FlameCore", flameMaterial,
                    new Vector3(16f, 0.9f, 13f), 18f, 0.4f, 1f, 0.4f, 1.5f, 2.2f, 5.4f,
                    systems, rates, shapes);
                AddFlameLayer(root.transform, "SurfaceFlame", flameMaterial,
                    new Vector3(30f, 1.2f, 23f), 20f, 0.65f, 1.45f, 0.5f, 2.2f, 3.2f, 7.5f,
                    systems, rates, shapes);
                AddFlameLayer(root.transform, "FlameTongues", flameMaterial,
                    new Vector3(22f, 1f, 17f), 7f, 1.1f, 2.35f, 1.4f, 3.8f, 4.2f, 9.5f,
                    systems, rates, shapes);
                AddEmberLayer(root.transform, "Embers", flameMaterial,
                    new Vector3(26f, 0.8f, 20f), 9f, 2.4f, 4.6f, 5.5f, 11f, 0.35f, 1.1f,
                    systems, rates, shapes);
            }
            // Fire smoke is emitted through the game's vanilla large-smoke catalogue by
            // ImpactFireManager. Keeping it out of this local pool avoids a second, flat
            // material competing with the ash-gray plume and keeps the pool flame-only.

            var lightObject = new GameObject("FireLight");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localPosition = Vector3.up * 5f;
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.33f, 0.05f);
            light.range = 58f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            light.enabled = false;

            var sizes = new ParticleSystem.MinMaxCurve[systems.Count];
            var lifetimes = new ParticleSystem.MinMaxCurve[systems.Count];
            for (int i = 0; i < systems.Count; i++)
            {
                sizes[i] = systems[i].main.startSize;
                lifetimes[i] = systems[i].main.startLifetime;
            }

            return new Visual
            {
                Root = root,
                Systems = systems.ToArray(),
                Deck = deck,
                BaseRates = rates.ToArray(),
                BaseShapes = shapes.ToArray(),
                BaseSizes = sizes,
                BaseLifetimes = lifetimes,
                Light = light
            };
        }

        private void FindMaterials()
        {
            if (templatesSearched) return;
            templatesSearched = true;
            int flameScore = int.MinValue;
            DamageParticles[] effects = Resources.FindObjectsOfTypeAll<DamageParticles>();
            for (int e = 0; e < effects.Length; e++)
            {
                if (effects[e] == null) continue;
                ParticleSystem[] systems = effects[e].GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < systems.Length; i++)
                {
                    ParticleSystemRenderer renderer = systems[i].GetComponent<ParticleSystemRenderer>();
                    if (renderer == null || renderer.sharedMaterial == null) continue;
                    string path = (effects[e].name + "/" + systems[i].name).ToLowerInvariant();
                    if (path.Contains("fire") || path.Contains("flame"))
                    {
                        int score = ScoreMaterial(path);
                        if (score > flameScore) { flameScore = score; flameMaterial = renderer.sharedMaterial; flameTemplate = systems[i]; }
                    }
                }
            }
            Plugin.Logger.LogInfo($"Fire flame material ready: {flameMaterial != null} " +
                $"(flipbook {DescribeFlipbook(flameTemplate)}).");
        }

        private static int ScoreMaterial(string path)
        {
            int score = path.Contains("flame") ? 60 : 40;
            if (path.Contains("damage") || path.Contains("burn")) score += 25;
            if (path.Contains("engine")) score += 8;
            string[] explosive = { "explosion", "spark", "shrapnel", "debris", "impact", "muzzle" };
            for (int i = 0; i < explosive.Length; i++)
                if (path.Contains(explosive[i])) score -= 80;
            return score;
        }

        private void AddFlameLayer(
            Transform parent, string name, Material material, Vector3 shapeScale, float rate,
            float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax,
            List<ParticleSystem> systems, List<float> rates, List<Vector3> shapes)
        {
            ParticleSystem system = CreateSystem(parent, name, material, shapeScale, rate,
                lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax, 220);
            ParticleSystem.MainModule main = system.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.30f, 0.02f, 0.68f),
                new Color(1f, 0.66f, 0.10f, 0.92f));
            main.gravityModifier = -0.05f;

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.strength = 1.8f;
            noise.frequency = 0.32f;
            noise.scrollSpeed = 0.42f;
            noise.damping = true;

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = FlameGradient();

            // Flames bloom from a small kernel and taper as they die, which reads far more
            // like burning fuel than a stream of equally sized billboards.
            ParticleSystem.SizeOverLifetimeModule sizeOverLife = system.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, FlameSizeCurve());
            Register(system, shapeScale, rate, systems, rates, shapes);
        }

        private void AddEmberLayer(
            Transform parent, string name, Material material, Vector3 shapeScale, float rate,
            float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax,
            List<ParticleSystem> systems, List<float> rates, List<Vector3> shapes)
        {
            ParticleSystem system = CreateSystem(parent, name, material, shapeScale, rate,
                lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax, 140);
            ParticleSystem.MainModule main = system.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.80f, 0.34f, 1f),
                new Color(1f, 0.34f, 0.05f, 0.96f));
            // Embers ride the plume and cool in the air instead of falling like debris.
            main.gravityModifier = 0.02f;

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.strength = 2.6f;
            noise.frequency = 0.46f;
            noise.scrollSpeed = 0.85f;
            noise.damping = true;

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = EmberGradient();

            ParticleSystem.SizeOverLifetimeModule sizeOverLife = system.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, EmberSizeCurve());
            Register(system, shapeScale, rate, systems, rates, shapes);
        }

        private ParticleSystem CreateSystem(
            Transform parent, string name, Material material, Vector3 shapeScale, float rate,
            float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax,
            int maxParticles)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            ParticleSystem system = gameObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 4f;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = Datum.origin;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            // The box is a ground footprint. Vertical velocity is explicit so particles do
            // not shoot away along box-face normals like an explosion emitter.
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = maxParticles;
            main.stopAction = ParticleSystemStopAction.None;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = rate;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = shapeScale;

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.y = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            ParticleSystemRenderer renderer = gameObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortMode = ParticleSystemSortMode.YoungestInFront;
            renderer.sortingFudge = -1f;
            ApplyTextureSheet(system);
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return system;
        }

        private void ApplyTextureSheet(ParticleSystem system)
        {
            if (flameTemplate == null) return;
            ParticleSystem.TextureSheetAnimationModule source = flameTemplate.textureSheetAnimation;
            ParticleSystem.TextureSheetAnimationModule target = system.textureSheetAnimation;
            // The vanilla flame texture is a flipbook atlas. Without the template's sheet
            // animation each billboard shows the whole atlas at once: a grid of dots.
            target.enabled = source.enabled;
            if (!source.enabled) return;
            target.mode = source.mode;
            target.numTilesX = source.numTilesX;
            target.numTilesY = source.numTilesY;
            target.animation = source.animation;
            target.rowMode = source.rowMode;
            target.frameOverTime = source.frameOverTime;
            target.frameOverTimeMultiplier = source.frameOverTimeMultiplier;
            target.startFrame = source.startFrame;
            target.startFrameMultiplier = source.startFrameMultiplier;
            target.cycleCount = source.cycleCount;
            target.rowIndex = source.rowIndex;
            // useRandomRow/flipU/flipV are deprecated in Unity 2022: rowMode and renderer.flip carry them.
            ParticleSystemRenderer sourceRenderer = flameTemplate.GetComponent<ParticleSystemRenderer>();
            ParticleSystemRenderer targetRenderer = system.GetComponent<ParticleSystemRenderer>();
            if (sourceRenderer != null && targetRenderer != null) targetRenderer.flip = sourceRenderer.flip;
            target.uvChannelMask = source.uvChannelMask;
            target.speedRange = source.speedRange;
            if (source.mode != ParticleSystemAnimationMode.Sprites) return;
            for (int i = target.spriteCount - 1; i >= 0; i--) target.RemoveSprite(i);
            for (int i = 0; i < source.spriteCount; i++) target.AddSprite(source.GetSprite(i));
        }

        private static string DescribeFlipbook(ParticleSystem template)
        {
            if (template == null) return "no template";
            ParticleSystem.TextureSheetAnimationModule sheet = template.textureSheetAnimation;
            if (!sheet.enabled) return "disabled";
            return sheet.numTilesX + "x" + sheet.numTilesY + " " + sheet.mode;
        }

        private static AnimationCurve FlameSizeCurve()
        {
            var curve = new AnimationCurve();
            curve.AddKey(new Keyframe(0f, 0.35f));
            curve.AddKey(new Keyframe(0.32f, 1f));
            curve.AddKey(new Keyframe(1f, 0.18f));
            return curve;
        }

        private static AnimationCurve EmberSizeCurve()
        {
            var curve = new AnimationCurve();
            curve.AddKey(new Keyframe(0f, 0.9f));
            curve.AddKey(new Keyframe(0.55f, 0.55f));
            curve.AddKey(new Keyframe(1f, 0.12f));
            return curve;
        }

        private static ParticleSystem.MinMaxGradient FlameGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.86f, 0.34f), 0f),
                    new GradientColorKey(new Color(1f, 0.50f, 0.07f), 0.30f),
                    new GradientColorKey(new Color(0.88f, 0.18f, 0.02f), 0.68f),
                    new GradientColorKey(new Color(0.14f, 0.04f, 0.01f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.9f, 0.07f),
                    new GradientAlphaKey(0.72f, 0.45f),
                    new GradientAlphaKey(0f, 1f)
                });
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        private static ParticleSystem.MinMaxGradient EmberGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.88f, 0.42f), 0f),
                    new GradientColorKey(new Color(1f, 0.42f, 0.05f), 0.55f),
                    new GradientColorKey(new Color(0.32f, 0.07f, 0.01f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.95f, 0.10f),
                    new GradientAlphaKey(0.55f, 0.62f),
                    new GradientAlphaKey(0f, 1f)
                });
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        private static void Register(
            ParticleSystem system, Vector3 shape, float rate,
            List<ParticleSystem> systems, List<float> rates, List<Vector3> shapes)
        {
            systems.Add(system);
            rates.Add(rate);
            shapes.Add(shape);
        }

        private static void Activate(Visual visual, GlobalPosition position, bool forest)
        {
            visual.Active = true;
            visual.Sleeping = false;
            visual.ClusterScale = 1f;
            visual.FlameIntensity = 0f;
            visual.Cell = null;
            visual.Configure(forest, position);
            visual.Root.SetActive(true);
            visual.SetPosition(position);
            float yaw = Mathf.Repeat(position.x * 0.071f + position.z * 0.039f, 360f);
            visual.Root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (visual.Systems != null)
            {
                for (int i = 0; i < visual.Systems.Length; i++)
                {
                    if (visual.Systems[i] == null) continue;
                    ParticleSystem.EmissionModule emission = visual.Systems[i].emission;
                    emission.rateOverTimeMultiplier = 0f;
                    visual.Systems[i].Play(true);
                }
            }
        }
    }
}
