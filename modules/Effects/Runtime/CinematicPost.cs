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
    /// <summary>Snapshot of sun elevation, cloud shade, rain and altitude for weather-reactive grading.</summary>
    internal readonly struct CinematicAtmosphere
    {
        internal readonly bool Valid;
        internal readonly float SunHeight, CloudShade, Precipitation, Altitude;

        internal CinematicAtmosphere(float sunHeight, float cloudShade, float precipitation, float altitude)
        {
            Valid = true;
            SunHeight = Finite(sunHeight, 1f, -1f, 1f);
            CloudShade = Finite(cloudShade, 0f, 0f, 1f);
            Precipitation = Finite(precipitation, 0f, 0f, 1f);
            Altitude = Finite(altitude, 0f, 0f, 200000f);
        }

        private static float Finite(float value, float fallback, float min, float max) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
    }

    /// <summary>A reversible, client-local cinematic look using the game's own URP effects.</summary>
    internal sealed class CinematicPost
    {
        private static readonly FieldInfo GameExposure = AccessTools.Field(typeof(ExposureController), "colorAdjustments");
        private static readonly Vector4 NeutralWheel = new Vector4(1f, 1f, 1f, 0f);
        private static readonly float[] LumSatTimes = { 0f, 0.25f, 0.60f, 0.85f, 1f };
        private static readonly float[] SatSatTimes = { 0f, 0.45f, 0.78f, 1f };

        private GameObject holder;
        private Volume nativeVolume, volume;
        private VolumeProfile profile;
        private SplitToning tone;
        private ShadowsMidtonesHighlights ranges;
        private LiftGammaGain lgg;
        private ColorCurves curves;
        private TextureCurve lumVsSat, satVsSat;
        private Bloom bloom;
        private Vignette vignette;
        private FilmGrain grain;
        private CinematicStreakPass streak;
        private Material streakMaterial;
        private CinematicLook lastLook = CinematicLook.Atmospheric;
        private float curveAmount = -1f, curveOvercast = -1f;

        internal void Apply(CinematicSettings settings, LevelInfo level, Transform parent,
            Camera world = null, Shader streakShader = null, bool optimizeStreaks = false,
            CinematicAtmosphere atmosphere = default)
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

            CinematicLook look = settings.Look != null ? settings.Look.Value : CinematicLook.Atmospheric;
            lastLook = look;

            float overcast = 0f, night = 0f, goldenAir = 0f;
            if (atmosphere.Valid)
            {
                overcast = Mathf.Clamp01(Mathf.Max(atmosphere.CloudShade, atmosphere.Precipitation * 0.85f));
                night = 1f - SmoothStep(-0.12f, 0f, atmosphere.SunHeight);
                float golden = SmoothStep(-0.05f, 0.08f, atmosphere.SunHeight) *
                    (1f - SmoothStep(0.14f, 0.34f, atmosphere.SunHeight)) * (1f - overcast * 0.65f);
                float lowAir = 1f - SmoothStep(1500f, 6000f, Mathf.Max(0f, atmosphere.Altitude));
                goldenAir = golden * lowAir;
            }

            // These operations precede URP's global saturation, so native G-LOC still removes their colour.
            // No ColorAdjustments or Tonemapping: display contrast, ExposureController and G-LOC keep ownership.
            float bloomGain, targetScatter, streakScale;
            Color bloomTint;
            if (look == CinematicLook.Tactical)
            {
                tone.shadows.Override(Color.Lerp(Color.grey, new Color(0.41f, 0.475f, 0.55f), strength));
                tone.highlights.Override(Color.Lerp(Color.grey, new Color(0.525f, 0.508f, 0.48f), strength));
                tone.balance.Override(-12f * strength);
                ranges.shadows.Override(Vector4.Lerp(NeutralWheel, new Vector4(1f, 1f, 1f, -0.08f), strength));
                ranges.midtones.Override(Vector4.Lerp(NeutralWheel, new Vector4(1f, 1f, 1f, -0.015f), strength));
                ranges.highlights.Override(Vector4.Lerp(NeutralWheel, new Vector4(1f, 1f, 1f, 0.025f), strength));
                ranges.shadowsStart.Override(0f);
                ranges.shadowsEnd.Override(0.3f);
                ranges.highlightsStart.Override(0.55f);
                ranges.highlightsEnd.Override(1f);
                lgg.active = false;
                curves.active = false;
                bloomGain = 0.8f;
                targetScatter = 0.65f;
                bloomTint = Color.white;
                streakScale = 1f;
            }
            else
            {
                bool atmospheric = look != CinematicLook.Realistic;
                float weight = atmospheric ? 1f : 0.6f;

                Color shadowClear = atmospheric ? new Color(0.455f, 0.482f, 0.518f) : new Color(0.475f, 0.492f, 0.512f);
                Color shadowMoody = atmospheric ? new Color(0.435f, 0.468f, 0.512f) : new Color(0.460f, 0.482f, 0.508f);
                tone.shadows.Override(Color.Lerp(Color.grey, Color.Lerp(shadowClear, shadowMoody, Mathf.Max(overcast, night)), strength));

                Color hiDay = atmospheric ? new Color(0.515f, 0.506f, 0.492f) : new Color(0.508f, 0.504f, 0.496f);
                Color hiGold = atmospheric ? new Color(0.56f, 0.505f, 0.435f) : new Color(0.54f, 0.505f, 0.455f);
                Color hiCool = atmospheric ? new Color(0.49f, 0.504f, 0.522f) : new Color(0.495f, 0.503f, 0.514f);
                Color hiTarget = Color.Lerp(hiDay, hiCool, Mathf.Clamp01(Mathf.Max(night, overcast * 0.65f)));
                hiTarget = Color.Lerp(hiTarget, hiGold, goldenAir);
                tone.highlights.Override(Color.Lerp(Color.grey, hiTarget, strength));
                tone.balance.Override((atmospheric ? -8f + 18f * goldenAir - 6f * overcast : -4f + 10f * goldenAir - 3f * overcast) * strength);

                float shadowLift = Mathf.Lerp(atmospheric ? -0.09f : -0.055f, atmospheric ? -0.045f : -0.025f, night);
                float midShift = atmospheric ? -0.015f : -0.008f;
                float hiShift = (atmospheric ? 0.03f : 0.02f) + (atmospheric ? 0.02f : 0.01f) * goldenAir;
                ranges.shadows.Override(Vector4.Lerp(NeutralWheel, new Vector4(1f, 1f, 1f, shadowLift), strength));
                ranges.midtones.Override(Vector4.Lerp(NeutralWheel, new Vector4(1f, 1f, 1f, midShift), strength));
                ranges.highlights.Override(Vector4.Lerp(NeutralWheel, new Vector4(1f, 1f, 1f, hiShift), strength));
                ranges.shadowsStart.Override(0f);
                ranges.shadowsEnd.Override(0.32f);
                ranges.highlightsStart.Override(0.55f);
                ranges.highlightsEnd.Override(1f);

                lgg.active = true;
                Vector4 liftTarget = atmospheric
                    ? new Vector4(0.985f, 0.992f, 1.004f, Mathf.Lerp(-0.014f, -0.004f, night))
                    : new Vector4(0.992f, 0.996f, 1.002f, Mathf.Lerp(-0.008f, -0.002f, night));
                Vector4 gammaTarget = new Vector4(1f, 1f, 1f, (atmospheric ? -0.012f : -0.006f) * (1f - night * 0.6f));
                float gScale = atmospheric ? 1f : 0.5f;
                Vector4 gainTarget = new Vector4(
                    1f + 0.035f * goldenAir * gScale,
                    1f + 0.01f * goldenAir * gScale,
                    1f - 0.025f * goldenAir * gScale,
                    0.01f * goldenAir * gScale);
                lgg.lift.Override(Vector4.Lerp(NeutralWheel, liftTarget, strength));
                lgg.gamma.Override(Vector4.Lerp(NeutralWheel, gammaTarget, strength));
                lgg.gain.Override(Vector4.Lerp(NeutralWheel, gainTarget, strength));

                curves.active = true;
                UpdateCurves(strength * weight, overcast);

                bloomGain = atmospheric ? 0.85f : 0.65f;
                targetScatter = (atmospheric ? 0.78f : 0.73f) + (atmospheric ? 0.04f : 0.03f) * goldenAir;
                bloomTint = Color.Lerp(Color.white, new Color(1f, 0.93f, 0.82f), goldenAir * (atmospheric ? 0.75f : 0.5f) * strength);
                streakScale = atmospheric ? 0.45f : 0.25f;
            }

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
            float exposure = bloom.active ? Exposure() : 0f;
            if (bloom.active)
            {
                float original = nativeBloom != null && nativeBloom.active ? nativeBloom.intensity.value : 0f;
                // Dampen extra bloom when vanilla already drives heavy night bloom (night intensity ~3 vs day 0.5).
                float nightDamp = Mathf.Lerp(1f, 0.25f, Mathf.InverseLerp(1.2f, 2.5f, original));
                bloom.intensity.Override(original + bloomGain * glow * nightDamp);
                // URP thresholds the raw HDR frame before exposure and converts this parameter from gamma.
                // A fixed threshold would make the whole daylight sky bloom at the game's usual negative EV.
                float threshold = Mathf.LinearToGammaSpace(1.2f * Mathf.Pow(2f, -exposure));
                bloom.threshold.Override(Mathf.Max(threshold, nativeBloom != null ? nativeBloom.threshold.value : 0f));
                bloom.scatter.Override(Mathf.Lerp(nativeBloom != null ? nativeBloom.scatter.value : 0.7f, targetScatter, strength));
                bloom.tint.Override(bloomTint);
                bloom.highQualityFiltering.Override(true);
            }

            float vig = (settings.Vignette != null ? Unit(settings.Vignette.Value) : 0.45f) * strength;
            vignette.active = vig > 0f;
            if (vignette.active)
            {
                float edge = nativeVignette != null && nativeVignette.active ? nativeVignette.intensity.value : 0f;
                vignette.intensity.Override(Mathf.Clamp01(edge + 0.24f * vig));
                vignette.smoothness.Override(Mathf.Lerp(nativeVignette != null ? nativeVignette.smoothness.value : 0.2f, 0.52f, vig));
                vignette.rounded.Override(false);
            }

            float grn = (settings.Grain != null ? Unit(settings.Grain.Value) : 0.35f) * strength;
            grain.active = grn > 0f;
            if (grain.active)
            {
                float noise = nativeGrain != null && nativeGrain.active ? nativeGrain.intensity.value : 0f;
                grain.intensity.Override(Mathf.Clamp01(noise + 0.10f * grn));
                grain.response.Override(0.75f);
            }

            ApplyStreak(world, streakShader, glow * streakScale, exposure, optimizeStreaks);
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
            lgg = profile.Add<LiftGammaGain>();
            curves = profile.Add<ColorCurves>();
            lumVsSat = CreateCurve(LumSatTimes);
            satVsSat = CreateCurve(SatSatTimes);
            curves.lumVsSat.Override(lumVsSat);
            curves.satVsSat.Override(satVsSat);
            curveAmount = curveOvercast = -1f;
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

        private void UpdateCurves(float amount, float overcast)
        {
            if (lumVsSat == null || satVsSat == null) return;
            if (Mathf.Abs(amount - curveAmount) < 0.004f && Mathf.Abs(overcast - curveOvercast) < 0.004f) return;

            if (amount != curveAmount) SetKeys(lumVsSat, LumSatTimes, new[]
            {
                Mathf.Lerp(0.5f, 0.34f, amount),
                Mathf.Lerp(0.5f, 0.48f, amount),
                Mathf.Lerp(0.5f, 0.49f, amount),
                Mathf.Lerp(0.5f, 0.44f, amount),
                Mathf.Lerp(0.5f, 0.32f, amount)
            });
            SetKeys(satVsSat, SatSatTimes, new[]
            {
                0.5f,
                Mathf.Lerp(0.5f, 0.485f - 0.025f * overcast, amount),
                Mathf.Lerp(0.5f, 0.43f - 0.04f * overcast, amount),
                Mathf.Lerp(0.5f, 0.38f - 0.04f * overcast, amount)
            });
            curveAmount = amount;
            curveOvercast = overcast;
        }

        private static TextureCurve CreateCurve(float[] times)
        {
            var keys = new Keyframe[times.Length];
            for (int i = 0; i < times.Length; i++) keys[i] = new Keyframe(times[i], 0.5f);
            return new TextureCurve(keys, 0.5f, false, new Vector2(0f, 1f));
        }

        private static void SetKeys(TextureCurve curve, float[] times, float[] values)
        {
            for (int i = 0; i < times.Length; i++) curve.MoveKey(i, new Keyframe(times[i], values[i]));
            for (int i = 0; i < times.Length; i++) curve.SmoothTangents(i, 0f);
            curve.SetDirty();
        }

        private static float SmoothStep(float edge0, float edge1, float x) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, x));

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
            state["cinematicLook"] = lastLook.ToString();
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
            lumVsSat?.Release();
            satVsSat?.Release();
            lumVsSat = satVsSat = null;
            holder = null;
            nativeVolume = volume = null;
            profile = null;
            tone = null;
            ranges = null;
            lgg = null;
            curves = null;
            bloom = null;
            vignette = null;
            grain = null;
            curveAmount = curveOvercast = -1f;
        }

        private void ApplyStreak(Camera world, Shader shader, float strength, float exposure, bool optimize)
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
            streak.Bind(world, streakMaterial, strength, exposure, optimize);
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
