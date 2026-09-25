using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Visuals.Configuration;
using BoscaliSummer.Features.Visuals.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Features.Visuals.Runtime
{
    /// <summary>
    /// The screen-space half of the visual layer. The game already runs ACES tonemapping, a
    /// day/night bloom, auto-exposure (<c>ExposureController</c> drives ColorAdjustments.postExposure)
    /// and a G-LOC blackout (<c>GLOC</c> drives vignette intensity and saturation) from its own
    /// <c>LevelInfo.PostProcessing</c> volume. This volume sits just above that one and only
    /// overrides parameters the game leaves alone, so none of those systems is fought:
    /// <list type="bullet">
    /// <item>grade: contrast, split-tone (shadows/midtones/highlights), film grain; bloom is boosted by
    /// scaling the game's own value rather than replacing it;</item>
    /// <item>G: red colour filter and red vignette tint under negative G, chromatic fringing under
    /// heavy positive G — cockpit view only, like the game's G-LOC.</item>
    /// </list>
    /// No camera motion blur: the game post-processes on an overlay camera whose depth is
    /// cleared, so URP's camera-only blur reprojects every pixel from the near plane and smears
    /// the whole view, cockpit included (seen in-game 2026-09-24).
    /// Sharpening lives in <see cref="Sharpening"/>. The game's own renderer already runs SSAO.
    /// </summary>
    internal sealed class VisualsManager : MonoBehaviour, ISceneService, IVisualEnhancements
    {
        /// <summary>Above the game's outdoor volume (0.5), below its night-vision volume (1) so NVG
        /// still wins where both set a parameter.</summary>
        private const float Priority = 0.75f;
        private const float RedoutEpsilon = 0.01f;

        public static VisualsManager Live { get; private set; }

        private VisualsSettings settings;
        private ManualLogSource logger;
        private readonly Sharpening sharpening = new Sharpening();

        private GameObject volumeHolder;
        private Volume volume;
        private VolumeProfile profile;
        private ColorAdjustments colorAdjustments;
        private ShadowsMidtonesHighlights splitTone;
        private FilmGrain filmGrain;
        private Bloom bloomQuality;
        private Vignette vignette;
        private ChromaticAberration fringe;

        private Bloom boostedBloom;
        private float bloomBase = -1f;
        private float bloomWritten = -1f;
        private float redout;
        private float strain;

        // Automation only: pretend a G load for a few seconds so the effects can be captured.
        private float debugG = 1f, debugUntil = -1f;

        internal void DebugG(float g, float seconds)
        {
            debugG = g;
            debugUntil = Time.unscaledTime + seconds;
        }

        public void Configure(VisualsSettings visualsSettings, ManualLogSource logSource)
        {
            settings = visualsSettings;
            logger = logSource;
            Live = this;
        }

        public bool IsEnabled => settings != null && settings.Enabled.Value;

        public bool CinematicPostFxEnabled
        {
            get => settings != null && settings.CinematicPostFxEnabled.Value;
            set { if (settings != null) settings.CinematicPostFxEnabled.Value = value; }
        }

        public float BloomBoost
        {
            get => settings != null ? settings.BloomBoost.Value : 1f;
            set { if (settings != null) settings.BloomBoost.Value = Mathf.Clamp(value, 0.5f, 2.5f); }
        }

        public bool SharpenEnabled
        {
            get => settings != null && settings.SharpenEnabled.Value;
            set { if (settings != null) settings.SharpenEnabled.Value = value; }
        }

        public float SharpenStrength
        {
            get => settings != null ? settings.SharpenStrength.Value : 0f;
            set { if (settings != null) settings.SharpenStrength.Value = Mathf.Clamp01(value); }
        }

        public bool GForceEffectsEnabled
        {
            get => settings != null && settings.GForceEffectsEnabled.Value;
            set { if (settings != null) settings.GForceEffectsEnabled.Value = value; }
        }

        public bool FoliageDynamicsEnabled
        {
            get => settings != null && settings.FoliageDynamicsEnabled.Value;
            set { if (settings != null) settings.FoliageDynamicsEnabled.Value = value; }
        }

        public float FoliageSwayStrength
        {
            get => settings != null ? settings.FoliageSwayStrength.Value : 1f;
            set { if (settings != null) settings.FoliageSwayStrength.Value = Mathf.Clamp(value, 0.2f, 2.5f); }
        }

        public void ResetForScene()
        {
            ReleaseBloom();
            TeardownVolume();
            redout = strain = 0f;
        }

        private void OnDestroy()
        {
            ReleaseBloom();
            TeardownVolume();
            sharpening.Restore();
            if (Live == this) Live = null;
        }

        private void Update()
        {
            if (settings == null || !settings.Enabled.Value) return;

            sharpening.Apply(settings.SharpenEnabled.Value, settings.SharpenStrength.Value);

            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (level == null || level.PostProcessing == null) return;
            if (!EnsureVolume(level)) return;

            bool grade = settings.CinematicPostFxEnabled.Value;
            colorAdjustments.contrast.overrideState = grade;
            splitTone.active = grade;
            filmGrain.active = grade;
            bloomQuality.active = grade;
            BoostBloom(level, grade ? settings.BloomBoost.Value : 1f);

            UpdateGForce();
        }

        private void UpdateGForce()
        {
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            bool cockpit = cameras != null && CameraStateManager.cameraMode == CameraMode.cockpit;
            float g = cameras != null ? cameras.gForce : 1f;
            if (Time.unscaledTime < debugUntil) g = debugG;
            float dt = Time.unscaledDeltaTime;

            float targetRedout = 0f, targetStrain = 0f;
            if (settings.GForceEffectsEnabled.Value && cockpit)
            {
                targetRedout = VisualsMath.CalculateGForceRedout(g);
                targetStrain = VisualsMath.CalculateGStrain(g);
            }
            // Onset a little faster than recovery, like blood pressure catching up.
            redout = Mathf.MoveTowards(redout, targetRedout, (targetRedout > redout ? 2.5f : 1.2f) * dt);
            strain = Mathf.MoveTowards(strain, targetStrain, (targetStrain > strain ? 2.5f : 1.2f) * dt);

            bool red = redout > RedoutEpsilon;
            colorAdjustments.colorFilter.overrideState = red;
            vignette.color.overrideState = red;
            if (red)
            {
                colorAdjustments.colorFilter.value = Color.Lerp(Color.white, new Color(1f, 0.38f, 0.33f), redout);
                vignette.color.value = Color.Lerp(Color.black, new Color(0.45f, 0f, 0f), redout);
            }
            fringe.active = strain > RedoutEpsilon;
            fringe.intensity.value = strain * 0.35f;
        }

        /// <summary>
        /// The game rewrites its bloom intensity (0.5 by day, 3 at night) once a second. Scale
        /// whatever it last wrote instead of overriding it, so the day/night switch survives.
        /// </summary>
        private void BoostBloom(LevelInfo level, float boost)
        {
            Bloom bloom = level.bloom;
            if (bloom != boostedBloom)
            {
                ReleaseBloom();
                boostedBloom = bloom;
            }
            if (bloom == null) return;
            float current = bloom.intensity.value;
            if (bloomBase < 0f || !Mathf.Approximately(current, bloomWritten)) bloomBase = current;
            // Night bloom (3, low threshold) is already strong: boost it by the square root only.
            bloomWritten = bloomBase * (bloomBase > 1f ? Mathf.Sqrt(boost) : boost);
            bloom.intensity.value = bloomWritten;
        }

        private void ReleaseBloom()
        {
            if (boostedBloom != null && bloomBase >= 0f && Mathf.Approximately(boostedBloom.intensity.value, bloomWritten))
                boostedBloom.intensity.value = bloomBase;
            boostedBloom = null;
            bloomBase = bloomWritten = -1f;
        }

        private bool EnsureVolume(LevelInfo level)
        {
            if (volume != null) return true;

            volumeHolder = new GameObject("BoscaliSummer.VisualsVolume");
            volumeHolder.transform.SetParent(transform, false);
            // Cameras only blend volumes on layers in their volume mask. The post-processing
            // overlay camera evaluates its own mask, so share the game's outdoor volume layer.
            volumeHolder.layer = level.PostProcessing.gameObject.layer;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "BoscaliSummer.VisualsProfile";

            colorAdjustments = profile.Add<ColorAdjustments>();
            colorAdjustments.contrast.value = 12f;
            colorAdjustments.colorFilter.value = Color.white;

            splitTone = profile.Add<ShadowsMidtonesHighlights>();
            splitTone.shadows.Override(new Vector4(0.96f, 0.99f, 1.06f, 0f));
            splitTone.midtones.Override(new Vector4(1f, 1f, 1f, 0f));
            splitTone.highlights.Override(new Vector4(1.05f, 1.01f, 0.95f, 0f));

            filmGrain = profile.Add<FilmGrain>();
            filmGrain.type.Override(FilmGrainLookup.Thin1);
            filmGrain.intensity.Override(0.14f);
            filmGrain.response.Override(0.85f);

            bloomQuality = profile.Add<Bloom>();
            bloomQuality.highQualityFiltering.Override(true);
            bloomQuality.scatter.Override(0.72f);

            vignette = profile.Add<Vignette>();
            vignette.color.value = Color.black;

            fringe = profile.Add<ChromaticAberration>();
            fringe.intensity.Override(0f);

            volume = volumeHolder.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = Priority;
            volume.sharedProfile = profile;
            logger?.LogInfo($"[Visuals] Post-processing volume on layer {volumeHolder.layer}, priority {Priority}.");
            return true;
        }

        private void TeardownVolume()
        {
            if (volumeHolder != null) Destroy(volumeHolder);
            if (profile != null) Destroy(profile);
            volumeHolder = null;
            volume = null;
            profile = null;
        }

        /// <summary>State for the automation readout.</summary>
        public void Describe(IDictionary<string, object> state)
        {
            state["volumeLayer"] = volumeHolder != null ? volumeHolder.layer : -1;
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            state["vanillaLayer"] = level != null && level.PostProcessing != null ? level.PostProcessing.gameObject.layer : -1;
            state["bloomBase"] = bloomBase;
            state["bloomWritten"] = bloomWritten;
            state["redout"] = redout;
            state["strain"] = strain;
            VolumeStack stack = VolumeManager.instance.stack;
            if (stack != null)
            {
                state["stackContrast"] = stack.GetComponent<ColorAdjustments>()?.contrast.value ?? float.NaN;
                state["stackGrain"] = stack.GetComponent<FilmGrain>()?.intensity.value ?? float.NaN;
                state["stackBloom"] = stack.GetComponent<Bloom>()?.intensity.value ?? float.NaN;
                state["stackTonemap"] = stack.GetComponent<Tonemapping>()?.mode.value.ToString() ?? "?";
            }
            sharpening.Describe(state);
        }
    }
}
