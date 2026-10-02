using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>The reduced-resolution cloud march: a URP pass on the main camera that fills the
    /// half-size sky (<see cref="CloudLowRes"/>) before transparents. The camera-following cube
    /// then composites it at the volume's render queue, so water, clouds and vanilla smoke keep
    /// their order. Enqueued only for the camera it was bound to; if it stops executing, the
    /// dressing falls back to the full-resolution march.</summary>
    internal sealed class WeatherCloudPass : ScriptableRenderPass, IDisposable
    {
        private readonly CloudLowRes targets = new CloudLowRes();
        private Material march, composite;
        private Camera camera;
        private Renderer volume;
        private Action<Camera> beforeRender;
        private bool reduced, temporal;
        private bool hooked;

        /// <summary>Frame of the last execution on the bound camera.</summary>
        internal int ExecutedFrame { get; private set; } = -1;
        internal int Height => targets.Height;

        internal WeatherCloudPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        /// <summary>Binds the volume to one camera: every other camera skips the cube (it is
        /// built for this camera's view). <paramref name="halfResolution"/> arms the pass,
        /// <paramref name="temporalUpdate"/> marches a quarter of it per frame;
        /// <paramref name="onRender"/> runs at render time with the camera's final pose.</summary>
        internal void Bind(Camera target, Renderer volumeRenderer, Material marchMaterial, Material compositeMaterial,
            bool halfResolution, bool temporalUpdate, Action<Camera> onRender)
        {
            camera = target;
            volume = volumeRenderer;
            march = marchMaterial;
            composite = compositeMaterial;
            reduced = halfResolution;
            temporal = temporalUpdate;
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
            if (!targets.Ensure(screen.width, screen.height)) return;
            // A frame without the pass leaves stale history behind.
            if (ExecutedFrame != Time.frameCount - 1) targets.InvalidateHistory();

            CommandBuffer cmd = CommandBufferPool.Get("Boscali Clouds");
            try
            {
                targets.Record(cmd, march, composite, temporal);
                // URP tracks its own attachments; give the camera its targets back.
                ScriptableRenderer renderer = renderingData.cameraData.renderer;
                cmd.SetRenderTarget(renderer.cameraColorTargetHandle, renderer.cameraDepthTargetHandle);
                context.ExecuteCommandBuffer(cmd);
                ExecutedFrame = Time.frameCount;
            }
            finally { CommandBufferPool.Release(cmd); }
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
            targets.Dispose();
            ExecutedFrame = -1;
        }
    }
}
