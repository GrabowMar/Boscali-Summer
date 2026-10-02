using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Modules.Immersion.Domain;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>
    /// Environmental surface shaders: drives canopy and cockpit surface materials with dynamic
    /// rain wetness, high-altitude frost, combat scorch, and ground dust/dirt.
    /// Performance optimized:
    /// - Evaluates aircraft damage via existing aircraft.partLookup (zero GC allocations, avoiding GetComponentsInChildren).
    /// - Updates global shader constants for custom shaders.
    /// - Pushes local MaterialPropertyBlocks only when state values exceed threshold.
    /// </summary>
    internal sealed class SurfaceImmersion
    {
        private const int MaxSurfaces = 16;
        private const float ChangeThreshold = 0.02f;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private static readonly int GlobalParamsId = Shader.PropertyToID("_SurfaceParams");
        private static readonly int GlobalWetnessId = Shader.PropertyToID("_SurfaceWetness");
        private static readonly int GlobalFrostId = Shader.PropertyToID("_SurfaceFrost");
        private static readonly int GlobalScorchId = Shader.PropertyToID("_SurfaceScorch");
        private static readonly int GlobalDirtId = Shader.PropertyToID("_SurfaceDirt");

        private readonly List<Renderer> surfaces = new List<Renderer>(MaxSurfaces);
        private readonly List<Color> baseColors = new List<Color>(MaxSurfaces);
        private readonly List<float> baseSmoothness = new List<float>(MaxSurfaces);
        private readonly List<Color> baseEmissions = new List<Color>(MaxSurfaces);
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        private Aircraft bound;
        private float wetness;
        private float frost;
        private float scorch;
        private float dirt;

        private float appliedWetness = -1f;
        private float appliedFrost = -1f;
        private float appliedScorch = -1f;
        private float appliedDirt = -1f;

        private float nextHpSampleTime;
        private float lastHp = -1f;
        private float startHp = 100f;

        internal ManualLogSource Logger { get; set; }

        public float Wetness => wetness;
        public float Frost => frost;
        public float Scorch => scorch;
        public float Dirt => dirt;

        public void Reset()
        {
            wetness = frost = scorch = dirt = 0f;
            appliedWetness = appliedFrost = appliedScorch = appliedDirt = -1f;
            if (bound != null)
            {
                block.Clear();
                for (int i = 0; i < surfaces.Count; i++)
                {
                    if (surfaces[i] != null)
                    {
                        surfaces[i].SetPropertyBlock(block);
                    }
                }
            }
            bound = null;
            surfaces.Clear();
            baseColors.Clear();
            baseSmoothness.Clear();
            baseEmissions.Clear();
            lastHp = -1f;
            ResetGlobalShaderProperties();
        }

        private static void ResetGlobalShaderProperties()
        {
            Shader.SetGlobalVector(GlobalParamsId, Vector4.zero);
            Shader.SetGlobalFloat(GlobalWetnessId, 0f);
            Shader.SetGlobalFloat(GlobalFrostId, 0f);
            Shader.SetGlobalFloat(GlobalScorchId, 0f);
            Shader.SetGlobalFloat(GlobalDirtId, 0f);
        }

        public void Tick(Aircraft aircraft, bool cockpitView, LevelInfo level, bool enabled, float dt)
        {
            if (!enabled || aircraft == null)
            {
                if (appliedWetness >= 0f) Reset();
                return;
            }

            if (aircraft != bound) Bind(aircraft);

            // Aircraft kinematics
            float airspeed = aircraft.speed;
            float altitudeM = aircraft.transform.position.y;
            float radarAlt = aircraft.radarAlt;
            bool onGround = radarAlt < 0.3f;
            float throttle = aircraft.GetInputs().throttle;

            // Environmental moisture
            float cloudDensity = level != null ? level.GetCloudOcclusion(aircraft.transform.position) : 0f;
            float rainRate = 0f;
            if (level != null && cloudDensity > 0.6f && altitudeM < 3000f)
            {
                rainRate = (cloudDensity - 0.6f) * 2.5f;
            }

            // Damage detection using existing aircraft.partLookup (zero garbage)
            float damageSpike = 0f;
            if (Time.unscaledTime >= nextHpSampleTime)
            {
                nextHpSampleTime = Time.unscaledTime + 1f;
                List<UnitPart> parts = aircraft.partLookup;
                if (parts != null && parts.Count > 0)
                {
                    float currentHp = 0f;
                    for (int i = 0; i < parts.Count; i++)
                    {
                        UnitPart part = parts[i];
                        if (part != null) currentHp += part.hitPoints;
                    }

                    if (lastHp < 0f)
                    {
                        startHp = Mathf.Max(100f, currentHp);
                    }
                    else if (currentHp < lastHp)
                    {
                        float lost = (lastHp - currentHp) / startHp;
                        damageSpike = Mathf.Clamp01(lost * 2f);
                    }
                    lastHp = currentHp;
                }
            }

            // Step physical state via pure ImmersionMath
            wetness = ImmersionMath.SurfaceWetnessStep(wetness, rainRate, cloudDensity, airspeed, dt);
            frost = ImmersionMath.SurfaceFrostStep(frost, altitudeM, dt);
            scorch = ImmersionMath.SurfaceScorchStep(scorch, damageSpike, throttle, dt);
            dirt = ImmersionMath.SurfaceDirtStep(dirt, radarAlt, airspeed, onGround, wetness, dt);

            // Update global constant buffer for custom shaders
            Shader.SetGlobalVector(GlobalParamsId, new Vector4(wetness, frost, scorch, dirt));
            Shader.SetGlobalFloat(GlobalWetnessId, wetness);
            Shader.SetGlobalFloat(GlobalFrostId, frost);
            Shader.SetGlobalFloat(GlobalScorchId, scorch);
            Shader.SetGlobalFloat(GlobalDirtId, dirt);

            // Update cockpit surface MaterialPropertyBlocks if delta exceeds threshold
            bool needsUpdate = Mathf.Abs(wetness - appliedWetness) > ChangeThreshold ||
                               Mathf.Abs(frost - appliedFrost) > ChangeThreshold ||
                               Mathf.Abs(scorch - appliedScorch) > ChangeThreshold ||
                               Mathf.Abs(dirt - appliedDirt) > ChangeThreshold;

            if (!needsUpdate || !cockpitView || surfaces.Count == 0) return;

            appliedWetness = wetness;
            appliedFrost = frost;
            appliedScorch = scorch;
            appliedDirt = dirt;

            ApplyToSurfaces();
        }

        private void ApplyToSurfaces()
        {
            float darken = Mathf.Clamp01(1f - (scorch * 0.45f + dirt * 0.25f));
            Color frostTint = new Color(0.6f, 0.75f, 0.9f, 0f) * (frost * 0.4f);

            for (int i = 0; i < surfaces.Count; i++)
            {
                Renderer r = surfaces[i];
                if (r == null) continue;

                r.GetPropertyBlock(block);

                Color c = baseColors[i] * darken;
                block.SetColor(ColorId, c);

                float sm = Mathf.Clamp01(baseSmoothness[i] + wetness * 0.35f - dirt * 0.15f);
                block.SetFloat(SmoothnessId, sm);
                block.SetFloat(GlossinessId, sm);

                Color em = baseEmissions[i] + frostTint;
                block.SetColor(EmissionColorId, em);

                block.SetFloat(GlobalWetnessId, wetness);
                block.SetFloat(GlobalFrostId, frost);
                block.SetFloat(GlobalScorchId, scorch);
                block.SetFloat(GlobalDirtId, dirt);

                r.SetPropertyBlock(block);
            }
        }

        private void Bind(Aircraft aircraft)
        {
            Reset();
            bound = aircraft;
            if (aircraft == null) return;

            Renderer[] cockpit = GameAccess.GetCockpitRenderers(aircraft);
            if (cockpit != null)
            {
                for (int i = 0; i < cockpit.Length && surfaces.Count < MaxSurfaces; i++)
                {
                    if (cockpit[i] == null) continue;
                    Consider(cockpit[i]);
                }
            }
            else if (aircraft.cockpit != null)
            {
                var queue = new Queue<Transform>(64);
                queue.Enqueue(aircraft.cockpit.transform);
                int visited = 0;
                while (queue.Count > 0 && surfaces.Count < MaxSurfaces && visited < 256)
                {
                    Transform t = queue.Dequeue();
                    visited++;
                    if (t == null) continue;
                    Renderer r = t.GetComponent<Renderer>();
                    if (r != null) Consider(r);
                    for (int c = 0; c < t.childCount; c++)
                        queue.Enqueue(t.GetChild(c));
                }
            }

            if (Logger != null && surfaces.Count > 0)
            {
                Logger.LogInfo("[Immersion] Surface shaders bound " + surfaces.Count + " cockpit renderers on " + aircraft.name + ".");
            }
        }

        private void Consider(Renderer renderer)
        {
            Material shared = renderer.sharedMaterial;
            if (shared == null) return;

            // Skip screen displays (screens are handled by MfdGlow)
            string name = renderer.name.ToLowerInvariant();
            if (name.Contains("screen") || name.Contains("mfd") || name.Contains("display") ||
                name.Contains("ddi") || name.Contains("hud") || name.Contains("crt"))
            {
                return;
            }

            Color baseCol = shared.HasProperty(ColorId) ? shared.GetColor(ColorId) : Color.white;
            float baseSm = shared.HasProperty(SmoothnessId) ? shared.GetFloat(SmoothnessId) :
                           (shared.HasProperty(GlossinessId) ? shared.GetFloat(GlossinessId) : 0.5f);
            Color baseEm = shared.HasProperty(EmissionColorId) ? shared.GetColor(EmissionColorId) : Color.black;

            surfaces.Add(renderer);
            baseColors.Add(baseCol);
            baseSmoothness.Add(baseSm);
            baseEmissions.Add(baseEm);
        }
    }
}
