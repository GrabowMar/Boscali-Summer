using System;
using BoscaliSummer.Core.Game;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Effects.Runtime
{
    /// <summary>Optional horizontal light streaks from bright HDR lights, before the world's post-processing.</summary>
    internal sealed class CinematicStreakPass : ScriptableRenderPass, IDisposable
    {
        private static readonly int Frame = Shader.PropertyToID("_BoscaliStreakFrame");
        private static readonly int Extract = Shader.PropertyToID("_BoscaliStreakExtract");
        private static readonly int Blur = Shader.PropertyToID("_BoscaliStreakBlur");
        private static readonly int StreakTexture = Shader.PropertyToID("_BoscaliStreakTex");
        private static readonly int Parameters = Shader.PropertyToID("_StreakParams");
        private Camera camera;
        private Material material;
        private float strength, exposure;
        private bool hooked;

        internal int ExecutedFrame { get; private set; } = -1;

        internal CinematicStreakPass() { renderPassEvent = RenderPassEvent.AfterRenderingTransparents; }

        /// <summary>The caller owns the material. A null target/material or zero strength unhooks the pass.</summary>
        internal void Bind(Camera target, Material streakMaterial, float amount, float postExposure)
        {
            if (target == null || streakMaterial == null || !(amount > 0f))
            {
                Dispose();
                return;
            }
            if (camera != target) ExecutedFrame = -1;
            camera = target;
            material = streakMaterial;
            strength = Mathf.Clamp01(amount);
            exposure = float.IsNaN(postExposure) || float.IsInfinity(postExposure) ? 0f : Mathf.Clamp(postExposure, -16f, 16f);
            if (hooked) return;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            hooked = true;
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera rendering)
        {
            if (rendering == null || rendering != camera || material == null || strength <= 0f) return;
            if (GameAccess.TryGetNightVisionState(out bool selected, out bool active) && (selected || active)) return;
            UniversalAdditionalCameraData data = rendering.GetUniversalAdditionalCameraData();
            // The game applies post-processing on its final overlay camera. The world
            // base camera still supplies the HDR lights this pass operates on.
            if (data == null || data.renderType != CameraRenderType.Base || !rendering.allowHDR ||
                data.scriptableRenderer == null) return;
            data.scriptableRenderer.EnqueuePass(this);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null || renderingData.cameraData.camera != camera || strength <= 0f ||
                !renderingData.cameraData.isHdrEnabled) return;
            RenderTextureDescriptor frame = renderingData.cameraData.cameraTargetDescriptor;
            frame.depthBufferBits = 0;
            frame.msaaSamples = 1;
            frame.useMipMap = false;
            frame.autoGenerateMips = false;
            RenderTextureDescriptor low = frame;
            low.width = Mathf.Max(1, (frame.width + 3) / 4);
            low.height = Mathf.Max(1, (frame.height + 3) / 4);

            // Threshold in display exposure, while every pixel stays in scene-linear HDR.
            // Ordinary white clouds stay below it; lights and afterburners can exceed it.
            float threshold = 4f * Mathf.Pow(2f, -exposure);
            material.SetVector(Parameters, new Vector4(threshold, threshold * 8f, strength, 1.5f));
            CommandBuffer cmd = CommandBufferPool.Get("Boscali Cinematic Light Streaks");
            try
            {
                ScriptableRenderer renderer = renderingData.cameraData.renderer;
                cmd.GetTemporaryRT(Frame, frame, FilterMode.Bilinear);
                cmd.GetTemporaryRT(Extract, low, FilterMode.Bilinear);
                cmd.GetTemporaryRT(Blur, low, FilterMode.Bilinear);
                cmd.Blit(renderer.cameraColorTargetHandle, Frame);
                cmd.Blit(Frame, Extract, material, 0);
                cmd.Blit(Extract, Blur, material, 1);
                cmd.SetGlobalTexture(StreakTexture, Blur);
                cmd.Blit(Frame, renderer.cameraColorTargetHandle, material, 2);
                cmd.SetRenderTarget(renderer.cameraColorTargetHandle, renderer.cameraDepthTargetHandle);
                cmd.ReleaseTemporaryRT(Blur);
                cmd.ReleaseTemporaryRT(Extract);
                cmd.ReleaseTemporaryRT(Frame);
                context.ExecuteCommandBuffer(cmd);
                ExecutedFrame = Time.frameCount;
            }
            finally { CommandBufferPool.Release(cmd); }
        }

        public void Dispose()
        {
            if (hooked) RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            hooked = false;
            camera = null;
            material = null;
            strength = 0f;
            ExecutedFrame = -1;
        }
    }
}
