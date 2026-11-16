using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Effects.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Effects.Runtime
{
    /// <summary>A reversible, client-local cinematic look using the game's own URP effects.</summary>
    internal sealed class CinematicPost
    {
        private static readonly FieldInfo GameExposure = AccessTools.Field(typeof(ExposureController), "colorAdjustments");
        private static readonly Vector4 NeutralWheel = new Vector4(1f, 1f, 1f, 0f);
        private GameObject holder;
        private Volume nativeVolume, volume;
        private VolumeProfile profile;
        private SplitToning tone;
        private ShadowsMidtonesHighlights ranges;
        private Bloom bloom;
        private Vignette vignette;
        private FilmGrain grain;
        private CinematicStreakPass streak;
        private Material streakMaterial;

        internal void Apply(CinematicSettings settings, LevelInfo level, Transform parent,
            Camera world = null, Shader streakShader = null, bool optimizeStreaks = false)
        {
            float strength = settings != null && settings.Enabled.Value ? Unit(settings.Strength.Value) : 0f;
            Volume native = level != null ? level.PostProcessing : null;
            bool goggles = GameAccess.TryGetNightVisionState(out bool selected, out bool active) && (selected || active);
            if (strength <= 0f || native == null || !native.isActiveAndEnabled || native.weight <= 0f || goggles)
            {
                Release();
                return;
            }
            if (nativeVolume != native || holder == null || profile == null || volume == null)
            {
                Release();
                Ensure(native, parent);
            }

            // These operations precede URP's global saturation, so native G-LOC still removes their colour.
            // No ColorAdjustments component: the display-grade contrast dial and native exposure keep ownership.
            tone.shadows.Override(Color.Lerp(Color.grey, new Color(0.41f, 0.475f, 0.55f), strength));
            tone.highlights.Override(Color.Lerp(Color.grey, new Color(0.525f, 0.508f, 0.48f), strength));
            tone.balance.Override(-12f * strength);
            ranges.shadows.Override(Vector4.Lerp(NeutralWheel, new Vector4(1f, 1f, 1f, -0.08f), strength));
            ranges.midtones.Override(Vector4.Lerp(NeutralWheel, new Vector4(1f, 1f, 1f, -0.015f), strength));
            ranges.highlights.Override(Vector4.Lerp(NeutralWheel, new Vector4(1f, 1f, 1f, 0.025f), strength));

            VolumeProfile source = native.HasInstantiatedProfile() ? native.profile : native.sharedProfile;
            Bloom nativeBloom = null;
            Vignette nativeVignette = null;
            FilmGrain nativeGrain = null;
            if (source != null)
            {
                source.TryGet(out nativeBloom);
                source.TryGet(out nativeVignette);
                source.TryGet(out nativeGrain);
            }

            float glow = Unit(settings.Bloom.Value) * strength;
            bloom.active = glow > 0f;
            if (bloom.active)
            {
                float original = nativeBloom != null && nativeBloom.active ? nativeBloom.intensity.value : 0f;
                bloom.intensity.Override(original + 0.8f * glow);
                // URP thresholds the raw HDR frame before exposure and converts this parameter from gamma.
                // A fixed threshold would make the whole daylight sky bloom at the game's usual negative EV.
                float threshold = Mathf.LinearToGammaSpace(1.2f * Mathf.Pow(2f, -Exposure()));
                bloom.threshold.Override(Mathf.Max(threshold, nativeBloom != null ? nativeBloom.threshold.value : 0f));
                bloom.scatter.Override(Mathf.Lerp(nativeBloom != null ? nativeBloom.scatter.value : 0.7f, 0.65f, strength));
                bloom.highQualityFiltering.Override(true);
            }
            float edge = nativeVignette != null && nativeVignette.active ? nativeVignette.intensity.value : 0f;
            vignette.intensity.Override(Mathf.Clamp01(edge + 0.06f * strength));
            vignette.smoothness.Override(Mathf.Lerp(nativeVignette != null ? nativeVignette.smoothness.value : 0.2f, 0.45f, strength));
            float noise = nativeGrain != null && nativeGrain.active ? nativeGrain.intensity.value : 0f;
            grain.intensity.Override(Mathf.Clamp01(noise + 0.025f * strength));
            ApplyStreak(world, streakShader, glow, optimizeStreaks);
        }

        private void Ensure(Volume native, Transform parent)
        {
            nativeVolume = native;
            holder = new GameObject("BoscaliSummer.Cinematic") { hideFlags = HideFlags.DontSave };
            holder.transform.SetParent(parent, false);
            holder.layer = native.gameObject.layer;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.DontSave;
            tone = profile.Add<SplitToning>();
            ranges = profile.Add<ShadowsMidtonesHighlights>();
            bloom = profile.Add<Bloom>();
            vignette = profile.Add<Vignette>();
            grain = profile.Add<FilmGrain>();
            // The game's built-in grain lookups are stripped; share the existing safe custom noise tile.
            grain.type.Override(FilmGrainLookup.Custom);
            grain.texture.Override(FeedCamera.Noise());
            volume = holder.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0.8f; // Above the display grade (0.75), below native night vision (1).
            volume.sharedProfile = profile;
        }

        internal static float Exposure()
        {
            try
            {
                var native = GameExposure?.GetValue(null) as ColorAdjustments;
                float ev = native != null ? native.postExposure.value : 0f;
                return float.IsNaN(ev) || float.IsInfinity(ev) ? 0f : Mathf.Clamp(ev, -12f, 12f);
            }
            catch (Exception) { return 0f; }
        }

        internal void Describe(IDictionary<string, object> state)
        {
            state["cinematic"] = volume != null && volume.isActiveAndEnabled;
            state["cinematicBloom"] = bloom != null && bloom.active ? bloom.intensity.value : 0f;
            state["cinematicStreakFrameAge"] = streak != null && streak.ExecutedFrame >= 0
                ? Time.frameCount - streak.ExecutedFrame : -1;
            state["cinematicStreakOptimized"] = streak != null && streak.OptimizedLastFrame;
        }

        internal void Release()
        {
            ReleaseStreak();
            if (holder != null)
            {
                holder.SetActive(false);
                Object.Destroy(holder);
            }
            if (profile != null)
            {
                foreach (VolumeComponent component in profile.components)
                    if (component != null) Object.Destroy(component);
                profile.components.Clear();
                Object.Destroy(profile);
            }
            holder = null;
            nativeVolume = volume = null;
            profile = null;
            tone = null;
            ranges = null;
            bloom = null;
            vignette = null;
            grain = null;
        }

        private void ApplyStreak(Camera world, Shader shader, float strength, bool optimize)
        {
            if (world == null || shader == null || !shader.isSupported || strength <= 0f)
            {
                ReleaseStreak();
                return;
            }
            if (streakMaterial != null && streakMaterial.shader != shader) ReleaseStreak();
            if (streakMaterial == null)
                streakMaterial = new Material(shader) { name = "BoscaliSummer.CinematicStreak", hideFlags = HideFlags.HideAndDontSave };
            streak ??= new CinematicStreakPass();
            streak.Bind(world, streakMaterial, strength, Exposure(), optimize);
        }

        private void ReleaseStreak()
        {
            streak?.Dispose();
            streak = null;
            if (streakMaterial != null) Object.Destroy(streakMaterial);
            streakMaterial = null;
        }

        private static float Unit(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);
    }
}
