using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Reuses the smoke layer serialized into Nuclear Option's Fuel Depot destruction
    /// effect. Nothing about the particle material, color, noise, velocity, or shape is
    /// recreated here; a smoke-only copy of the vanilla prefab is pooled per fire site.
    /// </summary>
    internal sealed class FuelDepotSmokePool
    {
        internal enum SmokeProfile
        {
            Building,
            Forest,
            Ruin
        }

        internal sealed class Visual
        {
            public GameObject Root;
            public ParticleSystem[] Systems;
            public float[] BaseRates;
            public float[] BaseVelocityXMin;
            public float[] BaseVelocityXMax;
            public float[] BaseVelocityZMin;
            public float[] BaseVelocityZMax;
            public Transform[] SourceRoots;
            public int[] SourceIndices;
            public float[] SourceIntensity;
            public float[] SourceDelay;
            public int ActiveSourceCount;
            public SmokeProfile Profile;
            public bool Active;
            public float IntensityScale;
            public float DriftScale;
            public float GrowthSeconds;
            public float StartLag;
            public float Yaw;
            public float PulseSeed;
            public float ExternalIntensity = 1f;
            public float ForestClusterScale = 1f;

            public void SetPosition(GlobalPosition position)
            {
                if (Root != null) Root.transform.position = position.ToLocalPosition();
            }

            public void SetForestClusterScale(float scale)
            {
                ForestClusterScale = Profile == SmokeProfile.Forest
                    ? Mathf.Clamp(scale, 1f, 3f)
                    : 1f;
            }

            public void SetPhase(float ageSeconds, float remainingFraction, Vector3 wind)
            {
                if (Systems == null || BaseRates == null) return;
                float fade = Smooth01(remainingFraction / 0.055f);
                float flameSupport = Mathf.Lerp(0.58f, 1f, Smooth01(remainingFraction / 0.22f));
                float pulse = 0.78f + Mathf.PerlinNoise(PulseSeed,
                    Time.timeSinceLevelLoad * 0.085f) * 0.28f;
                float clusterT = Profile == SmokeProfile.Forest
                    ? Mathf.Clamp01((ForestClusterScale - 1f) / 2f)
                    : 0f;
                float baseIntensity = fade * flameSupport * IntensityScale * pulse * ExternalIntensity *
                    Mathf.Lerp(1f, 1.30f, clusterT);

                Vector3 horizontalWind = new Vector3(wind.x, 0f, wind.z);
                float windStrength = Mathf.Min(horizontalWind.magnitude, 18f);
                Vector3 tiltedUp = (Vector3.up + horizontalWind * (0.018f * DriftScale)).normalized;
                if (Root != null)
                {
                    Root.transform.rotation = Quaternion.FromToRotation(Vector3.up, tiltedUp) *
                        Quaternion.Euler(0f, Yaw, 0f);
                    if (Profile == SmokeProfile.Forest)
                        Root.transform.localScale = new Vector3(
                            Mathf.Lerp(1.65f, 2.55f, clusterT),
                            Mathf.Lerp(1.40f, 2.15f, clusterT),
                            Mathf.Lerp(1.65f, 2.55f, clusterT));
                }
                for (int i = 0; i < Systems.Length && i < BaseRates.Length; i++)
                {
                    ParticleSystem system = Systems[i];
                    if (system == null) continue;
                    int sourceIndex = SourceIndices[i];
                    if (sourceIndex >= ActiveSourceCount ||
                        SourceRoots[sourceIndex] == null || !SourceRoots[sourceIndex].gameObject.activeSelf)
                        continue;
                    ParticleSystem.EmissionModule emission = system.emission;
                    // Preserve the complete vanilla system. Only its emission multiplier is
                    // faded in/out to match the mod fire lifetime.
                    if (BaseRates[i] > 0f)
                    {
                        float sourceGrowth = Smooth01(
                            (ageSeconds - StartLag - SourceDelay[sourceIndex]) /
                            Mathf.Max(GrowthSeconds, 1f));
                        float sourcePulse = 0.82f + Mathf.PerlinNoise(
                            PulseSeed + sourceIndex * 3.17f,
                            Time.timeSinceLevelLoad * 0.11f) * 0.26f;
                        emission.rateOverTimeMultiplier = BaseRates[i] * baseIntensity *
                            sourceGrowth * SourceIntensity[sourceIndex] * sourcePulse;
                    }

                    // The original silo plume is made for a static showcase object. Add a
                    // restrained world-space wind component so multiple urban columns shear
                    // together instead of forming identical vertical cylinders.
                    ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
                    velocity.enabled = true;
                    velocity.space = ParticleSystemSimulationSpace.World;
                    float drift = DriftScale * Mathf.Lerp(0.16f, 0.34f, windStrength / 18f);
                    velocity.x = new ParticleSystem.MinMaxCurve(
                        BaseVelocityXMin[i] + wind.x * drift,
                        BaseVelocityXMax[i] + wind.x * drift);
                    velocity.z = new ParticleSystem.MinMaxCurve(
                        BaseVelocityZMin[i] + wind.z * drift,
                        BaseVelocityZMax[i] + wind.z * drift);
                    if (!system.isPlaying) system.Play(true);
                }
            }

            private static float Smooth01(float value)
            {
                value = Mathf.Clamp01(value);
                return value * value * (3f - 2f * value);
            }
        }

        private sealed class Template
        {
            public GameObject Prefab;
            public readonly HashSet<string> SmokePaths = new HashSet<string>(StringComparer.Ordinal);
        }

        private static readonly BindingFlags InstanceFields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo DamageEffectsField =
            typeof(UnitPart).GetField("damageEffects", InstanceFields);
        private static readonly FieldInfo DisintegrationEffectsField =
            typeof(UnitPart).GetField("disintegrationEffects", InstanceFields);
        private static readonly FieldInfo WreckageField =
            typeof(Building).GetField("wreckage", InstanceFields);

        private readonly List<Visual> visuals = new List<Visual>(16);
        private Template template;
        private float nextResolveAttempt;
        private bool warnedUnavailable;

        public Visual Acquire(
            GlobalPosition position, Vector2 halfExtents,
            SmokeProfile profile = SmokeProfile.Building)
        {
            if (!TryResolveTemplate()) return null;
            for (int i = 0; i < visuals.Count; i++)
            {
                if (!visuals[i].Active)
                {
                    Activate(visuals[i], position, halfExtents, profile);
                    return visuals[i];
                }
            }

            Visual visual = CreateVisual();
            if (visual == null) return null;
            visuals.Add(visual);
            Activate(visual, position, halfExtents, profile);
            return visual;
        }

        public void Release(Visual visual)
        {
            if (visual == null || !visual.Active) return;
            visual.Active = false;
            if (visual.Systems != null)
            {
                for (int i = 0; i < visual.Systems.Length; i++)
                    if (visual.Systems[i] != null)
                        visual.Systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            if (visual.Root != null) visual.Root.SetActive(false);
        }

        public void Clear()
        {
            for (int i = 0; i < visuals.Count; i++)
                if (visuals[i].Root != null) UnityEngine.Object.Destroy(visuals[i].Root);
            visuals.Clear();
            template = null;
            nextResolveAttempt = 0f;
            warnedUnavailable = false;
        }

        private bool TryResolveTemplate()
        {
            if (template != null && template.Prefab != null) return true;
            if (Time.unscaledTime < nextResolveAttempt) return false;
            nextResolveAttempt = Time.unscaledTime + 2f;

            BuildingDefinition fuelDepot = FindFuelDepot();
            if (fuelDepot == null || fuelDepot.unitPrefab == null) return false;

            var candidates = new HashSet<GameObject>();
            UnitPart[] parts = fuelDepot.unitPrefab.GetComponentsInChildren<UnitPart>(true);
            for (int i = 0; i < parts.Length; i++)
            {
                IEnumerable damageEffects = DamageEffectsField?.GetValue(parts[i]) as IEnumerable;
                if (damageEffects != null)
                {
                    foreach (object item in damageEffects)
                    {
                        DamageEffect effect = item as DamageEffect;
                        if (effect != null && effect.prefab != null) candidates.Add(effect.prefab);
                    }
                }

                GameObject[] disintegration =
                    DisintegrationEffectsField?.GetValue(parts[i]) as GameObject[];
                if (disintegration != null)
                    for (int e = 0; e < disintegration.Length; e++)
                        if (disintegration[e] != null) candidates.Add(disintegration[e]);
            }

            Building building = fuelDepot.unitPrefab.GetComponentInChildren<Building>(true);
            GameObject wreckage = building == null ? null : WreckageField?.GetValue(building) as GameObject;
            if (wreckage != null) candidates.Add(wreckage);

            int bestScore = int.MinValue;
            GameObject bestPrefab = null;
            ParticleSystem bestSystem = null;
            foreach (GameObject candidate in candidates)
            {
                if (candidate == null) continue;
                ParticleSystem[] systems = candidate.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < systems.Length; i++)
                {
                    int score = ScoreSystem(candidate.transform, systems[i]);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    bestPrefab = candidate;
                    bestSystem = systems[i];
                }
            }

            if (bestPrefab == null || bestSystem == null)
            {
                if (!warnedUnavailable)
                {
                    warnedUnavailable = true;
                    Plugin.Logger.LogWarning(
                        "Fuel Depot definition was found, but its destruction smoke prefab could not be resolved.");
                }
                return false;
            }

            var resolved = new Template { Prefab = bestPrefab };
            ParticleSystem[] bestSystems = bestPrefab.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < bestSystems.Length; i++)
            {
                string descriptor = DescribeLayer(bestPrefab.transform, bestSystems[i]);
                // Fuel effects may layer two smoke systems. Preserve every explicitly named
                // smoke/soot/plume layer from the same vanilla prefab, while excluding its
                // flash, sparks, fireball, and debris systems.
                if (IsExplicitSmoke(descriptor) && !IsTransientExplosionLayer(descriptor))
                    resolved.SmokePaths.Add(GetPath(bestPrefab.transform, bestSystems[i].transform));
            }
            if (resolved.SmokePaths.Count == 0)
                resolved.SmokePaths.Add(GetPath(bestPrefab.transform, bestSystem.transform));

            template = resolved;
            Plugin.Logger.LogInfo(
                "Fire and ruin smoke using vanilla Fuel Depot destruction prefab '" +
                bestPrefab.name + "' (" + string.Join(", ", resolved.SmokePaths) + ").");
            return true;
        }

        private static BuildingDefinition FindFuelDepot()
        {
            if (Encyclopedia.i == null || Encyclopedia.i.buildings == null) return null;
            BuildingDefinition fallback = null;
            for (int i = 0; i < Encyclopedia.i.buildings.Count; i++)
            {
                BuildingDefinition definition = Encyclopedia.i.buildings[i];
                if (definition == null) continue;
                string key = definition.jsonKey ?? string.Empty;
                string name = definition.unitName ?? string.Empty;
                if (key.Equals("FuelContainer2x1", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Fuel Depot", StringComparison.OrdinalIgnoreCase))
                    return definition;
                string combined = (key + " " + name).ToLowerInvariant();
                if (combined.Contains("fuel") &&
                    (combined.Contains("depot") || combined.Contains("container") || combined.Contains("silo")))
                    fallback = definition;
            }
            return fallback;
        }

        private const int MaximumSmokeSources = 3;

        private Visual CreateVisual()
        {
            if (template == null || template.Prefab == null) return null;
            var root = new GameObject("BoscaliSummer.FuelDepotSmokeSources");
            root.transform.SetParent(Datum.origin, false);
            // Keep the hierarchy inactive while cloning and stripping the vanilla wreck
            // prefab. This prevents its scripts, audio, lights, and emitters from briefly
            // enabling before PrepareSmokeSource removes them.
            root.SetActive(false);

            var retained = new List<ParticleSystem>(template.SmokePaths.Count * MaximumSmokeSources);
            var rates = new List<float>(retained.Capacity);
            var velocityXMin = new List<float>(retained.Capacity);
            var velocityXMax = new List<float>(retained.Capacity);
            var velocityZMin = new List<float>(retained.Capacity);
            var velocityZMax = new List<float>(retained.Capacity);
            var sourceIndices = new List<int>(retained.Capacity);
            var sourceRoots = new Transform[MaximumSmokeSources];

            for (int source = 0; source < MaximumSmokeSources; source++)
            {
                GameObject clone = UnityEngine.Object.Instantiate(template.Prefab, root.transform, false);
                clone.name = "FuelDepotSmokeSource" + source;
                sourceRoots[source] = clone.transform;
                PrepareSmokeSource(clone, source, retained, rates,
                    velocityXMin, velocityXMax, velocityZMin, velocityZMax, sourceIndices);
                clone.SetActive(false);
            }

            if (retained.Count == 0)
            {
                UnityEngine.Object.Destroy(root);
                return null;
            }
            return new Visual
            {
                Root = root,
                Systems = retained.ToArray(),
                BaseRates = rates.ToArray(),
                BaseVelocityXMin = velocityXMin.ToArray(),
                BaseVelocityXMax = velocityXMax.ToArray(),
                BaseVelocityZMin = velocityZMin.ToArray(),
                BaseVelocityZMax = velocityZMax.ToArray(),
                SourceRoots = sourceRoots,
                SourceIndices = sourceIndices.ToArray(),
                SourceIntensity = new float[MaximumSmokeSources],
                SourceDelay = new float[MaximumSmokeSources]
            };
        }

        private void PrepareSmokeSource(
            GameObject root, int sourceIndex,
            List<ParticleSystem> retained, List<float> rates,
            List<float> velocityXMin, List<float> velocityXMax,
            List<float> velocityZMin, List<float> velocityZMax,
            List<int> sourceIndices)
        {
            AudioSource[] audio = root.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < audio.Length; i++)
            {
                audio[i].Stop();
                audio[i].enabled = false;
            }
            Light[] lights = root.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++) lights[i].enabled = false;
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = false;
            Rigidbody[] rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rigidbodies.Length; i++)
            {
                rigidbodies[i].detectCollisions = false;
                rigidbodies[i].isKinematic = true;
            }
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                if (!(renderers[i] is ParticleSystemRenderer)) renderers[i].enabled = false;

            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
                if (behaviours[i] != null) UnityEngine.Object.Destroy(behaviours[i]);

            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem system = systems[i];
                string path = GetPath(root.transform, system.transform);
                bool keep = template.SmokePaths.Contains(path);
                ParticleSystem.EmissionModule emission = system.emission;
                if (!keep)
                {
                    emission.enabled = false;
                    system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
                    if (renderer != null) renderer.enabled = false;
                    continue;
                }

                ParticleSystem.MainModule main = system.main;
                main.playOnAwake = false;
                main.loop = true;
                main.stopAction = ParticleSystemStopAction.None;
                main.simulationSpace = ParticleSystemSimulationSpace.Custom;
                main.customSimulationSpace = Datum.origin;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                emission.enabled = true;
                retained.Add(system);
                rates.Add(emission.rateOverTimeMultiplier);
                sourceIndices.Add(sourceIndex);
                ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
                velocityXMin.Add(velocity.x.constantMin);
                velocityXMax.Add(velocity.x.constantMax);
                velocityZMin.Add(velocity.z.constantMin);
                velocityZMax.Add(velocity.z.constantMax);
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private static void Activate(
            Visual visual, GlobalPosition position, Vector2 halfExtents, SmokeProfile profile)
        {
            visual.Active = true;
            visual.Profile = profile;
            visual.ExternalIntensity = 1f;
            visual.ForestClusterScale = 1f;
            bool forest = profile == SmokeProfile.Forest;
            bool ruin = profile == SmokeProfile.Ruin;
            float a = Signature(position, 0.013f, 0.029f);
            float b = Signature(position, -0.037f, 0.021f);
            float c = Signature(position, 0.047f, 0.011f);
            visual.ActiveSourceCount = forest ? 3 : ruin ? (a > 0.58f ? 2 : 1) : (a > 0.46f ? 3 : 2);
            // Each source is deliberately lighter and smaller than the previous single
            // column; total emission remains in the same bounded range.
            visual.IntensityScale = forest
                ? Mathf.Lerp(0.14f, 0.20f, a)
                : ruin ? Mathf.Lerp(0.10f, 0.16f, a) : Mathf.Lerp(0.16f, 0.24f, a);
            visual.DriftScale = forest
                ? Mathf.Lerp(1.45f, 2.05f, b)
                : ruin ? Mathf.Lerp(0.82f, 1.28f, b) : Mathf.Lerp(0.72f, 1.22f, b);
            visual.GrowthSeconds = forest
                ? Mathf.Lerp(7f, 13f, c)
                : ruin ? Mathf.Lerp(7f, 15f, c) : Mathf.Lerp(15f, 27f, c);
            visual.StartLag = Mathf.Lerp(0f, 3.5f, b);
            visual.Yaw = a * 360f;
            visual.PulseSeed = b * 17.3f + c * 31.7f;
            visual.Root.SetActive(true);
            visual.SetPosition(position);
            visual.Root.transform.localScale = Vector3.one;

            float radiusX = Mathf.Clamp(halfExtents.x * (forest ? 0.85f : 0.52f),
                2.5f, forest ? 36f : 16f);
            float radiusZ = Mathf.Clamp(halfExtents.y * (forest ? 0.85f : 0.52f),
                2.5f, forest ? 36f : 16f);
            float baseAngle = b * Mathf.PI * 2f;
            for (int source = 0; source < visual.SourceRoots.Length; source++)
            {
                Transform sourceRoot = visual.SourceRoots[source];
                bool active = source < visual.ActiveSourceCount;
                sourceRoot.gameObject.SetActive(active);
                if (!active) continue;

                float sourceSeed = Mathf.Repeat(a + source * 0.371f, 1f);
                float angle = baseAngle + source * Mathf.PI * 2f / visual.ActiveSourceCount;
                float radius = source == 0 ? (forest ? 0.38f : 0.34f) : Mathf.Lerp(forest ? 0.65f : 0.56f, forest ? 1.08f : 0.88f, sourceSeed);
                sourceRoot.localPosition = new Vector3(
                    Mathf.Cos(angle) * radiusX * radius,
                    0f,
                    Mathf.Sin(angle) * radiusZ * radius);
                sourceRoot.localRotation = Quaternion.Euler(0f, sourceSeed * 360f, 0f);
                float horizontalScale = forest
                    ? Mathf.Lerp(1.45f, 1.95f, sourceSeed)
                    : ruin ? Mathf.Lerp(0.34f, 0.50f, sourceSeed) : Mathf.Lerp(0.38f, 0.57f, sourceSeed);
                sourceRoot.localScale = new Vector3(
                    horizontalScale,
                    forest
                        ? Mathf.Lerp(1.35f, 1.85f, Mathf.Repeat(c + source * 0.217f, 1f))
                        : ruin
                            ? Mathf.Lerp(0.42f, 0.68f, Mathf.Repeat(c + source * 0.217f, 1f))
                            : Mathf.Lerp(0.58f, 0.86f, Mathf.Repeat(c + source * 0.217f, 1f)),
                    horizontalScale);
                visual.SourceIntensity[source] = forest
                    ? Mathf.Lerp(0.72f, 0.96f, sourceSeed)
                    : Mathf.Lerp(0.78f, 1.12f, sourceSeed);
                visual.SourceDelay[source] = source == 0
                    ? 0f
                    : Mathf.Lerp(0.6f, 2.4f, Mathf.Repeat(b + source * 0.293f, 1f));
            }

            for (int i = 0; i < visual.Systems.Length; i++)
            {
                ParticleSystem system = visual.Systems[i];
                if (system == null || visual.SourceIndices[i] >= visual.ActiveSourceCount) continue;
                ParticleSystem.EmissionModule emission = system.emission;
                if (visual.BaseRates[i] > 0f) emission.rateOverTimeMultiplier = 0f;
                system.Play(true);
            }
        }

        private static float Signature(GlobalPosition position, float xScale, float zScale)
        {
            return Mathf.Repeat(Mathf.Sin(position.x * xScale + position.z * zScale) * 43758.5453f, 1f);
        }

        private static int ScoreSystem(Transform prefabRoot, ParticleSystem system)
        {
            string descriptor = Describe(prefabRoot, system);
            bool explicitSmoke = IsExplicitSmoke(descriptor);
            int score = explicitSmoke ? 1200 : 0;
            if (descriptor.Contains("fuel")) score += 120;
            if (descriptor.Contains("dark") || descriptor.Contains("black") || descriptor.Contains("soot")) score += 180;
            if (descriptor.Contains("fireball") || descriptor.Contains("flash") ||
                descriptor.Contains("spark") || descriptor.Contains("debris") ||
                descriptor.Contains("fragment") || descriptor.Contains("shrapnel")) score -= 1400;
            if (descriptor.Contains("explosion") && !explicitSmoke) score -= 1000;
            if ((descriptor.Contains("fire") || descriptor.Contains("flame")) &&
                !explicitSmoke) score -= 700;

            ParticleSystem.MainModule main = system.main;
            score += Mathf.RoundToInt(Mathf.Min(main.startLifetime.constantMax, 40f) * 8f);
            score += Mathf.RoundToInt(Mathf.Min(main.startSize.constantMax, 60f) * 3f);
            if (main.loop) score += 140;
            Color color = main.startColor.colorMax;
            if ((color.r + color.g + color.b) / 3f < 0.62f) score += 130;
            return score;
        }

        private static string Describe(Transform root, ParticleSystem system)
        {
            return (root.name + "/" + DescribeLayer(root, system)).ToLowerInvariant();
        }

        private static string DescribeLayer(Transform root, ParticleSystem system)
        {
            string material = string.Empty;
            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer != null && renderer.sharedMaterial != null) material = renderer.sharedMaterial.name;
            return (GetPath(root, system.transform) + "/" + material).ToLowerInvariant();
        }

        private static bool IsExplicitSmoke(string descriptor)
        {
            return descriptor.Contains("smoke") || descriptor.Contains("soot") || descriptor.Contains("plume");
        }

        private static bool IsTransientExplosionLayer(string descriptor)
        {
            return descriptor.Contains("fireball") || descriptor.Contains("flash") ||
                descriptor.Contains("spark") || descriptor.Contains("debris") ||
                descriptor.Contains("fragment") || descriptor.Contains("shrapnel");
        }

        private static string GetPath(Transform root, Transform target)
        {
            if (target == root) return string.Empty;
            var names = new List<string>(8);
            Transform current = target;
            while (current != null && current != root)
            {
                names.Add(current.name);
                current = current.parent;
            }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }
    }
}
