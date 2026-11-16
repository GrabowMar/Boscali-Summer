using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Modules.Effects.Domain;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Effects.Runtime
{
    /// <summary>Composes AgX into URP's small HDR grading LUT, after native grading and before display LUTs.</summary>
    internal sealed class AgxTonemapping : IDisposable
    {
        private static readonly int Temporary = Shader.PropertyToID("_BoscaliAgxLut");
        private static readonly int AgxLut = Shader.PropertyToID("_AgxLut");
        private static readonly string[] Looks = { "agx-base", "agx-punchy", "agx-hue" };
        internal static AgxTonemapping Live { get; private set; }

        private UniversalRenderPipelineAsset asset;
        private ColorGradingMode savedMode;
        private Camera world;
        private AssetBundle bundle;
        private Shader[] shaders;
        private Material material;
        private readonly Texture3D[] looks = new Texture3D[3];
        private bool attempted;
        private AgxLook look;
        private int lastFrame = -1;

        internal void Apply(bool on, AgxLook selected, Camera camera, ManualLogSource logger)
        {
#if PUBLIC_RELEASE
            on = false; // Third-party LUT redistribution terms are unresolved.
#endif
            var current = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset != null && asset != current) Restore();
            if (!on || camera == null || current == null || !current.supportsHDR ||
                QualitySettings.activeColorSpace != ColorSpace.Linear || !Load(logger) || material == null)
            {
                Restore();
                return;
            }
            if (asset == null)
            {
                asset = current;
                savedMode = asset.colorGradingMode;
            }
            asset.colorGradingMode = ColorGradingMode.HighDynamicRange;
            look = selected >= AgxLook.Base && selected <= AgxLook.Hue ? selected : AgxLook.Base;
            material.SetTexture(AgxLut, looks[(int)look]);
            world = PostCamera(camera);
            Live = this;
        }

        internal bool CanApply(ref RenderingData data)
        {
            if (asset == null || material == null || data.cameraData.camera != world ||
                !data.cameraData.postProcessEnabled || !data.cameraData.isHdrEnabled ||
                data.cameraData.isHDROutputActive ||
                data.postProcessingData.gradingMode != ColorGradingMode.HighDynamicRange) return false;
            return !GameAccess.TryGetNightVisionState(out bool selected, out bool active) || !(selected || active);
        }

        // Nuclear Option draws world and cockpit without post, then grades both on a final overlay camera.
        // Follow the last active post-processing owner, so external views and cockpit stacks grade once.
        internal static Camera PostCamera(Camera camera)
        {
            if (camera == null || !camera.TryGetComponent(out UniversalAdditionalCameraData data) || data.renderType != CameraRenderType.Base)
                return camera;
            List<Camera> stack = data.cameraStack;
            if (stack == null) return camera;
            for (int i = stack.Count - 1; i >= 0; i--)
            {
                Camera overlay = stack[i];
                if (overlay != null && overlay.isActiveAndEnabled &&
                    overlay.TryGetComponent(out UniversalAdditionalCameraData post) && post.renderPostProcessing)
                    return overlay;
            }
            return camera;
        }

        internal Shader GetShader(string name, ManualLogSource logger)
        {
            if (!Load(logger)) return null;
            foreach (Shader shader in shaders)
                if (shader.name == name && shader.isSupported) return shader;
            return null;
        }

        internal void Compose(CommandBuffer cmd, RTHandle target)
        {
            RenderTextureDescriptor descriptor = target.rt.descriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            cmd.GetTemporaryRT(Temporary, descriptor, FilterMode.Bilinear);
            cmd.Blit(target.nameID, Temporary, material, 0);
            cmd.Blit(Temporary, target.nameID);
            cmd.ReleaseTemporaryRT(Temporary);
            lastFrame = Time.frameCount;
        }

        internal void Restore()
        {
            if (asset != null) asset.colorGradingMode = savedMode;
            asset = null;
            world = null;
            lastFrame = -1;
            if (Live == this) Live = null;
        }

        internal void Describe(IDictionary<string, object> state)
        {
            state["agx"] = Live == this;
            state["agxLook"] = look.ToString();
            state["agxFrameAge"] = lastFrame >= 0 ? Time.frameCount - lastFrame : -1;
        }

        public void Dispose()
        {
            Restore();
            if (material != null) UnityEngine.Object.Destroy(material);
            material = null;
            if (bundle != null) bundle.Unload(true);
            bundle = null;
            shaders = null;
            Array.Clear(looks, 0, looks.Length);
            attempted = false;
        }

        private bool Load(ManualLogSource logger)
        {
            if (attempted) return shaders != null;
            attempted = true;
            try
            {
                byte[] bytes = null;
#if !PUBLIC_RELEASE
                bytes = EmbeddedResources.ReadAll(typeof(AgxTonemapping).Assembly,
                    "BoscaliSummer.Effects.agx.bundle", 4 * 1024 * 1024);
#endif
                bytes ??= EmbeddedResources.ReadAll(typeof(AgxTonemapping).Assembly,
                    "BoscaliSummer.Effects.cinematic.bundle", 4 * 1024 * 1024);
                if (bytes == null) throw new InvalidOperationException("embedded effects shader bundle is missing");
                bundle = AssetBundle.LoadFromMemory(bytes);
                if (bundle == null) throw new InvalidOperationException("effects shader bundle could not be loaded");
                shaders = bundle.LoadAllAssets<Shader>();
                if (shaders.Length == 0) throw new InvalidOperationException("effects shader bundle is empty");
#if !PUBLIC_RELEASE
                Shader shader = null;
                foreach (Shader candidate in shaders)
                    if (candidate.name == "Hidden/Boscali/AgxLutComposite" && candidate.isSupported) shader = candidate;
                if (shader != null)
                {
                    foreach (Texture3D texture in bundle.LoadAllAssets<Texture3D>())
                        for (int i = 0; i < Looks.Length; i++)
                            if (texture.name == Looks[i]) looks[i] = texture;
                    foreach (Texture3D texture in looks)
                        if (texture == null) throw new InvalidOperationException("AgX look is missing");
                    material = new Material(shader) { name = "BoscaliSummer.AgX", hideFlags = HideFlags.HideAndDontSave };
                }
#endif
                return true;
            }
            catch (Exception error)
            {
                logger?.LogWarning("[Effects] Shader bundle unavailable; keeping native effects: " + error.Message);
                if (bundle != null) bundle.Unload(true);
                bundle = null;
                shaders = null;
                return false;
            }
        }
    }
}
