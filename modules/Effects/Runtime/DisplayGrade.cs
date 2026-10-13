using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Effects.Configuration;
using BoscaliSummer.Modules.Effects.Domain;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Effects.Runtime
{
    /// <summary>
    /// The client's display grade, built only from what URP already ships: one global volume just
    /// above the game's own (<c>LevelInfo.PostProcessing</c>, priority 0.5) and below its night
    /// vision (1), plus RCAS sharpening. The game keeps what it owns: <c>ExposureController</c>
    /// drives postExposure and <c>GLOC</c> drives ColorAdjustments.saturation on the game's
    /// profile, so saturation here goes through the channel mixer instead of overriding that.
    /// </summary>
    internal sealed class DisplayGrade : MonoBehaviour, ISceneService, IClientEffect
    {
        private const float Priority = 0.75f;
        private const float ApplySeconds = 0.25f;
        private const float LumaR = 0.2126f, LumaG = 0.7152f, LumaB = 0.0722f;

        private readonly Sharpening sharpening = new Sharpening();
        private EffectsSettings settings;
        private ManualLogSource logger;
        private string lutFolder;
        private GameObject holder;
        private VolumeProfile profile;
        private ColorAdjustments adjustments;
        private WhiteBalance balance;
        private ChannelMixer mixer;
        private ColorLookup lookup;
        private Texture2D lut;
        private string lutKey = "";
        private float nextApply;

        public string EffectId => "display-grade";

        public void Configure(EffectsSettings effectsSettings, ManualLogSource log)
        {
            settings = effectsSettings;
            logger = log;
            lutFolder = Path.Combine(Paths.ConfigPath, "BoscaliSummer", "luts");
            try { Directory.CreateDirectory(lutFolder); }
            catch (Exception error) { logger?.LogWarning("[Effects] LUT folder unavailable: " + error.Message); }
        }

        public void ResetForScene() => Teardown();

        public void ReleaseFx()
        {
            sharpening.Restore();
            Teardown();
        }

        public void DescribeFx(IDictionary<string, object> state)
        {
            state["grade"] = holder != null;
            state["lut"] = lut != null ? lut.width + "x" + lut.height : "none";
            sharpening.Describe(state);
        }

        private void OnDestroy() => ReleaseFx();

        private void Update()
        {
            if (settings == null || Time.unscaledTime < nextApply) return;
            nextApply = Time.unscaledTime + ApplySeconds;

            bool on = settings.Enabled.Value;
            sharpening.Apply(on && settings.SharpenEnabled.Value, settings.SharpenStrength.Value);
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (!on || level == null || level.PostProcessing == null)
            {
                Teardown();
                return;
            }
            EnsureVolume(level);

            adjustments.contrast.Override(Mathf.Clamp(settings.Contrast.Value, -100f, 100f));
            balance.temperature.Override(Mathf.Clamp(settings.Temperature.Value, -100f, 100f));

            // out = luma * (1 - s) + colour * s, written as mixer percentages.
            float s = 1f + Mathf.Clamp(settings.Saturation.Value, -100f, 100f) / 100f;
            float grey = (1f - s) * 100f, own = s * 100f;
            mixer.redOutRedIn.Override(LumaR * grey + own);
            mixer.redOutGreenIn.Override(LumaG * grey);
            mixer.redOutBlueIn.Override(LumaB * grey);
            mixer.greenOutRedIn.Override(LumaR * grey);
            mixer.greenOutGreenIn.Override(LumaG * grey + own);
            mixer.greenOutBlueIn.Override(LumaB * grey);
            mixer.blueOutRedIn.Override(LumaR * grey);
            mixer.blueOutGreenIn.Override(LumaG * grey);
            mixer.blueOutBlueIn.Override(LumaB * grey + own);

            Texture2D strip = LoadLut();
            lookup.texture.Override(strip);
            lookup.contribution.Override(strip != null ? Mathf.Clamp01(settings.LutStrength.Value) : 0f);
        }

        private void EnsureVolume(LevelInfo level)
        {
            if (holder != null) return;
            holder = new GameObject("BoscaliSummer.DisplayGrade");
            holder.transform.SetParent(transform, false);
            // Cameras only blend volumes on layers in their volume mask: share the game's.
            holder.layer = level.PostProcessing.gameObject.layer;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            adjustments = profile.Add<ColorAdjustments>();
            balance = profile.Add<WhiteBalance>();
            mixer = profile.Add<ChannelMixer>();
            lookup = profile.Add<ColorLookup>();

            Volume volume = holder.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = Priority;
            volume.sharedProfile = profile;
        }

        /// <summary>The configured .cube as a strip of the pipeline's LUT size; reloads when the name, size or file changes.</summary>
        private Texture2D LoadLut()
        {
            string name = Path.GetFileName(settings.Lut.Value ?? "");
            UniversalRenderPipelineAsset pipeline = UniversalRenderPipeline.asset;
            if (name.Length == 0 || pipeline == null)
            {
                lutKey = "";
                DropLut();
                return null;
            }
            if (!Path.HasExtension(name)) name += ".cube";
            string path = Path.Combine(lutFolder, name);
            int size = pipeline.colorGradingLutSize;
            string key = path + "|" + size + "|" + File.GetLastWriteTimeUtc(path).Ticks;
            if (key == lutKey) return lut;
            lutKey = key;
            DropLut();
            try
            {
                int cubeSize = 0;
                float[] cube = File.Exists(path) ? CubeLut.Parse(File.ReadAllText(path), out cubeSize) : null;
                if (cube == null)
                {
                    logger?.LogWarning("[Effects] No readable 3D .cube LUT at " + path);
                    return null;
                }
                // Linear format: URP indexes the user LUT with sRGB-encoded colour and wants the stored values raw.
                lut = new Texture2D(size * size, size, TextureFormat.RGBA32, false, true)
                {
                    name = "BoscaliSummer.Lut",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                lut.LoadRawTextureData(CubeLut.Bake(cube, cubeSize, size));
                lut.Apply(false, true);
                logger?.LogInfo("[Effects] LUT " + name + " (" + cubeSize + "^3) loaded at " + size + "^3.");
            }
            catch (Exception error)
            {
                DropLut();
                logger?.LogWarning("[Effects] LUT load failed: " + error.Message);
            }
            return lut;
        }

        private void DropLut()
        {
            if (lut != null) Destroy(lut);
            lut = null;
        }

        private void Teardown()
        {
            if (holder != null) Destroy(holder);
            if (profile != null) Destroy(profile);
            holder = null;
            profile = null;
            lutKey = "";
            DropLut();
        }
    }
}
