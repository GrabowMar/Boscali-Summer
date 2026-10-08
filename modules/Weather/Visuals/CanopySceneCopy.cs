using System;
using BoscaliSummer.Core.Fx;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>What the canopy glass refracts. The cockpit renders on a URP Overlay camera, which
    /// has no opaque texture of its own: <c>_CameraOpaqueTexture</c> is the base camera's, taken
    /// before clouds, in-cloud fog and smoke, so every bead showed clear blue sky inside an
    /// overcast. This pass copies the finished world frame at the start of the overlay camera,
    /// before the cockpit draws, into a half-size mipmapped target: beads refract what is really
    /// behind the glass, and the mips give wet film a cheap blur (librain's smudge pass).</summary>
    internal sealed class CanopySceneCopy : ScriptableRenderPass, IDisposable
    {
        private RenderTexture target;
        private Camera camera;
        private bool hooked;
        private int executedFrame = -1;

        internal CanopySceneCopy() { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques; }

        internal RenderTexture Texture => target;

        /// <summary>The copy ran for this camera last frame, so the texture is current.</summary>
        internal bool Ready(Camera glass) => glass == camera && target != null && executedFrame >= Time.frameCount - 1;

        /// <summary>Copy for <paramref name="glass"/> from the next render on. Only overlay cameras
        /// qualify: on a base camera the frame is still empty before opaques.</summary>
        internal void Arm(Camera glass)
        {
            camera = glass;
            if (!hooked)
            {
                RenderPipelineManager.beginCameraRendering += OnBeginCamera;
                hooked = true;
            }
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera rendering)
        {
            if (rendering == null || rendering != camera) return;
            UniversalAdditionalCameraData data = rendering.GetUniversalAdditionalCameraData();
            if (data == null || data.renderType != CameraRenderType.Overlay || data.scriptableRenderer == null) return;
            data.scriptableRenderer.EnqueuePass(this);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.camera != camera) return;
            RenderTextureDescriptor screen = renderingData.cameraData.cameraTargetDescriptor;
            if (!Ensure(Mathf.Max(1, screen.width / 2), Mathf.Max(1, screen.height / 2))) return;
            CommandBuffer cmd = CommandBufferPool.Get("Boscali Canopy Scene");
            try
            {
                ScriptableRenderer renderer = renderingData.cameraData.renderer;
                cmd.Blit(renderer.cameraColorTargetHandle, target);
                cmd.SetRenderTarget(renderer.cameraColorTargetHandle, renderer.cameraDepthTargetHandle);
                context.ExecuteCommandBuffer(cmd);
                executedFrame = Time.frameCount;
            }
            finally { CommandBufferPool.Release(cmd); }
        }

        private bool Ensure(int width, int height)
        {
            if (target != null && target.width == width && target.height == height) return true;
            Release();
            var created = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf)
            {
                name = "BoscaliCanopyScene",
                useMipMap = true,
                autoGenerateMips = true,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (!FxRtPool.Own(created)) { UnityEngine.Object.Destroy(created); return false; }
            target = created;
            return true;
        }

        private void Release()
        {
            if (target == null) return;
            FxRtPool.Disown(target);
            UnityEngine.Object.Destroy(target);
            target = null;
        }

        public void Dispose()
        {
            if (hooked) RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            hooked = false;
            camera = null;
            executedFrame = -1;
            Release();
        }
    }
}
