using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    internal sealed class FireVisualPool
    {
        internal enum LayerKind
        {
            Flame,
            Smoke
        }

        internal sealed class Visual
        {
            public GameObject Root;
            public ParticleSystem[] Systems;
            public LayerKind[] Kinds;
            public float[] BaseRates;
            public Vector3[] BaseShapes;
            public Light Light;
            public bool Active;
            public float FlameIntensity;
            public bool Forest;
            public float FootprintScale;
            public float EmissionScale;
            public float LightScale;
            public float GrowthSeconds;
            public float FlickerSeed;
            public float ClusterScale = 1f;
            public bool HasParticles => Systems != null && Systems.Length > 0;

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
                if (Root != null)
                {
                    float scale = forest ? Mathf.Lerp(1.04f, 1.18f, b) : Mathf.Lerp(0.68f, 0.84f, b);
                    Root.transform.localScale = Vector3.one * scale;
                }
            }

            public void SetPhase(float ageSeconds, float remainingFraction, Vector3 wind)
            {
                if (Systems == null || BaseRates == null) return;
                float growth = Smooth01(ageSeconds / Mathf.Max(GrowthSeconds, 1f));
                float flameEnd = Smooth01(remainingFraction / 0.22f);
                float smokeEnd = Smooth01(remainingFraction / 0.045f);
                float flare = 0.76f + Mathf.PerlinNoise(FlickerSeed, Time.timeSinceLevelLoad * 0.38f) * 0.34f;
                FlameIntensity = (0.018f + growth * 0.982f) * flameEnd * EmissionScale * flare;
                if (Forest)
                    FlameIntensity *= Mathf.Lerp(1f, 1.30f, (ClusterScale - 1f) / 2f);
                float smokeIntensity = (0.06f + growth * 0.94f)
                    * Mathf.Lerp(0.62f, 1f, flameEnd) * smokeEnd;
                float spread = Mathf.Lerp(FootprintScale * 0.24f, FootprintScale, growth) *
                    (Forest ? ClusterScale : 1f);

                for (int i = 0; i < Systems.Length && i < BaseRates.Length; i++)
                {
                    ParticleSystem system = Systems[i];
                    if (system == null) continue;
                    bool smoke = Kinds[i] == LayerKind.Smoke;
                    ParticleSystem.EmissionModule emission = system.emission;
                    emission.rateOverTimeMultiplier = BaseRates[i] *
                        (smoke ? smokeIntensity * EmissionScale : FlameIntensity);
                    ParticleSystem.ShapeModule shape = system.shape;
                    shape.scale = new Vector3(
                        BaseShapes[i].x * spread,
                        BaseShapes[i].y,
                        BaseShapes[i].z * spread);
                    ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
                    velocity.x = wind.x * (smoke ? 0.72f : 0.16f);
                    velocity.z = wind.z * (smoke ? 0.72f : 0.16f);
                }
            }

            private static float Smooth01(float value)
            {
                value = Mathf.Clamp01(value);
                return value * value * (3f - 2f * value);
            }

            private static float Signature(GlobalPosition position, float xScale, float zScale)
            {
                return Mathf.Repeat(Mathf.Sin(position.x * xScale + position.z * zScale) * 43758.5453f, 1f);
            }
        }

        private readonly List<Visual> visuals = new List<Visual>(24);
        private Material flameMaterial;
        private Material smokeMaterial;
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
            Visual visual = Create();
            visuals.Add(visual);
            Activate(visual, position, forest);
            return visual;
        }

        public void Release(Visual visual)
        {
            if (visual == null || !visual.Active) return;
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
                if (visuals[i].Root != null) UnityEngine.Object.Destroy(visuals[i].Root);
            visuals.Clear();
            templatesSearched = false;
            flameMaterial = smokeMaterial = null;
        }

        private Visual Create()
        {
            FindMaterials();
            var root = new GameObject("BoscaliSummer.FireSite");
            root.transform.SetParent(Datum.origin, false);
            var systems = new List<ParticleSystem>(3);
            var kinds = new List<LayerKind>(3);
            var rates = new List<float>(3);
            var shapes = new List<Vector3>(3);

            if (flameMaterial != null)
            {
                AddFlameLayer(root.transform, "SurfaceFlame", flameMaterial,
                    new Vector3(30f, 1.2f, 23f), 24f, 0.65f, 1.45f, 0.5f, 2.2f, 3.2f, 7.5f,
                    systems, kinds, rates, shapes);
                AddFlameLayer(root.transform, "FlameTongues", flameMaterial,
                    new Vector3(22f, 1f, 17f), 7.5f, 1.1f, 2.35f, 1.2f, 3.6f, 4.2f, 9.5f,
                    systems, kinds, rates, shapes);
            }
            // Fire smoke is emitted through the game's vanilla large-smoke catalogue by
            // ImpactFireManager. Keeping it out of this local pool avoids a second, flat
            // material competing with the ash-gray plume and keeps the pool flame-only.

            var lightObject = new GameObject("FireLight");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localPosition = Vector3.up * 5f;
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.28f, 0.045f);
            light.range = 58f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            light.enabled = false;

            return new Visual
            {
                Root = root,
                Systems = systems.ToArray(),
                Kinds = kinds.ToArray(),
                BaseRates = rates.ToArray(),
                BaseShapes = shapes.ToArray(),
                Light = light
            };
        }

        private void FindMaterials()
        {
            if (templatesSearched) return;
            templatesSearched = true;
            int flameScore = int.MinValue;
            int smokeScore = int.MinValue;
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
                        int score = ScoreMaterial(path, false);
                        if (score > flameScore) { flameScore = score; flameMaterial = renderer.sharedMaterial; }
                    }
                    if (path.Contains("smoke"))
                    {
                        int score = ScoreMaterial(path, true);
                        if (score > smokeScore) { smokeScore = score; smokeMaterial = renderer.sharedMaterial; }
                    }
                }
            }
            Plugin.Logger.LogInfo($"Fire materials ready: flame={(flameMaterial != null)}, smoke={(smokeMaterial != null)}.");
        }

        private static int ScoreMaterial(string path, bool smoke)
        {
            int score = smoke ? (path.Contains("smoke") ? 50 : 0) : (path.Contains("flame") ? 60 : 40);
            if (path.Contains("damage") || path.Contains("burn")) score += 25;
            if (path.Contains("engine")) score += 8;
            string[] explosive = { "explosion", "spark", "shrapnel", "debris", "impact", "muzzle" };
            for (int i = 0; i < explosive.Length; i++)
                if (path.Contains(explosive[i])) score -= 80;
            return score;
        }

        private static void AddFlameLayer(
            Transform parent, string name, Material material, Vector3 shapeScale, float rate,
            float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax,
            List<ParticleSystem> systems, List<LayerKind> kinds, List<float> rates, List<Vector3> shapes)
        {
            ParticleSystem system = CreateSystem(parent, name, material, shapeScale, rate,
                lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax, 220);
            ParticleSystem.MainModule main = system.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.22f, 0.015f, 0.72f),
                new Color(1f, 0.62f, 0.08f, 0.9f));
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
            Register(system, LayerKind.Flame, shapeScale, rate, systems, kinds, rates, shapes);
        }

        private static void AddSmokeLayer(
            Transform parent, Material material,
            List<ParticleSystem> systems, List<LayerKind> kinds, List<float> rates, List<Vector3> shapes)
        {
            Vector3 shapeScale = new Vector3(44f, 2f, 34f);
            const float rate = 8.5f;
            ParticleSystem system = CreateSystem(parent, "DriftingSmoke", material, shapeScale, rate,
                16f, 28f, 2.4f, 5.4f, 20f, 38f, 260);
            ParticleSystem.MainModule main = system.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.10f, 0.095f, 0.085f, 0.68f),
                new Color(0.32f, 0.31f, 0.29f, 0.84f));
            main.gravityModifier = -0.035f;

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.strength = 4.2f;
            noise.frequency = 0.12f;
            noise.scrollSpeed = 0.2f;
            noise.damping = true;

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = SmokeGradient();
            Register(system, LayerKind.Smoke, shapeScale, rate, systems, kinds, rates, shapes);
        }

        private static ParticleSystem CreateSystem(
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

            ParticleSystemRenderer renderer = gameObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortMode = ParticleSystemSortMode.YoungestInFront;
            renderer.sortingFudge = -1f;
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return system;
        }

        private static ParticleSystem.MinMaxGradient FlameGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.72f, 0.16f), 0f),
                    new GradientColorKey(new Color(1f, 0.22f, 0.025f), 0.52f),
                    new GradientColorKey(new Color(0.18f, 0.055f, 0.02f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.82f, 0.08f),
                    new GradientAlphaKey(0.58f, 0.62f),
                    new GradientAlphaKey(0f, 1f)
                });
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        private static ParticleSystem.MinMaxGradient SmokeGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.16f, 0.13f, 0.11f), 0f),
                    new GradientColorKey(new Color(0.24f, 0.23f, 0.22f), 0.45f),
                    new GradientColorKey(new Color(0.44f, 0.45f, 0.44f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.72f, 0.12f),
                    new GradientAlphaKey(0.52f, 0.68f),
                    new GradientAlphaKey(0f, 1f)
                });
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        private static void Register(
            ParticleSystem system, LayerKind kind, Vector3 shape, float rate,
            List<ParticleSystem> systems, List<LayerKind> kinds, List<float> rates, List<Vector3> shapes)
        {
            systems.Add(system);
            kinds.Add(kind);
            rates.Add(rate);
            shapes.Add(shape);
        }

        private static void Activate(Visual visual, GlobalPosition position, bool forest)
        {
            visual.Active = true;
            visual.ClusterScale = 1f;
            visual.FlameIntensity = 0f;
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
