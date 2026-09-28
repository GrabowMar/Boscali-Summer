using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Features.Weather.Visuals
{
    /// <summary>The reduced-resolution cloud march: a URP pass on the main camera that draws the
    /// sky into a half-size colour target and a matching depth target before transparents. The
    /// camera-following cube then composites it at the volume's render queue, so water, clouds
    /// and vanilla smoke keep their order. Enqueued only for the camera it was bound to; if it
    /// stops executing, the dressing falls back to the full-resolution march.</summary>
    internal sealed class WeatherCloudPass : ScriptableRenderPass, IDisposable
    {
        private const int LowResPass = 2;
        private static readonly int ColourId = Shader.PropertyToID("_CloudLowResColour");
        private static readonly int DepthId = Shader.PropertyToID("_CloudLowResDepth");
        private static readonly int SizeId = Shader.PropertyToID("_CloudLowResSize");

        private readonly RenderTargetIdentifier[] targets = new RenderTargetIdentifier[2];
        private Material march, composite;
        private Camera camera;
        private Renderer volume;
        private Action<Camera> beforeRender;
        private bool reduced;
        private RenderTexture colour, depth;
        private bool hooked;

        /// <summary>Frame of the last execution on the bound camera.</summary>
        internal int ExecutedFrame { get; private set; } = -1;
        internal int Width => colour != null ? colour.width : 0;
        internal int Height => colour != null ? colour.height : 0;

        internal WeatherCloudPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        /// <summary>Binds the volume to one camera: every other camera skips the cube (it is
        /// built for this camera's view). <paramref name="halfResolution"/> arms the pass;
        /// <paramref name="onRender"/> runs at render time with the camera's final pose.</summary>
        internal void Bind(Camera target, Renderer volumeRenderer, Material marchMaterial, Material compositeMaterial,
            bool halfResolution, Action<Camera> onRender)
        {
            camera = target;
            volume = volumeRenderer;
            march = marchMaterial;
            composite = compositeMaterial;
            reduced = halfResolution;
            beforeRender = onRender;
            if (!hooked && target != null)
            {
                RenderPipelineManager.beginCameraRendering += OnBeginCamera;
                hooked = true;
            }
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera rendering)
        {
            if (rendering == null) return;
            if (volume != null) volume.forceRenderingOff = rendering != camera;
            if (rendering != camera) return;
            beforeRender?.Invoke(rendering);
            if (!reduced || march == null || composite == null) return;
            UniversalAdditionalCameraData data = rendering.GetUniversalAdditionalCameraData();
            if (data == null || data.renderType != CameraRenderType.Base || data.scriptableRenderer == null) return;
            data.scriptableRenderer.EnqueuePass(this);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (march == null || composite == null || renderingData.cameraData.camera != camera) return;
            RenderTextureDescriptor screen = renderingData.cameraData.cameraTargetDescriptor;
            if (!EnsureTargets(Math.Max(1, (screen.width + 1) / 2), Math.Max(1, (screen.height + 1) / 2))) return;
            var size = new Vector4(colour.width, colour.height, 1f / colour.width, 1f / colour.height);
            march.SetVector(SizeId, size);
            composite.SetVector(SizeId, size);
            composite.SetTexture(ColourId, colour);
            composite.SetTexture(DepthId, depth);

            CommandBuffer cmd = CommandBufferPool.Get("Boscali Clouds");
            try
            {
                targets[0] = colour;
                targets[1] = depth;
                cmd.SetRenderTarget(targets, targets[0]);
                cmd.DrawProcedural(Matrix4x4.identity, march, LowResPass, MeshTopology.Triangles, 3);
                // URP tracks its own attachments; give the camera its targets back.
                ScriptableRenderer renderer = renderingData.cameraData.renderer;
                cmd.SetRenderTarget(renderer.cameraColorTargetHandle, renderer.cameraDepthTargetHandle);
                context.ExecuteCommandBuffer(cmd);
                ExecutedFrame = Time.frameCount;
            }
            finally { CommandBufferPool.Release(cmd); }
        }

        private bool EnsureTargets(int width, int height)
        {
            if (colour != null && colour.width == width && colour.height == height && colour.IsCreated() && depth.IsCreated())
                return true;
            ReleaseTargets();
            colour = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            { name = "Boscali Clouds Low", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            depth = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
            { name = "Boscali Clouds Low Depth", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            return colour.Create() && depth.Create();
        }

        private void ReleaseTargets()
        {
            if (colour != null) { colour.Release(); UnityEngine.Object.Destroy(colour); }
            if (depth != null) { depth.Release(); UnityEngine.Object.Destroy(depth); }
            colour = depth = null;
        }

        public void Dispose()
        {
            if (hooked) RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            hooked = false;
            if (volume != null) volume.forceRenderingOff = false;
            camera = null;
            volume = null;
            beforeRender = null;
            march = composite = null;
            ReleaseTargets();
            ExecutedFrame = -1;
        }
    }
}
